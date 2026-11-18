namespace Nullwave.Audio;

/// <summary>
/// Tempo and beat-phase tracker for arbitrary music.
///
/// The old detector flagged every loud transient (hats, snares, vocals) as a "beat", counted
/// every 4th one as a downbeat, and estimated tempo from the median gap between them, so on real
/// songs the grid was noisy and the tempo often wrong. This tracker instead:
///   1. builds an onset-strength envelope at a fixed hop (log-compressed spectral flux, bass
///      weighted), independent of frame rate;
///   2. finds the tempo by autocorrelating ~8 s of that envelope, scoring each period together
///      with its multiples and weighting by a prior around 120 BPM (avoids half/double errors);
///   3. finds the beat phase with a comb filter (the grid offset that lines up the most onsets);
///   4. keeps a continuous, smoothly corrected beat position, so beats are predicted ahead
///      rather than detected after they happen;
///   5. picks the downbeat as the beat-in-bar that consistently carries the most bass.
/// It is a pure function of the samples it is fed, so it is tested offline in the self test.
/// </summary>
public sealed class BeatTracker
{
    public const int Hop = 512;
    private const int Window = 1024;
    private const int EnvLength = 768;           // ~8.2 s at 48 kHz / 512
    private const int AnalyseEvery = 8;          // hops between tempo/phase updates (~85 ms)
    private const float MinBpm = 68f, MaxBpm = 190f;

    private readonly float _rate;
    private readonly Fft _fft = new(Window);
    private readonly float[] _frame = new float[Window];
    private readonly float[] _mag = new float[Window / 2];
    private readonly float[] _prev = new float[Window / 2];
    private readonly float[] _env = new float[EnvLength];
    private readonly float[] _lowEnv = new float[EnvLength];
    private readonly float[] _phaseEnv = new float[EnvLength];
    private readonly float[] _pwork = new float[EnvLength];
    private float _phaseMean;
    private int _phaseVotes;
    private double _phaseCandidate;
    private readonly float[] _work = new float[EnvLength];
    private readonly float[] _acf;
    private readonly float[] _downAcc = new float[4];
    private long _hops;
    private int _sinceAnalyse;
    private int _envCount;
    private float _period;                       // in hops
    private float _candidate;
    private int _candidateVotes;
    private double _position;                    // continuous beat position at the latest hop
    private long _lastBeatInt = long.MinValue;
    private int _downOffset;
    private float _localMean;

    public float Bpm => _period > 0 ? 60f * _rate / Hop / _period : 0f;
    public float Confidence { get; private set; }
    public double Position => _position;
    public bool Locked => _period > 0 && Confidence > 0.12f;
    public int DownbeatOffset => _downOffset;
    public double HopSeconds => Hop / (double)_rate;
    public long Hops => _hops;
    /// <summary>Last onset strength (0..~1) for visual flashes.</summary>
    public float Onset { get; private set; }
    /// <summary>Diagnostics: tempo score per lag and phase score per hop offset from the last analysis.</summary>
    public readonly float[] DebugTempo = new float[EnvLength];
    public readonly float[] DebugPhase = new float[EnvLength];

    public BeatTracker(float sampleRate = 48000f)
    {
        _rate = sampleRate;
        _acf = new float[EnvLength];
    }

    public void Reset()
    {
        Array.Clear(_prev);
        Array.Clear(_env);
        Array.Clear(_lowEnv);
        Array.Clear(_phaseEnv);
        _phaseVotes = 0;
        Array.Clear(_downAcc);
        Array.Clear(_ring);
        _ringPos = 0;
        _samples = 0;
        _sinceHop = 0;
        _lockedFrames = 0;
        _hops = 0;
        _envCount = 0;
        _period = 0;
        _candidate = 0;
        _candidateVotes = 0;
        _position = 0;
        _lastBeatInt = long.MinValue;
        Confidence = 0;
    }

    private readonly float[] _ring = new float[Window];
    private int _ringPos;
    private long _samples;
    private int _sinceHop;

    /// <summary>Feeds mono samples. Returns the number of beat boundaries crossed.</summary>
    public int Push(ReadOnlySpan<float> mono)
    {
        int beats = 0;
        foreach (float v in mono)
        {
            _ring[_ringPos] = v;
            _ringPos = (_ringPos + 1) % Window;
            _samples++;
            if (++_sinceHop < Hop || _samples < Window) continue;
            _sinceHop = 0;
            // unroll the ring (oldest first) into the analysis frame
            int tail = Window - _ringPos;
            Array.Copy(_ring, _ringPos, _frame, 0, tail);
            Array.Copy(_ring, 0, _frame, tail, _ringPos);
            beats += ProcessHop();
        }
        return beats;
    }

