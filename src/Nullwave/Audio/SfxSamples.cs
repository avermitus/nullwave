using NAudio.Wave;

namespace Nullwave.Audio;

/// <summary>Small predecoded mono layers. All I/O happens before the audio callback starts.</summary>
internal static class SfxSamples
{
    private static readonly Lazy<IReadOnlyDictionary<SfxType, float[]>> Cache = new(Load);
    public static IReadOnlyDictionary<SfxType, float[]> All => Cache.Value;

    private static IReadOnlyDictionary<SfxType, float[]> Load()
    {
        var sounds = new Dictionary<SfxType, float[]>();
        string folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Audio");
        foreach (var type in new[] { SfxType.Kill, SfxType.Explosion, SfxType.Dash })
        {
            string path = Path.Combine(folder, type.ToString().ToLowerInvariant() + ".wav");
            if (!File.Exists(path)) continue;
            try
            {
                using var reader = new WaveFileReader(path);
                // Import pipeline guarantees mono 48 kHz PCM16, at most two seconds per layer.
                if (reader.WaveFormat.SampleRate != 48000 || reader.WaveFormat.Channels != 1 ||
                    reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm || reader.WaveFormat.BitsPerSample != 16 ||
                    reader.Length < 2 || reader.Length > 48000 * 2 * 2 || reader.Length % 2 != 0) continue;
                var bytes = new byte[(int)reader.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = reader.Read(bytes, read, bytes.Length - read);
                    if (n == 0) break;
                    read += n;
                }
                if (read != bytes.Length) continue;
                var samples = new float[bytes.Length / 2];
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2, 2)) / 32768f;
                sounds[type] = samples;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
            {
                // An absent or damaged optional layer leaves the procedural voice intact.
            }
        }
        return sounds;
    }
}
