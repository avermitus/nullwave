using Nullwave.Core;

namespace Nullwave.Audio;

/// <summary>
/// Musical understanding on top of the spectrum: what is drums and what is melody, which chord
/// and key are playing, what the sound's texture is, and where the song's sections start and
/// repeat. Classic signal processing only (no models), cheap enough to run every frame.
///
/// - Drums vs. melody: median-filter harmonic/percussive separation on a log-frequency
///   spectrogram (harmonic = median over time, percussive = median over frequency).
/// - Harmony: a 12-note chroma from a long 8192-point FFT, matched to 24 major/minor triads
///   (chord) and, over a ~12 s memory, to the Krumhansl key profiles (key).
/// - Timbre: brightness (spectral centroid) and an MFCC-style shape vector; a short vs. long
///   average gives a novelty score that fires when the texture changes.
/// - Structure: one feature vector per beat (chroma + timbre). A boundary is where the last 4
///   beats differ sharply from the 8 before; each new section is compared with earlier ones so a
///   returning chorus gets the same label.
/// </summary>
public sealed class MusicAnalyzer
{
    public const int WindowSize = 8192;
    private const int LogBins = 72;
    private const int TimeMedian = 9;
    private const int FreqMedian = 7;
    private const int TimbreBands = 16;
    private const int TimbreCoeffs = 8;
    // per beat: raw chroma (12) + raw timbre (8) + loudness + drum share
    private const int FeatureSize = 12 + TimbreCoeffs + 2;

    private readonly Fft _fft = new(WindowSize);
    private readonly float[] _mag = new float[WindowSize / 2];
    private readonly float[] _logMag = new float[LogBins];
    private readonly float[,] _history = new float[TimeMedian, LogBins];
    private int _histIndex, _histCount;
    private readonly float[] _harm = new float[LogBins];
    private readonly float[] _perc = new float[LogBins];
    private readonly float[] _scratch = new float[Math.Max(TimeMedian, FreqMedian)];
    private readonly float[] _bandCeil = new float[6];
    private readonly float[] _bandFloor = new float[6];
    private readonly float[] _bandSmooth = new float[6];
    private bool _bandPrimed;

    private readonly float[] _chroma = new float[12];
    private readonly float[] _chromaLong = new float[12];
    private readonly float[] _frameChroma = new float[12];
    private int[]? _binPc;
    private float[]? _binW;
    private float _binRate;

    private int _chordCandidate = -1;
    private float _chordCandidateTime;
    private int _chord = -1;
    private float _chordAge;
    private int _keyCandidate = -1;
    private float _keyCandidateTime;
    private int _key = -1;
    private float _keyWarm;

    private readonly float[] _timbre = new float[TimbreCoeffs];
    private readonly float[] _timbreShort = new float[TimbreCoeffs];
    private readonly float[] _timbreLong = new float[TimbreCoeffs];
    private float _noveltyMean = 0.05f;
    private float _textureCooldown;
    private float _timbreWarm;

    private readonly List<float[]> _beats = new();
    private readonly List<(int Start, int End, int Label)> _sections = new();
    private int _labelCount;
    private int _confirmAt = -1;
    private float _provisionalDist;
    private int _provisionalPrevLabel;
    private int _provisionalPrevBoundary;
    private bool _provisionalWasNew;
    private long _lastBeatIndex = long.MinValue;
    private long _internalBeat;
    private readonly Fft _fftSmall = new(2048);
    private readonly float[] _magSmall = new float[1024];

    /// <summary>When false the analyzer skips song-structure detection (another instance does it ahead of playback).</summary>
    public bool Structure { get; set; } = true;
    /// <summary>Beat position (tracker clock) at which the last detected section starts (for scheduling).</summary>
    public double LastBoundaryBeatIndex { get; private set; }
    // where in the tracker's beat the kick actually lands (0..1): structure beats are re-aligned to it
    private readonly float[] _phaseHist = new float[16];
    private float _kickPhase;
    private int _phaseRecalc;
    private double _clock;
    private double _beatStartClock;
    private readonly List<double> _beatStarts = new();
    private long _beatStartFrame;
    private readonly List<long> _beatStartFrames = new();
    /// <summary>Set by the caller before Analyze: audio frame index at the end of the window.</summary>
    public long WindowEndFrame { get; set; }
    /// <summary>Audio frame index at which the last detected section's first beat began.</summary>
    public long LastBoundaryFrame { get; private set; }
    /// <summary>Analyzer clock (sum of dt) — the time base of <see cref="LastBoundaryTime"/>.</summary>
    public double Clock => _clock;
    /// <summary>Analyzer-clock time at which the last detected section's first beat began.</summary>
    public double LastBoundaryTime { get; private set; }
    private readonly float[] _beatAccum = new float[FeatureSize];
    private int _beatAccumCount;
    private float _beatClock;
    private int _lastBoundaryBeat;
    private float _boundaryMean = 0.01f, _boundaryVar = 0.0001f;
    private int _section, _sectionLabel;
    private float _silence;

