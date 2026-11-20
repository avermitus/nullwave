using Nullwave.Core;

namespace Nullwave.Audio;

public sealed class BeatDetector
{
    private readonly float[] _history = new float[56];
    private int _index;
    private int _count;
    private float _lastBeat = -99f;
    private readonly float _k;
    private readonly float _minGap;

    /// <param name="k">threshold in standard deviations above the recent mean</param>
    /// <param name="minGap">refractory time between hits, seconds</param>
    public BeatDetector(float k = 1.35f, float minGap = 0.115f)
    {
        _k = k;
        _minGap = minGap;
    }

    public void Reset()
    {
        Array.Clear(_history);
        _index = 0;
        _count = 0;
        _lastBeat = -99f;
    }

    public bool Push(float onset, float time, out float strength, out float threshold)
    {
        float mean = 0;
        int n = Math.Max(1, _count);
        for (int i = 0; i < n; i++) mean += _history[i];
        mean /= n;
        float variance = 0;
        for (int i = 0; i < n; i++)
        {
            float d = _history[i] - mean;
            variance += d * d;
        }
        variance /= n;
        float std = MathF.Sqrt(variance);
        threshold = mean + std * _k + 0.0012f;
        strength = threshold <= 0 ? 0 : onset / threshold;
        bool beat = onset > threshold && onset > 0.0025f && time - _lastBeat > _minGap;
        if (beat) _lastBeat = time;
        _history[_index] = onset;
        _index = (_index + 1) % _history.Length;
        _count = Math.Min(_count + 1, _history.Length);
        return beat;
    }
}

/// <summary>
/// Turns a window of audio into the reactive values the game uses. Every band is measured in dB
/// and normalised against its own recent range (a fast-attack ceiling and a slow-rising floor),
/// so loud, compressed or clipped masters still swing the full 0..1 range instead of pinning at
/// the top, and quiet sources are lifted instead of flatlining. Kick, snare and hi-hat onsets are
/// detected separately from log-spectral flux in their own bands.
/// </summary>
public sealed class SpectrumAnalyzer
{
    public const int FftSize = 2048;
    private const int Bands = 6;
    private readonly Fft _fft = new(FftSize);
    private readonly float[] _magnitudes = new float[FftSize / 2];
    private readonly float[] _prevLog = new float[FftSize / 2];
    private readonly float[] _smooth = new float[Bands];
    private readonly float[] _ceil = new float[Bands];
    private readonly float[] _floor = new float[Bands];
    private readonly float[] _spectrumSmooth = new float[AudioState.SpectrumBars];
    private readonly BeatDetector _beats = new();
    private readonly BeatDetector _kick = new(2.0f, 0.2f);
    private readonly BeatDetector _snare = new(2.2f, 0.18f);
    private readonly BeatDetector _hat = new(1.8f, 0.08f);
    private float _specCeil = -20f, _specFloor = -90f;
    private float _fluxCeil = 0.5f;
    private bool _primed;

    /// <summary>Magnitude spectrum of the last analysed window (2048-point FFT).</summary>
    public ReadOnlySpan<float> Magnitudes => _magnitudes;

    public void Reset()
    {
        Array.Clear(_prevLog);
        Array.Clear(_smooth);
        Array.Clear(_spectrumSmooth);
        _beats.Reset(); _kick.Reset(); _snare.Reset(); _hat.Reset();
        _primed = false;
    }

    private static float Db(float v) => 20f * MathF.Log10(MathF.Max(v, 1e-7f));

    /// <summary>Normalises a dB value against the band's adaptive range (at least 6 dB wide).</summary>
    private float Adapt(int i, float db, float dt)
    {
        if (!_primed) { _ceil[i] = db + 3f; _floor[i] = db - 12f; }
        _ceil[i] = db > _ceil[i] ? M.Lerp(_ceil[i], db, 1f - MathF.Exp(-dt * 30f)) : _ceil[i] - dt * 2.5f;
        _floor[i] = db < _floor[i] ? M.Lerp(_floor[i], db, 1f - MathF.Exp(-dt * 8f)) : _floor[i] + dt * 1.2f;
        if (_ceil[i] - _floor[i] < 6f) _floor[i] = _ceil[i] - 6f;
        return M.Clamp01((db - _floor[i]) / (_ceil[i] - _floor[i]));
    }

