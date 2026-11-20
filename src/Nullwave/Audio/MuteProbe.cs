using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Nullwave.Audio;

/// <summary>
/// Diagnostic (--muteprobe): does per-app loopback still hear an app whose own Windows audio
/// session is muted? The delayed Spotify mode depends on it (Spotify is muted at the speakers and
/// replayed by the game 0.75 s later). Plays a test tone in this process, captures this process,
/// and compares the captured level unmuted vs. muted vs. volume 0.
/// </summary>
public static class MuteProbe
{
    public static int Run()
    {
        if (!ProcessLoopbackCapture.IsSupported)
        {
            Console.WriteLine("MUTEPROBE | per-app capture unsupported on this Windows build");
            return 2;
        }
        using var output = new WaveOutEvent { DesiredLatency = 80, NumberOfBuffers = 3 };
        output.Init(new SampleToWaveProvider(new Tone()));
        output.Volume = 0.2f;
        output.Play();
        Thread.Sleep(400);

        float peak = 0f;
        var capture = new ProcessLoopbackCapture(Environment.ProcessId);
        capture.DataAvailable += (_, e) =>
        {
            var fmt = capture.WaveFormat;
            float p = 0f;
            if (fmt.Encoding == WaveFormatEncoding.IeeeFloat)
                for (int i = 0; i + 3 < e.BytesRecorded; i += 4) p = MathF.Max(p, MathF.Abs(BitConverter.ToSingle(e.Buffer, i)));
            else
                for (int i = 0; i + 1 < e.BytesRecorded; i += 2) p = MathF.Max(p, MathF.Abs(BitConverter.ToInt16(e.Buffer, i) / 32768f));
            lock (capture) peak = MathF.Max(peak, p);
        };
        capture.StartRecording();

        float Measure()
        {
            Thread.Sleep(300);
            lock (capture) peak = 0f;
            Thread.Sleep(1200);
            lock (capture) return peak;
        }

        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        AudioSessionControl? own = null;
        var mgr = device.AudioSessionManager;
        mgr.RefreshSessions();
        for (int i = 0; i < mgr.Sessions.Count; i++)
            if (mgr.Sessions[i].GetProcessID == (uint)Environment.ProcessId) own = mgr.Sessions[i];

        float normal = Measure();
        float muted = -1f, zero = -1f;
        if (own != null)
        {
            own.SimpleAudioVolume.Mute = true;
            muted = Measure();
            own.SimpleAudioVolume.Mute = false;
            own.SimpleAudioVolume.Volume = 0f;
            zero = Measure();
            own.SimpleAudioVolume.Volume = 1f;
        }
        // route this process to a silent output and check capture still hears it there
        float routed = -1f;
        string routedTo = "(none)";
        var silent = AppAudioRouter.FindSilentEndpoint(enumerator);
        if (silent != null && AppAudioRouter.SetAppEndpoint(Environment.ProcessId, silent.ID))
        {
            routedTo = silent.FriendlyName;
            Thread.Sleep(600);
            routed = Measure();
            AppAudioRouter.SetAppEndpoint(Environment.ProcessId, null);
        }
        Console.WriteLine($"MUTEPROBE | routed to {routedTo}: capture={routed:0.0000} route now='{AppAudioRouter.GetAppEndpoint(Environment.ProcessId)}'");
        capture.StopRecording();
        capture.Dispose();
        output.Stop();
        Console.WriteLine($"MUTEPROBE | session={(own != null)} normal={normal:0.0000} muted={muted:0.0000} volume0={zero:0.0000}");
        return 0;
    }

    private sealed class Tone : ISampleProvider
    {
        private long _n;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i += 2)
            {
                float s = MathF.Sin(_n++ * 440f * MathF.Tau / 48000f) * 0.5f;
                buffer[offset + i] = s;
                buffer[offset + i + 1] = s;
            }
            return count;
        }
    }
}