    private static readonly float[] MajorProfile = [6.35f, 2.23f, 3.48f, 2.33f, 4.38f, 4.09f, 2.52f, 5.19f, 2.39f, 3.66f, 2.29f, 2.88f];
    private static readonly float[] MinorProfile = [6.33f, 2.68f, 3.52f, 5.38f, 2.60f, 3.53f, 2.54f, 4.75f, 3.98f, 2.69f, 3.34f, 3.17f];

    public int SectionCount => _sections.Count;

    public void Reset()
    {
        Array.Clear(_history);
        _histIndex = _histCount = 0;
        _bandPrimed = false;
        Array.Clear(_bandSmooth);
        Array.Clear(_chroma);
        Array.Clear(_chromaLong);
        _chord = _chordCandidate = -1;
        _key = _keyCandidate = -1;
        _keyWarm = _timbreWarm = 0f;
        Array.Clear(_timbreShort);
        Array.Clear(_timbreLong);
        _noveltyMean = 0.05f;
        _beats.Clear();
        _beatStarts.Clear();
        _beatStartFrames.Clear();
        _sections.Clear();
        _labelCount = 0;
        _confirmAt = -1;
        _lastBeatIndex = long.MinValue;
        Array.Clear(_phaseHist);
        _kickPhase = 0f;
        _phaseRecalc = 0;
        Array.Clear(_beatAccum);
        _beatAccumCount = 0;
        _lastBoundaryBeat = 0;
        _boundaryMean = 0.01f;
        _boundaryVar = 0.0001f;
        _section = _sectionLabel = 0;
    }

    private static float Db(float v) => 20f * MathF.Log10(MathF.Max(v, 1e-7f));

    private static float Median(float[] buf, int n)
    {
        for (int i = 1; i < n; i++)
        {
            float v = buf[i];
            int j = i - 1;
            while (j >= 0 && buf[j] > v) { buf[j + 1] = buf[j]; j--; }
            buf[j + 1] = v;
        }
        return buf[n / 2];
    }

