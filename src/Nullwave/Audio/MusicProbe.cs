namespace Nullwave.Audio;

/// <summary>
/// Diagnostic (--musicprobe [seconds] [--track file]): runs the real audio engine (quietly) and
/// prints what the music analysis hears: drums vs. melody, chord, key, brightness and sections.
/// </summary>
public static class MusicProbe
{
    private static readonly string[] Notes = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    public static int Run(float seconds, string? track)
    {
        using var engine = new AudioEngine(enableOutput: true, enableCapture: track == null);
        engine.SetVolumes(0.2f, 0f);
        if (track != null && !engine.LoadLocalTrack(track)) { Console.WriteLine("MUSICPROBE | " + engine.LastError); return 2; }
        var s = engine.State;
        int sections = 0, repeats = 0, chordChanges = 0;
        float t = 0f, nextLog = 1f;
        float pMin = 9f, pMax = 0f, hMin = 9f, hMax = 0f;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        float last = 0f;
        while (t < seconds)
        {
            Thread.Sleep(12);
            float now = (float)clock.Elapsed.TotalSeconds;
            engine.Update(now - last);
            last = now;
            t = now;
            if (s.ChordChanged) chordChanges++;
            pMin = MathF.Min(pMin, s.PercLow); pMax = MathF.Max(pMax, s.PercLow);
            hMin = MathF.Min(hMin, s.HarmMid); hMax = MathF.Max(hMax, s.HarmMid);
            if (s.SectionChanged) { sections++; if (s.SectionRepeat) repeats++; Console.WriteLine($"MUSICPROBE | t={t:0.00}s SECTION {s.Section} label={s.SectionLabel}{(s.SectionRepeat ? " (repeat)" : "")}{(s.SectionCorrected ? " (corrected)" : "")} beatPos={s.BeatPosition:0.00} locked={s.BeatLocked} downbeatOffset={s.DownbeatOffset} startBeat={engine.LastSectionStartBeat}"); }
            if (s.KeyChanged) Console.WriteLine($"MUSICPROBE | t={t:0.0}s KEY CHANGE");
            if (s.TextureChanged) Console.WriteLine($"MUSICPROBE | t={t:0.0}s TEXTURE CHANGE");
            if (t >= nextLog)
            {
                nextLog += 1f;
                float a0 = pMin, a1 = pMax, b0 = hMin, b1 = hMax;
                pMin = 9f; pMax = 0f; hMin = 9f; hMax = 0f;
                string chord = s.ChordRoot < 0 ? "-" : Notes[s.ChordRoot] + (s.ChordMinor ? "m" : "");
                string key = s.Key < 0 ? "-" : Notes[s.Key % 12] + (s.Key >= 12 ? " minor" : " major");
                Console.WriteLine($"MUSICPROBE | t={t:0}s src={s.SourceName} play={engine.PlaybackSeconds:0.00}s beatPos={s.BeatPosition:0.0} kickDrums={a0:0.00}..{a1:0.00} midMelody={b0:0.00}..{b1:0.00} perc%={s.Percussive:0.00} chord={chord} key={key} bright={s.Brightness:0.00} bpm={s.Bpm:0}");
            }
        }
        Console.WriteLine($"MUSICPROBE | done chordChanges={chordChanges} sections={sections} repeats={repeats}");
        return 0;
    }
}
