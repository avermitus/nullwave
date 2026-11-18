using Nullwave.Audio;

namespace Nullwave.Tools;

/// <summary>
/// Offline accuracy benchmark for <see cref="BeatTracker"/>: synthesises songs in several styles
/// with known beat times (off-beat hats, syncopated kicks, bass lines, chords, vocal-like noise,
/// human timing jitter, random start phase) and measures tempo error and beat-time error.
/// Run with --beatbench; a short version runs inside --selftest.
/// </summary>
public static class BeatBench
{
    public enum Style { FourOnFloor, Backbeat, HipHop, DrumAndBass, Sparse }

    public sealed record Result(Style Style, float TrueBpm, float Bpm, float MedianErrMs, float P90ErrMs, float HitRate, float DownbeatRate, float MeanSignedMs);

    public static int Run()
    {
        Console.WriteLine("NULLWAVE beat tracker bench");
        var cases = new (Style, float)[]
        {
            (Style.FourOnFloor, 128f), (Style.FourOnFloor, 122f), (Style.Backbeat, 96f), (Style.Backbeat, 140f),
            (Style.HipHop, 88f), (Style.HipHop, 100f), (Style.DrumAndBass, 174f), (Style.Sparse, 110f), (Style.Backbeat, 76f),
        };
        int pass = 0;
        foreach (var (style, bpm) in cases)
        {
            var r = Measure(style, bpm, 34f, seed: (int)(bpm * 7) + (int)style);
            var (oldPrecision, oldBpm) = MeasureOld(style, bpm, 34f, seed: (int)(bpm * 7) + (int)style);
            Console.WriteLine($"  {"",-12} old detector: {oldPrecision:P0} of its beats were real beats, tempo {oldBpm:0.0}");
            bool ok = Good(r);
            if (ok) pass++;
            Console.WriteLine($"  {style,-12} {bpm,5:0} bpm -> {r.Bpm,6:0.0}  median {r.MedianErrMs,5:0.0} ms  p90 {r.P90ErrMs,5:0.0} ms  bias {r.MeanSignedMs,6:0.0} ms  hits {r.HitRate:P0}  downbeats {r.DownbeatRate:P0}  {(ok ? "ok" : "MISS")}");
        }
        Console.WriteLine($"RESULT: {pass}/{cases.Length} songs tracked");
        return pass == cases.Length ? 0 : 1;
    }

    /// <summary>Tempo within 2% (or exactly double/half: the same grid at another metrical level), beats on time.</summary>
    public static bool Good(Result r)
    {
        float ratio = r.Bpm / r.TrueBpm;
        bool tempo = MathF.Abs(ratio - 1f) < 0.02f || MathF.Abs(ratio - 2f) < 0.04f || MathF.Abs(ratio - 0.5f) < 0.01f;
        return tempo && r.HitRate > 0.9f && r.MedianErrMs < 25f;
    }