    /// <param name="window">the latest <see cref="WindowSize"/> mono samples (ending at "now")</param>
    /// <param name="mags">the 2048-point magnitude spectrum of the same moment (for drums vs. melody)</param>
    public void Analyze(ReadOnlySpan<float> window, ReadOnlySpan<float> mags, float sampleRate, float dt, AudioState state)
    {
        if (window.Length < WindowSize) return;
        if (mags.Length < 64)
        {
            _fftSmall.Magnitudes(window.Slice(window.Length - 2048, 2048), _magSmall);
            mags = _magSmall;
        }
        dt = Math.Clamp(dt, 0.001f, 0.1f);
        _clock += dt;

        // ------------------------------------------------ drums vs. melody (HPSS on log bins)
        int bins = mags.Length;
        float binHz = sampleRate / (bins * 2f);
        float total = 0f;
        for (int b = 0; b < LogBins; b++)
        {
            float f0 = 40f * MathF.Pow(16000f / 40f, b / (float)LogBins);
            float f1 = 40f * MathF.Pow(16000f / 40f, (b + 1) / (float)LogBins);
            int i0 = Math.Clamp((int)(f0 / binHz), 1, bins - 1);
            int i1 = Math.Clamp((int)(f1 / binHz), i0 + 1, bins);
            float s = 0f;
            for (int i = i0; i < i1; i++) s += float.IsFinite(mags[i]) ? mags[i] : 0f;
            _logMag[b] = s / (i1 - i0);
            total += _logMag[b];
        }
        bool silent = total < 1e-5f;
        _silence = silent ? _silence + dt : 0f;
        for (int b = 0; b < LogBins; b++) _history[_histIndex, b] = _logMag[b];
        _histIndex = (_histIndex + 1) % TimeMedian;
        _histCount = Math.Min(_histCount + 1, TimeMedian);
        float pTot = 0f, hTot = 0f;
        Span<float> pBand = stackalloc float[3];
        Span<float> hBand = stackalloc float[3];
        for (int b = 0; b < LogBins; b++)
        {
            for (int k = 0; k < _histCount; k++) _scratch[k] = _history[k, b];
            float h = Median(_scratch, _histCount);
            int n = 0;
            for (int k = -FreqMedian / 2; k <= FreqMedian / 2; k++)
                _scratch[n++] = _logMag[Math.Clamp(b + k, 0, LogBins - 1)];
            float p = Median(_scratch, n);
            float x = _logMag[b];
            float h2 = h * h, p2 = p * p, den = h2 + p2 + 1e-12f;
            _harm[b] = x * h2 / den;
            _perc[b] = x * p2 / den;
            float f = 40f * MathF.Pow(16000f / 40f, (b + 0.5f) / LogBins);
            int band = f < 250f ? 0 : f < 2000f ? 1 : 2;
            pBand[band] += _perc[b];
            hBand[band] += _harm[b];
            pTot += _perc[b];
            hTot += _harm[b];
        }
        for (int i = 0; i < 3; i++)
        {
            float pv = AdaptBand(i, Db(pBand[i]), dt);
            float hv = AdaptBand(3 + i, Db(hBand[i]), dt);
            // drums snap, melody glides
            _bandSmooth[i] = pv > _bandSmooth[i] ? pv : M.ExpSmooth(_bandSmooth[i], pv, 10f, dt);
            _bandSmooth[3 + i] = M.ExpSmooth(_bandSmooth[3 + i], hv, 5f, dt);
        }
        _bandPrimed = true;
        if (silent) for (int i = 0; i < 6; i++) _bandSmooth[i] = M.ExpSmooth(_bandSmooth[i], 0f, 4f, dt);
        state.PercLow = _bandSmooth[0]; state.PercMid = _bandSmooth[1]; state.PercHigh = _bandSmooth[2];
        state.HarmLow = _bandSmooth[3]; state.HarmMid = _bandSmooth[4]; state.HarmHigh = _bandSmooth[5];
        state.Percussive = M.ExpSmooth(state.Percussive, pTot / (pTot + hTot + 1e-9f), 4f, dt);

        // ------------------------------------------------ timbre: brightness + shape
        float cNum = 0f, cDen = 0f;
        Span<float> tb = stackalloc float[TimbreBands];
        for (int b = 0; b < LogBins; b++)
        {
            float f = 40f * MathF.Pow(16000f / 40f, (b + 0.5f) / LogBins);
            cNum += f * _logMag[b];
            cDen += _logMag[b];
            tb[b * TimbreBands / LogBins] += _logMag[b];
        }
        float centroid = cDen > 1e-9f ? cNum / cDen : 200f;
        state.Brightness = M.ExpSmooth(state.Brightness, M.Clamp01(MathF.Log2(MathF.Max(centroid, 50f) / 150f) / 6f), 6f, dt);
        if (!silent)
        {
            for (int k = 0; k < TimbreCoeffs; k++)
            {
                float c = 0f;
                for (int j = 0; j < TimbreBands; j++)
                    c += MathF.Log(tb[j] + 1e-6f) * MathF.Cos(MathF.PI * (k + 1) * (j + 0.5f) / TimbreBands);
                _timbre[k] = c / TimbreBands;
            }
            _timbreWarm += dt;
            float novelty = 0f;
            for (int k = 0; k < TimbreCoeffs; k++)
            {
                _timbreShort[k] = M.ExpSmooth(_timbreShort[k], _timbre[k], 2f, dt);
                _timbreLong[k] = M.ExpSmooth(_timbreLong[k], _timbre[k], 0.25f, dt);
                float d = _timbreShort[k] - _timbreLong[k];
                novelty += d * d;
            }
            novelty = MathF.Sqrt(novelty);
            _noveltyMean = M.ExpSmooth(_noveltyMean, novelty, 0.15f, dt);
            state.TimbreNovelty = M.Clamp01(novelty / MathF.Max(0.02f, _noveltyMean * 3f));
            _textureCooldown -= dt;
            if (_timbreWarm > 6f && _textureCooldown <= 0f && novelty > _noveltyMean * 2.4f && novelty > 0.08f)
            {
                state.TextureChanged = true;
                _textureCooldown = 4f;
            }
        }

        // ------------------------------------------------ harmony: chroma, chord, key
        _fft.Magnitudes(window.Slice(window.Length - WindowSize, WindowSize), _mag);
        DetectMelody(sampleRate, dt, state, silent);
        BuildChromaMap(sampleRate);
        Array.Clear(_frameChroma);
        for (int i = 0; i < _mag.Length; i++)
        {
            int pc = _binPc![i];
            if (pc < 0) continue;
            _frameChroma[pc] += _mag[i] * _binW![i];
        }
        float cmax = 0f;
        for (int k = 0; k < 12; k++) cmax = MathF.Max(cmax, _frameChroma[k]);
        if (!silent && cmax > 1e-6f)
        {
            for (int k = 0; k < 12; k++)
            {
                float v = _frameChroma[k] / cmax;
                _chroma[k] = M.ExpSmooth(_chroma[k], v, 6f, dt);
                _chromaLong[k] = M.ExpSmooth(_chromaLong[k], v, 0.08f, dt);
                state.Chroma[k] = _chroma[k];
            }
            _keyWarm += dt;
            UpdateChord(dt, state);
            UpdateKey(dt, state);
        }

        // ------------------------------------------------ structure: per-beat features
        if (!Structure) return;
        // learn where the kick lands inside the tracker's beat, so each structure "beat" starts on it
        if (state.BeatLocked && double.IsFinite(state.BeatPosition))
        {
            double frac = state.BeatPosition - Math.Floor(state.BeatPosition);
            int bin = Math.Clamp((int)(frac * 16), 0, 15);
            for (int k = 0; k < 16; k++) _phaseHist[k] *= 0.997f;
            _phaseHist[bin] += pBand[0];
        }
        if (!silent && cmax > 1e-6f)
        {
            for (int k = 0; k < 12; k++) _beatAccum[k] += _frameChroma[k] / cmax;
            for (int k = 0; k < TimbreCoeffs; k++) _beatAccum[12 + k] += _timbre[k] * 0.6f;
            _beatAccum[12 + TimbreCoeffs] += M.Clamp01((Db(total) + 80f) / 80f) * 1.5f;
            _beatAccum[13 + TimbreCoeffs] += pTot / (pTot + hTot + 1e-9f);
            _beatAccumCount++;
        }
        // beats come from the tracker's clock (locked) or a steady 0.5 s fallback
        long beatIndex;
        if (state.BeatLocked && double.IsFinite(state.BeatPosition)) beatIndex = (long)Math.Floor(state.BeatPosition - _kickPhase);
        else
        {
            _beatClock += dt;
            if (_beatClock >= 0.5f) { _beatClock -= 0.5f; _internalBeat++; }
            beatIndex = _internalBeat;
        }
        if (_lastBeatIndex == long.MinValue) { _lastBeatIndex = beatIndex; _beatStartClock = _clock; _beatStartFrame = WindowEndFrame; }
        if (beatIndex != _lastBeatIndex)
        {
            long done = _lastBeatIndex;
            _lastBeatIndex = beatIndex;
            if (_beatAccumCount > 0 && beatIndex > done)
            {
                var f = new float[FeatureSize];
                for (int k = 0; k < FeatureSize; k++) f[k] = _beatAccum[k] / _beatAccumCount;
                bool barStart = ((done - state.DownbeatOffset) % 4 + 4) % 4 == 0;
                _beatStarts.Add(_beatStartClock);
                _beatStartFrames.Add(_beatStartFrame);
                PushBeat(f, done, barStart, state);
                // re-estimate the kick phase every 8 beats (rarely, so the grid does not jitter)
                if (++_phaseRecalc >= 8)
                {
                    _phaseRecalc = 0;
                    int peak = 0;
                    for (int k = 1; k < 16; k++) if (_phaseHist[k] > _phaseHist[peak]) peak = k;
                    float sum = 0f;
                    for (int k = 0; k < 16; k++) sum += _phaseHist[k];
                    if (sum > 1e-6f && _phaseHist[peak] > sum * 0.12f) _kickPhase = peak / 16f;
                }
            }
            Array.Clear(_beatAccum);
            _beatAccumCount = 0;
            _beatStartClock = _clock;
            _beatStartFrame = WindowEndFrame;
        }
        state.Section = _section;
        state.SectionLabel = _sectionLabel;
        state.SectionCount = _sections.Count;
    }