    public bool Analyze(float[] mono, int count, float sampleRate, float dt, AudioState state)
    {
        if (count < FftSize) return false;
        int start = count - FftSize;
        double rmsSum = 0;
        for (int i = 0; i < FftSize; i++)
        {
            float s = mono[start + i];
            rmsSum += s * s;
        }
        float rms = MathF.Sqrt((float)(rmsSum / FftSize));
        _fft.Magnitudes(mono.AsSpan(start, FftSize), _magnitudes);
        int bins = FftSize / 2;
        float binHz = sampleRate / FftSize;

        // log-spectral flux, overall and per instrument band: scale invariant, so gain and clipping do not matter
        float flux = 0f, kickFlux = 0f, snareFlux = 0f, hatFlux = 0f;
        int kick0 = (int)(40f / binHz), kick1 = (int)(130f / binHz);
        int sn0 = (int)(180f / binHz), sn1 = (int)(2500f / binHz);
        int hat0 = (int)(6000f / binHz), hat1 = Math.Min(bins - 1, (int)(16000f / binHz));
        double logSum = 0, linSum = 0;
        int flatCount = 0;
        for (int i = 1; i < bins; i++)
        {
            float m = _magnitudes[i];
            float lm = MathF.Log(1f + 1000f * m);
            float d = lm - _prevLog[i];
            _prevLog[i] = lm;
            if (d > 0f)
            {
                flux += d;
                if (i >= kick0 && i <= kick1) kickFlux += d;
                else if (i >= sn0 && i <= sn1) snareFlux += d;
                else if (i >= hat0 && i <= hat1) hatFlux += d;
            }
            if (i >= sn0 && i <= hat1) { logSum += Math.Log(m + 1e-9); linSum += m; flatCount++; }
        }
        flux /= bins;
        kickFlux /= Math.Max(1, kick1 - kick0 + 1);
        snareFlux /= Math.Max(1, sn1 - sn0 + 1);
        hatFlux /= Math.Max(1, hat1 - hat0 + 1);
        float flatness = flatCount > 0 && linSum > 1e-9 ? (float)(Math.Exp(logSum / flatCount) / (linSum / flatCount)) : 0f;

        float t = state.Time;
        state.KickHit = _kick.Push(kickFlux, t, out _, out _);
        state.SnareHit = _snare.Push(snareFlux, t, out _, out _);
        state.HatHit = _hat.Push(hatFlux, t, out _, out _);
        state.KickPulse = MathF.Max(state.KickPulse * MathF.Exp(-dt * 9f), state.KickHit ? 1f : 0f);
        state.SnarePulse = MathF.Max(state.SnarePulse * MathF.Exp(-dt * 8f), state.SnareHit ? 1f : 0f);
        state.HatPulse = MathF.Max(state.HatPulse * MathF.Exp(-dt * 14f), state.HatHit ? 1f : 0f);
        state.Flatness = M.ExpSmooth(state.Flatness, M.Clamp01(flatness * 2.2f), 6f, dt);

        // band levels in dB, each normalised to its own recent range
        float bass = Adapt(0, Db(BandEnergy(30, 150, sampleRate)), dt);
        float lowMid = Adapt(1, Db(BandEnergy(150, 420, sampleRate)), dt);
        float mid = Adapt(2, Db(BandEnergy(420, 1600, sampleRate)), dt);
        float high = Adapt(3, Db(BandEnergy(1600, 5200, sampleRate)), dt);
        float air = Adapt(4, Db(BandEnergy(5200, 15000, sampleRate)), dt);
        float loud = Adapt(5, Db(rms), dt);
        // the flux itself, against its own recent peak
        _fluxCeil = MathF.Max(flux, _fluxCeil * MathF.Exp(-dt * 0.4f));
        float fluxNorm = M.Clamp01(flux / MathF.Max(1e-4f, _fluxCeil));

        Smooth(ref _smooth[0], bass, dt, 26f, 6f);
        Smooth(ref _smooth[1], lowMid, dt, 24f, 5f);
        Smooth(ref _smooth[2], mid, dt, 22f, 5f);
        Smooth(ref _smooth[3], high, dt, 22f, 6f);
        Smooth(ref _smooth[4], air, dt, 20f, 6f);
        Smooth(ref _smooth[5], loud, dt, 20f, 4f);

        float onset = flux * 4f;
        bool beat = _beats.Push(onset, state.Time, out float beatStrength, out _);
        // detected hits punch through their band, so a brick-walled low end still kicks
        state.Bass = M.Clamp01(_smooth[0] * 0.75f + state.KickPulse * 0.45f);
        state.LowMid = _smooth[1];
        state.Mid = M.Clamp01(_smooth[2] * 0.8f + state.SnarePulse * 0.3f);
        state.High = M.Clamp01(_smooth[3] * 0.8f + state.HatPulse * 0.25f);
        state.Air = _smooth[4];
        state.Rms = _smooth[5];
        state.Loudness = rms;
        state.Flux = fluxNorm;
        state.Energy = M.Clamp01(_smooth[5] * 0.55f + _smooth[0] * 0.2f + fluxNorm * 0.25f + state.KickPulse * 0.1f);
        state.Beat = beat;
        state.BeatPulse = MathF.Max(state.BeatPulse * MathF.Exp(-dt * 4.5f), beat ? 1f : 0f);
        if (beat)
        {
            state.BeatCount++;
            if (state.BeatCount % 4 == 0)
            {
                state.Downbeat = true;
                state.BarCount++;
                state.DownPulse = 1f;
            }
            state.Confidence = Math.Clamp(beatStrength / 4f, 0.15f, 1f);
        }
        else
        {
            state.DownPulse = MathF.Max(state.DownPulse * MathF.Exp(-dt * 6f), 0f);
        }

        // spectrum bars in dB against a shared adaptive range, so the shape survives any gain
        float frameMax = -200f;
        for (int i = 0; i < AudioState.SpectrumBars; i++)
        {
            float f = 35f * MathF.Pow(16000f / 35f, i / (float)(AudioState.SpectrumBars - 1));
            float next = 35f * MathF.Pow(16000f / 35f, (i + 1) / (float)(AudioState.SpectrumBars - 1));
            int b0 = Math.Clamp((int)(f / binHz), 1, bins - 1);
            int b1 = Math.Clamp((int)(next / binHz), b0 + 1, bins);
            float sum = 0;
            for (int b = b0; b < b1; b++) sum += _magnitudes[b];
            float db = Db(sum / Math.Max(1, b1 - b0));
            frameMax = MathF.Max(frameMax, db);
            float v = M.Clamp01((db - _specFloor) / MathF.Max(20f, _specCeil - _specFloor));
            _spectrumSmooth[i] = M.ExpSmooth(_spectrumSmooth[i], v, v > _spectrumSmooth[i] ? 24f : 6f, dt);
            state.Spectrum[i] = _spectrumSmooth[i];
        }
        if (!_primed) { _specCeil = frameMax; _specFloor = frameMax - 50f; }
        _specCeil = frameMax > _specCeil ? frameMax : _specCeil - dt * 2f;
        _specFloor = _specCeil - 48f;
        _primed = true;

        state.CopyBands();
        return true;
    }

    private float BandEnergy(float lo, float hi, float sampleRate)
    {
        int n = FftSize;
        int b0 = Math.Clamp((int)(lo * n / sampleRate), 1, n / 2 - 1);
        int b1 = Math.Clamp((int)(hi * n / sampleRate), b0 + 1, n / 2);
        float sum = 0;
        for (int b = b0; b < b1; b++) sum += _magnitudes[b];
        return sum / Math.Max(1, b1 - b0);
    }

    private static void Smooth(ref float current, float target, float dt, float attack, float release)
    {
        current = M.ExpSmooth(current, target, target > current ? attack : release, dt);
    }
}
