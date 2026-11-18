using System.Diagnostics;
using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;
using Nullwave.Render;
using Nullwave.UI;
using Nullwave.World;
using GameWorld = Nullwave.World.World;

namespace Nullwave.Tools;

public static class SelfTest
{
    private static int _checks;

    public static int Run()
    {
        Console.WriteLine("NULLWAVE self test");
        var sw = Stopwatch.StartNew();
        try
        {
            TestMeta();
            TestSeeds();
            TestDamageAndBurst();
            TestLongRun();
            TestEndlessWave();
            TestModels();
            TestBeatFire();
            TestMapLayouts();
            TestHealthDisplay();
            TestSettings();
            TestBeatTracker();
            TestFireOnRealBeats();
            TestMusicDirector();
            TestBackdropVariety();
            TestRacerFeel();
            TestLoudAudioStaysReactive();
            TestSongLasers();
            TestNewEnemies();
            TestSfx();
            TestRunTelemetry();
            TestRenderStateContract();
            TestAttunementLoadout();
            TestSeedReplayAndEntry();
            TestPhaseSync();
            TestBossPhaseChoreography();
            Console.WriteLine($"RESULT: PASS ({_checks} assertions, {sw.Elapsed.TotalSeconds:0.0}s)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("RESULT: FAIL");
            Console.WriteLine(ex);
            return 1;
        }
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
    }

    private static void Finite(World.Player p)
    {
        Check(float.IsFinite(p.Pos.X) && float.IsFinite(p.Pos.Y), "player position finite");
        Check(float.IsFinite(p.Vel.X) && float.IsFinite(p.Vel.Y), "player velocity finite");
    }

    private static void TestMeta()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nullwave-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "save.json");
        var meta = MetaProgress.Load(path);
        Check(meta.UnlockedModifiers.Contains("metronome"), "starter modifier metronome");
        Check(meta.UnlockedModifiers.Contains("glass-choir"), "starter modifier glass choir");
        Check(meta.ToggleModifier("metronome"), "toggle selected modifier");
        meta.Save();
        var loaded = MetaProgress.Load(path);
        Check(loaded.SelectedModifiers.Contains("metronome"), "modifier selection persisted");
        loaded.BestWave = 12;
        loaded.BestScore = 60_000;
        loaded.TotalBossKills = 3;
        loaded.TotalGrazes = 500;
        int bossesBeforeRun = loaded.TotalBossKills;
        loaded.EvaluateUnlocks();
        Check(loaded.UnlockedModifiers.Contains("noise-floor"), "wave unlock");
        Check(loaded.UnlockedModifiers.Contains("phase-skin"), "graze unlock");
        var run = new RunSummary
        {
            Seed = 12345,
            Wave = 12,
            Score = 60_000,
            Kills = 100,
            Grazes = 500,
            BossKills = 3,
            Shatters = 30,
            Bursts = 5,
            MaxCombo = 40,
            Duration = 212f,
            NoHitWaves = 2,
            Movement = 2,
            Modifiers = ["metronome"],
        };
        var unlocks = loaded.RecordRun(run);
        Check(loaded.Runs == 1, "run recorded");
        Check(loaded.BestWave == 12, "best wave recorded");
        Check(loaded.TotalBossKills == bossesBeforeRun + 3, "boss total recorded");
        Check(File.Exists(path), "save file exists");
        var config = new RunConfig { Seed = 999, SelectedModifiers = ["metronome", "noise-floor"] };
        var mods = ModifierCatalog.Build(config.SelectedModifiers);
        Check(mods.MaxHealth == 3, "default health");
        Check(mods.SpawnCount > 1.2f, "noise floor increases density");
        Check(mods.ScoreMultiplier > 1.2f, "score multiplier stacks");
        Console.WriteLine($"  meta: unlocks={unlocks.Count} selected={loaded.SelectedModifiers.Count} save={new FileInfo(path).Length} bytes");
    }

    private static void TestSeeds()
    {
        ulong a = SeedCode.DailySeed(new DateTime(2026, 11, 17));
        ulong b = SeedCode.DailySeed(new DateTime(2026, 11, 17));
        ulong c = SeedCode.DailySeed(new DateTime(2026, 11, 18));
        Check(a == b, "daily seed deterministic");
        Check(a != c, "daily seed changes by day");
        Check(SeedCode.ToCode(a).Length == 8, "daily code length");
        Check(SeedCode.ToCode(0).Length == 8, "zero seed code");
        Check(SeedCode.ToCode(ulong.MaxValue).Length == 8, "max seed code");
        Console.WriteLine($"  seeds: daily={SeedCode.ToCode(a)} tomorrow={SeedCode.ToCode(c)}");
    }

    private static void TestDamageAndBurst()
    {
        var world = new GameWorld(new RunConfig { Seed = 1234 });
        world.StartGrace = 0f;
        int health = world.Player.Health;
        world.HurtPlayer();
        Check(world.Player.Health == health - 1, "player takes one damage");
        world.HurtPlayer();
        Check(world.Player.Health == health - 1, "invulnerability blocks second hit");
        world.Player.Invuln = 0;
        world.Player.Resonance = world.Player.ResonanceMax;
        world.Burst();
        Check(world.Player.Resonance < 1f, "burst consumes resonance");
        Check(world.Bursts == 1, "burst counted");
        Console.WriteLine($"  damage: health={world.Player.Health} burst={world.Bursts}");
    }

    private static void TestLongRun()
    {
        var audio = new AudioState();
        var visual = new SynthVisual();
        var world = new GameWorld(new RunConfig
        {
            Seed = 0xDEADBEEF1234,
            UnlockedAttunements = new HashSet<string> { "homing", "rear", "phase" },
        });
        world.Player.InvincibleDemo = true;
        float dt = 1f / 60f;
        const int ticks = 30000;
        var sw = Stopwatch.StartNew();
        for (int t = 0; t < ticks; t++)
        {
            audio.ClearBeatFlags();
            audio.Time += dt;
            visual.Advance(dt, audio);
            if (world.Phase == GamePhase.Attuning && world.Offers.Count > 0)
                world.ChooseAttunement(0);
            if (world.Phase == GamePhase.Dead)
            {
                throw new InvalidOperationException("Invincible pilot died at tick " + t);
            }
            world.Step(dt, audio, Pilot.Think(world));
            if (t % 600 == 0)
            {
                Finite(world.Player);
                Check(world.EnemyBullets.Count <= 2600, "enemy bullet cap");
                Check(world.PlayerBullets.Count <= 900, "player bullet cap");
                Check(world.Particles.Count <= 4200, "particle cap");
                Check(world.Lasers.Count <= 40, "laser cap");
                Check(world.Score >= 0, "nonnegative score");
            }
        }
        sw.Stop();
        Console.WriteLine($"  soak: ticks={ticks} sim={sw.Elapsed.TotalSeconds:0.00}s waves={world.Wave} score={world.Score} kills={world.Kills} grazes={world.Grazes} bulletMax={world.EnemyBullets.Count} fx={world.Particles.Count}");
        Check(world.Wave >= 2, "waves advanced during soak");
        Check(world.Kills > 10, "pilot killed enemies");
        Check(world.Grazes > 0, "grazes occurred");
        Check(world.Score > 0, "score accumulated");
    }

    private static void TestEndlessWave()
    {
        var audio = new AudioState();
        var visual = new SynthVisual();
        var world = new GameWorld(new RunConfig { Seed = 0x77777777UL });
        world.Player.InvincibleDemo = true;
        world.BeginWave(100);
        float dt = 1f / 60f;
        for (int t = 0; t < 2400; t++)
        {
            audio.ClearBeatFlags();
            audio.Time += dt;
            visual.Advance(dt, audio);
            if (world.Phase == GamePhase.Attuning && world.Offers.Count > 0)
                world.ChooseAttunement(0);
            world.Step(dt, audio, Pilot.Think(world));
            Finite(world.Player);
            Check(world.EnemyBullets.Count <= 2600, "wave100 enemy bullet cap");
            if (t % 300 == 0)
                Check(world.Enemies.Count <= 30, "active enemy cap");
        }
        Console.WriteLine($"  endless: wave={world.Wave} movement={world.Movement} enemies={world.Enemies.Count} queued={world.QueuedEnemies} bullets={world.EnemyBullets.Count} boss={world.IsBossFight}");
        Check(world.Wave >= 100, "wave 100 retained");
        Check(world.Enemies.Count > 0 || world.QueuedEnemies > 0 || world.IsBossFight, "wave 100 spawned content");
    }

    private static void TestModels()
    {
        int meshes = 0, verts = 0;
        foreach (var mesh in Models.All)
        {
            meshes++;
            CheckMesh(mesh, "model " + meshes);
            verts += mesh.VertexCount;
        }
        var world = new GameWorld(new RunConfig { Seed = 4242 });
        foreach (var o in world.Obstacles)
        {
            var m = Models.Monolith(o);
            CheckMesh(m, "monolith");
            Check(ReferenceEquals(m, Models.Monolith(o)), "monolith mesh cached");
        }
        Console.WriteLine($"  models: meshes={meshes} verts={verts} monoliths={world.Obstacles.Count}");
    }

    private static void CheckMesh(Mesh mesh, string label)
    {
        Check(mesh.VertexCount > 0 && mesh.VertexCount % 3 == 0, label + " has whole triangles");
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            var p = mesh.Positions[i];
            var n = mesh.Normals[i];
            Check(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z), label + " finite position");
            Check(MathF.Abs(n.Length() - 1f) < 0.01f, label + " unit normal");
        }
    }

    /// <summary>Enemy fire must land on the beat-locked eighth grid; ticks must match the tempo.</summary>
    private static void TestBeatFire()
    {
        var audio = new AudioState();
        var visual = new SynthVisual();
        var world = new GameWorld(new RunConfig { Seed = 0xBEA7UL });
        world.Player.InvincibleDemo = true;
        float dt = 1f / 60f;
        int beats = 0, eighths = 0, spawnSteps = 0, offGrid = 0;
        float maxTelegraph = 0f;
        const int ticks = 60 * 40;
        for (int t = 0; t < ticks; t++)
        {
            audio.ClearBeatFlags();
            audio.Time += dt;
            visual.Advance(dt, audio);
            if (world.Phase == GamePhase.Attuning && world.Offers.Count > 0) world.ChooseAttunement(0);
            int before = world.EnemyBullets.Count;
            int wave = world.Wave;
            world.Step(dt, audio, Pilot.Think(world));
            if (world.BeatTick) beats++;
            if (world.EighthTick) eighths++;
            Check(world.EighthInBar is >= 0 and < 8, "eighth in bar range");
            Check(world.BeatPhase is >= 0f and < 1f, "beat phase range");
            foreach (var e in world.Enemies)
            {
                Check(e.Telegraph is >= 0f and <= 1f, "telegraph range");
                maxTelegraph = MathF.Max(maxTelegraph, e.Telegraph);
                Check(MathF.Abs(e.Pos.X) <= world.ArenaW * 0.5f + 1f && MathF.Abs(e.Pos.Y) <= world.ArenaH * 0.5f + 1f, "enemy inside arena");
            }
            // waves 1-2 hold only drifters and orbiters, whose fire is purely rhythmic
            if (wave <= 2 && world.EnemyBullets.Count > before)
            {
                spawnSteps++;
                if (!world.EighthTick) offGrid++;
            }
        }
        float seconds = ticks * dt;
        float expected = seconds * visual.Bpm / 60f;
        Console.WriteLine($"  beat: beats={beats} expected~{expected:0} eighths={eighths} volleys={spawnSteps} offgrid={offGrid} telegraphMax={maxTelegraph:0.00}");
        Check(MathF.Abs(beats - expected) <= 4f, "beat ticks follow tempo");
        Check(MathF.Abs(eighths - expected * 2f) <= 8f, "eighth ticks follow tempo");
        Check(spawnSteps > 3, "enemies fired during early waves");
        Check(offGrid == 0, "enemy volleys land on the eighth grid");
        Check(maxTelegraph > 0.6f, "telegraph ramps before firing");
    }

    private static void TestMapLayouts()
    {
        var world = new GameWorld(new RunConfig { Seed = 0x1A40UL });
        Check(world.ArenaW >= 5000f && world.ArenaH >= 3200f, "arena is the larger size");
        var seen = new HashSet<string>();
        for (int movement = 0; movement < 7; movement++)
        {
            world.Movement = movement;
            world.Player.Pos = new Vector2(600f * (movement % 3 - 1), 300f);
            world.GenerateObstacles();
            seen.Add(world.LayoutName);
            Check(world.Obstacles.Count >= 8, "layout " + world.LayoutName + " places monoliths");
            for (int i = 0; i < world.Obstacles.Count; i++)
            {
                var o = world.Obstacles[i];
                Check(MathF.Abs(o.Pos.X) + o.Radius < world.ArenaW * 0.5f && MathF.Abs(o.Pos.Y) + o.Radius < world.ArenaH * 0.5f, "monolith inside arena");
                Check(M.Dist(o.Pos, world.Player.Pos) > o.Radius + 200f, "monolith clear of player");
                for (int j = i + 1; j < world.Obstacles.Count; j++)
                    Check(M.Dist(o.Pos, world.Obstacles[j].Pos) > o.Radius + world.Obstacles[j].Radius, "monoliths do not overlap");
            }
        }
        Console.WriteLine($"  map: {world.ArenaW}x{world.ArenaH} layouts={string.Join(",", seen)}");
        Check(seen.Count == 5, "all five layouts generated");
    }

    private static void TestHealthDisplay()
    {
        var audio = new AudioState();
        var world = new GameWorld(new RunConfig { Seed = 77 });
        world.StartGrace = 0f;
        world.Step(1f / 60f, audio, InputFrame.Empty);
        Check(MathF.Abs(world.Player.HealthDisplay - world.Player.Health) < 0.01f, "health display starts in sync");
        world.HurtPlayer();
        world.Step(1f / 60f, audio, InputFrame.Empty);
        Check(world.Player.HealthDisplay > world.Player.Health + 0.5f, "health bar shows the lost chunk");
        for (int i = 0; i < 180; i++) world.Step(1f / 60f, audio, InputFrame.Empty);
        Check(MathF.Abs(world.Player.HealthDisplay - world.Player.Health) < 0.01f, "health display drains to real health");
        Console.WriteLine($"  health: hp={world.Player.Health} display={world.Player.HealthDisplay:0.00}");
    }

    private static void TestSettings()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nullwave-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "save.json");
        var meta = MetaProgress.Load(path);
        Check(meta.Settings.MusicVolume == 70 && meta.Settings.Minimap, "settings defaults");
        meta.Settings.MusicVolume = 0;
        meta.Settings.BeatOffsetMs = 90;
        meta.Settings.Minimap = false;
        meta.Settings.Bloom = 999;
        meta.Save();
        var loaded = MetaProgress.Load(path);
        Check(loaded.Settings.MusicVolume == 0 && loaded.Settings.BeatOffsetMs == 90 && !loaded.Settings.Minimap, "settings persisted");
        loaded.Settings.Sanitize();
        Check(loaded.Settings.Bloom == 150, "settings clamped on load");
        Check(!File.ReadAllText(path).Contains("\"Master\""), "computed volumes not serialised");

        // beat offset delays the grid: with +250ms the first downbeat tick arrives later
        int FirstTick(int offset)
        {
            var previous = GameSettings.Current;
            GameSettings.Current = new GameSettings { BeatOffsetMs = offset };
            var audio = new AudioState();
            var w = new GameWorld(new RunConfig { Seed = 5 });
            int first = -1, beats = 0;
            for (int t = 0; t < 600 && first < 0; t++)
            {
                audio.Time += 1f / 60f;
                w.Step(1f / 60f, audio, InputFrame.Empty);
                if (w.BeatTick && ++beats == 2) first = t;
            }
            GameSettings.Current = previous;
            return first;
        }
        int baseTick = FirstTick(0), lateTick = FirstTick(200);
        Check(lateTick - baseTick is >= 10 and <= 14, "beat offset shifts enemy timing by ~200ms");
        Console.WriteLine($"  settings: persisted ok, offset ticks {baseTick}->{lateTick}");
    }

    private static void TestBeatTracker()
    {
        foreach (var (style, bpm) in new[] { (BeatBench.Style.FourOnFloor, 128f), (BeatBench.Style.Backbeat, 96f), (BeatBench.Style.DrumAndBass, 174f) })
        {
            var r = BeatBench.Measure(style, bpm, 26f, seed: 11 + (int)bpm);
            Console.WriteLine($"  tracker: {style} {bpm} -> {r.Bpm:0.0} bpm, median {r.MedianErrMs:0.0} ms, hits {r.HitRate:P0}");
            Check(BeatBench.Good(r), $"tracker locks {style} {bpm}");
        }
    }

    /// <summary>
    /// End to end: a synthetic song goes through the tracker into the game, and enemy volleys are
    /// timed against the song's true beats.
    /// </summary>
    private static void TestFireOnRealBeats()
    {
        const int rate = 48000;
        float bpm = 118f;
        var song = BeatBench.Synthesize(BeatBench.Style.Backbeat, bpm, 70f, rate, 99, out var beats, out _);
        var tracker = new BeatTracker(rate);
        var audio = new AudioState();
        var world = new GameWorld(new RunConfig { Seed = 0x5EED1UL });
        world.Player.InvincibleDemo = true;
        float dt = 1f / 60f;
        int per = rate / 60;
        var volleyErrs = new List<double>();
        float surgeSum = 0f;
        int surgeN = 0;
        for (int f = 0; f * per + per <= song.Length; f++)
        {
            int crossed = tracker.Push(song.AsSpan(f * per, per));
            audio.ClearBeatFlags();
            audio.Time += dt;
            audio.BeatLocked = tracker.Locked;
            audio.BeatPosition = tracker.Position;
            audio.DownbeatOffset = tracker.DownbeatOffset;
            if (tracker.Locked) audio.Bpm = tracker.Bpm;
            audio.Beat = crossed > 0 && tracker.Locked;
            if (world.Phase == GamePhase.Attuning && world.Offers.Count > 0) world.ChooseAttunement(0);
            int before = world.EnemyBullets.Count;
            int wave = world.Wave;
            world.Step(dt, audio, Pilot.Think(world));
            double t = (f + 1) * per / (double)rate;
            if (t > 12 && wave <= 4 && world.EnemyBullets.Count > before && world.EighthTick)
                volleyErrs.Add(beats.Min(b => Math.Abs(b - t)) * 1000.0);
            if (t > 12) { surgeSum += world.BeatSurge(2.4f); surgeN++; }
        }
        volleyErrs.Sort();
        double median = volleyErrs.Count > 0 ? volleyErrs[volleyErrs.Count / 2] : 999;
        int onBeat = volleyErrs.Count(e => e < 45);
        Console.WriteLine($"  on-beat fire: volleys={volleyErrs.Count} median={median:0.0} ms within45ms={onBeat}/{volleyErrs.Count} surgeMean={surgeSum / Math.Max(1, surgeN):0.000}");
        Check(volleyErrs.Count >= 8, "enemies fired while tracking a song");
        Check(median < 30, "enemy volleys land on the song's beats");
        Check(onBeat >= volleyErrs.Count * 0.85, "nearly every volley is on a beat");
        Check(MathF.Abs(surgeSum / Math.Max(1, surgeN) - 1f) < 0.05f, "beat surge keeps average speed");
    }

    /// <summary>Verse -> breakdown -> build -> drop -> chorus: the director must read each section.</summary>
    private static void TestMusicDirector()
    {
        var d = new MusicDirector();
        var a = new AudioState { BeatLocked = true };
        float dt = 1f / 60f, bpm = 124f;
        float maxBreak = 0, maxBuild = 0, dropAt = -1, intensityChorus = 0, breakIntensity = 1;
        int drops = 0;
        float hueBreak = 0, hueDrop = 0;
        for (int f = 0; f < 60 * 46; f++)
        {
            float t = f * dt;
            float loud, high, bright;
            if (t < 14) { loud = 0.08f; high = 0.3f; bright = 0.45f; }                    // verse
            else if (t < 24) { loud = 0.012f; high = 0.08f; bright = 0.2f; }              // breakdown
            else if (t < 30) { float k = (t - 24) / 6f; loud = 0.012f + k * 0.05f; high = 0.1f + k * 0.7f; bright = 0.3f + k * 0.5f; } // build
            else { loud = 0.16f; high = 0.55f; bright = 0.7f; }                           // drop + chorus
            double prev = a.BeatPosition;
            a.BeatPosition += dt * bpm / 60.0;
            a.ClearBeatFlags();
            a.Beat = Math.Floor(a.BeatPosition) > Math.Floor(prev);
            a.Downbeat = a.Beat && (long)Math.Floor(a.BeatPosition) % 4 == 0;
            a.Loudness = loud * (a.Beat ? 1.3f : 1f);
            a.High = high;
            a.Air = high * 0.4f;
            for (int i = 0; i < AudioState.SpectrumBars; i++)
            {
                float x = i / 47f;
                a.Spectrum[i] = MathF.Exp(-MathF.Abs(x - bright) * 5f);
            }
            d.Update(a, dt);
            if (d.DropTriggered) { drops++; if (dropAt < 0) dropAt = t; }
            if (t > 20 && t < 24) { maxBreak = MathF.Max(maxBreak, d.Breakdown); breakIntensity = MathF.Min(breakIntensity, d.Intensity); hueBreak = d.MoodHue; }
            if (t > 26 && t < 30) maxBuild = MathF.Max(maxBuild, d.Build);
            if (t > 40) { intensityChorus = d.Intensity; hueDrop = d.MoodHue; }
            Check(float.IsFinite(d.MoodHue) && d.Kick >= 0f && d.Kick <= 1f, "director outputs finite");
        }
        Console.WriteLine($"  director: breakdown={maxBreak:0.00} build={maxBuild:0.00} drop@{dropAt:0.0}s drops={drops} chorusIntensity={intensityChorus:0.00} breakIntensity={breakIntensity:0.00} hue {hueBreak:0.00}->{hueDrop:0.00}");
        Check(maxBreak > 0.6f, "breakdown recognised");
        Check(maxBuild > 0.3f, "build-up recognised");
        Check(dropAt >= 30f && dropAt < 30.2f, "drop lands on the chorus's first beat");
        Check(drops == 1, "exactly one drop");
        Check(intensityChorus > 0.7f && breakIntensity < 0.3f, "intensity follows the song's range");
        Check(hueDrop - hueBreak > 0.2f, "mood hue moves warmer from breakdown to drop");
    }

    private static void TestBackdropVariety()
    {
        var rng = new Random();
        var styles = new HashSet<int>();
        var seen = new HashSet<string>();
        var prev = Render.BackdropGenome.Random(rng);
        for (int i = 0; i < 300; i++)
        {
            var g = Render.BackdropGenome.Random(rng, prev.Style);
            Check(g.Style != prev.Style, "consecutive backdrops change style");
            styles.Add(g.Style);
            string key = $"{g.Style}:{g.Params}:{g.PaletteOffset}";
            Check(seen.Add(key), "no repeated backdrop genome");
            prev = g;
        }
        Console.WriteLine($"  backdrops: 300 unique genomes over {styles.Count} styles");
        Check(styles.Count == Render.BackdropGenome.StyleCount, "every style appears");
    }

    private static void TestRacerFeel()
    {
        var audio = new AudioState();
        var world = new GameWorld(new RunConfig { Seed = 31 });
        world.StartGrace = 0f;
        float dt = 1f / 60f;
        // full throttle for 2 s: top speed reached and flow builds
        for (int t = 0; t < 120; t++)
            world.Step(dt, audio, new InputFrame(new Vector2(1, 0.2f), new Vector2(1, 0), false, false, false, false));
        Check(world.Player.SpeedFrac > 0.95f, "ship reaches top speed");
        float built = world.Flow;
        Check(built > 0.15f, "flow builds while flying fast");
        // let go: the ship glides rather than stopping dead
        world.Step(dt * 6, audio, InputFrame.Empty);
        Check(world.Player.SpeedFrac > 0.5f, "momentum carries the ship");
        world.Player.Invuln = 0f;
        world.HurtPlayer();
        Check(world.Flow < built * 0.5f, "getting hit drains flow");
        // enemy fire is faster than the original tuning
        world.SpawnEnemyBullet(Vector2.Zero, new Vector2(200, 0), 5f, 0.5f, BulletKind.Orb);
        Check(world.EnemyBullets[^1].Vel.Length() >= 200f * 1.7f, "enemy bullets fly faster");
        Console.WriteLine($"  racer: top={Player.TopSpeed} flowAfter2s={built:0.00} bulletPace={World.World.BulletPace}");
    }

    /// <summary>Loud, clipped masters must not flatten the visuals (the old analyser pinned 88% of frames).</summary>
    private static void TestLoudAudioStaysReactive()
    {
        var normal = BeatBench.MeasureDynamics(1f);
        var clipped = BeatBench.MeasureDynamics(20f);
        var quiet = BeatBench.MeasureDynamics(0.05f);
        Console.WriteLine($"  dynamics: bass std {quiet.bassStd:0.00}/{normal.bassStd:0.00}/{clipped.bassStd:0.00} energy std {quiet.energyStd:0.00}/{normal.energyStd:0.00}/{clipped.energyStd:0.00} pinned {clipped.pinned:P0} (quiet/normal/clipped)");
        foreach (var d in new[] { quiet, normal, clipped })
        {
            Check(d.bassStd > 0.07f && d.midStd > 0.05f && d.highStd > 0.05f, "bands keep moving at any level");
            Check(d.energyStd > 0.05f, "energy keeps moving at any level");
            Check(d.pinned < 0.1f, "values do not pin at maximum");
            Check(d.kicks > 10 && d.snares > 10 && d.hats > 10, "kick, snare and hat onsets detected");
        }
    }
    private static GameWorld RunSong(float intensity, float build, float breakdown, bool dropAt5s, float seconds, out int maxDropLasers, out float minWarm)
    {
        var audio = new AudioState();
        var visual = new SynthVisual();
        var world = new GameWorld(new RunConfig { Seed = 0x50E6UL });
        world.Player.InvincibleDemo = true;
        world.BeginWave(3);
        float dt = 1f / 60f;
        maxDropLasers = 0;
        minWarm = 99f;
        var seen = new HashSet<LaserBeam>();
        for (int t = 0; t < (int)(seconds * 60); t++)
        {
            audio.ClearBeatFlags();
            audio.Time += dt;
            visual.Advance(dt, audio);
            world.SetMusicCue(intensity, build, breakdown, dropAt5s && t == 300);
            if (world.Phase == GamePhase.Attuning && world.Offers.Count > 0) world.ChooseAttunement(0);
            world.Step(dt, audio, InputFrame.Empty);
            int bossLasers = 0;
            foreach (var l in world.Lasers)
            {
                if (!l.BossLaser) continue;
                bossLasers++;
                if (seen.Add(l)) minWarm = MathF.Min(minWarm, l.Warmup);
            }
            maxDropLasers = Math.Max(maxDropLasers, bossLasers);
        }
        return world;
    }

    private static void TestSongLasers()
    {
        var loud = RunSong(0.9f, 0f, 0f, false, 40f, out _, out float warmLoud);
        var quiet = RunSong(0.9f, 0f, 1f, false, 40f, out _, out _);
        var drop = RunSong(0.3f, 0f, 0f, true, 12f, out int dropBeams, out float warmDrop);
        var build = RunSong(0.3f, 0.9f, 0f, false, 30f, out _, out _);
        Console.WriteLine($"  song lasers: high-energy volleys={loud.SongLaserVolleys} breakdown={quiet.SongLaserVolleys} build={build.SongLaserVolleys} dropBeams={dropBeams} minWarmup={MathF.Min(warmLoud, warmDrop):0.00}s");
        Check(loud.SongLaserVolleys >= 3, "high-energy sections fire laser patterns");
        Check(quiet.SongLaserVolleys == 0, "breakdowns stay laser-free");
        Check(build.SongLaserVolleys >= 3, "build-ups sweep scan beams");
        Check(dropBeams >= 6, "the drop fires a six-beam starburst");
        Check(MathF.Min(warmLoud, warmDrop) >= 0.9f, "every song laser warns for two beats");
    }

    private static void TestNewEnemies()
    {
        var audio = new AudioState();
        var visual = new SynthVisual();
        var world = new GameWorld(new RunConfig { Seed = 0xE11E5UL });
        world.Player.InvincibleDemo = true;
        world.BeginWave(9);
        world.ClearSpawnQueueForCapture();
        world.SpawnForCapture(EnemyRole.Weaver, world.Player.Pos + new Vector2(500, 0));
        world.SpawnForCapture(EnemyRole.Mortar, world.Player.Pos + new Vector2(-650, 0));
        world.SpawnForCapture(EnemyRole.Phantom, world.Player.Pos + new Vector2(0, 450));
        world.SpawnForCapture(EnemyRole.Prism, world.Player.Pos + new Vector2(0, -480));
        Enemy Find(EnemyRole r) => world.Enemies.First(e => e.Role == r);
        var phantom = Find(EnemyRole.Phantom);
        Vector2 phantomStart = phantom.Pos;
        bool sawStrike = false, sawPrismBeam = false, phantomMoved = false, sawMine = false;
        float dt = 1f / 60f;
        for (int t = 0; t < 60 * 12; t++)
        {
            audio.ClearBeatFlags();
            audio.Time += dt;
            visual.Advance(dt, audio);
            world.Step(dt, audio, InputFrame.Empty);
            sawStrike |= world.Strikes.Count > 0;
            sawPrismBeam |= world.Lasers.Any(l => MathF.Abs(l.Length - 560f) < 1f);
            sawMine |= world.EnemyBullets.Any(b => b.Kind == BulletKind.Orb && b.Vel.Length() < 60f);
            if (!phantom.Dead && M.Dist(phantom.Pos, phantomStart) > 150f) phantomMoved = true;
            foreach (var e in world.Enemies) Check(float.IsFinite(e.Pos.X) && float.IsFinite(e.Pos.Y), "new enemy positions finite");
        }
        Console.WriteLine($"  new enemies: mortarStrike={sawStrike} prismBeam={sawPrismBeam} phantomBlink={phantomMoved} weaverMine={sawMine}");
        Check(sawStrike, "mortar lobs telegraphed strikes");
        Check(sawPrismBeam, "prism projects its short beam");
        Check(phantomMoved, "phantom teleports");
        Check(sawMine, "weaver lays slow mines");
    }

    private static void TestSfx()
    {
        foreach (SfxType type in Enum.GetValues<SfxType>())
        {
            var (peak, rms, finite) = SfxProbe.Render(type);
            Check(finite, type + " finite");
            Check(peak > 0.05f && peak < 1f, type + " level in range");
            Check(rms > 0.002f, type + " audible");
        }
        Console.WriteLine("  sfx: all " + Enum.GetValues<SfxType>().Length + " effects finite, audible, unclipped");
    }

    private static RunSummary TelemetryRun(ulong seed, int wave, long score, int combo, bool daily = false) => new()
    {
        Seed = seed,
        Daily = daily,
        Wave = wave,
        Score = score,
        MaxCombo = combo,
        Kills = wave * 10,
        Grazes = wave * 3,
        Duration = wave * 30f,
        Movement = wave % 5,
        MovementName = "CITY",
        Attunements = wave / 2,
        Modifiers = daily ? [] : ["metronome"],
    };

    /// <summary>TASK-001: BestCombo, RecentRuns flight recorder (cap, order, persistence) and personal-best flags.</summary>
    private static void TestRunTelemetry()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nullwave-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "save.json");
        try
        {
            var meta = MetaProgress.Load(path);
            Check(meta.BestCombo == 0 && meta.RecentRuns.Count == 0, "fresh archive has no chain or history");
            Check(!meta.LastRecords.Any, "no records before any run");

            // first run sets every best (no previous best -> banner without deltas)
            meta.RecordRun(TelemetryRun(0x1001, 5, 10_000, 12));
            var first = meta.LastRecords;
            Check(first.Score && first.Wave && first.Combo, "first run breaks score, wave and chain");
            Check(first.PreviousScore == 0 && first.PreviousWave == 0 && first.PreviousCombo == 0, "first run has zero previous bests");
            Check(meta.BestCombo == 12, "best combo recorded");
            Check(meta.RecentRuns.Count == 1 && meta.RecentRuns[0].Score == 10_000, "first run in history");
            Check(meta.RecentRuns[0].Records == (RunRecords.Score | RunRecords.Wave | RunRecords.Combo), "history keeps record flags");
            Check(meta.RecentRuns[0].Seed == SeedCode.ToCode(0x1001) && meta.RecentRuns[0].Modifiers == 1, "history keeps seed code and modifier count");
            Check(Screens.RecordBanner(first, TelemetryRun(0x1001, 5, 10_000, 12)) == "NEW RECORDS // SCORE // WAVE // CHAIN", "first-run banner has no deltas");

            // worse run: no records, bests untouched, newest first
            meta.RecordRun(TelemetryRun(0x1002, 3, 4_000, 7));
            Check(!meta.LastRecords.Any, "worse run breaks nothing");
            Check(meta.BestScore == 10_000 && meta.BestWave == 5 && meta.BestCombo == 12, "bests unchanged by a worse run");
            Check(meta.RecentRuns[0].Seed == SeedCode.ToCode(0x1002) && meta.RecentRuns[1].Seed == SeedCode.ToCode(0x1001), "history is newest first");
            Check(meta.RecentRuns[0].Records == RunRecords.None, "worse run stored without flags");

            // equal is not a record; only strictly better counts, and only the stats that improved
            meta.RecordRun(TelemetryRun(0x1003, 5, 10_000, 19));
            var chainOnly = meta.LastRecords;
            Check(chainOnly.Records == RunRecords.Combo, "tying score/wave is not a record, longer chain is");
            Check(chainOnly.PreviousCombo == 12 && meta.BestCombo == 19, "previous chain kept for the delta, best chain updated");
            var chainSummary = TelemetryRun(0x1003, 5, 10_000, 19);
            Check(Screens.RecordBanner(chainOnly, chainSummary) == "NEW RECORD // CHAIN +7", "single-record banner shows the delta");

            meta.RecordRun(TelemetryRun(0x1004, 9, 25_000, 3, daily: true));
            var scoreWave = meta.LastRecords;
            Check(scoreWave.Records == (RunRecords.Score | RunRecords.Wave), "score + wave record without chain");
            Check(scoreWave.PreviousScore == 10_000 && scoreWave.PreviousWave == 5, "previous score/wave captured before update");
            string banner = Screens.RecordBanner(scoreWave, TelemetryRun(0x1004, 9, 25_000, 3, daily: true));
            Check(banner.StartsWith("NEW RECORDS // SCORE +") && banner.EndsWith("// WAVE +4"), "multi-record banner lists score and wave deltas");
            string entry = Screens.RecentSignalEntry(meta.RecentRuns[0]);
            Check(entry.StartsWith("D " + SeedCode.ToCode(0x1004) + " W09 ") && entry.EndsWith(" CH3*"), "archive strip entry shows daily tag, seed, wave, chain and record star");
            Check(!Screens.RecentSignalEntry(meta.RecentRuns[2]).EndsWith("*"), "non-record entry has no star");

            // cap at 8, oldest dropped
            for (int i = 0; i < 10; i++) meta.RecordRun(TelemetryRun(0x2000UL + (ulong)i, 1 + i % 3, 100 + i, 1));
            Check(meta.RecentRuns.Count == MetaProgress.RecentRunCap, "history capped at 8");
            Check(meta.RecentRuns[0].Seed == SeedCode.ToCode(0x2009) && meta.RecentRuns[^1].Seed == SeedCode.ToCode(0x2002), "cap keeps the newest 8 in order");
            Check(meta.Runs == 14, "aggregate run count unaffected by the cap");

            // persistence round-trip
            var loaded = MetaProgress.Load(path);
            Check(loaded.BestCombo == 19, "best combo persisted");
            Check(loaded.RecentRuns.Count == 8, "history persisted");
            for (int i = 0; i < 8; i++)
            {
                Check(loaded.RecentRuns[i].Seed == meta.RecentRuns[i].Seed, "persisted history order " + i);
                Check(loaded.RecentRuns[i].Score == meta.RecentRuns[i].Score && loaded.RecentRuns[i].Wave == meta.RecentRuns[i].Wave, "persisted history stats " + i);
            }
            Check(!loaded.LastRecords.Any, "record flags are not persisted");

            // older save.json (no BestCombo / RecentRuns): loads, and its first chain is adopted silently
            string legacyPath = Path.Combine(dir, "legacy.json");
            File.WriteAllText(legacyPath, "{\"Version\":1,\"Runs\":5,\"BestWave\":8,\"BestScore\":30000,\"UnlockedModifiers\":[\"metronome\"]}");
            var legacy = MetaProgress.Load(legacyPath);
            Check(legacy.Runs == 5 && legacy.BestScore == 30_000, "legacy save loads");
            Check(legacy.BestCombo == 0 && legacy.RecentRuns.Count == 0, "legacy save gets empty telemetry");
            legacy.RecordRun(TelemetryRun(0x3001, 4, 9_000, 25));
            Check(legacy.LastRecords.Records == RunRecords.None, "legacy first chain is not called a record");
            Check(legacy.BestCombo == 25 && legacy.RecentRuns.Count == 1, "legacy first chain becomes the baseline");
            legacy.RecordRun(TelemetryRun(0x3002, 4, 9_000, 30));
            Check(legacy.LastRecords.Records == RunRecords.Combo, "legacy save flags chains after the baseline");

            // oversized / null-polluted history in a hand-edited save is sanitised on load
            string bigPath = Path.Combine(dir, "big.json");
            var runs = string.Join(",", Enumerable.Range(0, 12).Select(i => "{\"Seed\":\"S" + i + "\",\"Score\":" + i + "}"));
            File.WriteAllText(bigPath, "{\"RecentRuns\":[null," + runs + "]}");
            var big = MetaProgress.Load(bigPath);
            Check(big.RecentRuns.Count == 8 && big.RecentRuns[0].Seed == "S0", "oversized history trimmed to newest 8, nulls dropped");

            // integration: a real simulated death produces a summary the recorder accepts
            var world = new GameWorld(new RunConfig { Seed = 0x7E1E });
            world.BeginWave(3);
            world.StartGrace = 0f;
            world.Player.Health = 1;
            world.MaxCombo = 33;
            world.Score = 999_999;
            world.ForceDeathForTest();
            Check(world.Summary != null, "forced death builds a run summary");
            meta.RecordRun(world.Summary!);
            Check(meta.LastRecords.Score && meta.LastRecords.Combo, "simulated run breaks score and chain");
            Check(meta.RecentRuns[0].Seed == SeedCode.ToCode(0x7E1E) && meta.RecentRuns[0].MaxCombo == 33 && meta.RecentRuns[0].Wave == world.Summary!.Wave, "simulated run lands in history");
            Check(meta.BestCombo == 33 && meta.RecentRuns.Count == 8, "simulated run updates chain, cap holds");
            Console.WriteLine($"  telemetry: history={meta.RecentRuns.Count}/{MetaProgress.RecentRunCap} bestChain={meta.BestCombo} lastBanner=\"{Screens.RecordBanner(meta.LastRecords, world.Summary!)}\"");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp cleanup is best effort */ }
        }
    }

    /// <summary>
    /// TASK-001: the render-state contract PostFx/Renderer restore, checked as blend maths (no GL
    /// context in the self test; --smoke reads the live state back).
    /// </summary>
    private static void TestRenderStateContract()
    {
        var b = RenderBaseline.Default;
        Check(b.Blend && !b.DepthTest, "baseline: blend on, depth off");
        Check(b.Mode == BlendMode.Alpha, "baseline: standard alpha blend");
        Check(b.ActiveUnit == 0 && b.VertexArray == 0, "baseline: unit 0 active, no VAO");
        Check(b.SamplerUnits == PostFx.SamplerUnits && PostFx.SamplerUnits == 4, "baseline clears every unit the post chain binds (scene + 3 bloom)");

        int legacyTranslucent = 0;
        for (int i = 0; i <= 20; i++)
        {
            float a = i / 20f;
            // the composite writes alpha 1; UI layers must keep it there
            Check(MathF.Abs(BlendMode.Alpha.ResultAlpha(a, 1f) - 1f) < 1e-6f, "ui alpha blend keeps opaque dest alpha at " + a);
            Check(MathF.Abs(BlendMode.Additive.ResultAlpha(a, 1f) - 1f) < 1e-6f, "ui glow blend keeps opaque dest alpha at " + a);
            // stacking several translucent layers still stays opaque
            float dst = 1f;
            for (int layer = 0; layer < 4; layer++) dst = BlendMode.Alpha.ResultAlpha(a * 0.5f, BlendMode.Additive.ResultAlpha(a, dst));
            Check(MathF.Abs(dst - 1f) < 1e-6f, "stacked ui layers stay opaque at " + a);
            if (BlendMode.LegacyAlpha.ResultAlpha(a, 1f) < 0.999f) legacyTranslucent++;

            // colour maths is unchanged from the old glBlendFunc
            var src = new Vector3(0.9f, 0.4f, 0.1f);
            var dstC = new Vector3(0.1f, 0.2f, 0.7f);
            Check(Vector3.Distance(BlendMode.Alpha.ResultColor(src, a, dstC, 1f), BlendMode.LegacyAlpha.ResultColor(src, a, dstC, 1f)) < 1e-6f, "alpha colour unchanged at " + a);
            Check(Vector3.Distance(BlendMode.Additive.ResultColor(src, a, dstC, 1f), BlendMode.LegacyAdditive.ResultColor(src, a, dstC, 1f)) < 1e-6f, "additive colour unchanged at " + a);
            Check(Vector3.Distance(BlendMode.Alpha.ResultColor(src, a, dstC, 1f), Vector3.Lerp(dstC, src, a)) < 1e-5f, "alpha colour is a lerp at " + a);
        }
        Check(legacyTranslucent >= 19, "legacy blend would have left the backbuffer translucent (regression guard)");
        Check(MathF.Abs(BlendMode.LegacyAlpha.ResultAlpha(0.5f, 1f) - 0.75f) < 1e-6f, "legacy blend: half-alpha UI left dest alpha 0.75");
        Console.WriteLine($"  render state: baseline={b.Mode.SrcColor}/{b.Mode.DstColor}+{b.Mode.SrcAlphaFactor}/{b.Mode.DstAlphaFactor} units={b.SamplerUnits} legacyTranslucentSamples={legacyTranslucent}/21");
    }

    /// <summary>TASK-002: 3-letter attunement tags, zero-allocation GetActive query, and ShowAttunements setting persistence.</summary>
    private static void TestAttunementLoadout()
    {
        Check(AttunementCatalog.All.Length == 12, "all 12 attunements in catalog");
        var tags = new HashSet<string>();
        var ids = new HashSet<string>();
        for (int i = 0; i < AttunementCatalog.All.Length; i++)
        {
            var def = AttunementCatalog.All[i];
            Check(!string.IsNullOrWhiteSpace(def.Id) && ids.Add(def.Id), "unique attunement id " + def.Id);
            Check(def.Tag.Length == 3 && def.Tag == def.Tag.ToUpperInvariant(), "3-char uppercase tag for " + def.Id);
            Check(tags.Add(def.Tag), "unique 3-char tag " + def.Tag);
            Check(def.MaxStacks is 3 or 4, "valid max stacks for " + def.Id);
        }

        var player = new World.Player();
        Span<ActiveAttunement> buf = stackalloc ActiveAttunement[12];
        Check(AttunementCatalog.GetActive(player, buf) == 0 && player.AttunementCount == 0, "fresh player has 0 active attunements");

        // apply out of catalog order; GetActive must return in deterministic catalog order
        AttunementCatalog.Apply(player, "grazer");
        AttunementCatalog.Apply(player, "split");
        AttunementCatalog.Apply(player, "split");
        AttunementCatalog.Apply(player, "homing");
        AttunementCatalog.Apply(player, "homing");
        AttunementCatalog.Apply(player, "homing");
        AttunementCatalog.Apply(player, "pierce");

        int count = AttunementCatalog.GetActive(player, buf);
        Check(count == 4, "4 distinct active attunements");
        Check(player.AttunementCount == 7, "7 total attunement stacks");
        Check(buf[0].Id == "split" && buf[0].Tag == "SPL" && buf[0].Level == 2 && buf[0].MaxStacks == 4 && buf[0].CatalogIndex == 0, "split entry in catalog order");
        Check(buf[1].Id == "pierce" && buf[1].Tag == "PRC" && buf[1].Level == 1 && buf[1].MaxStacks == 4 && buf[1].CatalogIndex == 2, "pierce entry in catalog order");
        Check(buf[2].Id == "homing" && buf[2].Tag == "HOM" && buf[2].Level == 3 && buf[2].MaxStacks == 3 && buf[2].CatalogIndex == 6, "homing entry maxed at 3/3");
        Check(buf[3].Id == "grazer" && buf[3].Tag == "GRZ" && buf[3].Level == 1 && buf[3].MaxStacks == 3 && buf[3].CatalogIndex == 11, "grazer entry at index 11");

        int sumLevels = 0;
        for (int i = 0; i < count; i++) sumLevels += buf[i].Level;
        Check(sumLevels == player.AttunementCount, "sum of active levels equals Player.AttunementCount");

        // smaller destination span safely caps output without throwing
        Span<ActiveAttunement> small = stackalloc ActiveAttunement[2];
        Check(AttunementCatalog.GetActive(player, small) == 2 && small[0].Tag == "SPL" && small[1].Tag == "PRC", "bounded span write");

        // ShowAttunements setting defaults to true and persists across save/load
        string dir = Path.Combine(Path.GetTempPath(), "nullwave-attune-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "save.json");
            var meta = MetaProgress.Load(path);
            Check(meta.Settings.ShowAttunements, "ShowAttunements defaults to true");
            meta.Settings.ShowAttunements = false;
            meta.Save();
            var reloaded = MetaProgress.Load(path);
            Check(!reloaded.Settings.ShowAttunements, "ShowAttunements=false persisted");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best-effort temp cleanup */ }
        }
        Console.WriteLine($"  loadout: tags={string.Join(",", AttunementCatalog.All.Select(a => a.Tag))} active={count} stacks={player.AttunementCount}");
    }

    /// <summary>TASK-003: SeedCode normalization/parsing, recorded seed replay resolution, and custom 8-char seed entry.</summary>
    private static void TestSeedReplayAndEntry()
    {
        Check(SeedCode.NormalizeChar('i') == '1' && SeedCode.NormalizeChar('I') == '1', "I normalizes to 1");
        Check(SeedCode.NormalizeChar('o') == '0' && SeedCode.NormalizeChar('O') == '0', "O normalizes to 0");
        Check(SeedCode.NormalizeChar('a') == 'A' && SeedCode.NormalizeChar('9') == '9', "alphanumeric normalized uppercase");
        Check(SeedCode.NormalizeChar('-') == '\0' && SeedCode.NormalizeChar(' ') == '\0', "non-alphabet rejected");

        Check(!SeedCode.TryParse("   ---  ", out _, out _), "empty/invalid seed rejected");
        Check(SeedCode.TryParse("nw-0111t20e", out ulong parsed, out string canonical), "NW- prefix and lowercase accepted");
        Check(canonical == "0111T20E" && SeedCode.ToCode(parsed) == canonical, "canonical round-trip exact");
        Check(SeedCode.TryParse("i-o-a-b", out _, out string shortCode) && shortCode.Length == SeedCode.CodeLength, "short code padded to 8-char canonical");
        Check(SeedCode.NormalizeCode("0111o20i") == SeedCode.NormalizeCode("01110201"), "O and I match 0 and 1");

        string dir = Path.Combine(Path.GetTempPath(), "nullwave-seed-replay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "save.json");
            var meta = MetaProgress.Load(path);
            Check(meta.GetReplayableSeeds().Count == 0, "fresh archive has no replayable seeds");

            // High 64-bit seed (> 34^8) to verify exact SeedValue preservation alongside 8-char code lookup
            ulong s1 = 0xCAFEBABE_5EEDC0DEUL;
            ulong s2 = 0xDEADC0DE_12345678UL;
            string c1 = SeedCode.ToCode(s1);
            string c2 = SeedCode.ToCode(s2);

            meta.RecordRun(TelemetryRun(s1, 7, 24_000, 18));
            meta.RecordRun(TelemetryRun(s2, 11, 62_500, 31, daily: true));
            meta.RecordRun(TelemetryRun(s1, 9, 38_400, 22));

            var seeds = meta.GetReplayableSeeds();
            Check(seeds.Count == 2 && seeds[0] == c1 && seeds[1] == c2, "replayable seeds ordered newest first without duplicates");
            Check(meta.TryGetSeedInfo(c1.ToLowerInvariant(), out int bw1, out long bs1, out int r1, out bool d1), "seed info lookup case-insensitive");
            Check(bw1 == 9 && bs1 == 38_400 && r1 == 2 && !d1, "seed 1 aggregated best wave, best score and run count");
            Check(meta.TryGetSeedInfo(c2, out int bw2, out long bs2, out int r2, out bool d2) && bw2 == 11 && bs2 == 62_500 && r2 == 1 && d2, "seed 2 daily info");
            Check(!meta.TryGetSeedInfo("ZZZZZZZZ", out _, out _, out _, out _), "unplayed custom seed returns false for info");

            // Persist and reload: exact 64-bit SeedValue survives JSON serialization
            var reloaded = MetaProgress.Load(path);
            Check(reloaded.ResolveSeed(c1) == s1 && reloaded.ResolveSeed(c2) == s2, "exact 64-bit SeedValue resolved after reload");

            // Custom typed seed code (never played before) resolves deterministically via FromCode
            const string typedRaw = "c0ff-ee1i";
            Check(SeedCode.TryParse(typedRaw, out ulong customExpected, out string customCanonical), "custom typed seed parses");
            Check(reloaded.ResolveSeed(typedRaw) == customExpected, "custom seed resolves to deterministic FromCode value");

            // Verify starting a GameWorld with the replayed seed produces identical layout and enemy waves
            var worldA = new GameWorld(new RunConfig { Seed = reloaded.ResolveSeed(c1) });
            var worldB = new GameWorld(new RunConfig { Seed = s1 });
            worldA.BeginWave(4);
            worldB.BeginWave(4);
            Check(worldA.Seed == worldB.Seed && worldA.LayoutName == worldB.LayoutName && worldA.Obstacles.Count == worldB.Obstacles.Count, "replayed seed reproduces exact arena layout");
            for (int i = 0; i < worldA.Obstacles.Count; i++)
                Check(worldA.Obstacles[i].Pos == worldB.Obstacles[i].Pos, "replayed obstacle position " + i);

            Console.WriteLine($"  seed replay: replayable={seeds.Count} top={c1} (W{bw1:00} {bs1:N0} runs={r1}) custom={customCanonical}");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best-effort temp cleanup */ }
        }
    }

    /// <summary>TASK-004: Phase Sync timing window boundaries and on-beat vs off-beat dash rewards.</summary>
    private static void TestPhaseSync()
    {
        var probe = new GameWorld(new RunConfig { Seed = 0x51C0UL });
        probe.SetBeatClockForTest(4.00, 120f);
        Check(probe.IsInPhaseSyncWindow(out float err0) && err0 < 1f, "BeatPhase 0.00 is in Phase Sync window");

        probe.SetBeatClockForTest(4.08, 120f);
        Check(probe.IsInPhaseSyncWindow(out float err8) && MathF.Abs(err8 - 40f) < 1f, "BeatPhase 0.08 (~40ms at 120 BPM) is in window");

        probe.SetBeatClockForTest(4.92, 120f);
        Check(probe.IsInPhaseSyncWindow(out float err92) && MathF.Abs(err92 - 40f) < 1f, "BeatPhase 0.92 (~40ms before beat) is in window");

        probe.SetBeatClockForTest(4.50, 120f);
        Check(!probe.IsInPhaseSyncWindow(out float err50) && MathF.Abs(err50 - 250f) < 1f, "BeatPhase 0.50 (~250ms) is outside window");

        probe.SetBeatClockForTest(4.25, 120f);
        Check(!probe.IsInPhaseSyncWindow(out float err25) && MathF.Abs(err25 - 125f) < 1f, "BeatPhase 0.25 (~125ms) is outside window");

        // Off-beat vs On-beat dash comparison
        var audioOff = new AudioState { Bpm = 120f };
        var audioOn = new AudioState { Bpm = 120f };
        var wOff = new GameWorld(new RunConfig { Seed = 0x9999UL });
        var wOn = new GameWorld(new RunConfig { Seed = 0x9999UL });

        float dt = 1f / 60f;
        // AdvanceBeatClock adds dt * 120 / 60 = 1/30 (~0.0333) beat per step
        wOff.SetBeatClockForTest(4.50, 120f);
        wOn.SetBeatClockForTest(3.98, 120f);

        var dashInput = new InputFrame(new Vector2(1, 0), new Vector2(1, 0), false, true, false, false);
        wOff.Step(dt, audioOff, dashInput);
        wOn.Step(dt, audioOn, dashInput);

        Check(wOff.Player.IsDashing, "off-beat dash executes normally");
        Check(wOff.PhaseSyncs == 0 && wOff.Player.PhaseSyncs == 0, "off-beat dash does not increment PhaseSyncs");
        Check(wOff.Player.PhaseSyncFlash == 0f, "off-beat dash does not flash Phase Sync");
        Check(wOff.Player.Resonance == 0f, "off-beat dash grants no resonance");
        Check(MathF.Abs(wOff.Player.DashCooldown - wOff.Player.DashCooldownMax) < 1e-4f, "off-beat dash uses baseline cooldown");

        Check(wOn.Player.IsDashing, "on-beat dash executes");
        Check(wOn.PhaseSyncs == 1 && wOn.Player.PhaseSyncs == 1, "on-beat dash increments PhaseSyncs");
        Check(wOn.Player.PhaseSyncFlash > 0.9f, "on-beat dash triggers PhaseSyncFlash");
        Check(wOn.Player.LastPhaseSyncErrorMs <= GameWorld.PhaseSyncWindowMs, "recorded sync error within 65ms");
        Check(wOn.Flow >= wOff.Flow + 0.045f, "on-beat dash grants +0.05 Flow synergy boost");
        Check(wOn.Player.Resonance >= 4.9f, "on-beat dash grants +5 Resonance");
        Check(wOn.Player.DashCooldown < wOff.Player.DashCooldown, "on-beat dash reduces dash cooldown");
        Check(wOn.Score > wOff.Score, "on-beat dash awards score bonus");
        Check(wOn.FloatTexts.Any(t => t.Text == "PHASE SYNC"), "on-beat dash spawns PHASE SYNC float text");
        Check(wOn.Shockwaves.Count > wOff.Shockwaves.Count, "on-beat dash spawns expanding shockwave ring");

        // Verify RunSummary captures PhaseSyncs on death
        wOn.StartGrace = 0f;
        wOn.Player.Invuln = 0f;
        wOn.Player.IsDashing = false;
        wOn.Player.Health = 1;
        wOn.ForceDeathForTest();
        Check(wOn.Summary != null && wOn.Summary!.PhaseSyncs == 1, "RunSummary records PhaseSyncs");

        Console.WriteLine($"  phase sync: err0={err0:0.0}ms err40={err8:0.0}ms syncs={wOn.PhaseSyncs} flowGain={wOn.Flow - wOff.Flow:0.000} res={wOn.Player.Resonance:0.0} cd={wOn.Player.DashCooldown:0.00}s vs {wOff.Player.DashCooldown:0.00}s");
    }

    /// <summary>TASK-005: Choir boss phase subtitles, 3-vs-4 phase threshold transitions, fair bullet-clear ring, and monotonic phase guard.</summary>
    private static void TestBossPhaseChoreography()
    {
        var subtitles = new[]
        {
            GameWorld.BossPhaseSubtitle(0, 3),
            GameWorld.BossPhaseSubtitle(1, 3),
            GameWorld.BossPhaseSubtitle(2, 3),
            GameWorld.BossPhaseSubtitle(3, 4),
        };
        Check(subtitles.All(s => !string.IsNullOrWhiteSpace(s)) && subtitles.Distinct().Count() == 4, "4 distinct boss phase subtitles");
        Check(GameWorld.ComputeDesiredBossPhase(0.95f, 3) == 0 && GameWorld.ComputeDesiredBossPhase(0.60f, 3) == 1 && GameWorld.ComputeDesiredBossPhase(0.30f, 3) == 2, "3-phase threshold computation");
        Check(GameWorld.ComputeDesiredBossPhase(0.70f, 4) == 1 && GameWorld.ComputeDesiredBossPhase(0.45f, 4) == 2 && GameWorld.ComputeDesiredBossPhase(0.20f, 4) == 3, "4-phase BLIND CHOIR threshold computation");

        // 3-phase Choir transition test
        var audio = new AudioState { Bpm = 120f };
        var world = new GameWorld(new RunConfig { Seed = 0xB055UL });
        world.Player.InvincibleDemo = true;
        world.BeginWave(5);
        world.ClearSpawnQueueForCapture();
        world.SpawnForCapture(EnemyRole.Boss, Vector2.Zero);
        var boss = world.BossEnemy!;
        boss.Spawning = false;
        boss.SpawnTimer = 0f;
        boss.BossAttackBeats = 0.4f;

        // Spawn enemy bullets both inside (<580) and outside (>700) BossPhaseClearRadius
        world.SpawnEnemyBullet(boss.Pos + new Vector2(180f, 0f), Vector2.Zero, 6f, 0.9f, BulletKind.Orb, 4f);
        world.SpawnEnemyBullet(boss.Pos + new Vector2(0f, -420f), Vector2.Zero, 6f, 0.9f, BulletKind.Orb, 4f);
        world.SpawnEnemyBullet(boss.Pos + new Vector2(820f, 0f), Vector2.Zero, 6f, 0.9f, BulletKind.Orb, 4f);
        Check(world.EnemyBullets.Count == 3, "3 test enemy bullets placed");

        // Drop boss HP into Phase II (< 2/3)
        boss.Hp = boss.MaxHp * 0.60f;
        world.Step(1f / 60f, audio, InputFrame.Empty);

        Check(boss.BossPhase == 1 && boss.HighestTriggeredPhase == 1, "boss transitioned to phase 1");
        Check(boss.PhaseBreakTimer > 1.0f && boss.BossInvuln > 1.0f, "phase transition sets PhaseBreakTimer and BossInvuln");
        Check(world.EventText.Contains("HARMONIC CONVERGENCE"), "phase transition emits phase subtitle event");
        Check(world.Shockwaves.Count >= 2, "phase transition emits dual shockwaves");
        Check(world.EnemyBullets.Count == 1 && M.Dist(world.EnemyBullets[0].Pos, boss.Pos) > 700f, "nearby bullets cleared while far bullet survives");

        // Stepping again at same HP must not re-trigger phase 1
        int shocksBefore = world.Shockwaves.Count;
        world.Step(1f / 60f, audio, InputFrame.Empty);
        Check(boss.BossPhase == 1 && boss.HighestTriggeredPhase == 1, "monotonic guard prevents phase re-trigger");
        Check(world.Shockwaves.Count == shocksBefore, "no duplicate phase shockwave");

        // Wait out BossInvuln and verify telegraph ramps before attack
        float maxBossTelegraph = 0f;
        for (int i = 0; i < 110; i++)
        {
            world.Step(1f / 60f, audio, InputFrame.Empty);
            maxBossTelegraph = MathF.Max(maxBossTelegraph, boss.Telegraph);
        }
        Check(boss.BossInvuln <= 0f, "boss invuln expires");
        Check(maxBossTelegraph > 0.5f, "boss telegraph ramps over final beat before volley");

        // 4-phase BLIND CHOIR modifier test
        var blindWorld = new GameWorld(new RunConfig { Seed = 0xB11DUL, SelectedModifiers = ["blind-choir"] });
        Check(blindWorld.Mods.BossPhases == 4, "blind-choir sets 4 boss phases");
        blindWorld.Player.InvincibleDemo = true;
        blindWorld.BeginWave(5);
        blindWorld.ClearSpawnQueueForCapture();
        blindWorld.SpawnForCapture(EnemyRole.Boss, Vector2.Zero);
        var blindBoss = blindWorld.BossEnemy!;
        blindBoss.Spawning = false;
        blindBoss.Hp = blindBoss.MaxHp * 0.20f;
        blindWorld.Step(1f / 60f, audio, InputFrame.Empty);
        Check(blindBoss.BossPhase == 3 && blindBoss.HighestTriggeredPhase == 3, "blind-choir reaches phase 3 (IV)");
        Check(blindWorld.EventText.Contains("BLIND CHOIR"), "phase IV emits BLIND CHOIR subtitle");

        Console.WriteLine($"  boss choreography: clearRadius={GameWorld.BossPhaseClearRadius:0} phase={boss.BossPhase + 1}/{world.Mods.BossPhases} teleMax={maxBossTelegraph:0.00} blindPhase={blindBoss.BossPhase + 1}/{blindWorld.Mods.BossPhases}");
    }
}