    /// <summary>
    /// Level of a band against its own recent average (drums: ~0.5 s, melody: ~3 s), so hits and
    /// swells stand out even in dense, loud, compressed mixes. 0.35 = its usual level.
    /// </summary>
    private float _melodyPitch = 60f, _melodyConf, _noteCandidate = -1f, _noteCandidateTime;

    /// <summary>
    /// Lead melody pitch: harmonic-product spectrum over 180–1100 Hz (a note counts only if its
    /// 2nd and 3rd harmonics are there too, so overtones are not mistaken for the melody), refined
    /// with parabolic interpolation. Confidence = how far the peak stands above the band.
    /// </summary>
    private void DetectMelody(float rate, float dt, AudioState state, bool silent)
    {
        float binHz = rate / WindowSize;
        int n = _mag.Length;
        int k0 = Math.Max(2, (int)(180f / binHz)), k1 = Math.Min((int)(1100f / binHz), (n - 2) / 3);
        float best = float.MinValue, sumLog = 0f;
        int bestK = -1, cnt = 0;
        for (int k = k0; k <= k1; k++)
        {
            float a = MathF.Log(_mag[k] + 1e-9f);
            float h2 = MathF.Max(_mag[2 * k - 1], MathF.Max(_mag[2 * k], _mag[2 * k + 1]));
            float h3 = MathF.Max(_mag[3 * k - 1], MathF.Max(_mag[3 * k], _mag[3 * k + 1]));
            float score = a + 0.8f * MathF.Log(h2 + 1e-9f) + 0.5f * MathF.Log(h3 + 1e-9f);
            sumLog += a; cnt++;
            if (score > best) { best = score; bestK = k; }
        }
        float conf = 0f;
        if (!silent && bestK > k0 && bestK < k1 && cnt > 0)
        {
            float prominence = MathF.Log(_mag[bestK] + 1e-9f) - sumLog / cnt;
            conf = M.Clamp01((prominence - 1.5f) / 3f);
            float m0 = _mag[bestK - 1], m1 = _mag[bestK], m2 = _mag[bestK + 1];
            float den = m0 - 2f * m1 + m2;
            float delta = MathF.Abs(den) > 1e-12f ? M.Clamp(0.5f * (m0 - m2) / den, -0.5f, 0.5f) : 0f;
            float f = (bestK + delta) * binHz;
            float midi = 69f + 12f * MathF.Log2(f / 440f);
            if (conf > 0.25f)
            {
                if (MathF.Abs(midi - _melodyPitch) > 0.6f)
                {
                    // a new note must hold for ~50 ms before the pitch jumps to it
                    if (MathF.Abs(midi - _noteCandidate) > 0.6f) { _noteCandidate = midi; _noteCandidateTime = 0f; }
                    else _noteCandidateTime += dt;
                    if (_noteCandidateTime > 0.05f) { _melodyPitch = midi; state.MelodyNoteChanged = true; }
                }
                else _melodyPitch = M.ExpSmooth(_melodyPitch, midi, 12f, dt);
            }
        }
        _melodyConf = M.ExpSmooth(_melodyConf, conf, 8f, dt);
        state.MelodyPitch = _melodyPitch;
        state.MelodyConfidence = _melodyConf;
    }

