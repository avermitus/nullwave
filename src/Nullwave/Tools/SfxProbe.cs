using NAudio.Wave;
using Nullwave.Audio;

namespace Nullwave.Tools;

/// <summary>Renders one sound effect offline (optionally to a WAV) and measures it.</summary>
public static class SfxProbe
{
    public static (float peak, float rms, bool finite) Render(SfxType type, string? wavPath = null)
    {
        var bank = new SoundBank();
        bank.Trigger(type, 1f, 0f);
        const int chunk = 1024;
        var buf = new float[chunk];
        var all = new List<float>();
        for (int n = 0; n < 48000 * 2 * 2 / chunk; n++)
        {
            Array.Clear(buf);
            bank.Mix(buf, 0, chunk);
            all.AddRange(buf);
        }
        float peak = 0f; double sum = 0; bool finite = true;
        foreach (float x in all) { if (!float.IsFinite(x)) finite = false; peak = MathF.Max(peak, MathF.Abs(x)); sum += x * x; }
        if (wavPath != null)
        {
            using var w = new WaveFileWriter(wavPath, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
            w.WriteSamples(all.ToArray(), 0, all.Count);
        }
        return (peak, (float)Math.Sqrt(sum / all.Count), finite);
    }
}
