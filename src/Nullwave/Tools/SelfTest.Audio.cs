using Nullwave.Audio;

namespace Nullwave.Tools;

public static partial class SelfTest
{
    private static void TestElectronicAudio()
    {
        static float[] Render(SoundBank bank, int length = 8192, int chunk = 512)
        {
            var samples = new float[length];
            for (int offset = 0; offset < length; offset += chunk)
                bank.Mix(samples, offset, Math.Min(chunk, length - offset));
            return samples;
        }
        static SoundBank Tuned(int root, bool minor, SfxType type, bool enabled = true)
        {
            var bank = new SoundBank();
            bank.SetHarmony(root, minor, enabled);
            bank.Trigger(type);
            return bank;
        }
        static double Difference(float[] a, float[] b) => a.Zip(b, (x, y) => Math.Abs(x-y)).Average();

        var generated = new SoundBank();
        Check(generated.GeneratedSampleCount == Enum.GetValues<SfxType>().Length, "every effect has a decoded fal layer");
        foreach (var type in Enum.GetValues<SfxType>())
        {
            var withSample = new SoundBank();
            var fallback = new SoundBank(useGeneratedSamples: false);
            withSample.Trigger(type);
            fallback.Trigger(type);
            Check(Difference(Render(withSample), Render(fallback)) > 0.001,
                type + " includes its generated layer and has an audible procedural fallback");
        }

        var c = Render(Tuned(0, true, SfxType.BassShot));
        var g = Render(Tuned(7, true, SfxType.BassShot));
        Check(Difference(c, g) > 0.01, "weapon tuning follows a different chord root");
        var major = Render(Tuned(0, false, SfxType.Burst));
        var minor = Render(Tuned(0, true, SfxType.Burst));
        Check(Difference(major, minor) > 0.01, "cascade chord distinguishes major and minor");
        Check(Difference(Render(Tuned(0, true, SfxType.Burst, false)),
            Render(Tuned(9, false, SfxType.Burst, false))) == 0, "in-key setting off keeps a fixed sound palette");
        var changing = Tuned(0, true, SfxType.Burst);
        changing.SetHarmony(9, false);
        Check(Difference(Render(changing), minor) == 0, "already-triggered voices retain their chord through changes");
        Check(Difference(Render(Tuned(0, true, SfxType.Shot), chunk: 256),
            Render(Tuned(0, true, SfxType.Shot), chunk: 2048)) == 0, "audio is independent of callback chunk size");

        var types = Enum.GetValues<SfxType>();
        foreach (int root in new[] { 0, 6, 11 })
        foreach (bool isMinor in new[] { false, true })
        {
            // Every voice together, over the 48-voice limit, including delayed notes and hard pans.
            var bank = new SoundBank();
            bank.SetHarmony(root, isMinor);
            for (int i = 0; i < 72; i++) bank.Trigger(types[i % types.Length], 2f, i % 2 == 0 ? -1f : 1f);
            bank.PlayNote(96, 2f, 0f, 0.05f);
            var mixed = Render(bank, 48000, 1024);
            Check(mixed.All(float.IsFinite), "dense mix stays finite in every tested key");
            Check(mixed.Max(x => MathF.Abs(x)) <= 0.94001f, "dense effects bus is bounded below full scale");
            Check(mixed.Any(x => MathF.Abs(x) > 0.02f), "voice saturation remains audible");
        }
        foreach (var type in types)
        {
            var tail = Render(Tuned(11, false, type), 48000 * 12);
            Check(tail.All(float.IsFinite), type + " high-key filter remains stable through full tail");
            Check(tail.TakeLast(4800).Max(x => MathF.Abs(x)) < 0.001f, type + " decays to silence");
        }
        var fingerprints = new[] { SfxType.Shot, SfxType.DrumKick, SfxType.DrumSnare, SfxType.DrumHat, SfxType.BassShot, SfxType.LeadShot }
            .Select(type => Render(Tuned(0, true, type))).ToArray();
        for (int i = 0; i < fingerprints.Length; i++)
        for (int j = 0; j < i; j++)
            Check(Difference(fingerprints[i], fingerprints[j]) > 0.01, "each weapon voice has a distinct waveform");
        Console.WriteLine("  electronic audio: chord snapshots, ship voices, bounded dense mixes, stable tails, callback invariance");
    }
}