    private float AdaptBand(int i, float db, float dt)
    {
        bool drums = i < 3;
        if (!_bandPrimed) _bandCeil[i] = db;
        _bandCeil[i] = M.ExpSmooth(_bandCeil[i], db, drums ? 2f : 0.35f, dt);   // running mean (dB)
        return M.Clamp01(0.35f + (db - _bandCeil[i]) / (drums ? 9f : 7f));
    }

    private void BuildChromaMap(float rate)
    {
        if (_binPc != null && _binRate == rate) return;
        _binRate = rate;
        _binPc = new int[_mag.Length];
        _binW = new float[_mag.Length];
        float binHz = rate / WindowSize;
        for (int i = 0; i < _mag.Length; i++)
        {
            float f = i * binHz;
            _binPc[i] = -1;
            if (f < 100f || f > 2200f) continue;
            float midi = 69f + 12f * MathF.Log2(f / 440f);
            float near = MathF.Round(midi);
            float w = 1f - 2f * MathF.Abs(midi - near);   // bins between two notes count less
            if (w <= 0f) continue;
            _binPc[i] = (((int)near % 12) + 12) % 12;
            _binW[i] = w;
        }
    }

    private void UpdateChord(float dt, AudioState state)
    {
        float norm = 0f;
        for (int k = 0; k < 12; k++) norm += _chroma[k] * _chroma[k];
        norm = MathF.Sqrt(norm) * MathF.Sqrt(3f);
        if (norm < 1e-6f) return;
        int best = -1;
        float bestScore = 0f, second = 0f;
        for (int root = 0; root < 12; root++)
            for (int minor = 0; minor < 2; minor++)
            {
                float s = _chroma[root] + _chroma[(root + (minor == 1 ? 3 : 4)) % 12] + _chroma[(root + 7) % 12];
                s /= norm;
                if (s > bestScore) { second = bestScore; bestScore = s; best = root + minor * 12; }
                else if (s > second) second = s;
            }
        state.ChordConfidence = M.Clamp01((bestScore - 0.5f) * 2f) * M.Clamp01((bestScore - second) * 8f + 0.3f);
        if (best != _chordCandidate) { _chordCandidate = best; _chordCandidateTime = 0f; }
        else _chordCandidateTime += dt;
        _chordAge += dt;
        if (_chordCandidate != _chord && _chordCandidateTime > 0.18f && bestScore > 0.62f)
        {
            _chord = _chordCandidate;
            _chordAge = 0f;
            state.ChordChanged = true;
        }
        state.ChordRoot = _chord < 0 ? -1 : _chord % 12;
        state.ChordMinor = _chord >= 12;
        state.ChordAge = _chordAge;
    }

