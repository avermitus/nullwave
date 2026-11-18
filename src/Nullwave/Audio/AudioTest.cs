using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Nullwave.Audio;

public static class AudioTest
{
    /// <param name="app">Optional app to capture; "self" captures this test's own tone via per-app loopback.</param>
    public static int Run(string? app = null)
    {
        Console.WriteLine("NULLWAVE audio loopback test" + (app != null ? " (app: " + app + ")" : ""));
        var tone = new PulseTone();
        using var output = new WaveOutEvent { DesiredLatency = 80, NumberOfBuffers = 3 };
        output.Init(new SampleToWaveProvider(tone));
        output.Volume = 0.35f;
        using var engine = new AudioEngine(enableOutput: false);
        if (app != null)
        {
            bool ok = app.Equals("self", StringComparison.OrdinalIgnoreCase)
                ? engine.SelectApp(new AudioApp(Environment.ProcessId, "self", "SELF", 0f))
                : engine.SelectApp(app);
            Console.WriteLine($"App capture: {ok} target={engine.CapturedApp?.Display ?? "(none)"} pid={engine.CapturedApp?.ProcessId} error={engine.LastError}");
            if (!ok) return 3;
        }
        Console.WriteLine($"Device: {(string.IsNullOrWhiteSpace(engine.State.DeviceName) ? "(none)" : engine.State.DeviceName)}");
        Console.WriteLine($"Capture: {engine.CaptureAvailable} Output: {engine.OutputAvailable} Error: {engine.LastError}");
        output.Play();
        float elapsed = 0;
        int detectedBeats = 0;
        float peakBass = 0, peakMid = 0, peakHigh = 0, peakRms = 0;
        float previousBeatTime = -10;
        bool heardSource = false;
        while (elapsed < 7.0f)
        {
            Thread.Sleep(12);
            engine.Update(0.016f);
            elapsed += 0.016f;
            if (engine.State.Beat)
            {
                detectedBeats++;
                if (elapsed - previousBeatTime > 0.15f)
                    previousBeatTime = elapsed;
            }
            peakBass = MathF.Max(peakBass, engine.State.Bass);
            peakMid = MathF.Max(peakMid, engine.State.Mid);
            peakHigh = MathF.Max(peakHigh, engine.State.High);
            peakRms = MathF.Max(peakRms, engine.State.Rms);
            heardSource |= engine.State.External;
            if (elapsed > 1.0f && (int)(elapsed * 2) != (int)((elapsed - 0.016f) * 2))
                Console.WriteLine($"  t={elapsed:0.0}s src={engine.State.SourceName} ext={engine.State.External} rms={engine.State.Rms:0.00} bass={engine.State.Bass:0.00} mid={engine.State.Mid:0.00} bpm={engine.State.Bpm:0.0} beats={detectedBeats}");
        }
        Console.WriteLine($"Loop complete at {elapsed:0.0}s; stopping tone.");
        output.Stop();
        Console.WriteLine("Tone stopped.");
        bool external = engine.State.External;
        bool pass = peakRms > 0.02f && (peakBass > 0.03f || peakMid > 0.03f) && detectedBeats >= 3 && (app == null || (engine.State.AppCapture && heardSource));
        Console.WriteLine($"RESULT: {(pass ? "PASS" : "FAIL")} peakRms={peakRms:0.000} bass={peakBass:0.000} mid={peakMid:0.000} high={peakHigh:0.000} beats={detectedBeats} bpm={engine.State.Bpm:0.0} external={external}");
        if (!pass)
            Console.WriteLine("Audio test could not detect the loopback tone. The game still supports its generative score and will report the device state in-game.");
        return pass ? 0 : 2;
    }

    private sealed class PulseTone : ISampleProvider
    {
        private readonly WaveFormat _format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        private double _pos;
        private long _noise = 42;
        public WaveFormat WaveFormat => _format;

        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i += 2)
            {
                double t = _pos / 48000.0;
                double beat = t * 128.0 / 60.0;
                double phase = beat - Math.Floor(beat);
                double age = phase * 60.0 / 128.0;
                float kick = MathF.Exp((float)-age * 16f) * MathF.Sin((float)(age * (55 + 120 * MathF.Exp((float)-age * 40f))) * MathF.Tau) * 0.35f;
                int step = (int)(beat * 4);
                float hat = 0;
                if (step % 2 == 1 && age < 0.06)
                {
                    _noise ^= _noise << 13; _noise ^= _noise >> 7; _noise ^= _noise << 17;
                    hat = (float)((_noise & 0xffff) / 32767.5 - 1.0) * MathF.Exp((float)-age * 70f) * 0.06f;
                }
                float bass = MathF.Sin((float)t * 110f * MathF.Tau) * 0.03f;
                float s = kick + hat + bass;
                buffer[offset + i] = s;
                buffer[offset + i + 1] = s;
                _pos++;
            }
            return count;
        }
    }
}
