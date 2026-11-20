using NAudio.Wave;

namespace Nullwave.Audio;

/// <summary>
/// Sample-accurate 48 kHz stereo lookahead ring buffer. Delays music playback by ~0.75 s
/// (36,000 stereo frames) while keeping game SFX mixed post-delay at zero latency, enabling
/// exact-frame FFT/beat alignment and rising pre-drop anticipation before drops hit the speakers.
/// </summary>
public sealed class LookaheadAudioBuffer : ISampleProvider
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const float DefaultLookaheadSeconds = 0.75f;
    private const int LookbackHeadroomFrames = 16384;

    private readonly WaveFormat _format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
    private readonly int _lookaheadFrames;
    private readonly int _ringFrames;
    private readonly float[] _stereoRing;
    private readonly float[] _monoRing;
    private float[] _pullBuf = new float[8192];
    private readonly object _swapLock = new();
    private ISampleProvider _source;

    private int _playCursor;
    private int _writeCursor;
    private long _totalPlayedFrames;
    private long _totalWrittenFrames;

    public LookaheadAudioBuffer(ISampleProvider source, float lookaheadSeconds = DefaultLookaheadSeconds)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _lookaheadFrames = Math.Clamp((int)MathF.Round(lookaheadSeconds * SampleRate), 1024, SampleRate * 2);
        _ringFrames = _lookaheadFrames + LookbackHeadroomFrames;
        _stereoRing = new float[_ringFrames * Channels];
        _monoRing = new float[_ringFrames];
        ResetCursorsLocked();
    }

    public WaveFormat WaveFormat => _format;
    public int LookaheadFrames => _lookaheadFrames;
    public float LookaheadSeconds => _lookaheadFrames / (float)SampleRate;
    public long PlayedFrames => Volatile.Read(ref _totalPlayedFrames);
    /// <summary>Total frames written into the buffer (the newest buffered frame's index).</summary>
    public long WrittenFrames => Volatile.Read(ref _totalWrittenFrames);

    /// <summary>Atomically swaps the upstream stereo 48 kHz source and clears stale buffered audio.</summary>
    public void SetSource(ISampleProvider source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_swapLock)
        {
            _source = source;
            ResetCursorsLocked();
        }
    }

    private void ResetCursorsLocked()
    {
        Array.Clear(_stereoRing);
        Array.Clear(_monoRing);
        _playCursor = 0;
        _writeCursor = _lookaheadFrames % _ringFrames;
        Volatile.Write(ref _totalPlayedFrames, 0);
        Volatile.Write(ref _totalWrittenFrames, 0);
    }

    /// <summary>
    /// Audio-thread pull: outputs delayed stereo audio from the playback cursor while pulling fresh
    /// frames from upstream into the write cursor (exact <see cref="LookaheadFrames"/> delay).
    /// </summary>
    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / Channels;
        if (frames <= 0) return 0;
        int samples = frames * Channels;

        lock (_swapLock)
        {
            if (_pullBuf.Length < samples)
                _pullBuf = new float[samples];

            int pulled = _source.Read(_pullBuf, 0, samples);
            if (pulled < samples)
                Array.Clear(_pullBuf, pulled, samples - pulled);

            int play = _playCursor;
            int write = _writeCursor;
            int ring = _ringFrames;
            var stereo = _stereoRing;
            var mono = _monoRing;
            var src = _pullBuf;

            for (int f = 0; f < frames; f++)
            {
                int outIdx = offset + f * 2;
                int pIdx = play * 2;
                buffer[outIdx] = stereo[pIdx];
                buffer[outIdx + 1] = stereo[pIdx + 1];

                float l = src[f * 2];
                float r = src[f * 2 + 1];
                int wIdx = write * 2;
                stereo[wIdx] = l;
                stereo[wIdx + 1] = r;
                mono[write] = (l + r) * 0.5f;

                if (++play >= ring) play = 0;
                if (++write >= ring) write = 0;
            }

            Volatile.Write(ref _playCursor, play);
            Volatile.Write(ref _writeCursor, write);
            Volatile.Write(ref _totalPlayedFrames, _totalPlayedFrames + frames);
            Volatile.Write(ref _totalWrittenFrames, _totalWrittenFrames + frames);
        }

        return samples;
    }

    /// <summary>
    /// Copies a playback-aligned mono FFT window centered slightly ahead of the speaker cursor
    /// (default +1024 frames = half a 2048-sample FFT) so transients snap on the exact frame heard.
    /// </summary>
    public bool CopyPlaybackWindow(Span<float> monoOut, int latencyCompensationFrames = 1024)
    {
        if (monoOut.IsEmpty || monoOut.Length > LookbackHeadroomFrames - 512) return false;
        long played = Volatile.Read(ref _totalPlayedFrames);
        if (played <= 0) return false;

        int comp = Math.Clamp(latencyCompensationFrames, 0, _lookaheadFrames - 256);
        int play = Volatile.Read(ref _playCursor);
        int ring = _ringFrames;
        int end = play + comp;
        if (end >= ring) end -= ring;
        int start = end - monoOut.Length;
        if (start < 0) start += ring;

        var mono = _monoRing;
        for (int i = 0; i < monoOut.Length; i++)
        {
            int idx = start + i;
            if (idx >= ring) idx -= ring;
            monoOut[i] = Volatile.Read(ref mono[idx]);
        }
        return true;
    }

    /// <summary>
    /// Copies the newest buffered mono samples (ending at the write cursor), i.e. the audio the
    /// speakers will play <see cref="LookaheadSeconds"/> from now.
    /// </summary>
    public bool CopyAheadWindow(Span<float> monoOut)
    {
        if (monoOut.IsEmpty || monoOut.Length > _ringFrames - 512) return false;
        if (Volatile.Read(ref _totalWrittenFrames) < monoOut.Length) return false;
        int ring = _ringFrames;
        int start = Volatile.Read(ref _writeCursor) - monoOut.Length;
        if (start < 0) start += ring;
        var mono = _monoRing;
        for (int i = 0; i < monoOut.Length; i++)
        {
            int idx = start + i;
            if (idx >= ring) idx -= ring;
            monoOut[i] = Volatile.Read(ref mono[idx]);
        }
        return true;
    }

    /// <summary>
    /// Copies monotonic newly played mono samples since <paramref name="lastFrame"/> for beat tracking.
    /// </summary>
    public int CopyNewPlaybackMono(ref long lastFrame, Span<float> monoOut)
    {
        long played = Volatile.Read(ref _totalPlayedFrames);
        if (played <= 0 || monoOut.IsEmpty) return 0;
        if (lastFrame < 0 || lastFrame > played)
        {
            lastFrame = played;
            return 0;
        }
        long delta = played - lastFrame;
        if (delta <= 0) return 0;

        int maxHistory = LookbackHeadroomFrames - 1024;
        if (delta > maxHistory)
        {
            lastFrame = played - Math.Min(monoOut.Length, maxHistory);
            delta = played - lastFrame;
        }

        int n = (int)Math.Min(delta, monoOut.Length);
        int ring = _ringFrames;
        int start = (int)(lastFrame % ring);
        var mono = _monoRing;
        for (int i = 0; i < n; i++)
        {
            int idx = start + i;
            if (idx >= ring) idx -= ring;
            monoOut[i] = Volatile.Read(ref mono[idx]);
        }
        lastFrame += n;
        return n;
    }

    /// <summary>
    /// Scans the future lookahead window ahead of playback in 6 buckets (~125 ms each) and returns
    /// a 0..1 tension ramp rising as a significant energy jump (>= 4.5 dB) approaches the speakers.
    /// </summary>
    public float ComputeDropAnticipation(out float futurePeakRms, out float currentRms)
    {
        futurePeakRms = 0f;
        currentRms = 0f;
        long written = Volatile.Read(ref _totalWrittenFrames);
        if (written < 2048) return 0f;

        int play = Volatile.Read(ref _playCursor);
        int ring = _ringFrames;
        var mono = _monoRing;

        // Current playback RMS over ~250 ms ending at the speaker cursor (strided by 4 for speed)
        const int currentFrames = 12000;
        const int stride = 4;
        int curStart = play - currentFrames;
        if (curStart < 0) curStart += ring;
        double curSumSq = 0;
        int curSamples = currentFrames / stride;
        for (int i = 0; i < currentFrames; i += stride)
        {
            int idx = curStart + i;
            if (idx >= ring) idx -= ring;
            float s = Volatile.Read(ref mono[idx]);
            curSumSq += s * s;
        }
        currentRms = MathF.Sqrt((float)(curSumSq / Math.Max(1, curSamples)));

        const int buckets = 6;
        int bucketFrames = Math.Max(stride, _lookaheadFrames / buckets);
        int bucketSamples = bucketFrames / stride;
        float best = 0f;

        for (int b = 0; b < buckets; b++)
        {
            int bStart = play + b * bucketFrames;
            if (bStart >= ring) bStart -= ring;
            double sumSq = 0;
            for (int i = 0; i < bucketFrames; i += stride)
            {
                int idx = bStart + i;
                if (idx >= ring) idx -= ring;
                float s = Volatile.Read(ref mono[idx]);
                sumSq += s * s;
            }
            float rms = MathF.Sqrt((float)(sumSq / Math.Max(1, bucketSamples)));
            if (rms > futurePeakRms) futurePeakRms = rms;

            if (rms <= 0.04f) continue;
            float dbJump = 20f * MathF.Log10((rms + 1e-5f) / (currentRms + 1e-5f));
            if (dbJump < 4.5f) continue;

            float strength = Math.Clamp((dbJump - 4.5f) / 9.0f, 0.25f, 1f);
            float proximity = 1f - b * 0.13f; // 1.0 right before playback, 0.35 at ~0.75 s ahead
            float candidate = Math.Clamp(strength * proximity, 0f, 1f);
            if (candidate > best) best = candidate;
        }

        return best;
    }
}