    private void UpdateKey(float dt, AudioState state)
    {
        if (_keyWarm < 3f) return;
        float mean = 0f;
        for (int k = 0; k < 12; k++) mean += _chromaLong[k];
        mean /= 12f;
        int best = -1;
        float bestR = -1f, second = -1f;
        for (int root = 0; root < 12; root++)
            for (int minor = 0; minor < 2; minor++)
            {
                var prof = minor == 1 ? MinorProfile : MajorProfile;
                float pm = minor == 1 ? 3.7058f : 3.4825f;
                float sxy = 0f, sxx = 0f, syy = 0f;
                for (int k = 0; k < 12; k++)
                {
                    float x = _chromaLong[(root + k) % 12] - mean;
                    float y = prof[k] - pm;
                    sxy += x * y; sxx += x * x; syy += y * y;
                }
                float r = sxy / MathF.Sqrt(sxx * syy + 1e-9f);
                if (r > bestR) { second = bestR; bestR = r; best = root + minor * 12; }
                else if (r > second) second = r;
            }
        state.KeyConfidence = M.Clamp01(bestR);
        if (best != _keyCandidate) { _keyCandidate = best; _keyCandidateTime = 0f; }
        else _keyCandidateTime += dt;
        // settle on a first guess after ~6 s; only call a real key change once the song has been
        // heard for a while and the new key has held for several seconds
        bool first = _key < 0 && _keyWarm > 6f && _keyCandidateTime > 2f && bestR > 0.5f;
        bool change = _key >= 0 && _keyWarm > 20f && _keyCandidate != _key && _keyCandidateTime > 6f && bestR > 0.65f && bestR - second > 0.05f;
        if (first || change)
        {
            if (change) state.KeyChanged = true;
            _key = _keyCandidate;
        }
        state.Key = _key;
    }

    private static float Cosine(float[] a, float[] b)
    {
        float ab = 0f, aa = 0f, bb = 0f;
        for (int i = 0; i < a.Length; i++) { ab += a[i] * b[i]; aa += a[i] * a[i]; bb += b[i] * b[i]; }
        return ab / MathF.Sqrt(aa * bb + 1e-12f);
    }

