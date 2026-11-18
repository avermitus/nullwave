using System.Diagnostics;
using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;
using Nullwave.Render;
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
}