    /// <summary>Synthesises a song and scores the tracker from <paramref name="warmup"/> s onward.</summary>
    public static Result Measure(Style style, float bpm, float seconds, int seed, float warmup = 8f)
    {
        const int rate = 48000;
        var song = Synthesize(style, bpm, seconds, rate, seed, out var beatTimes, out double firstDownbeat);
        var tracker = new BeatTracker(rate);
        var trackerBeats = new List<(double time, long index)>();
        const int chunk = 480;
        for (int i = 0; i < song.Length; i += chunk)
        {
            int n = Math.Min(chunk, song.Length - i);
            long before = (long)Math.Floor(tracker.Position);
            int crossed = tracker.Push(song.AsSpan(i, n));
            if (crossed > 0 && tracker.Locked)
            {
                long now = (long)Math.Floor(tracker.Position);
                double t = (i + n) / (double)rate;
                // position is continuous: back-date the crossing within the chunk
                double frac = tracker.Position - now;
                double secPerBeat = 60.0 / Math.Max(1f, tracker.Bpm);
                trackerBeats.Add((t - frac * secPerBeat, now));
            }
        }

        if (Environment.GetEnvironmentVariable("NULLWAVE_BEATDEBUG") == "1")
        {
            double hopsPerSec = rate / (double)BeatTracker.Hop;
            var top = Enumerable.Range(20, 120).OrderByDescending(l => tracker.DebugTempo[l]).Take(4)
                .Select(l => $"{60 * hopsPerSec / l:0.0}bpm={tracker.DebugTempo[l]:0.000}");
            Console.WriteLine($"    {style} {bpm}: tempo top " + string.Join("  ", top));
            double P = hopsPerSec * 60 / tracker.Bpm;
            // where does the true beat sit, in hops back from the newest hop?
            double tNow = song.Length / (double)rate;
            double lastTrue = beatTimes.Where(b => b <= tNow).Max();
            double trueBack = (tNow - lastTrue) * hopsPerSec;
            var ph = Enumerable.Range(0, (int)Math.Ceiling(P)).OrderByDescending(f => tracker.DebugPhase[f]).Take(3)
                .Select(f => $"{f}:{tracker.DebugPhase[f]:0.00}");
            Console.WriteLine($"    phase best " + string.Join("  ", ph) + $"   true={trueBack % P:0.0} of {P:0.0}");
        }
        double beatSec = 60.0 / bpm;
        float ratio = tracker.Bpm / bpm;
        int level = MathF.Abs(ratio - 2f) < 0.06f ? 2 : MathF.Abs(ratio - 0.5f) < 0.03f ? -2 : 1;
        var grid = new List<double>();
        if (level == 2) foreach (var bt in beatTimes) { grid.Add(bt); grid.Add(bt + beatSec * 0.5); }
        else grid.AddRange(beatTimes);
        var errs = new List<double>();
        double signed = 0;
        int hits = 0, scored = 0, downHits = 0, downScored = 0;
        foreach (var (t, index) in trackerBeats)
        {
            if (t < warmup) continue;
            scored++;
            int nearest = 0;
            double best = double.MaxValue;
            for (int k = 0; k < grid.Count; k++)
            {
                double d = Math.Abs(grid[k] - t);
                if (d < best) { best = d; nearest = k; }
            }
            double signedErr = t - grid[nearest];
            errs.Add(best * 1000.0);
            if (Environment.GetEnvironmentVariable("NULLWAVE_BEATDEBUG") == "2") Console.Write($"{t:0}:{signedErr * 1000:0} ");
            signed += signedErr * 1000.0;
            if (best < 0.07) hits++;
            bool trackerDown = ((index - tracker.DownbeatOffset) % 4 + 4) % 4 == 0;
            if (trackerDown)
            {
                downScored++;
                int beatInBar = (int)Math.Round((grid[nearest] - firstDownbeat) / beatSec);
                if (((beatInBar % 4) + 4) % 4 == 0) downHits++;
            }
        }
        errs.Sort();
        float median = errs.Count > 0 ? (float)errs[errs.Count / 2] : 999f;
        float p90 = errs.Count > 0 ? (float)errs[(int)(errs.Count * 0.9)] : 999f;
        // expected beats in the scored window, so missed beats count against the hit rate
        int expected = 0;
        foreach (var bt in grid) if (bt >= warmup + 0.05 && bt <= seconds - 0.2) expected++;
        if (level == -2) expected /= 2;
        float hitRate = expected == 0 ? 0 : MathF.Min(hits, expected) / (float)Math.Max(expected, scored);
        return new Result(style, bpm, tracker.Bpm, median, p90, hitRate, downScored == 0 ? 0 : downHits / (float)downScored, scored == 0 ? 0 : (float)(signed / scored));
    }

    /// <summary>The pre-V5 detector: flags onsets as beats, tempo = median gap. For comparison only.</summary>
    public static (float precision, float bpm) MeasureOld(Style style, float bpm, float seconds, int seed)
    {
        const int rate = 48000;
        var song = Synthesize(style, bpm, seconds, rate, seed, out var beatTimes, out _);
        var analyzer = new SpectrumAnalyzer();
        var state = new AudioState();
        float dt = 1f / 60f;
        int hop = rate / 60;
        var buf = new float[SpectrumAnalyzer.FftSize];
        var detected = new List<double>();
        for (int end = SpectrumAnalyzer.FftSize; end < song.Length; end += hop)
        {
            Array.Copy(song, end - buf.Length, buf, 0, buf.Length);
            state.Time = end / (float)rate;
            state.ClearBeatFlags();
            analyzer.Analyze(buf, buf.Length, rate, dt, state);
            if (state.Beat && state.Time > 8f) detected.Add(state.Time);
        }
        int real = 0;
        foreach (var t in detected)
            if (beatTimes.Any(b => Math.Abs(b - t) < 0.07)) real++;
        var gaps = new List<double>();
        for (int i = 1; i < detected.Count; i++) { double g = detected[i] - detected[i - 1]; if (g > 0.28 && g < 1.35) gaps.Add(g); }
        gaps.Sort();
        float oldBpm = gaps.Count == 0 ? 0 : (float)(60.0 / gaps[gaps.Count / 2]);
        return (detected.Count == 0 ? 0 : real / (float)detected.Count, oldBpm);
    }