    private static float[] Mean(List<float[]> list, int from, int count)
    {
        var m = new float[FeatureSize];
        for (int i = from; i < from + count; i++)
            for (int k = 0; k < FeatureSize; k++) m[k] += list[i][k];
        for (int k = 0; k < FeatureSize; k++) m[k] /= count;
        return m;
    }

    /// <summary>
    /// One finished beat. Sections almost always start on a bar line, so a boundary is tested
    /// only on bar-start beats and decided from that single beat (no waiting): it must differ from
    /// the 8 beats before far more than this song's beats usually do. Four beats later the call is
    /// checked: a one-off (a fill, a hit) is taken back, and the label is re-checked with more
    /// evidence. Both corrections are reported as section changes flagged SectionCorrected.
    /// </summary>
    private void PushBeat(float[] f, long beatIndex, bool barStart, AudioState state)
    {
        if (_beats.Count >= 4096) { Reset(); _beatStarts.Clear(); }
        if (_beatStarts.Count > _beats.Count + 1) _beatStarts.RemoveRange(0, _beatStarts.Count - _beats.Count - 1);
        if (_beatStartFrames.Count > _beats.Count + 1) _beatStartFrames.RemoveRange(0, _beatStartFrames.Count - _beats.Count - 1);
        _beats.Add(f);
        int n = _beats.Count;
        if (n < 10) return;
        if (_sections.Count == 0)
        {
            // the first section starts with the song (skip the first beat while the analysis settles)
            _sections.Add((1, -1, 0));
            _labelCount = 1;
            _section = 0;
            _sectionLabel = 0;
            _lastBoundaryBeat = 1;
        }

        if (_confirmAt >= 0 && n >= _confirmAt) Confirm(state);

        // how unlike this beat is to its closest twin among the last 16: chords from the current
        // loop always have a recent twin, the first beat of a new section usually does not
        float nearest = 0f;
        for (int w = Math.Max(0, n - 17); w < n - 1; w++) nearest = MathF.Max(nearest, Cosine(f, _beats[w]));
        float d1 = 1f - nearest;
        float std = MathF.Sqrt(_boundaryVar);
        bool ready = _confirmAt < 0 && n - _lastBoundaryBeat >= 16 && n >= 18;
        // bar starts are where sections usually begin; elsewhere demand a much clearer change
        float sigmas = barStart ? 3f : 5f;
        bool boundary = ready && d1 > _boundaryMean + sigmas * std && d1 > (barStart ? 0.03f : 0.05f);
        int start = n - 1;
        bool backup = false;
        // backup: a section whose first beat looked familiar is still caught a bar later, when the
        // last 4 beats as a whole differ from the 8 before
        if (!boundary && ready && barStart && n >= 20 && n - 4 - _lastBoundaryBeat >= 16)
        {
            float d4 = 1f - Cosine(Mean(_beats, n - 4, 4), Mean(_beats, n - 12, 8));
            if (d4 > MathF.Max(0.035f, _boundaryMean * 1.5f + 3f * std))
            {
                boundary = true;
                backup = true;
                start = n - 4;
                beatIndex -= 3;
            }
        }
        // learn what "normal" beat-to-beat change looks like for this song (after warm-up, with
        // outliers capped so one odd beat does not raise the bar for the real boundaries)
        if (!boundary && n >= 18)
        {
            float dc = MathF.Min(d1, _boundaryMean + 0.05f);
            _boundaryMean = M.Lerp(_boundaryMean, dc, 0.08f);
            _boundaryVar = M.Lerp(_boundaryVar, (dc - _boundaryMean) * (dc - _boundaryMean), 0.08f);
        }
        if (!boundary) return;

        // provisional call from the section's first beat(s)
        f = _beats[start];
        _provisionalPrevLabel = _sectionLabel;
        _provisionalPrevBoundary = _lastBoundaryBeat;
        _provisionalDist = MathF.Max(d1, 0.03f);
        _sections[^1] = (_sections[^1].Start, start, _sections[^1].Label);
        // a backup catch already has four beats of evidence: label it properly, nothing to confirm
        int label = backup ? BestMatch(Mean(_beats, start, 4), 4, out float sim) : BestMatch(f, 1, out sim);
        bool repeat = label >= 0 && sim > (backup ? RepeatSimilarity : RepeatSimilarityBeat) && label != _sectionLabel;
        _provisionalWasNew = !repeat;
        if (!repeat) label = _labelCount++;
        _sections.Add((start, -1, label));
        _lastBoundaryBeat = start;
        _sectionLabel = label;
        _section = _sections.Count - 1;
        _confirmAt = backup ? -1 : start + 4;
        LastRepeatSimilarity = sim;
        LastBoundaryBeatIndex = beatIndex + _kickPhase;
        LastBoundaryTime = start < _beatStarts.Count ? _beatStarts[start] : _clock;
        LastBoundaryFrame = start < _beatStartFrames.Count ? _beatStartFrames[start] : WindowEndFrame;
        state.SectionChanged = true;
        state.SectionRepeat = repeat;
    }

