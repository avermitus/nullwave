using System.Diagnostics;
using System.Numerics;
using NAudio.Wave;
using Nullwave.Audio;
using Nullwave.Core;
using Nullwave.Render;
using Nullwave.UI;
using Nullwave.World;
using GameWorld = Nullwave.World.World;

namespace Nullwave.Tools;

public static partial class SelfTest
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
            TestPlayerActionBuffer();
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
            TestElectronicAudio();
            TestRunTelemetry();
            TestRenderStateContract();
            TestAttunementLoadout();
            TestSeedReplayAndEntry();
            TestPhaseSync();
            TestBossPhaseChoreography();
            TestSpotifyIntegration();
            TestHardModifierRecordPreset();
            TestAimCameraAndLaserVisuals();
            TestBackdropAudioReactivity();
            TestAbyssalThemeAndLighting();
            TestAttunementSynergies();
            TestOverdriveAndHitStop();
            TestLookaheadAudioBuffer();
            Test3DBackdropAndReadability();
            TestMusicDrivenBackdrop();
            TestMusicAnalysis();
            TestMusicToWorlds();
            TestEnemiesChase();
            TestBeatPockets();
            TestMelodyPitch();
            TestGravityWells();
            TestMelodyRails();
            TestLiveStreamBuffer();
            TestSongRecords();
            TestInstrumentShips();
            TestInKeyNotes();
            TestSongBoss();
            TestRunRecap();
            TestRumbleAndCoach();
            TestEdmShow();
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

    private static void TestPlayerActionBuffer()
    {
        const float tick = 1f / 60f;
        var audio = new AudioState { Bpm = 120f };
        var world = new GameWorld(new RunConfig { Seed = 0xA6710UL });
        var actions = new PlayerActionBuffer();
        float accumulator = 0f;

        void Update(float dt, InputFrame frame) =>
            Game.StepPlaying(world, audio, dt, frame, true, ref accumulator, ref actions);

        var dash = new InputFrame(Vector2.UnitX, Vector2.UnitX, false, true, false, false);
        var burst = new InputFrame(Vector2.Zero, Vector2.UnitX, false, false, false, true);
        var dashAndBurst = new InputFrame(Vector2.UnitX, Vector2.UnitX, false, true, false, true);
        var held = new InputFrame(Vector2.UnitY, Vector2.UnitY, true, false, true, false);
        world.Player.Resonance = world.Player.ResonanceMax;
        Update(tick / 4f, dash);
        world.HitStop = tick;
        Update(tick / 4f, burst);
        Check(world.SessionTime == 0f && !world.Player.IsDashing && world.Bursts == 0,
            "dash and burst wait across zero-tick updates");

        Update(tick / 2f, InputFrame.Empty);
        Check(world.HitStop == 0f && !world.Player.IsDashing && world.Bursts == 0,
            "the tick that ends hit-stop does not consume dash or burst");
        Update(tick, held);
        Check(world.Player.IsDashing && world.Player.DashDir == Vector2.UnitY && world.Bursts == 1,
            "the first active tick delivers both presses with the latest movement");
        Check(world.Player.Focus > 0f && world.Player.ShotCounter > 0,
            "held focus and fire reach the same player tick");

        // A dash rejected on tick one would fire on tick two if its edge were replayed.
        world.HitStop = 0f;
        world.Player.IsDashing = false;
        world.Player.DashCooldown = 2f * tick;
        Update(3f * tick, dash);
        Check(world.Player.DashCooldown == 0f && !world.Player.IsDashing,
            "catch-up ticks do not retry a press after cooldown clears");

        world.Player.Resonance = world.Player.ResonanceMax;
        Update(tick / 2f, dashAndBurst);
        world.Phase = GamePhase.Attuning;
        Update(tick / 2f, InputFrame.Empty);
        world.Phase = GamePhase.Playing;
        Update(tick, InputFrame.Empty);
        Check(!world.Player.IsDashing && world.Bursts == 1,
            "a modal transition clears both pending presses");

        int wave = world.Wave;
        float playerTime = world.Player.Time;
        Update(tick / 2f, dashAndBurst);
        world.Phase = GamePhase.Intermission;
        world.IntermissionTimer = tick;
        Update(2f * tick, InputFrame.Empty);
        Check(world.Wave == wave + 1 && world.Player.Time > playerTime &&
            !world.Player.IsDashing && world.Bursts == 1,
            "intermission entry clears presses pending from the previous wave");

        playerTime = world.Player.Time;
        world.Phase = GamePhase.Intermission;
        world.IntermissionTimer = tick;
        Update(2f * tick, dashAndBurst);
        Check(world.Wave == wave + 2 && world.Player.Time > playerTime &&
            !world.Player.IsDashing && world.Bursts == 1,
            "taps during intermission do not reach the next wave in the same catch-up update");

        Update(tick, dashAndBurst);
        Check(world.Player.IsDashing && world.Bursts == 2,
            "fresh dash and burst presses work after intermission");
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
        Check(loaded.Settings.Bloom == GameSettings.MaxBloom, "settings clamped on load");
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
        var names = new HashSet<string>();
        var gens = new HashSet<int>();
        var atmos = new HashSet<int>();
        Render.BackdropGenome? prev = null;
        const int count = 1000;
        for (int i = 0; i < count; i++)
        {
            var g = Render.BackdropGenome.Random(rng, prev);
            Check(Render.BackdropGenome.Is3DOnly(g), "backdrop is one map-anchored 3D world");
            var l = g.Layers[0];
            Check(l.Generator is >= 0 and < Render.BackdropGenome.GeneratorCount, "scene in range");
            Check(g.Atmosphere is >= 0 and < Render.BackdropGenome.AtmosphereCount, "atmosphere in range");
            gens.Add(l.Generator);
            atmos.Add(g.Atmosphere);
            if (prev != null)
                Check(l.Generator != prev.Layers[0].Generator, "consecutive backdrops switch worlds");
            names.Add(g.Name + l.GenA + l.GenB + g.Global);
            prev = g;
        }
        Console.WriteLine($"  backdrops: {count} generated, {names.Count} distinct, {gens.Count}/{Render.BackdropGenome.GeneratorCount} worlds, {atmos.Count}/{Render.BackdropGenome.AtmosphereCount} atmospheres");
        Check(names.Count == count, "no two backdrops alike");
        Check(gens.Count == Render.BackdropGenome.GeneratorCount, "every world appears");
        Check(atmos.Count == Render.BackdropGenome.AtmosphereCount, "every atmosphere appears");
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

    /// <summary>TASK-006: Spotify desktop/web window-title parsing, FormatTrack truncation, and RunRecord Track persistence.</summary>
    private static void TestSpotifyIntegration()
    {
        // Desktop Idle & Ad
        Check(SpotifyBridge.ParseWindowTitle("").State == SpotifyPlaybackState.Idle, "empty desktop title -> Idle");
        Check(SpotifyBridge.ParseWindowTitle("Spotify").State == SpotifyPlaybackState.Idle, "Spotify -> Idle");
        Check(SpotifyBridge.ParseWindowTitle("Spotify Premium").State == SpotifyPlaybackState.Idle, "Spotify Premium -> Idle");
        Check(SpotifyBridge.ParseWindowTitle("Advertisement").State == SpotifyPlaybackState.Ad, "Advertisement -> Ad");

        // Desktop Playing (including hyphenated song titles)
        var t1 = SpotifyBridge.ParseWindowTitle("Svdden Death - Behemoth");
        Check(t1.IsPlaying && t1.Artist == "Svdden Death" && t1.Title == "Behemoth" && t1.DisplayTrack == "Svdden Death - Behemoth", "desktop artist-title parsed");

        var t2 = SpotifyBridge.ParseWindowTitle("Svdden Death - Behemoth - VIP");
        Check(t2.IsPlaying && t2.Artist == "Svdden Death" && t2.Title == "Behemoth - VIP" && t2.DisplayTrack == "Svdden Death - Behemoth - VIP", "hyphenated title preserved");

        // Browser Web Player variants
        var w1 = SpotifyBridge.ParseWindowTitle("Marauda - Blunder - Spotify - Google Chrome", isBrowserProcess: true);
        Check(w1.IsPlaying && w1.IsWebPlayer && w1.Artist == "Marauda" && w1.Title == "Blunder", "chrome web player parsed");

        var w2 = SpotifyBridge.ParseWindowTitle("Mercy • Megalodon - Spotify — Mozilla Firefox", isBrowserProcess: true);
        Check(w2.IsPlaying && w2.IsWebPlayer && w2.Artist == "Megalodon" && w2.Title == "Mercy", "firefox bullet web player parsed");

        var wIdle = SpotifyBridge.ParseWindowTitle("Spotify – Web Player: Music for everyone - Microsoft Edge", isBrowserProcess: true);
        Check(wIdle.State == SpotifyPlaybackState.Idle && wIdle.IsWebPlayer, "web player idle detected");

        var wOther = SpotifyBridge.ParseWindowTitle("YouTube - Google Chrome", isBrowserProcess: true);
        Check(wOther.State == SpotifyPlaybackState.NotRunning, "unrelated browser tab ignored");

        // FormatTrack truncation helper
        Check(Screens.FormatTrack("Svdden Death - Behemoth", 38) == "SVDDEN DEATH - BEHEMOTH", "FormatTrack uppercases short track");
        Check(Screens.FormatTrack("Very Long Artist Name - Very Long Track Title", 20).Length == 20 && Screens.FormatTrack("Very Long Artist Name - Very Long Track Title", 20).EndsWith(".."), "FormatTrack truncates with ..");

        // Bridge override propagation
        var bridge = new SpotifyBridge();
        bridge.SetOverrideForTest(t1);
        bridge.Update(2f);
        Check(bridge.Current.IsPlaying && bridge.Current.DisplayTrack == "Svdden Death - Behemoth", "bridge override preserved");

        // RunRecord Track persistence + backward-compatible legacy save
        string dir = Path.Combine(Path.GetTempPath(), "nullwave-spotify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "save.json");
            var meta = MetaProgress.Load(path);
            var run = TelemetryRun(0x5071UL, 8, 55_000, 24);
            run.Track = t1.DisplayTrack;
            meta.RecordRun(run);
            Check(meta.RecentRuns[0].Track == "Svdden Death - Behemoth", "RunRecord stores Track");

            var reloaded = MetaProgress.Load(path);
            Check(reloaded.RecentRuns[0].Track == "Svdden Death - Behemoth", "Track survives JSON reload");

            string legacyPath = Path.Combine(dir, "legacy.json");
            File.WriteAllText(legacyPath, "{\"RecentRuns\":[{\"Seed\":\"001N4C4M\",\"Wave\":4,\"Score\":9000}]}");
            var legacy = MetaProgress.Load(legacyPath);
            Check(legacy.RecentRuns.Count == 1 && legacy.RecentRuns[0].Track == "", "legacy RunRecord defaults Track to empty");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best-effort temp cleanup */ }
        }
        Console.WriteLine($"  spotify: desktop=\"{t1.DisplayTrack}\" web=\"{w2.DisplayTrack}\"");
    }

    /// <summary>TASK-006: hard/all/CSV modifier presets and Phase-Sync-aware Pilot dash timing.</summary>
    private static void TestHardModifierRecordPreset()
    {
        var hard = ModifierCatalog.ResolvePreset("hard");
        Check(hard.Count == 15 && !hard.Contains("quiet-signal"), "hard preset selects 15 modifiers excluding quiet-signal");
        var hardMods = ModifierCatalog.Build(hard);
        Check(MathF.Abs(hardMods.ScoreMultiplier - 25f) < 1e-4f, "hard preset hits 25x score cap");
        Check(hardMods.BossPhases == 4 && !hardMods.SynthDisabled, "hard preset enables BLIND CHOIR without disabling synth");

        var all = ModifierCatalog.ResolvePreset("all");
        Check(all.Count == 16 && all.Contains("quiet-signal"), "all preset selects all 16 modifiers");
        Check(ModifierCatalog.Build(all).SynthDisabled, "all preset enables quiet-signal");

        var csv = ModifierCatalog.ResolvePreset("metronome, unknown-mod, blind-choir");
        Check(csv.Count == 2 && csv.Contains("metronome") && csv.Contains("blind-choir"), "CSV preset filters valid IDs");

        // Pilot Phase Sync dash timing check
        var world = new GameWorld(new RunConfig { Seed = 0x9110UL });
        world.Player.Pos = Vector2.Zero;
        world.Player.DashCooldown = 0f;
        world.SpawnEnemyBullet(new Vector2(65f, 0f), new Vector2(-100f, 0f), 5f, 0.5f, BulletKind.Orb, 4f);

        world.SetBeatClockForTest(2.50, 120f); // mid-beat, outside Phase Sync window
        var offBeatFrame = Pilot.Think(world);
        Check(!offBeatFrame.Dash, "Pilot holds dash off-beat for medium-range threat (65u)");

        world.SetBeatClockForTest(2.01, 120f); // on-beat, inside Phase Sync window
        var onBeatFrame = Pilot.Think(world);
        Check(onBeatFrame.Dash, "Pilot dashes on-beat to trigger Phase Sync for medium-range threat");

        // Emergency close threat (<44u) dashes immediately even off-beat
        world.EnemyBullets.Clear();
        world.SpawnEnemyBullet(new Vector2(30f, 0f), new Vector2(-100f, 0f), 5f, 0.5f, BulletKind.Orb, 4f);
        world.SetBeatClockForTest(2.50, 120f);
        var emergencyFrame = Pilot.Think(world);
        Check(emergencyFrame.Dash, "Pilot emergency-dashes immediately when bullet < 44u even off-beat");

        Console.WriteLine($"  hard preset: count={hard.Count} mult=x{hardMods.ScoreMultiplier:0.00} bossPhases={hardMods.BossPhases} pilotSync={onBeatFrame.Dash}");
    }

    /// <summary>TASK-007: Aim-tilt/oblique matrix z=0 invariance, LiftPoint parity, and expanded SongLasers/Choir laser choreography.</summary>
    private static void TestAimCameraAndLaserVisuals()
    {
        var clamped = Renderer.ClampAimTilt(new Vector2(3.5f, -4.2f));
        Check(clamped == new Vector2(1f, -1f), "ClampAimTilt clamps extreme coordinates to [-1, 1]");
        var nanClamped = Renderer.ClampAimTilt(new Vector2(float.NaN, float.PositiveInfinity));
        Check(nanClamped == Vector2.Zero, "ClampAimTilt sanitizes NaN/Inf to zero");

        Vector2 cam = new(320f, -180f);
        Vector2 view = new(800f, 450f);
        Vector2 obZero = Renderer.ComputeObliqueVec(Vector2.Zero);
        Vector2 obRight = Renderer.ComputeObliqueVec(new Vector2(0.85f, -0.60f));
        var matZero = Renderer.BuildWorldMeshMatrix(cam, view, obZero);
        var matTilt = Renderer.BuildWorldMeshMatrix(cam, view, obRight);

        // z=0 ground footprint must remain 100% invariant regardless of AimTilt
        var pGround = new Vector4(510f, -40f, 0f, 1f);
        Check(Vector4.Distance(Vector4.Transform(pGround, matZero), Vector4.Transform(pGround, matTilt)) < 1e-6f, "z=0 world projection invariant across AimTilt");

        // LiftPoint(pos, z, oblique) must match the 3D oblique matrix projection of (pos.X, pos.Y, z)
        const float liftZ = 95f;
        Vector2 pos = new(510f, -40f);
        Vector2 lifted = Renderer.LiftPoint(pos, liftZ, obRight);
        var proj3D = Vector4.Transform(new Vector4(pos.X, pos.Y, liftZ, 1f), matTilt);
        var projLift = Vector4.Transform(new Vector4(lifted.X, lifted.Y, 0f, 1f), matTilt);
        Check(MathF.Abs(proj3D.X - projLift.X) < 1e-5f && MathF.Abs(proj3D.Y - projLift.Y) < 1e-5f, "LiftPoint matches BuildWorldMeshMatrix X/Y projection");

        // Expanded SongLasers: 8-beam Drop Starburst, 2-beam Build Scan, High-Energy Gate/Cross/Cage, 0 in Breakdown
        RunSong(0.3f, 0f, 0f, true, 12f, out int dropBeams, out float warmDrop);
        Check(dropBeams >= 8, "Drop STARBURST fires 8 beams");
        Check(warmDrop >= 0.9f, "8-beam Drop STARBURST preserves 2-beat warmup");

        var buildWorld = RunSong(0.3f, 0.9f, 0f, false, 16f, out int buildBeams, out _);
        Check(buildWorld.SongLaserVolleys >= 2 && buildBeams >= 2, "Build SCAN fires dual opposing sweeps");

        var highWorld = RunSong(0.65f, 0f, 0f, false, 30f, out int highBeams, out _);
        Check(highWorld.SongLaserVolleys >= 3 && highBeams >= 3, "High-energy 0.65f cadence triggers Gate/Cross/Prism Cage");

        var breakWorld = RunSong(0.95f, 0f, 1.0f, true, 24f, out int breakBeams, out _);
        Check(breakWorld.SongLaserVolleys == 0 && breakBeams == 0, "Breakdown suppresses all song lasers even on drop cue");

        // Choir Boss Phase II+ spawns multi-beam Choir laser volleys cleanly
        var audio = new AudioState { Bpm = 120f };
        var bossWorld = new GameWorld(new RunConfig { Seed = 0x7007UL });
        bossWorld.Player.InvincibleDemo = true;
        bossWorld.BeginWave(5);
        bossWorld.ClearSpawnQueueForCapture();
        bossWorld.SpawnForCapture(EnemyRole.Boss, Vector2.Zero);
        var boss = bossWorld.BossEnemy!;
        boss.Spawning = false;
        boss.Hp = boss.MaxHp * 0.55f;
        bossWorld.Step(1f / 60f, audio, InputFrame.Empty);
        Check(boss.BossPhase == 1, "Choir enters Phase II");
        int maxChoirLasers = 0;
        for (int i = 0; i < 60 * 10; i++)
        {
            bossWorld.Step(1f / 60f, audio, InputFrame.Empty);
            int c = 0;
            foreach (var l in bossWorld.Lasers) if (l.BossLaser) c++;
            maxChoirLasers = Math.Max(maxChoirLasers, c);
        }
        Check(maxChoirLasers >= 2, "Choir Phase II+ fires multi-beam laser volleys");

        Console.WriteLine($"  aim & lasers: oblique=({obRight.X:0.00},{obRight.Y:0.00}) dropBeams={dropBeams} buildBeams={buildBeams} highVolleys={highWorld.SongLaserVolleys} choirMaxLasers={maxChoirLasers}");
    }

    /// <summary>TASK-008/009: Per-generator onset drives (Kick/Snare/Hat/Drop/Build/Breakdown) and BackgroundFrag uniform hygiene.</summary>
    private static void TestBackdropAudioReactivity()
    {
        var audio = new AudioState
        {
            BeatPulse = 0.45f,
            SnarePulse = 0.80f,
            HatPulse = 0.65f,
        };
        var mood = new MusicDirector();
        mood.ForceForCapture(intensity: 0.9f, build: 0.50f, breakdown: 0.25f, drop: 0.90f, hue: 0.2f, kick: 0.70f);

        var d = Renderer.ComputeBackdropAudioDrives(audio, mood, 1.2f);
        Check(MathF.Abs(d.Kick - 0.84f) < 1e-5f, "Kick uses mood.Kick * fx");
        Check(MathF.Abs(d.Snare - 0.96f) < 1e-5f, "Snare uses audio.SnarePulse * fx");
        Check(MathF.Abs(d.Hat - 0.78f) < 1e-5f, "Hat uses audio.HatPulse * fx");
        Check(MathF.Abs(d.Drop - 1.08f) < 1e-5f, "Drop uses mood.DropPulse * fx");
        Check(MathF.Abs(d.Build - 0.60f) < 1e-5f, "Build uses mood.Build * fx");
        Check(MathF.Abs(d.Breakdown - 0.30f) < 1e-5f, "Breakdown uses mood.Breakdown * fx");

        // mood == null falls back to BeatPulse for Kick and 0 for section cues
        var noMood = Renderer.ComputeBackdropAudioDrives(audio, null, 1.0f);
        Check(MathF.Abs(noMood.Kick - 0.45f) < 1e-5f, "null mood falls back to audio.BeatPulse");
        Check(MathF.Abs(noMood.Snare - 0.80f) < 1e-5f && MathF.Abs(noMood.Hat - 0.65f) < 1e-5f, "null mood preserves Snare and Hat");
        Check(noMood.Drop == 0f && noMood.Build == 0f && noMood.Breakdown == 0f, "null mood zeroes section drives");

        // fx == 0 zeroes all drives
        var muted = Renderer.ComputeBackdropAudioDrives(audio, mood, 0f);
        Check(muted == new Renderer.BackdropAudioDrives(0f, 0f, 0f, 0f, 0f, 0f), "fx=0 zeroes all backdrop drives");

        // extreme / non-finite inputs stay safely clamped in [0, 3]
        var extreme = Renderer.ComputeBackdropAudioDrives(
            new AudioState { BeatPulse = float.NaN, SnarePulse = 10f, HatPulse = float.PositiveInfinity },
            mood, 2.5f);
        Check(extreme.Kick <= 3f && extreme.Snare == 3f && extreme.Hat == 0f && extreme.Drop <= 3f, "extreme/non-finite drives clamped safely");
        var nanFx = Renderer.ComputeBackdropAudioDrives(audio, mood, float.NaN);
        Check(nanFx == new Renderer.BackdropAudioDrives(0f, 0f, 0f, 0f, 0f, 0f), "NaN fx yields zero drives");

        // BackgroundFrag uniform contract: active onset/transition uniforms present, zero dead V7 uniforms
        string frag = Shaders.BackgroundFrag;
        foreach (string required in new[]
        {
            "uniform float uSnare;",
            "uniform float uHat;",
            "uniform float uKick;",
            "uniform float uDrop;",
            "uniform float uMix;",
        })
        {
            Check(frag.Contains(required, StringComparison.Ordinal), "BackgroundFrag declares " + required);
        }

        foreach (string dead in new[]
        {
            "uStyleA", "uStyleB",
            "uParamA", "uParamB",
            "uMutA", "uMutB",
            "uPalOffA", "uPalOffB",
            "uPalFreqA", "uPalFreqB",
        })
        {
            Check(!frag.Contains(dead, StringComparison.Ordinal), "BackgroundFrag removed dead V7 uniform " + dead);
        }

        Console.WriteLine($"  backdrop audio: kick={d.Kick:0.00} snare={d.Snare:0.00} hat={d.Hat:0.00} drop={d.Drop:0.00} deadV7Uniforms=0");
    }

    /// <summary>TASK-009: Abyssal dark theme setting persistence, CLI parsing, dynamic combat light projection, and shader contracts.</summary>
    private static void TestAbyssalThemeAndLighting()
    {
        var def = new GameSettings();
        Check(def.Theme == VisualTheme.Standard && !def.IsAbyssal, "Theme defaults to Standard");

        Check(GameSettings.TryParseTheme("abyssal", out var t1) && t1 == VisualTheme.Abyssal, "TryParseTheme accepts abyssal");
        Check(GameSettings.TryParseTheme("NOIR", out var t2) && t2 == VisualTheme.Abyssal, "TryParseTheme accepts noir");
        Check(GameSettings.TryParseTheme("dark", out var t3) && t3 == VisualTheme.Abyssal, "TryParseTheme accepts dark");
        Check(GameSettings.TryParseTheme("void", out var t4) && t4 == VisualTheme.Abyssal, "TryParseTheme accepts void");
        Check(GameSettings.TryParseTheme("standard", out var t5) && t5 == VisualTheme.Standard, "TryParseTheme accepts standard");
        Check(!GameSettings.TryParseTheme("neon-banana", out _), "TryParseTheme rejects unknown theme");
        Check(!GameSettings.TryParseTheme("", out _), "TryParseTheme rejects empty theme");

        def.Theme = (VisualTheme)99;
        def.Sanitize();
        Check(def.Theme == VisualTheme.Standard, "Sanitize resets invalid enum value to Standard");

        // Persistence round-trip + legacy save compatibility
        string dir = Path.Combine(Path.GetTempPath(), "nullwave-abyssal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "save.json");
            var meta = MetaProgress.Load(path);
            Check(meta.Settings.Theme == VisualTheme.Standard, "fresh save defaults to Standard");
            meta.Settings.Theme = VisualTheme.Abyssal;
            meta.Save();
            var reloaded = MetaProgress.Load(path);
            Check(reloaded.Settings.Theme == VisualTheme.Abyssal && reloaded.Settings.IsAbyssal, "Abyssal theme persists across save/load");

            string legacyPath = Path.Combine(dir, "legacy.json");
            File.WriteAllText(legacyPath, "{\"Settings\":{\"Bloom\":90}}");
            var legacy = MetaProgress.Load(legacyPath);
            Check(legacy.Settings.Theme == VisualTheme.Standard && legacy.Settings.Bloom == 90, "legacy save without Theme defaults to Standard");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best-effort temp cleanup */ }
        }

        // ProjectBackdropLight math: camera center -> (0,0), +Y world -> -Y screen, radius proportional to view.Y, finite/clamp safety
        Vector2 cam = new(240f, -120f);
        Vector2 view = new(800f, 450f);
        var center = Renderer.ProjectBackdropLight(cam, 450f, 1.25f, cam, view);
        Check(MathF.Abs(center.X) < 1e-6f && MathF.Abs(center.Y) < 1e-6f, "camera center projects to (0,0)");
        Check(MathF.Abs(center.Z - 0.5f) < 1e-5f && MathF.Abs(center.W - 1.25f) < 1e-5f, "radius scales by 2*view.Y and intensity preserved");

        var offset = Renderer.ProjectBackdropLight(cam + new Vector2(450f, 225f), 225f, 5.0f, cam, view);
        Check(MathF.Abs(offset.X - 0.5f) < 1e-5f && MathF.Abs(offset.Y + 0.25f) < 1e-5f, "world +X -> +X, world +Y -> -Y in backdrop UV space");
        Check(MathF.Abs(offset.Z - 0.25f) < 1e-5f && offset.W == 3f, "radius scales linearly and extreme intensity clamps to 3");

        var bad = Renderer.ProjectBackdropLight(new Vector2(float.NaN, float.PositiveInfinity), -50f, float.NaN, cam, view);
        Check(bad.X == 0f && bad.Y == 0f && bad.Z >= 0.02f && bad.W == 0f, "non-finite/negative light parameters sanitized safely");

        // Shader contract checks across BackgroundFrag, MeshFrag, and CompositeFrag
        Check(Shaders.BackgroundFrag.Contains("uniform float uDarkTheme;", StringComparison.Ordinal), "BackgroundFrag declares uDarkTheme");
        Check(Shaders.BackgroundFrag.Contains("uniform int uLightCount;", StringComparison.Ordinal), "BackgroundFrag declares uLightCount");
        Check(Shaders.BackgroundFrag.Contains("uniform vec4 uLights[12];", StringComparison.Ordinal), "BackgroundFrag declares uLights[12]");
        Check(Shaders.BackgroundFrag.Contains("uniform vec3 uLightCols[12];", StringComparison.Ordinal), "BackgroundFrag declares uLightCols[12]");
        Check(Shaders.MeshFrag.Contains("uniform float uDarkTheme;", StringComparison.Ordinal), "MeshFrag declares uDarkTheme");
        Check(Shaders.CompositeFrag.Contains("uniform float uDarkTheme;", StringComparison.Ordinal), "CompositeFrag declares uDarkTheme");

        Console.WriteLine($"  abyssal theme: maxLights={Renderer.MaxBackdropLights} offset=({offset.X:0.00},{offset.Y:0.00}) r={offset.Z:0.00} shaders=3/3");
    }

    /// <summary>TASK-010: Harmonic Overclock attunement synergy evolutions (Seeker Salvo, Prism Lance, Rail Cadence, Void Phantom).</summary>
    private static void TestAttunementSynergies()
    {
        // 1. Evaluation, WouldUnlock, CountActive, zero-alloc GetActive
        var p = new World.Player();
        Check(SynergyCatalog.Evaluate(p) == SynergyFlags.None && SynergyCatalog.CountActive(p.ActiveSynergies) == 0, "fresh player has zero synergies");
        AttunementCatalog.Apply(p, "split");
        AttunementCatalog.Apply(p, "split");
        AttunementCatalog.Apply(p, "homing");
        Check(p.ActiveSynergies == SynergyFlags.None, "2 split + 1 homing does not unlock Seeker Salvo yet");
        Check(SynergyCatalog.WouldUnlock(p, "homing")?.Flag == SynergyFlags.SeekerSalvo, "WouldUnlock previews Seeker Salvo on 2nd homing");
        Check(SynergyCatalog.WouldUnlock(p, "split") == null, "WouldUnlock returns null when partner is still below 2");
        AttunementCatalog.Apply(p, "homing");
        Check(p.ActiveSynergies == SynergyFlags.SeekerSalvo && SynergyCatalog.CountActive(p.ActiveSynergies) == 1, "Seeker Salvo activates at split>=2 && homing>=2");
        Check(SynergyCatalog.WouldUnlock(p, "homing") == null, "already-active synergy is not re-previewed");

        foreach (var id in new[] { "pierce", "pierce", "ricochet", "ricochet", "heavy", "heavy", "rapid", "rapid", "phase", "phase", "grazer", "grazer" })
            AttunementCatalog.Apply(p, id);
        Check(SynergyCatalog.CountActive(p.ActiveSynergies) == 4, "all 4 synergies active at stack 2+");
        Span<int> idx = stackalloc int[4];
        Check(SynergyCatalog.GetActive(p, idx) == 4, "GetActive fills 4 catalog indices without allocating");

        // 2. Seeker Salvo firing
        var audio = new AudioState { Bpm = 120f };
        var wSeeker = new GameWorld(new RunConfig { Seed = 0x1010UL });
        wSeeker.Player.InvincibleDemo = true;
        AttunementCatalog.Apply(wSeeker.Player, "split");
        AttunementCatalog.Apply(wSeeker.Player, "split");
        AttunementCatalog.Apply(wSeeker.Player, "homing");
        AttunementCatalog.Apply(wSeeker.Player, "homing");
        wSeeker.Player.FireTimer = 0f;
        wSeeker.Step(1f / 60f, audio, new InputFrame(Vector2.Zero, new Vector2(1, 0), true, false, false, false));
        int seekerCount = 0;
        float maxSeekerHoming = 0f, baseHoming = 0f;
        foreach (var b in wSeeker.PlayerBullets)
        {
            if (b.SynergyKind == 1) { seekerCount++; maxSeekerHoming = MathF.Max(maxSeekerHoming, b.Homing); }
            else baseHoming = MathF.Max(baseHoming, b.Homing);
        }
        Check(seekerCount == 2 && maxSeekerHoming > baseHoming, "Seeker Salvo spawns 2 flanking micro-seekers with stronger homing");

        // 3. Rail Cadence 4th shot
        var wRail = new GameWorld(new RunConfig { Seed = 0x1011UL });
        wRail.Player.InvincibleDemo = true;
        foreach (var id in new[] { "heavy", "heavy", "rapid", "rapid" }) AttunementCatalog.Apply(wRail.Player, id);
        int railCountBefore4 = 0, railCountOn4 = 0;
        float railSpeed = 0f, railDamage = 0f;
        for (int shot = 1; shot <= 4; shot++)
        {
            wRail.PlayerBullets.Clear();
            wRail.Player.FireTimer = 0f;
            wRail.Step(1f / 60f, audio, new InputFrame(Vector2.Zero, new Vector2(1, 0), true, false, false, false));
            foreach (var b in wRail.PlayerBullets)
            {
                if (b.SynergyKind != 3) continue;
                if (shot < 4) railCountBefore4++;
                else { railCountOn4++; railSpeed = b.Vel.Length(); railDamage = b.Damage; }
            }
        }
        Check(railCountBefore4 == 0 && railCountOn4 == 1, "Rail Cadence fires hyper-rail bolt exclusively on every 4th shot");
        Check(railSpeed >= 2200f && railDamage > wRail.Player.Damage * 1.9f, "Rail Cadence bolt has hyper velocity and 2.1x damage");

        // 4. Prism Lance wall ricochet supercharge
        var wPrism = new GameWorld(new RunConfig { Seed = 0x1012UL });
        wPrism.Player.InvincibleDemo = true;
        foreach (var id in new[] { "pierce", "pierce", "ricochet", "ricochet" }) AttunementCatalog.Apply(wPrism.Player, id);
        wPrism.PlayerBullets.Clear();
        float startDmg = wPrism.Player.Damage;
        int startPierce = wPrism.Player.PierceLevel;
        wPrism.SpawnPlayerBullet(new Vector2(wPrism.ArenaW * 0.5f - 12f, 0f), new Vector2(1500f, 0f), 4f, startDmg, 1.0f, startPierce, 0f, 2);
        wPrism.Step(1f / 60f, audio, InputFrame.Empty);
        Check(wPrism.PlayerBullets.Count == 1, "ricocheted Prism Lance bolt survives wall impact");
        var lance = wPrism.PlayerBullets[0];
        Check(lance.SynergyKind == 2 && lance.Damage > startDmg * 1.25f && lance.Pierce == startPierce + 1, "Prism Lance ricochet supercharges damage, pierce, and visual kind");

        // 5. Void Phantom dash shockwave (bullet cancel + Resonance + enemy damage)
        var wVoid = new GameWorld(new RunConfig { Seed = 0x1013UL });
        wVoid.Player.InvincibleDemo = true;
        foreach (var id in new[] { "phase", "phase", "grazer", "grazer" }) AttunementCatalog.Apply(wVoid.Player, id);
        wVoid.ClearSpawnQueueForCapture();
        wVoid.Enemies.Clear();
        wVoid.EnemyBullets.Clear();
        wVoid.SpawnForCapture(EnemyRole.Drifter, wVoid.Player.Pos + new Vector2(110f, 0f));
        var target = wVoid.Enemies[0];
        target.Spawning = false;
        float hpBefore = target.Hp;
        wVoid.SpawnEnemyBullet(wVoid.Player.Pos + new Vector2(85f, 0f), new Vector2(-10f, 0f), 5f, 0.5f, BulletKind.Orb);
        wVoid.SpawnEnemyBullet(wVoid.Player.Pos + new Vector2(-95f, 0f), new Vector2(10f, 0f), 5f, 0.5f, BulletKind.Orb);
        wVoid.Player.Resonance = 0f;
        wVoid.Step(1f / 60f, audio, new InputFrame(new Vector2(0, -1), new Vector2(1, 0), false, true, false, false));
        Check(wVoid.EnemyBullets.Count == 0, "Void Phantom dash cancels enemy bullets inside 135u");
        Check(wVoid.Player.Resonance > 0f, "Void Phantom bullet cancellation grants Resonance");
        Check(target.Hp < hpBefore, "Void Phantom dash shockwave damages enemies inside 180u");

        Console.WriteLine($"  synergies: active=4/4 seekers={seekerCount} railSpeed={railSpeed:0} prismDmg={startDmg:0.0}->{lance.Damage:0.0} voidRes=+{wVoid.Player.Resonance:0.0}");
    }

    /// <summary>TASK-011: Drop Overdrive, 3x Phase-Sync Overdrive, micro-hitstop, GPU shockwave refraction, and high-intensity reactive backdrop drives.</summary>
    private static void TestOverdriveAndHitStop()
    {
        var audio = new AudioState { Bpm = 120f, BeatPulse = 0.7f, SnarePulse = 0.8f, HatPulse = 0.6f };
        var w = new GameWorld(new RunConfig { Seed = 0x1101UL });
        w.Player.InvincibleDemo = true;
        w.StartGrace = 0f;

        // 1. Drop Overdrive window
        Check(!w.IsDropOverdrive && MathF.Abs(w.OverdriveFireScale - 1f) < 1e-5f, "no overdrive active initially");
        w.OnMusicDrop(1.0f, false);
        Check(w.IsDropOverdrive && w.DropOverdriveTimer >= 3.9f, "OnMusicDrop activates 4.0s Drop Overdrive");
        Check(w.OverdriveFireScale < 0.85f && w.OverdriveDamageScale > 1.15f && w.OverdriveScoreBonus >= 0.5f, "Drop Overdrive boosts fire rate, damage, and score multiplier");
        bool hasDropHeavy = false;
        foreach (var sw in w.Shockwaves) if (sw.Heavy) hasDropHeavy = true;
        Check(hasDropHeavy, "OnMusicDrop spawns Heavy shockwave for GPU refraction");
        for (int i = 0; i < 250; i++) w.Step(1f / 60f, audio, InputFrame.Empty);
        Check(!w.IsDropOverdrive && w.DropOverdriveTimer == 0f, "Drop Overdrive decays cleanly to zero after 4s");

        // 2. 3x Phase-Sync Overdrive + off-beat / damage streak reset
        var ws = new GameWorld(new RunConfig { Seed = 0x1102UL });
        ws.StartGrace = 0f;
        ws.Player.Health = 3;
        ws.Player.MaxHealth = 3;
        int syncHeavyCount = 0;
        for (int k = 1; k <= 3; k++)
        {
            ws.Shockwaves.Clear();
            ws.Player.DashCooldown = 0f;
            ws.Player.IsDashing = false;
            ws.BeatPhase = 0.02f; // dead-center on beat
            ws.Step(1f / 60f, audio, new InputFrame(new Vector2(1, 0), new Vector2(1, 0), false, true, false, false));
            Check(ws.Player.PhaseSyncStreak == k, $"on-beat dash {k} increments PhaseSyncStreak");
            if (k == 3)
            {
                foreach (var sw in ws.Shockwaves) if (sw.Heavy) syncHeavyCount++;
            }
        }
        Check(ws.Player.IsSyncOverdrive && ws.Player.SyncOverdriveTimer >= 4.9f, "3rd consecutive Phase Sync ignites 5.0s Sync Overdrive");
        Check(syncHeavyCount >= 1, "3x Sync Overdrive emits a Heavy shockwave");
        Check(ws.OverdriveResonanceScale >= 1.29f && ws.OverdriveScoreBonus >= 0.34f, "Sync Overdrive boosts Resonance and score multiplier");

        // Off-beat dash resets streak with zero health or cooldown penalty
        ws.Player.DashCooldown = 0f;
        ws.Player.IsDashing = false;
        ws.BeatPhase = 0.50f; // dead off-beat
        int hpBefore = ws.Player.Health;
        ws.Step(1f / 60f, audio, new InputFrame(new Vector2(1, 0), new Vector2(1, 0), false, true, false, false));
        Check(ws.Player.PhaseSyncStreak == 0 && ws.Player.Health == hpBefore, "off-beat dash resets streak with zero health penalty");

        // Taking damage resets streak
        ws.Player.PhaseSyncStreak = 2;
        ws.Player.IsDashing = false;
        ws.Player.Invuln = 0f;
        ws.HurtPlayer();
        Check(ws.Player.PhaseSyncStreak == 0, "HurtPlayer resets PhaseSyncStreak");

        // 3. Micro-Hitstop on Elite kill and Resonance Cascade (Burst)
        var wh = new GameWorld(new RunConfig { Seed = 0x1103UL });
        wh.Player.InvincibleDemo = true;
        wh.ClearSpawnQueueForCapture();
        wh.Enemies.Clear();
        wh.SpawnForCapture(EnemyRole.Drifter, wh.Player.Pos + new Vector2(80f, 0f));
        var elite = wh.Enemies[0];
        elite.Elite = true;
        elite.Spawning = false;
        elite.Hp = 1f;
        wh.SpawnPlayerBullet(elite.Pos, new Vector2(1000f, 0f), 8f, 50f, 1f, 0);
        wh.Step(1f / 60f, audio, InputFrame.Empty);
        Check(wh.HitStop >= 0.02f && wh.HitStop <= 0.09f, "Elite kill triggers bounded micro-hitstop");
        float sessionBefore = wh.SessionTime;
        for (int i = 0; i < 6; i++) wh.Step(1f / 60f, audio, InputFrame.Empty);
        Check(wh.HitStop <= 0f && wh.SessionTime > sessionBefore, "HitStop drains cleanly without stalling SessionTime");

        wh.BeginWave(1);
        wh.Player.Resonance = wh.Player.ResonanceMax;
        wh.Burst();
        Check(wh.HitStop >= 0.028f && wh.HitStop <= 0.09f, "Burst triggers micro-hitstop");

        // 4. GPU Shockwave Projection & High-Intensity Reactive Backdrop Drives + shader contracts
        Vector2 cam = new(100f, -50f);
        Vector2 view = new(800f, 450f);
        var swCenter = Renderer.ProjectScreenShockwave(cam, 450f, 0.85f, cam, view);
        Check(MathF.Abs(swCenter.X - 0.5f) < 1e-5f && MathF.Abs(swCenter.Y - 0.5f) < 1e-5f && MathF.Abs(swCenter.Z - 0.5f) < 1e-5f, "ProjectScreenShockwave centers camera at (0.5, 0.5)");
        var swBad = Renderer.ProjectScreenShockwave(new Vector2(float.NaN, 0f), -10f, float.PositiveInfinity, cam, view);
        Check(float.IsFinite(swBad.X) && swBad.Z == 0f && swBad.W == 0f, "ProjectScreenShockwave sanitizes non-finite inputs");
        Check(Renderer.EffectiveShockwaveStrength(0.85f, true, 100) == 0f, "ReduceFlashing suppresses shockwave distortion");
        Check(Renderer.EffectiveShockwaveStrength(0.85f, false, 0) == 0f, "ScreenShake=0 suppresses shockwave distortion");
        Check(Renderer.EffectiveShockwaveStrength(0.85f, false, 100) > 0.8f, "normal settings allow shockwave distortion");

        var mood = new MusicDirector();
        mood.ForceForCapture(0.9f, 0.2f, 0f, 0.8f, 0.1f, 0.7f);
        var dBase = Renderer.ComputeBackdropAudioDrives(audio, mood, 1.0f, overdrive: 0f, reduceFlashing: false);
        var dOd = Renderer.ComputeBackdropAudioDrives(audio, mood, 1.0f, overdrive: 1.0f, reduceFlashing: false);
        var dSafe = Renderer.ComputeBackdropAudioDrives(audio, mood, 1.0f, overdrive: 1.0f, reduceFlashing: true);
        Check(dOd.Drop > dBase.Drop && dOd.Kick > dBase.Kick && dOd.Snare > dBase.Snare, "Overdrive amplifies reactive backdrop drives");
        Check(dSafe.Drop < dOd.Drop * 0.5f && dSafe.Kick < dOd.Kick * 0.5f, "ReduceFlashing dampens reactive backdrop drives");

        Check(Shaders.BackgroundFrag.Contains("uniform float uOverdrive;", StringComparison.Ordinal), "BackgroundFrag declares uOverdrive");
        Check(Shaders.BackgroundFrag.Contains("uniform float uMotionScale;", StringComparison.Ordinal), "BackgroundFrag declares uMotionScale");
        Check(Shaders.CompositeFrag.Contains("uniform int uShockwaveCount;", StringComparison.Ordinal), "CompositeFrag declares uShockwaveCount");
        Check(Shaders.CompositeFrag.Contains("uniform vec4 uShockwaves[4];", StringComparison.Ordinal), "CompositeFrag declares uShockwaves[4]");

        Console.WriteLine($"  overdrive & hitstop: dropOD=4.0s syncOD=5.0s burstStop={wh.HitStop * 1000f:0}ms dropDrive={dBase.Drop:0.00}->{dOd.Drop:0.00} safe={dSafe.Drop:0.00}");
    }

    private sealed class FuncStereoProvider : ISampleProvider
    {
        private readonly Func<long, (float L, float R)> _gen;
        private long _frame;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(LookaheadAudioBuffer.SampleRate, 2);
        public FuncStereoProvider(Func<long, (float L, float R)> gen) => _gen = gen;
        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / 2;
            for (int f = 0; f < frames; f++)
            {
                var (l, r) = _gen(_frame++);
                buffer[offset + f * 2] = l;
                buffer[offset + f * 2 + 1] = r;
            }
            return frames * 2;
        }
    }

    /// <summary>TASK-013: Sample-accurate 0.75 s lookahead delay, pre-drop DropAnticipation + beat-1 drop snap, and local .wav track player.</summary>
    private static void TestLookaheadAudioBuffer()
    {
        // 1. Sample-accurate impulse delay: 36,000 frames exact at 48 kHz
        var impulse = new FuncStereoProvider(frame => frame == 0 ? (1f, -0.5f) : (0f, 0f));
        var lookahead = new LookaheadAudioBuffer(impulse, LookaheadAudioBuffer.DefaultLookaheadSeconds);
        Check(lookahead.LookaheadFrames == 36000 && MathF.Abs(lookahead.LookaheadSeconds - 0.75f) < 1e-5f, "0.75s lookahead equals 36,000 frames at 48 kHz");

        float[] chunk = new float[2048]; // 1024 stereo frames
        long firstHitFrame = -1;
        float hitL = 0f, hitR = 0f;
        long total = 0;
        for (int iter = 0; iter < 40; iter++)
        {
            int n = lookahead.Read(chunk, 0, chunk.Length);
            int frames = n / 2;
            for (int f = 0; f < frames; f++)
            {
                float l = chunk[f * 2], r = chunk[f * 2 + 1];
                if (firstHitFrame < 0 && (MathF.Abs(l) > 0.001f || MathF.Abs(r) > 0.001f))
                {
                    firstHitFrame = total + f;
                    hitL = l;
                    hitR = r;
                }
            }
            total += frames;
        }
        Check(firstHitFrame == 36000, $"impulse emerges at exact frame 36000 (got {firstHitFrame})");
        Check(MathF.Abs(hitL - 1f) < 1e-5f && MathF.Abs(hitR + 0.5f) < 1e-5f, "impulse stereo channels preserved exactly");

        // 2. Pre-drop DropAnticipation rises BEFORE the drop reaches playback, and MusicDirector snaps DropTriggered on landing
        const int sr = LookaheadAudioBuffer.SampleRate;
        const long dropSourceFrame = sr * 3L; // drop starts at 3.0s in source -> plays at 3.75s out of speakers
        var dropSrc = new FuncStereoProvider(frame =>
        {
            float t = frame / (float)sr;
            if (frame < dropSourceFrame)
            {
                // quiet atmospheric passage (~0.025 amp)
                float s = MathF.Sin(t * 220f * MathF.Tau) * 0.025f;
                return (s, s);
            }
            // loud 128 BPM bass drop (~0.65 amp)
            float beatT = ((frame - dropSourceFrame) / (float)sr) % (60f / 128f);
            float kick = MathF.Sin(beatT * 68f * MathF.Tau) * MathF.Exp(-beatT * 10f) * 0.65f;
            float bass = MathF.Sin(t * 110f * MathF.Tau) * 0.35f;
            float v = kick + bass;
            return (v, v);
        });
        var dropBuf = new LookaheadAudioBuffer(dropSrc, 0.75f);
        var analyzer = new SpectrumAnalyzer();
        var director = new MusicDirector();
        var state = new AudioState { LookaheadSeconds = dropBuf.LookaheadSeconds };
        float[] pull = new float[1600]; // 800 stereo frames = 1/60 s at 48 kHz
        float[] fftWin = new float[SpectrumAnalyzer.FftSize];
        float maxPreDropAnt = 0f;
        float dropTime = -1f;
        const float dt = 1f / 60f;
        for (int step = 0; step < 320; step++) // ~5.33 s
        {
            dropBuf.Read(pull, 0, pull.Length);
            float t = dropBuf.PlayedFrames / (float)sr;
            state.ClearBeatFlags();
            state.Time = t;
            if (dropBuf.CopyPlaybackWindow(fftWin))
                analyzer.Analyze(fftWin, fftWin.Length, sr, dt, state);
            float ant = dropBuf.ComputeDropAnticipation(out _, out _);
            state.DropAnticipation = ant;
            if (t > 2.5f && t < 3.70f)
                maxPreDropAnt = MathF.Max(maxPreDropAnt, ant);
            director.Update(state, dt);
            if (director.DropTriggered && dropTime < 0f)
                dropTime = t;
        }
        Check(maxPreDropAnt > 0.5f, $"DropAnticipation rises above 0.5 before playback drop (got {maxPreDropAnt:0.00})");
        Check(dropTime >= 3.65f && dropTime <= 4.15f, $"MusicDirector triggers drop right as drop lands at playback (t={dropTime:0.00}s)");

        // 3. Real .wav LocalTrackSource + AudioEngine.LoadLocalTrack round-trip, seamless looping & missing-file fallback
        string dir = Path.Combine(Path.GetTempPath(), "nullwave-track-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string wavPath = Path.Combine(dir, "test_signal_loop.wav");
        try
        {
            using (var writer = new WaveFileWriter(wavPath, new WaveFormat(44100, 16, 2)))
            {
                // 0.25s stereo 44.1 kHz sine wave (also exercises WdlResamplingSampleProvider 44.1 -> 48 kHz)
                int frames = 44100 / 4;
                float[] samples = new float[frames * 2];
                for (int i = 0; i < frames; i++)
                {
                    float s = MathF.Sin(i * 440f / 44100f * MathF.Tau) * 0.45f;
                    samples[i * 2] = s;
                    samples[i * 2 + 1] = s;
                }
                writer.WriteSamples(samples, 0, samples.Length);
            }

            using (var track = new LocalTrackSource(wavPath))
            {
                Check(track.WaveFormat.SampleRate == LookaheadAudioBuffer.SampleRate && track.WaveFormat.Channels == 2, "LocalTrackSource normalizes 44.1kHz wav to 48kHz stereo");
                Check(track.TrackName == "test signal loop", "LocalTrackSource derives clean TrackName");
                // Pull 0.6s (> 2x file length) to prove seamless looping past EOF with non-zero audio
                float[] readBuf = new float[4800];
                float energyEnd = 0f;
                for (int k = 0; k < 12; k++)
                {
                    int got = track.Read(readBuf, 0, readBuf.Length);
                    Check(got == readBuf.Length, "LocalTrackSource returns full buffer across loop boundary");
                    if (k == 11)
                        for (int j = 0; j < readBuf.Length; j++) energyEnd += readBuf[j] * readBuf[j];
                }
                Check(energyEnd > 1f, "LocalTrackSource produces continuous non-zero audio after looping past EOF");
            }

            using (var engine = new AudioEngine(enableOutput: false, enableCapture: false))
            {
                Check(!engine.LoadLocalTrack(Path.Combine(dir, "does_not_exist_12345.wav")), "missing track returns false cleanly");
                Check(!engine.LocalTrackActive && !engine.State.LocalTrackActive, "failed load preserves non-local fallback state");
                Check(engine.LoadLocalTrack(wavPath), "valid .wav loads into AudioEngine");
                Check(engine.LocalTrackActive && engine.State.LocalTrackActive && engine.State.LookaheadSeconds >= 0.74f, "loaded track sets LocalTrackActive and 0.75s lookahead");
                engine.Update(1f / 60f);
                Check(engine.State.LocalTrackActive && engine.State.LocalTrackName == "test signal loop", "AudioEngine.Update preserves local track state");
                engine.UnloadLocalTrack();
                Check(!engine.LocalTrackActive && !engine.State.LocalTrackActive, "UnloadLocalTrack cleanly restores SynthScore");
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best-effort temp cleanup */ }
        }

        Check(Shaders.BackgroundFrag.Contains("uniform float uAnticipation;", StringComparison.Ordinal), "BackgroundFrag declares uAnticipation");
        Console.WriteLine($"  lookahead audio: delay={firstHitFrame}f ({lookahead.LookaheadSeconds:0.00}s) preDropAnt={maxPreDropAnt:0.00} dropSnap={dropTime:0.00}s wavLoop=PASS");
    }

    /// <summary>TASK-014: 100% stationary 3D-only backdrops, zero 2D generators/ops, zero camera/aim UV panning, and combat readability contracts.</summary>
    private static void Test3DBackdropAndReadability()
    {
        Check(BackdropGenome.GeneratorCount == 6 && BackdropGenome.MaxLayers == 1 && BackdropGenome.OpCount == 0, "BackdropGenome: six single-layer 3D worlds");

        var rng = new Random(14014);
        var seen = new HashSet<int>();
        BackdropGenome? prev = null;
        for (int i = 0; i < 60; i++)
        {
            var g = BackdropGenome.Random(rng, prev);
            Check(BackdropGenome.Is3DOnly(g), $"genome {i} is one 3D world with zero 2D ops");
            Check(g.Layers[0].Blend == 0 && MathF.Abs(g.Layers[0].Gain - 1f) < 1e-5f, "single 3D base layer uses Blend=0, Gain=1");
            seen.Add(g.Layers[0].Generator);
            prev = g;
        }
        Check(seen.Count == BackdropGenome.GeneratorCount, "all 3D worlds reachable within 60 rolls");

        var badOps = BackdropGenome.Random(new Random(7));
        badOps.Layers[0].Ops[0] = 2;
        Check(!BackdropGenome.Is3DOnly(badOps), "Is3DOnly rejects non-zero 2D space-warping ops");
        var badMulti = BackdropGenome.Random(new Random(8));
        badMulti.Layers.Add(new BackdropGenome.Layer { Generator = 1 });
        Check(!BackdropGenome.Is3DOnly(badMulti), "Is3DOnly rejects multi-layer stacks");

        // Map-anchored contract: rays are cast through the world camera onto the arena plane,
        // geometry is animated by the beat clock and by shockwaves anchored in the world.
        string frag = Shaders.BackgroundFrag;
        foreach (string required in new[]
        {
            "uniform vec2 uCam;", "uniform vec2 uView;", "uniform float uOblique;",
            "uniform float uBeatPos;", "uniform vec4 uWaves[6];", "vec3 onFloor = vec3((uCam",
            "mapSea", "mapCrust", "mapDunes", "mapGyroid", "renderNebula", "renderAurora", "atmosphere(",
            "uniform float uSpec[16];", "uniform vec4 uFocus[16];",
            "uniform float uDarkTheme;", "uniform vec4 uLights[12];", "uniform float uOverdrive;", "uniform float uAnticipation;",
        })
        {
            Check(frag.Contains(required, StringComparison.Ordinal), "BackgroundFrag contains " + required);
        }
        foreach (string forbidden in new[]
        {
            "vec2 uv = screenPos;", "uAim", "uVel", "uTravel", "mapPrism", "mapCubes", "mapRings", "SCAN GRID",
            "genInk", "genPlasma", "genTruchet", "genHex", "applyOp", "styleKaleido",
        })
        {
            Check(!frag.Contains(forbidden, StringComparison.Ordinal), "BackgroundFrag retired " + forbidden);
        }
        // the 3D camera must match the 2D world projection on the floor plane (z = 0)
        Check(MathF.Abs(Renderer.Oblique - 0.42f) < 1e-5f, "backdrop camera uses the world oblique");
        Console.WriteLine($"  3d backdrop: worlds={seen.Count}/{BackdropGenome.GeneratorCount} mapAnchored=true beatClock=true");
    }


    /// <summary>Each spectrum band moves its own part of the world, snapping on hits and settling after.</summary>
    private static void TestMusicDrivenBackdrop()
    {
        var bars = new float[AudioState.SpectrumBars];
        var avg = new float[16];
        var outp = new float[16];
        // steady mid-level mix for 3 s: bands settle near their level share
        for (int i = 0; i < bars.Length; i++) bars[i] = 0.4f;
        for (int t = 0; t < 180; t++) Renderer.ComputeBackdropSpectrum(bars, avg, outp, 1f / 60f);
        float steady = outp[8];
        // a hi-hat burst: only the top bands jump, instantly
        for (int i = 36; i < 48; i++) bars[i] = 0.95f;
        Renderer.ComputeBackdropSpectrum(bars, avg, outp, 1f / 60f);
        float burst = outp[14];
        Check(burst > steady + 0.5f, "treble band snaps up on a hat burst");
        Check(MathF.Abs(outp[2] - steady) < 0.05f, "bass band ignores a hat burst");
        for (int i = 36; i < 48; i++) bars[i] = 0.4f;
        for (int t = 0; t < 30; t++) Renderer.ComputeBackdropSpectrum(bars, avg, outp, 1f / 60f);
        Check(outp[14] < steady + 0.15f, "treble band settles within half a second");
        bars[0] = float.NaN;
        Renderer.ComputeBackdropSpectrum(bars, avg, outp, 1f / 60f);
        Check(outp.All(float.IsFinite), "spectrum mapping survives NaN input");
        Console.WriteLine($"  music backdrop: steady={steady:0.00} hatBurst={burst:0.00} bassDuringHat={outp[2]:0.00}");
    }

    /// <summary>The Spotify buffer FIFO primes, plays in order, recovers from underruns and sheds clock drift.</summary>
    private static void TestLiveStreamBuffer()
    {
        var live = new LiveStreamSource();
        var out1 = new float[960];
        live.Read(out1, 0, out1.Length);
        Check(out1.All(v => v == 0f), "silent until primed");
        var chunk = new float[960];   // 10 ms stereo
        for (int k = 0; k < 8; k++)
        {
            for (int f = 0; f < 480; f++) { chunk[f * 2] = k * 480 + f; chunk[f * 2 + 1] = -(k * 480 + f); }
            live.Write(chunk);
        }
        live.Read(out1, 0, out1.Length);
        Check(out1[0] == 0f && out1[2] == 1f && out1[3] == -1f, "plays captured frames in order, stereo intact");
        // drain past the end: one underrun, then silence until re-primed
        var big = new float[48000];
        live.Read(big, 0, big.Length);
        Check(live.Underruns == 1, "underrun counted once");
        // capture running ahead of the output: the excess is dropped back to the cushion
        var burst = new float[48000];
        live.Write(burst);
        live.Read(out1, 0, out1.Length);
        Check(live.Fill < 48000 / 4, "clock drift excess is shed");
        Console.WriteLine($"  spotify buffer fifo: underruns={live.Underruns} fillAfterDrift={live.Fill}");
    }


    /// <summary>
    /// A synthetic 120 BPM song (A: C / G with kicks, B: Am / F with kicks and hats, A again):
    /// chords, key, drums vs. melody and the returning section must all be recognised.
    /// </summary>
    private static void TestMusicAnalysis()
    {
        const int rate = 48000;
        const float beatLen = 0.5f;
        const int beatsPerSection = 24;
        int total = (int)(beatsPerSection * 3 * beatLen * rate);
        var sig = new float[total];
        var rng = new Random(7);
        float Midi(int m) => 440f * MathF.Pow(2f, (m - 69) / 12f);
        // (root midi, minor) per 2-beat chord, by section
        (int, bool)[] a = [(60, false), (65, false), (67, false), (60, false)];
        (int, bool)[] b = [(57, true), (62, true), (65, false), (64, true)];
        for (int i = 0; i < total; i++)
        {
            float t = i / (float)rate;
            int beat = (int)(t / beatLen);
            int section = beat / beatsPerSection;
            var prog = section == 1 ? b : a;
            var (root, minor) = prog[(beat / 2) % 4];
            if (beat == 44) (root, minor) = (71, false);   // a one-beat foreign chord: a fill, not a new section
            float s = 0f;
            foreach (int iv in new[] { 0, minor ? 3 : 4, 7 })
            {
                float f = Midi(root + iv);
                s += MathF.Sin(MathF.Tau * f * t) * 0.12f + MathF.Sin(MathF.Tau * 2f * f * t) * 0.03f;
            }
            float tb = t - beat * beatLen;
            s += MathF.Sin(MathF.Tau * 55f * tb) * MathF.Exp(-tb * 22f) * 0.6f;
            if (section == 1)
            {
                float th = (t + beatLen * 0.5f) % beatLen;
                s += (float)(rng.NextDouble() * 2 - 1) * MathF.Exp(-th * 50f) * 0.18f;
            }
            sig[i] = s;
        }

        var spectrum = new SpectrumAnalyzer();
        var music = new MusicAnalyzer();
        var state = new AudioState { BeatLocked = true };
        var win2048 = new float[SpectrumAnalyzer.FftSize];
        float dt = 1f / 60f;
        int hop = rate / 60;
        int chordFrames = 0, chordRight = 0;
        float kickPerc = 0f, gapPerc = 0f; int kickN = 0, gapN = 0;
        float hatsA = 0f, hatsB = 0f; int hatsAN = 0, hatsBN = 0;
        float harmMid = 0f; int harmN = 0;
        int keyAfterA = -2;
        var changes = new List<(int beat, int label, bool repeat)>();
        var starts = new List<double>();
        var sims = new List<float>();
        long analyzeTicks = 0; int analyzeFrames = 0;
        int lastBeat = -1;
        for (int pos = MusicAnalyzer.WindowSize; pos < total; pos += hop)
        {
            float t = pos / (float)rate;
            int beat = (int)(t / beatLen);
            state.ClearBeatFlags();
            Array.Copy(sig, pos - win2048.Length, win2048, 0, win2048.Length);
            spectrum.Analyze(win2048, win2048.Length, rate, dt, state);
            // the beat tracker's clock, as the engine provides it
            state.BeatPosition = t / beatLen;
            state.BeatLocked = true;
            state.DownbeatOffset = 0;
            lastBeat = beat;
            long t0 = Stopwatch.GetTimestamp();
            music.Analyze(sig.AsSpan(pos - MusicAnalyzer.WindowSize, MusicAnalyzer.WindowSize), spectrum.Magnitudes, rate, dt, state);
            analyzeTicks += Stopwatch.GetTimestamp() - t0;
            analyzeFrames++;
            int section = beat / beatsPerSection;
            float inBeat = t - beat * beatLen;
            var prog = section == 1 ? b : a;
            var (root, minor) = prog[(beat / 2) % 4];
            // judge the chord once the window has settled into the new chord
            float inChord = t - (beat / 2) * 2 * beatLen;
            if (inChord > 0.45f && t > 2f && beat != 44 && beat != 45)
            {
                chordFrames++;
                if (state.ChordRoot == root % 12 && state.ChordMinor == minor) chordRight++;
            }
            if (t > 2f && inBeat < 0.06f) { kickPerc += state.PercLow; kickN++; }
            if (t > 2f && inBeat > 0.3f && inBeat < 0.45f) { gapPerc += state.PercLow; gapN++; }
            if (t > 2f) { harmMid += state.HarmMid; harmN++; }
            // in B the hats sit on the off-beats: the high drum part should spike there
            if (section == 1 && beat > beatsPerSection + 4)
            {
                float off = (inBeat + beatLen * 0.5f) % beatLen;
                if (off < 0.05f) { hatsB += state.PercHigh; hatsBN++; }
                else if (off > 0.2f && off < 0.35f) { hatsA += state.PercHigh; hatsAN++; }
            }
            if (beat == beatsPerSection - 1) keyAfterA = state.Key;
            if (state.SectionChanged)
            {
                changes.Add((beat, state.SectionLabel, state.SectionRepeat));
                sims.Add(music.LastRepeatSimilarity);
                if (!state.SectionCorrected) starts.Add(music.LastBoundaryBeatIndex);
            }
        }
        float chordAcc = chordRight / (float)Math.Max(1, chordFrames);
        float kickRatio = (kickPerc / Math.Max(1, kickN)) / MathF.Max(0.01f, gapPerc / Math.Max(1, gapN));
        float hatRatio = (hatsB / Math.Max(1, hatsBN)) / MathF.Max(0.01f, hatsA / Math.Max(1, hatsAN));
        Console.WriteLine($"  music analysis cost: {analyzeTicks * 1000.0 / Stopwatch.Frequency / Math.Max(1, analyzeFrames):0.000} ms/frame");
        Console.WriteLine($"  music analysis: chordAcc={chordAcc:P0} key={keyAfterA} kickPerc/gap={kickRatio:0.0}x hatOn/off={hatRatio:0.0}x harmMid={harmMid / Math.Max(1, harmN):0.00} sections=[{string.Join(" ", changes.Select(c => $"b{c.beat}:L{c.label}{(c.repeat ? "R" : "")}"))}] starts=[{string.Join(" ", starts.Select(x => x.ToString("0.00")))}] corrections={music.Corrections} sims=[{string.Join(" ", sims.Select(x => x.ToString("0.000")))}]");
        Check(chordAcc > 0.6f, "chords recognised (C, G, Am, F)");
        Check(keyAfterA == 0, "key recognised as C major");
        Check(kickRatio > 1.5f, "drum part spikes on the kick, quiet between");
        Check(hatRatio > 1.3f, "hi-hats spike the high drum part on the off-beats");
        Check(harmMid / Math.Max(1, harmN) > 0.25f, "sustained chords live in the melody part");
        // found on the section's first beat, decided as soon as that beat ends
        var toB = changes.LastOrDefault(c => c.beat >= beatsPerSection && c.beat < beatsPerSection * 2);
        var backToA = changes.LastOrDefault(c => c.beat >= beatsPerSection * 2);
        var firstToB = changes.FirstOrDefault(c => c.beat >= beatsPerSection && c.beat < beatsPerSection * 2);
        var firstToA = changes.FirstOrDefault(c => c.beat >= beatsPerSection * 2);
        Check(starts.Any(x => Math.Abs(x - beatsPerSection) < 0.2) && starts.Any(x => Math.Abs(x - beatsPerSection * 2) < 0.2), "sections located on their exact first beat");
        Check(firstToB.beat <= beatsPerSection + 1 && firstToA.beat <= beatsPerSection * 2 + 1, "section changes reported within one beat");
        Check(toB != default && toB.label != 0, "section B gets a new label");
        Check(backToA != default && backToA.repeat && backToA.label == 0, "returning A section recognised as a repeat");
        Check(music.Corrections <= 1, "quick calls rarely need correcting");
        // the one-beat fill at beat 44 must not leave a lasting section change
        var lastBeforeA = changes.LastOrDefault(c => c.beat < beatsPerSection * 2);
        Check(lastBeforeA == default || lastBeforeA.label == toB.label || lastBeforeA.beat < 44, "a one-beat fill is taken back");
    }

    /// <summary>Chords tint the world relative to the key; a returning section gets its old world back.</summary>
    private static void TestMusicToWorlds()
    {
        Check(Renderer.ChordShiftFor(0, false, 0) == 0f, "home chord keeps the world's own colours");
        Check(Renderer.ChordShiftFor(7, false, 7) == 0f, "home chord is relative to the key");
        float fifth = MathF.Abs(Renderer.ChordShiftFor(7, false, 0));
        float tritone = MathF.Abs(Renderer.ChordShiftFor(6, false, 0));
        Check(fifth > 0f && tritone > fifth * 3f, "distant chords tint more than neighbours");
        Check(Renderer.ChordShiftFor(-1, false, 0) == 0f, "no chord, no tint");

        var rng = new Random(3);
        var mem = new SectionWorldMemory();
        var a = BackdropGenome.Random(rng);
        var b = mem.Choose(1, false, 5, a, () => BackdropGenome.Random(rng, a));
        Check(b != a, "new section gets a new world");
        var back = mem.Choose(0, true, 5, b, () => BackdropGenome.Random(rng, b));
        Check(back == a, "returning section brings its world back");
        var bAgain = mem.Choose(1, true, 5, back, () => BackdropGenome.Random(rng, back));
        Check(bAgain == b, "second section's world returns too");
        var other = mem.Choose(0, true, 6, bAgain, () => BackdropGenome.Random(rng, bAgain));
        Check(other != a, "a new song starts a fresh memory");
        Console.WriteLine($"  music to worlds: fifthTint={fifth:0.000} tritoneTint={tritone:0.000} memory=ok");
    }

    /// <summary>Flee at full speed: every enemy type keeps hunting you and none is left behind.</summary>
    private static void TestEnemiesChase()
    {
        var audio = new AudioState();
        var visual = new SynthVisual();
        var world = new GameWorld(new RunConfig { Seed = 0xC4A5EUL });
        world.Player.InvincibleDemo = true;
        world.StartGrace = 0f;
        world.BeginWave(4);
        world.ClearSpawnQueueForCapture();
        world.Player.Pos = new Vector2(-1900f, 0f);
        var roles = new[] { EnemyRole.Drifter, EnemyRole.Orbiter, EnemyRole.Sniper, EnemyRole.Dasher, EnemyRole.Turret, EnemyRole.Splitter, EnemyRole.Swarm, EnemyRole.Weaver, EnemyRole.Mortar, EnemyRole.Phantom, EnemyRole.Prism };
        for (int i = 0; i < roles.Length; i++)
            world.SpawnForCapture(roles[i], world.Player.Pos + M.FromAngle(i / (float)roles.Length * M.Tau, 520f));
        var enemies = world.Enemies.ToList();
        float dt = 1f / 60f;
        float worst = 0f, sum = 0f;
        string worstRole = "";
        for (int t = 0; t < 60 * 3; t++)
        {
            audio.ClearBeatFlags(); audio.Time += dt; visual.Advance(dt, audio);
            world.Step(dt, audio, new InputFrame(new Vector2(1f, 0f), new Vector2(1f, 0f), false, false, false, false));
        }
        int alive = 0;
        foreach (var e in enemies)
        {
            if (e.Dead) continue;
            alive++;
            float d = M.Dist(e.Pos, world.Player.Pos);
            sum += d;
            if (d > worst) { worst = d; worstRole = e.Role.ToString(); }
        }
        float avg = sum / Math.Max(1, alive);
        Console.WriteLine($"  chase: fled {world.Player.Pos.X + 1900f:0}u at speed {world.Player.SpeedFrac:0.00}; avgDist={avg:0} worst={worst:0} ({worstRole}) baseSpeed={GameWorld.EnemyBaseSpeed} farBoost={GameWorld.ChaseBoost(2000f):0.0}x");
        Check(world.Player.SpeedFrac > 0.9f, "player fled at top speed");
        Check(worst < 1100f, "no enemy type is left behind when you flee");
        Check(avg < 750f, "the pack stays on your tail");
        Check(GameWorld.EnemyBaseSpeed >= 170f, "enemies faster than before (100)");
    }

    /// <summary>The lead melody's notes are tracked from a harmonic-rich synthetic tune.</summary>
    private static void TestMelodyPitch()
    {
        const int rate = 48000;
        int[] notes = [60, 64, 67, 72, 69, 65, 62, 71];
        const float noteLen = 0.4f;
        int total = (int)(notes.Length * noteLen * rate) + MusicAnalyzer.WindowSize;
        var sig = new float[total];
        for (int i = 0; i < total; i++)
        {
            float t = i / (float)rate - MusicAnalyzer.WindowSize / (float)rate;
            int k = Math.Clamp((int)(t / noteLen), 0, notes.Length - 1);
            float f = 440f * MathF.Pow(2f, (notes[k] - 69) / 12f);
            float s = 0f;
            for (int h = 1; h <= 4; h++) s += MathF.Sin(MathF.Tau * f * h * t) * 0.25f / h;
            sig[i] = s;
        }
        var spectrum = new SpectrumAnalyzer();
        var music = new MusicAnalyzer();
        var state = new AudioState();
        var win = new float[SpectrumAnalyzer.FftSize];
        int frames = 0, right = 0;
        for (int pos = MusicAnalyzer.WindowSize; pos < total; pos += rate / 60)
        {
            float t = pos / (float)rate - MusicAnalyzer.WindowSize / (float)rate;
            Array.Copy(sig, pos - win.Length, win, 0, win.Length);
            spectrum.Analyze(win, win.Length, rate, 1f / 60f, state);
            music.Analyze(sig.AsSpan(pos - MusicAnalyzer.WindowSize, MusicAnalyzer.WindowSize), spectrum.Magnitudes, rate, 1f / 60f, state);
            float inNote = t % noteLen;
            int k = (int)(t / noteLen);
            if (k >= notes.Length || inNote < 0.2f) continue;   // the 8192 window needs ~0.17 s to fill with the new note
            frames++;
            if (MathF.Abs(state.MelodyPitch - notes[k]) < 0.5f && state.MelodyConfidence > 0.3f) right++;
        }
        float acc = right / (float)Math.Max(1, frames);
        Console.WriteLine($"  melody pitch: accuracy={acc:P0} conf={state.MelodyConfidence:0.00}");
        Check(acc > 0.8f, "melody notes tracked");
    }

    private static AudioState MusicState(float harmLow, float melodyConf, float pitch) => new()
    {
        HarmLow = harmLow, HarmMid = 0.6f, Rms = 0.1f, ChordRoot = 0,
        MelodyConfidence = melodyConf, MelodyPitch = pitch, Bpm = 120f,
    };

    /// <summary>Bass gravity wells: open on a bass note, bend and swallow bullets, slingshot the ship, detonate on a drop.</summary>
    private static void TestGravityWells()
    {
        var world = new GameWorld(new RunConfig { Seed = 0x6EA7UL });
        world.Player.InvincibleDemo = true;
        world.StartGrace = 0f;
        world.BeginWave(2);
        world.ClearSpawnQueueForCapture();
        world.SpawnForCapture(EnemyRole.Splitter, world.Player.Pos + new Vector2(2400f, 1400f));
        float dt = 1f / 60f;
        var audio = MusicState(0.9f, 0f, 60f);
        audio.ChordChanged = true;
        world.Step(dt, audio, InputFrame.Empty);
        audio.ChordChanged = false;
        Check(world.Wells.Count == 1, "a bass note opens a gravity well");
        for (int t = 0; t < 90; t++) world.Step(dt, audio, InputFrame.Empty);
        var w = world.Wells[0];
        Check(w.Strength > 0.6f, "the well grows with the bass");
        Check(M.Dist(w.Pos, world.Player.Pos) > 250f, "the well opens away from the ship");

        // a shot passing beside the core is bent toward it; one aimed at the core is swallowed
        var pass = world.SpawnEnemyBullet(w.Pos + new Vector2(-w.Radius * 0.9f, 120f), new Vector2(380f, 0f), 5f, 0.5f, BulletKind.Orb)!;
        var into = world.SpawnEnemyBullet(w.Pos + new Vector2(0f, -150f), new Vector2(0f, 300f), 5f, 0.5f, BulletKind.Orb)!;
        for (int t = 0; t < 50; t++) world.Step(dt, audio, InputFrame.Empty);
        float bend = MathF.Abs(MathF.Atan2(pass.Vel.Y, pass.Vel.X)) * 180f / MathF.PI;
        Check(bend > 8f, "bullets bend around the well");
        Check(!world.EnemyBullets.Contains(into), "the core swallows bullets");

        // skim the ring at speed, then break away: slingshot
        var p = world.Player;
        p.Pos = w.Pos + new Vector2(0f, -w.Radius * 0.55f);
        p.Vel = new Vector2(540f, 0f);
        for (int t = 0; t < 60; t++)
        {
            Vector2 toWell = M.SafeNorm(w.Pos - p.Pos);
            Vector2 tangent = new(-toWell.Y, toWell.X);
            if (Vector2.Dot(tangent, p.Vel) < 0f) tangent = -tangent;
            world.Step(dt, audio, new InputFrame(M.SafeNorm(tangent + toWell * 0.35f), tangent, false, false, false, false));
        }
        for (int t = 0; t < 40 && p.SlingBoost <= 0f; t++)
            world.Step(dt, audio, new InputFrame(M.SafeNorm(p.Pos - w.Pos), M.SafeNorm(p.Pos - w.Pos), false, false, false, false));
        float boost = p.SlingBoost;
        Check(boost > 0.3f, "skimming a well and breaking away slingshots the ship");

        // a drop detonates the well
        world.SpawnForCapture(EnemyRole.Splitter, w.Pos + new Vector2(120f, 0f));
        var victim = world.Enemies[^1];
        float hp = victim.Hp;
        world.OnMusicDrop(1f, false);
        Check(world.Wells.Count == 0, "a drop collapses the wells");
        Check(victim.Hp < hp || victim.Dead, "the collapse hurts enemies near the well");
        Console.WriteLine($"  gravity wells: strength={w.Strength:0.00} radius={w.Radius:0} bend={bend:0}deg slingshot={boost:0.00}");
    }

    /// <summary>Melody rails: drawn by the tune, curve with its pitch, boost a rider, let tuned bullets through.</summary>
    private static void TestMelodyRails()
    {
        var world = new GameWorld(new RunConfig { Seed = 0x7A11UL });
        world.StartGrace = 0f;
        world.BeginWave(2);
        world.ClearSpawnQueueForCapture();
        world.SpawnForCapture(EnemyRole.Splitter, world.Player.Pos + new Vector2(2400f, 1400f));
        float dt = 1f / 60f;
        var audio = MusicState(0f, 0.8f, 72f);
        for (int t = 0; t < 40; t++) world.Step(dt, audio, InputFrame.Empty);
        audio.MelodyPitch = 79f;   // the tune climbs: the rail turns
        for (int t = 0; t < 50; t++) world.Step(dt, audio, InputFrame.Empty);
        Check(world.Rails.Count == 1, "a melody draws a rail");
        var rail = world.Rails[0];
        Check(rail.Points.Count > 25, "the rail grows ahead while the melody plays");
        Vector2 d0 = M.SafeNorm(rail.Points[3] - rail.Points[0]);
        Vector2 d1 = M.SafeNorm(rail.Points[^1] - rail.Points[^4]);
        float turn = MathF.Acos(M.Clamp(Vector2.Dot(d0, d1), -1f, 1f)) * 180f / MathF.PI;
        Check(turn > 20f, "the rail curves with the melody's pitch");
        Check(rail.Hues[0] != rail.Hues[^1], "the rail is coloured by its notes");

        // ride it
        var p = world.Player;
        int k0 = 2;
        p.Pos = rail.Points[k0];
        Vector2 along = M.SafeNorm(rail.Points[k0 + 1] - rail.Points[k0]);
        p.Vel = along * 520f;
        float topSeen = 0f;
        for (int t = 0; t < 30; t++)
        {
            int k = 0; float best = float.MaxValue;
            for (int i = 0; i < rail.Points.Count - 1; i++) { float dd = M.DistSq(rail.Points[i], p.Pos); if (dd < best) { best = dd; k = i; } }
            Vector2 dir = M.SafeNorm(rail.Points[Math.Min(k + 2, rail.Points.Count - 1)] - p.Pos);
            world.Step(dt, audio, new InputFrame(dir, dir, false, false, false, false));
            topSeen = MathF.Max(topSeen, p.Vel.Length());
        }
        Check(p.RailRiding, "moving along the rail rides it");
        Check(topSeen > Player.TopSpeed * 1.1f, "riding boosts speed past the normal top speed");

        // a tuned bullet passes through a rider; an untuned one does not
        p.Invuln = 0f;
        int health = p.Health;
        var tuned = world.SpawnEnemyBullet(p.Pos, Vector2.Zero, 5f, 0.5f, BulletKind.Orb)!;
        tuned.Tuned = true;
        p.RailRiding = true;
        world.ResolveCollisionsForTest();
        Check(p.Health == health, "tuned bullets pass through a ship riding the rail");
        p.RailRiding = false;
        world.ResolveCollisionsForTest();
        Check(p.Health < health, "off the rail they hit as usual");
        Console.WriteLine($"  melody rails: points={rail.Points.Count} turn={turn:0}deg rideSpeed={topSeen:0} (top {Player.TopSpeed})");
    }
}