    /// <summary>
    /// Runs a song through the spectrum analyser at the given gain (hard clipped at +-1) and
    /// reports how much the reactive values move: standard deviation and the share of frames
    /// pinned at the top. Loud, clipped masters must not flatten the visuals.
    /// </summary>
    public static (float bassStd, float midStd, float highStd, float energyStd, float pinned, int kicks, int snares, int hats) MeasureDynamics(float gain, int seed = 5)
    {
        const int rate = 48000;
        var song = Synthesize(Style.Backbeat, 120f, 30f, rate, seed, out _, out _);
        for (int i = 0; i < song.Length; i++) song[i] = Math.Clamp(song[i] * gain, -1f, 1f);
        var analyzer = new SpectrumAnalyzer();
        var state = new AudioState();
        float dt = 1f / 60f;
        int hop = rate / 60;
        var buf = new float[SpectrumAnalyzer.FftSize];
        var bass = new List<float>(); var mid = new List<float>(); var high = new List<float>(); var energy = new List<float>();
        int pinned = 0, frames = 0, kicks = 0, snares = 0, hats = 0;
        for (int end = SpectrumAnalyzer.FftSize; end < song.Length; end += hop)
        {
            Array.Copy(song, end - buf.Length, buf, 0, buf.Length);
            state.Time = end / (float)rate;
            state.ClearBeatFlags();
            analyzer.Analyze(buf, buf.Length, rate, dt, state);
            if (state.Time < 8f) continue;
            bass.Add(state.Bass); mid.Add(state.Mid); high.Add(state.High); energy.Add(state.Energy);
            frames++;
            if (state.Bass > 0.97f && state.Energy > 0.97f) pinned++;
            if (state.KickHit) kicks++;
            if (state.SnareHit) snares++;
            if (state.HatHit) hats++;
        }
        static float Std(List<float> v) { float m = v.Average(); return MathF.Sqrt(v.Sum(x => (x - m) * (x - m)) / v.Count); }
        return (Std(bass), Std(mid), Std(high), Std(energy), pinned / (float)Math.Max(1, frames), kicks, snares, hats);
    }

    public static float[] Synthesize(Style style, float bpm, float seconds, int rate, int seed, out List<double> beatTimes, out double firstDownbeat)
    {
        var rng = new Random(seed);
        int total = (int)(seconds * rate);
        var buf = new float[total];
        double beat = 60.0 / bpm;
        double start = rng.NextDouble() * beat * 3;    // song starts mid-bar
        firstDownbeat = start;
        beatTimes = new List<double>();
        for (double t = start; t < seconds; t += beat) beatTimes.Add(t);

        double Jit() => (rng.NextDouble() - 0.5) * 0.016;   // +-8 ms human timing

        // step grid of sixteenths
        int steps = (int)((seconds - start) / (beat / 4)) + 1;
        float[] chordRoots = { 55f, 65.4f, 49f, 73.4f };
        for (int s = 0; s < steps; s++)
        {
            double t = start + s * beat / 4 + Jit() * 0.5;
            int inBar = s % 16;
            int bar = s / 16;
            bool kick = style switch
            {
                Style.FourOnFloor => inBar % 4 == 0,
                Style.Backbeat => inBar == 0 || inBar == 8 || (inBar == 10 && bar % 2 == 1),
                Style.HipHop => inBar == 0 || inBar == 7 || inBar == 10 || (inBar == 13 && bar % 2 == 0),
                Style.DrumAndBass => inBar == 0 || inBar == 10,
                _ => inBar == 0 || (inBar == 8 && bar % 2 == 0),
            };
            bool snare = style switch
            {
                Style.FourOnFloor => inBar == 4 || inBar == 12,
                Style.DrumAndBass => inBar == 4 || inBar == 12 || (inBar == 15 && bar % 2 == 1),
                Style.Sparse => inBar == 12,
                _ => inBar == 4 || inBar == 12,
            };
            bool hat = style switch
            {
                Style.FourOnFloor => inBar % 4 == 2,           // open off-beat hats
                Style.HipHop => inBar % 2 == 0,
                Style.Sparse => inBar % 4 == 2 && bar % 2 == 0,
                _ => true,                                     // sixteenths
            };
            if (kick) Kick(buf, t, rate, 0.75f);
            if (snare) Snare(buf, t + Jit(), rate, 0.42f, rng);
            if (hat) Hat(buf, t + Jit(), rate, inBar % 4 == 2 ? 0.16f : 0.09f, rng);
            if (inBar % 2 == 0)
                Tone(buf, t, rate, chordRoots[bar % 4] * (inBar % 8 == 6 ? 1.5f : 1f), beat * 0.45, 0.16f, saw: true);
            if (inBar == 0)
                foreach (float mult in new[] { 4f, 5f, 6f })
                    Tone(buf, t, rate, chordRoots[bar % 4] * mult, beat * 3.8, 0.035f, saw: false);
            // vocal-ish noise bursts deliberately off the grid
            if (rng.NextDouble() < 0.05) Breath(buf, t + beat * 0.37, rate, 0.12f, rng);
        }
        // light master noise floor
        for (int i = 0; i < total; i++) buf[i] += (float)(rng.NextDouble() - 0.5) * 0.004f;
        return buf;
    }