    /// <summary>Best-matching earlier section for a signature of <paramref name="len"/> beats (sliding over every earlier section).</summary>
    private int BestMatch(float[] sig, int len, out float bestSim)
    {
        int label = -1;
        bestSim = 0f;
        for (int i = 0; i < _sections.Count; i++)
        {
            var (start, end, lab) = _sections[i];
            if (end < 0) continue;   // only closed sections
            for (int w = start; w + len <= end; w++)
            {
                float sim = Cosine(sig, len == 1 ? _beats[w] : Mean(_beats, w, len));
                if (sim > bestSim) { bestSim = sim; label = lab; }
            }
        }
        return label;
    }

    private void Confirm(AudioState state)
    {
        _confirmAt = -1;
        int s0 = _sections[^1].Start;
        if (s0 < 8 || s0 + 4 > _beats.Count) return;
        var sig4 = Mean(_beats, s0, 4);
        // were the new section's first four beats all unlike the bars before it? (a one-off beat
        // such as a fill is followed by beats that do have recent twins)
        float d4 = 0f;
        for (int j = 0; j < 4; j++)
        {
            float nearest = 0f;
            for (int w = Math.Max(0, s0 - 16); w < s0; w++) nearest = MathF.Max(nearest, Cosine(_beats[s0 + j], _beats[w]));
            d4 += (1f - nearest) * 0.25f;
        }
        if (d4 < MathF.Max(0.02f, _provisionalDist * 0.35f))
        {
            // a one-off beat, not a new section: take it back
            if (_provisionalWasNew) _labelCount--;
            _sections.RemoveAt(_sections.Count - 1);
            _sections[^1] = (_sections[^1].Start, -1, _sections[^1].Label);
            _sectionLabel = _provisionalPrevLabel;
            _lastBoundaryBeat = _provisionalPrevBoundary;
            _section = _sections.Count - 1;
            state.SectionChanged = true;
            state.SectionRepeat = true;
            state.SectionCorrected = true;
            Corrections++;
            return;
        }
        // re-check the label with four beats of evidence
        var saved = _sections[^1];
        _sections.RemoveAt(_sections.Count - 1);
        int label = BestMatch(sig4, 4, out float sim);
        _sections.Add(saved);
        bool repeat = label >= 0 && sim > RepeatSimilarity && label != _provisionalPrevLabel;
        int current = saved.Label;
        if (repeat && label != current)
        {
            if (_provisionalWasNew) _labelCount--;
            _sections[^1] = (saved.Start, -1, label);
        }
        else if (!repeat && !_provisionalWasNew)
        {
            label = _labelCount++;
            _sections[^1] = (saved.Start, -1, label);
        }
        else return;
        _sectionLabel = label;
        LastRepeatSimilarity = sim;
        state.SectionChanged = true;
        state.SectionRepeat = repeat;
        state.SectionCorrected = true;
        Corrections++;
    }

    /// <summary>How many provisional section calls were corrected (diagnostics).</summary>
    public int Corrections { get; private set; }
    /// <summary>Single-beat similarity needed to call a section start a repeat straight away.</summary>
    public const float RepeatSimilarityBeat = 0.975f;

    /// <summary>Cosine similarity needed to call a new section a repeat of an earlier one.</summary>
    public const float RepeatSimilarity = 0.97f;
    /// <summary>Best match found for the last labelled section (diagnostics).</summary>
    public float LastRepeatSimilarity { get; private set; }
}
