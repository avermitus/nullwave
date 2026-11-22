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
    /// <summary>Four ship phrases, then dash, kills, hazards and a cascade in C minor / A minor.</summary>
    public static void RenderShowcase(string path)
    {
        var bank = new SoundBank();
        bank.SetHarmony(0, true);
        const int framesPerStep = 6000; // sixteenths at 120 BPM
        var samples = new float[framesPerStep * 2];
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        for (int step = 0; step < 144; step++)
        {
            if (step == 64) bank.SetHarmony(9, true);
            float pan = MathF.Sin(step * 0.7f) * 0.4f;
            if (step < 16 && step % 2 == 0) bank.Trigger(SfxType.Shot, 0.4f, pan);
            else if (step >= 16 && step < 32)
            {
                if (step % 4 == 0) bank.Trigger(SfxType.DrumKick, 0.45f);
                if (step % 8 == 4) bank.Trigger(SfxType.DrumSnare, 0.45f);
                if (step % 2 == 1) bank.Trigger(SfxType.DrumHat, 0.3f, pan);
            }
            else if (step >= 32 && step < 48 && step % 4 == 0) bank.Trigger(SfxType.BassShot, 0.7f, pan);
            else if (step >= 48 && step < 64) bank.Trigger(SfxType.LeadShot, 0.25f, pan);
            else if (step == 64) bank.Trigger(SfxType.Dash, 0.6f, -0.4f);
            else if (step == 72 || step == 74 || step == 76) bank.Trigger(SfxType.Kill, 0.5f, pan);
            else if (step == 80) bank.Trigger(SfxType.Pickup, 0.6f);
            else if (step == 88) bank.Trigger(SfxType.LaserWarn, 0.6f);
            else if (step == 93) bank.Trigger(SfxType.LaserFire, 0.6f);
            else if (step == 100) bank.Trigger(SfxType.Hurt, 0.5f);
            else if (step == 108) bank.Trigger(SfxType.Boss, 0.6f);
            else if (step == 124) bank.Trigger(SfxType.Burst, 0.7f);
            Array.Clear(samples);
            bank.Mix(samples, 0, samples.Length);
            writer.WriteSamples(samples, 0, samples.Length);
        }
    }
}