    private static void Kick(float[] b, double t, int rate, float amp)
    {
        int s0 = (int)(t * rate);
        double phase = 0;
        for (int i = 0; i < rate * 0.28 && s0 + i < b.Length; i++)
        {
            if (s0 + i < 0) continue;
            double x = i / (double)rate;
            double f = 48 + 110 * Math.Exp(-x * 38);
            phase += f / rate;
            b[s0 + i] += (float)(Math.Sin(phase * Math.Tau) * Math.Exp(-x * 9) * amp);
        }
    }

    private static void Snare(float[] b, double t, int rate, float amp, Random rng)
    {
        int s0 = (int)(t * rate);
        for (int i = 0; i < rate * 0.18 && s0 + i < b.Length; i++)
        {
            if (s0 + i < 0) continue;
            double x = i / (double)rate;
            double n = rng.NextDouble() * 2 - 1;
            b[s0 + i] += (float)((n * 0.8 + Math.Sin(x * 190 * Math.Tau) * 0.4) * Math.Exp(-x * 22) * amp);
        }
    }

    private static void Hat(float[] b, double t, int rate, float amp, Random rng)
    {
        int s0 = (int)(t * rate);
        double prev = 0;
        for (int i = 0; i < rate * 0.05 && s0 + i < b.Length; i++)
        {
            if (s0 + i < 0) continue;
            double n = rng.NextDouble() * 2 - 1;
            double hp = n - prev;
            prev = n;
            b[s0 + i] += (float)(hp * Math.Exp(-i / (double)rate * 70) * amp);
        }
    }

    private static void Breath(float[] b, double t, int rate, float amp, Random rng)
    {
        int s0 = (int)(t * rate);
        int len = (int)(rate * 0.3);
        for (int i = 0; i < len && s0 + i < b.Length; i++)
        {
            if (s0 + i < 0) continue;
            double env = Math.Sin(Math.PI * i / len);
            b[s0 + i] += (float)((rng.NextDouble() * 2 - 1) * env * amp * 0.6 + Math.Sin(i / (double)rate * 330 * Math.Tau) * env * amp);
        }
    }

    private static void Tone(float[] b, double t, int rate, float freq, double dur, float amp, bool saw)
    {
        int s0 = (int)(t * rate);
        int len = (int)(dur * rate);
        for (int i = 0; i < len && s0 + i < b.Length; i++)
        {
            if (s0 + i < 0) continue;
            double x = i / (double)rate;
            double ph = x * freq % 1.0;
            double v = saw ? ph * 2 - 1 : Math.Sin(ph * Math.Tau);
            double env = Math.Min(1.0, x * 200) * Math.Min(1.0, (dur - x) * 30);
            b[s0 + i] += (float)(v * env * amp);
        }
    }
}