    private int ProcessHop()
    {
        _fft.Magnitudes(_frame, _mag);
        float low = 0f, mid = 0f, high = 0f;
        int lowBin = (int)(180f * Window / _rate);
        int midBin = (int)(2500f * Window / _rate);
        for (int b = 1; b < _mag.Length; b++)
        {
            float m = MathF.Log(1f + 400f * _mag[b]);
            float d = m - _prev[b];
            _prev[b] = m;
            if (d <= 0f) continue;
            if (b <= lowBin) low += d;
            else if (b <= midBin) mid += d;
            else high += d;
        }
        // normalise per band so wide bands do not dominate
        low /= lowBin; mid /= (midBin - lowBin); high /= (_mag.Length - midBin);
        float flux = low * 2.5f + mid * 1.2f + high * 0.7f;
        // timing follows kicks and snares; hats mark subdivisions and pull the phase off the beat
        float pflux = low * 2f + mid * 2.5f + high * 0.1f;
        // remove the slow-moving mean so sustained loudness does not look like onsets
        _localMean += (flux - _localMean) * 0.06f;
        _phaseMean += (pflux - _phaseMean) * 0.06f;
        float o = MathF.Max(0f, flux - _localMean);
        int idx = (int)(_hops % EnvLength);
        _env[idx] = o;
        _phaseEnv[idx] = MathF.Max(0f, pflux - _phaseMean);
        _lowEnv[idx] = low;
        _hops++;
        _envCount = Math.Min(_envCount + 1, EnvLength);
        Onset = Math.Clamp(o * 4f, 0f, 1f);

        if (++_sinceAnalyse >= AnalyseEvery && _envCount >= 360)
        {
            _sinceAnalyse = 0;
            Analyse();
        }

        // advance the beat position by one hop
        if (_period > 0)
        {
            _position += 1.0 / _period;
            long n = (long)Math.Floor(_position);
            if (_lastBeatInt == long.MinValue) _lastBeatInt = n;
            if (n > _lastBeatInt)
            {
                int crossed = (int)Math.Min(4, n - _lastBeatInt);
                _lastBeatInt = n;
                // which beat of the bar carries the bass? accumulate low onsets around the beat
                float bass = 0f;
                for (int k = 0; k < 3; k++) bass += _lowEnv[(int)((_hops - 1 - k + EnvLength) % EnvLength)];
                int slot = (int)(((n % 4) + 4) % 4);
                for (int s = 0; s < 4; s++) _downAcc[s] *= 0.97f;
                _downAcc[slot] += bass;
                int best = 0;
                for (int s = 1; s < 4; s++) if (_downAcc[s] > _downAcc[best]) best = s;
                if (_downAcc[best] > _downAcc[_downOffset] * 1.15f) _downOffset = best;
                return crossed;
            }
        }
        return 0;
    }

    private float AcfAt(float lag)
    {
        if (lag < 1f) return 0f;
        int l0 = (int)lag;
        float f = lag - l0;
        return _acf[l0] * (1f - f) + _acf[l0 + 1] * f;
    }

    private float Env(long hop) => _env[(int)(((hop % EnvLength) + EnvLength) % EnvLength)];

    private void Analyse()
    {
        int n = _envCount;
        long newest = _hops - 1;
        // copy chronologically and normalise
        float mean = 0f;
        for (int k = 0; k < n; k++) { _work[k] = Env(newest - n + 1 + k); mean += _work[k]; }
        mean /= n;
        float energy = 0f;
        for (int k = 0; k < n; k++) { _work[k] -= mean; energy += _work[k] * _work[k]; }
        if (energy < 1e-6f) { Confidence *= 0.9f; return; }

        float hopsPerSec = _rate / Hop;
        int minLag = (int)MathF.Floor(hopsPerSec * 60f / MaxBpm);
        int maxLag = (int)MathF.Ceiling(hopsPerSec * 60f / MinBpm);
        int acfMax = Math.Min(n - 1, maxLag * 4 + 2);
        for (int lag = 1; lag <= acfMax; lag++)
        {
            float s = 0f;
            for (int k = lag; k < n; k++) s += _work[k] * _work[k - lag];
            _acf[lag] = s / (energy * (n - lag) / n);
        }

        int bestLag = 0;
        float bestScore = float.MinValue, sum = 0f, sumSq = 0f;
        int count = 0;
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            // A true beat repeats at 2x and 4x (bars) and splits evenly into eighths and sixteenths.
            // The subdivision terms are what reject 3:2 and 4:3 impostors produced by syncopation.
            float score = _acf[lag];
            if (lag * 2 <= acfMax) score += 0.6f * _acf[lag * 2];
            if (lag * 4 <= acfMax) score += 0.6f * _acf[lag * 4];
            score += 0.8f * AcfAt(lag * 0.5f) + 0.4f * AcfAt(lag * 0.25f);
            float bpm = 60f * hopsPerSec / lag;
            float octaves = MathF.Log2(bpm / 120f);
            score *= MathF.Exp(-0.5f * octaves * octaves / (0.55f * 0.55f));
            DebugTempo[lag] = score;
            sum += score; sumSq += score * score; count++;
            if (score > bestScore) { bestScore = score; bestLag = lag; }
        }
        if (bestLag <= minLag || bestLag >= maxLag || bestScore <= 0f) { Confidence *= 0.9f; return; }
        float m0 = sum / count;
        float sd = MathF.Sqrt(MathF.Max(1e-9f, sumSq / count - m0 * m0));
        Confidence = Math.Clamp((bestScore - m0) / (sd * 4f), 0f, 1f);

