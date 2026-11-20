using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Nullwave.Audio;

/// <summary>
/// Zero-dependency local audio file player built on NAudio's <see cref="AudioFileReader"/>.
/// Normalizes any supported format (.wav, .mp3, .m4a, .aiff, etc.) to 48 kHz stereo float and loops seamlessly.
/// </summary>
public sealed class LocalTrackSource : ISampleProvider, IDisposable
{
    public const int TargetSampleRate = LookaheadAudioBuffer.SampleRate;

    private readonly AudioFileReader _reader;
    private readonly ISampleProvider _chain;
    private readonly WaveFormat _format = WaveFormat.CreateIeeeFloatWaveFormat(TargetSampleRate, 2);
    private bool _disposed;

    public string FilePath { get; }
    public string TrackName { get; }
    /// <summary>How many times the track has wrapped around (each loop is a new "track" for setlists).</summary>
    public volatile int LoopCount;
    public WaveFormat WaveFormat => _format;

    public LocalTrackSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Track path is empty.", nameof(path));
        string full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new FileNotFoundException("Local audio file not found.", full);

        FilePath = full;
        string rawName = Path.GetFileNameWithoutExtension(full);
        TrackName = string.IsNullOrWhiteSpace(rawName) ? "LOCAL TRACK" : rawName.Replace('_', ' ').Trim();

        _reader = new AudioFileReader(full);
        ISampleProvider provider = _reader;
        if (provider.WaveFormat.Channels == 1)
            provider = new MonoToStereoSampleProvider(provider);
        else if (provider.WaveFormat.Channels > 2)
            provider = new StereoDownmixProvider(provider);

        if (provider.WaveFormat.SampleRate != TargetSampleRate)
            provider = new WdlResamplingSampleProvider(provider, TargetSampleRate);

        _chain = provider;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_disposed || count <= 0) return 0;

        int filled = 0;
        int loops = 0;
        while (filled < count && loops <= 2)
        {
            int read = _chain.Read(buffer, offset + filled, count - filled);
            if (read > 0)
            {
                filled += read;
                continue;
            }

            // Seamless loop back to start of track; bounded to 2 resets so empty files yield silence.
            if (_reader.Length <= 0) break;
            _reader.Position = 0;
            loops++;
            LoopCount++;
        }

        if (filled < count)
            Array.Clear(buffer, offset + filled, count - filled);

        return count;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _reader.Dispose(); } catch { /* best-effort close */ }
    }

    /// <summary>Downmixes >2-channel sources to interleaved stereo without per-call heap allocations.</summary>
    private sealed class StereoDownmixProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _srcChannels;
        private float[] _scratch = new float[4096];

        public WaveFormat WaveFormat { get; }

        public StereoDownmixProvider(ISampleProvider source)
        {
            _source = source;
            _srcChannels = source.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / 2;
            int needed = frames * _srcChannels;
            if (_scratch.Length < needed) _scratch = new float[needed];
            int got = _source.Read(_scratch, 0, needed);
            int gotFrames = got / _srcChannels;
            for (int f = 0; f < gotFrames; f++)
            {
                int baseIdx = f * _srcChannels;
                buffer[offset + f * 2] = _scratch[baseIdx];
                buffer[offset + f * 2 + 1] = _scratch[baseIdx + 1];
            }
            return gotFrames * 2;
        }
    }
}
