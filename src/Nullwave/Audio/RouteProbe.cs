using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Nullwave.Audio;

/// <summary>
/// Diagnostic (--routeprobe [app]): routes the app to a silent output for ~2 s, checks per-app
/// capture still hears it there, then restores the routing. Proves the buffered-Spotify path.
/// </summary>
public static class RouteProbe
{
    public static int Run(string? appName)
    {
        appName = string.IsNullOrWhiteSpace(appName) ? "spotify" : appName;
        using var enumerator = new MMDeviceEnumerator();
        var app = AudioApps.Find(enumerator, appName);
        if (app == null) { Console.WriteLine($"ROUTEPROBE | {appName} not running"); return 2; }
        Console.WriteLine($"ROUTEPROBE | app={app.Display} pid={app.ProcessId} supported={AppAudioRouter.IsSupported} {AppAudioRouter.LastError}");
        string? before = AppAudioRouter.GetAppEndpoint(app.ProcessId);
        Console.WriteLine($"ROUTEPROBE | current route='{before}' err={AppAudioRouter.LastError}");
        var silent = AppAudioRouter.FindSilentEndpoint(enumerator);
        Console.WriteLine($"ROUTEPROBE | silent output={(silent?.FriendlyName ?? "(none)")}");
        if (silent == null || before == null) return 3;

        float peak = 0f;
        var capture = new ProcessLoopbackCapture(app.ProcessId);
        capture.DataAvailable += (_, e) =>
        {
            float p = 0f;
            if (capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                for (int i = 0; i + 3 < e.BytesRecorded; i += 4) p = MathF.Max(p, MathF.Abs(BitConverter.ToSingle(e.Buffer, i)));
            else
                for (int i = 0; i + 1 < e.BytesRecorded; i += 2) p = MathF.Max(p, MathF.Abs(BitConverter.ToInt16(e.Buffer, i) / 32768f));
            lock (capture) peak = MathF.Max(peak, p);
        };
        capture.StartRecording();
        float Measure(int ms)
        {
            Thread.Sleep(250);
            lock (capture) peak = 0f;
            Thread.Sleep(ms);
            lock (capture) return peak;
        }
        float SessionPeak(MMDevice dev)
        {
            float best = 0f;
            var m = dev.AudioSessionManager;
            m.RefreshSessions();
            for (int i = 0; i < m.Sessions.Count; i++)
            {
                try
                {
                    var s = m.Sessions[i];
                    string n = Process.GetProcessById((int)s.GetProcessID).ProcessName;
                    if (n.Equals(app.Name, StringComparison.OrdinalIgnoreCase)) best = MathF.Max(best, s.AudioMeterInformation.MasterPeakValue);
                }
                catch { }
            }
            return best;
        }

        using var defaultDev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        float normal = Measure(1000);
        float normalDefaultMeter = SessionPeak(defaultDev);
        var pids = Process.GetProcessesByName(app.Name).Select(p => p.Id).ToList();
        bool routed = true;
        AppAudioRouter.WriteMarker(app.Name);
        foreach (int pid in pids) routed &= AppAudioRouter.SetAppEndpoint(pid, silent.ID);
        float routedCapture = Measure(1500);
        float routedDefaultMeter = SessionPeak(defaultDev);
        float routedSilentMeter = SessionPeak(silent);
        string? during = AppAudioRouter.GetAppEndpoint(app.ProcessId);
        foreach (int pid in pids) AppAudioRouter.SetAppEndpoint(pid, null);
        AppAudioRouter.ClearMarker();
        float restored = Measure(800);
        string? after = AppAudioRouter.GetAppEndpoint(app.ProcessId);
        capture.StopRecording();
        capture.Dispose();
        Console.WriteLine($"ROUTEPROBE | routedOk={routed} pids={pids.Count} during='{during}'");
        Console.WriteLine($"ROUTEPROBE | capture peak normal={normal:0.000} routed={routedCapture:0.000} restored={restored:0.000}");
        Console.WriteLine($"ROUTEPROBE | spotify meter on default normal={normalDefaultMeter:0.000} routed={routedDefaultMeter:0.000}; on silent={routedSilentMeter:0.000}");
        Console.WriteLine($"ROUTEPROBE | after restore route='{after}' err={AppAudioRouter.LastError}");
        return 0;
    }
}