        // parabolic refinement of the peak
        float y0 = _acf[bestLag - 1], y1 = _acf[bestLag], y2 = _acf[bestLag + 1];
        float denom = y0 - 2f * y1 + y2;
        float period = bestLag + (MathF.Abs(denom) > 1e-6f ? Math.Clamp(0.5f * (y0 - y2) / denom, -0.5f, 0.5f) : 0f);

        // tempo hysteresis: accept a new tempo only if it repeats, smooth small drifts
        if (_period <= 0f)
        {
            _period = period;
        }
        else if (MathF.Abs(period - _period) / _period < 0.04f)
        {
            _period += (period - _period) * 0.25f;
            _candidateVotes = 0;
        }
        else if (MathF.Abs(period - _candidate) / MathF.Max(1f, _candidate) < 0.04f)
        {
            if (++_candidateVotes >= 3) { _period = period; _candidateVotes = 0; }
        }
        else
        {
            _candidate = period;
            _candidateVotes = 1;
        }

        // phase: comb filter over the recent past at the current period, on the kick/snare envelope
        float pmean = 0f;
        for (int k = 0; k < n; k++)
        {
            _pwork[k] = _phaseEnv[(int)(((newest - n + 1 + k) % EnvLength + EnvLength) % EnvLength)];
            pmean += _pwork[k];
        }
        pmean /= n;
        for (int k = 0; k < n; k++) _pwork[k] -= pmean;
        float P = _period;
        int steps = (int)MathF.Ceiling(P);
        float bestPhaseScore = float.MinValue;
        float bestPhase = 0f;
        for (int phi = 0; phi < steps; phi++)
        {
            float s = 0f, w = 1f;
            for (int k = 0; k < 14; k++)
            {
                float back = phi + k * P;
                if (back >= n - 2) break;
                int b0 = (int)back;
                float f = back - b0;
                float v = _pwork[n - 1 - b0] * (1f - f) + _pwork[n - 2 - b0] * f;
                s += v * w;
                w *= 0.93f;
            }
            DebugPhase[phi] = s;
            if (s > bestPhaseScore) { bestPhaseScore = s; bestPhase = phi; }
        }
        // the newest hop sits bestPhase hops after a beat => fractional beat position now
        double measured = bestPhase / P;
        double current = _position - Math.Floor(_position);
        double err = measured - current;
        if (err > 0.5) err -= 1.0;
        if (err < -0.5) err += 1.0;
        // pull toward the measured phase; a fresh lock snaps, an established one glides, and a
        // big jump (e.g. half a beat) is only taken if several measurements agree on it
        // score of the phase we are currently tracking, for the flip margin
        float curPhi = (float)(current * P);
        int c0 = (int)curPhi % steps;
        float cf = curPhi - (int)curPhi;
        float currentScore = DebugPhase[c0] * (1f - cf) + DebugPhase[(c0 + 1) % steps] * cf;
        if (_lockedFrames < 12)
        {
            _position += err * 0.8;
        }
        else if (Math.Abs(err) < 0.16)
        {
            _position += err * 0.3;
            _phaseVotes = 0;
        }
        else if (bestPhaseScore > currentScore * 1.12f + 1e-4f)
        {
            double diff = Math.Abs(measured - _phaseCandidate);
            diff = Math.Min(diff, 1.0 - diff);
            if (_phaseVotes > 0 && diff < 0.18) { _phaseVotes++; _phaseCandidate = measured; }
            else { _phaseCandidate = measured; _phaseVotes = 1; }
            if (_phaseVotes >= 10) { _position += err; _phaseVotes = 0; }
        }
        else
        {
            _phaseVotes = 0;
        }
        _lockedFrames++;
    }

    private int _lockedFrames;
}
