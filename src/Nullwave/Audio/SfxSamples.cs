using NAudio.Wave;

namespace Nullwave.Audio;

/// <summary>One predecoded mono layer; <see cref="RootHz"/> &gt; 0 marks a pitched sample the bank retunes.</summary>
public sealed record SfxSample(float[] Data, float RootHz);

/// <summary>Small predecoded mono layers. All I/O happens before the audio callback starts.</summary>
internal static class SfxSamples
{
    private static readonly Lazy<IReadOnlyDictionary<SfxType, SfxSample>> Cache = new(Load);
    public static IReadOnlyDictionary<SfxType, SfxSample> All => Cache.Value;

    /// <summary>Measured roots of pitched layers, from manifest.json (written by Assets/Source/fal/prepare_sfx.py).</summary>
    private static Dictionary<string, float> Roots(string folder)
    {
        var roots = new Dictionary<string, float>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
            foreach (var e in doc.RootElement.GetProperty("effects").EnumerateArray())
                if (e.TryGetProperty("rootHz", out var hz) && e.TryGetProperty("effect", out var name))
                    roots[name.GetString() ?? ""] = (float)hz.GetDouble();
        }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // without a manifest every layer plays at its recorded pitch
        }
        return roots;
    }

    private static IReadOnlyDictionary<SfxType, SfxSample> Load()
    {
        var sounds = new Dictionary<SfxType, SfxSample>();
        string folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Audio");
        var roots = Roots(folder);
        foreach (var type in Enum.GetValues<SfxType>())
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
                sounds[type] = new SfxSample(samples, roots.GetValueOrDefault(type.ToString().ToLowerInvariant()));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
            {
                // An absent or damaged optional layer leaves the procedural voice intact.
            }
        }
        return sounds;
    }
}
