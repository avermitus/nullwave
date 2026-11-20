using NAudio.Wave;

namespace Nullwave.Audio;

/// <summary>
/// FIFO between a live capture (e.g. Spotify on a silent output) and the game's output, feeding
/// the 0.75 s lookahead buffer. Keeps a small jitter cushion, re-primes after an underrun and
/// drops the excess if the capture clock runs ahead of the output clock.
/// </summary>
public sealed class LiveStreamSource : ISampleProvider
{
    private const int Rate = 48000;
    private const int RingFrames = Rate * 2;
    private const int CushionFrames = Rate * 6 / 100;   // 60 ms
    private const int MaxFillFrames = Rate / 4;          // 250 ms
    private readonly float[] _ring = new float[RingFrames * 2];
    private readonly object _gate = new();
    private int _read, _write, _fill;
    private bool _primed;

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);
    public int Underruns { get; private set; }
    public long FramesWritten { get; private set; }
    public int Fill { get { lock (_gate) return _fill; } }

    /// <summary>Capture thread: append interleaved stereo frames.</summary>
    public void Write(ReadOnlySpan<float> stereo)
    {
        int frames = stereo.Length / 2;
        lock (_gate)
        {
            for (int f = 0; f < frames; f++)
            {
                _ring[_write * 2] = stereo[f * 2];
                _ring[_write * 2 + 1] = stereo[f * 2 + 1];
                _write = (_write + 1) % RingFrames;
                if (_fill < RingFrames) _fill++;
                else _read = (_read + 1) % RingFrames;
            }
            FramesWritten += frames;
        }
    }

    public void Clear()
    {
        lock (_gate) { _read = _write = _fill = 0; _primed = false; }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / 2;
        lock (_gate)
        {
            if (!_primed && _fill >= CushionFrames) _primed = true;
            if (_primed && _fill > MaxFillFrames)
            {
                int drop = _fill - CushionFrames;
                _read = (_read + drop) % RingFrames;
                _fill -= drop;
            }
            int n = _primed ? Math.Min(frames, _fill) : 0;
            for (int f = 0; f < n; f++)
            {
                buffer[offset + f * 2] = _ring[_read * 2];
                buffer[offset + f * 2 + 1] = _ring[_read * 2 + 1];
                _read = (_read + 1) % RingFrames;
            }
            _fill -= n;
            if (n < frames)
            {
                Array.Clear(buffer, offset + n * 2, (frames - n) * 2);
                if (_primed) { _primed = false; Underruns++; }
            }
        }
        return frames * 2;
    }
}
