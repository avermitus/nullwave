using System.Diagnostics;

namespace Nullwave.Audio;

/// <summary>
/// Diagnostic (--buffertest): a stand-in player (ffplay, quiet 120 BPM thumps) is captured like
/// Spotify, routed to a silent output and replayed 0.75 s late; then the routing is checked restored.
/// </summary>
public static class BufferTest
{
    public static int Run()
    {
        var psi = new ProcessStartInfo("ffplay",
            "-nodisp -autoexit -loglevel quiet -volume 25 -f lavfi \"aevalsrc=0.5*sin(2*PI*70*t)*exp(-9*mod(t\\,0.5)):s=48000:d=14\"")
        { UseShellExecute = false, CreateNoWindow = true };
        using var player = Process.Start(psi);
        if (player == null) { Console.WriteLine("BUFFERTEST | ffplay failed to start"); return 2; }
        Thread.Sleep(900);
        var engine = new AudioEngine(enableOutput: true, enableCapture: true) { BufferAppName = "ffplay" };
        engine.SetAppBuffering(true);
        bool selected = engine.SelectApp("ffplay");
        float t = 0f, firstCapture = -1f, firstAnalysed = -1f;
        bool buffered = false;
        string source = "";
        int beats = 0;
        while (t < 9f)
        {
            Thread.Sleep(16);
            engine.Update(1f / 60f);
            t += 1f / 60f;
            buffered |= engine.AppBuffered;
            if (engine.State.AppBuffered) source = engine.State.SourceName;
            // delay = from the moment the live feed starts to the first loud frame the game analyses
            if (firstCapture < 0 && engine.State.SourceName.Contains("+0.75S")) firstCapture = t;
            if (firstCapture >= 0 && firstAnalysed < 0 && t > firstCapture && engine.LastWindowPeak > 0.05f) firstAnalysed = t;
            if (engine.State.Beat) beats++;
        }
        var pids = engine.RoutedPids.ToList();
        string during = pids.Count > 0 ? AppAudioRouter.GetAppEndpoint(pids[0]) ?? "?" : "?";
        int underruns = engine.LiveUnderruns;
        long frames = engine.LiveFramesWritten;
        engine.Dispose();
        string after = pids.Count > 0 ? AppAudioRouter.GetAppEndpoint(pids[0]) ?? "?" : "?";
        try { player.Kill(); } catch { }
        Console.WriteLine($"BUFFERTEST | selected={selected} buffered={buffered} routedPids={pids.Count} source='{source}' note='{engine.State.BufferNote}'");
        Console.WriteLine($"BUFFERTEST | captureFrames={frames} underruns={underruns} firstCapture={firstCapture:0.00}s firstAnalysed={firstAnalysed:0.00}s delay={firstAnalysed - firstCapture:0.00}s beats={beats} bpm={engine.State.Bpm:0}");
        Console.WriteLine($"BUFFERTEST | routeDuring={(during.Length > 0 ? "silent output" : "default")} routeAfter='{after}' err={AppAudioRouter.LastError}");
        bool pass = buffered && frames > 48000 * 4 && during.Length > 0 && after == "" && firstAnalysed - firstCapture > 0.6f;
        Console.WriteLine("BUFFERTEST | " + (pass ? "PASS" : "FAIL"));
        return pass ? 0 : 1;
    }
}
