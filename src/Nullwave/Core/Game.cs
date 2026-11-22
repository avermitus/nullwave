using System.Numerics;
using Nullwave.Audio;
using Nullwave.Render;
using Nullwave.UI;
using Nullwave.World;
using Silk.NET.Windowing;
using GameWorld = Nullwave.World.World;

namespace Nullwave.Core;

public enum AppState
{
    Title,
    Archive,
    Playing,
    Paused,
    Attuning,
    GameOver,
    Settings,
}

public sealed class Game : IDisposable
{
    private readonly IWindow _window;
    private readonly InputState? _input;
    private readonly bool _capture;
    private readonly Renderer _renderer;
    private readonly AudioEngine _audio;
    private readonly MetaProgress _meta;
    private GameWorld? _world;
    private GameWorld? _demo;
    private AppState _state = AppState.Title;
    private AppState _archiveReturn = AppState.Title;
    private AppState _settingsReturn = AppState.Title;
    private readonly SettingsMenu _settingsMenu = new();
    private float _frameDt = 1f / 60f;
    /// <summary>The simulation runs 120 steps a second, so motion is as smooth as a 120 Hz screen.</summary>
    public const float SimTick = 1f / 120f;
    /// <summary>Catch-up limit per frame (about 83 ms of game time).</summary>
    public const int MaxSimSteps = 10;
    private readonly MusicDirector _director = new();
    private int _backdropMovement = -1;
    private bool _speedDemo;
    private bool _rosterHold;
    private bool _captureCue;
    private float _edmScript = -1f;

    /// <summary>Capture "edm": six seconds of build-up, then the drop, with kicks on the beat clock.</summary>
    private void RunEdmScript(float dt)
    {
        const float dropAt = 6f;
        if (_renderer.RenderedFrames == 0) return;   // the capture warms up before it records
        float before = _edmScript;
        _edmScript += dt;
        float t = _edmScript;
        double bp = _audio.State.BeatPosition;
        float kick = MathF.Exp(-(float)(bp - Math.Floor(bp)) * 7f);
        float build = t < dropAt ? M.Clamp01(t / (dropAt - 0.5f)) : 0f;
        float drop = t >= dropAt ? MathF.Exp(-(t - dropAt) * 0.7f) : 0f;
        _director.ForceForCapture(t < dropAt ? 0.5f + build * 0.3f : 0.95f, build, 0f, drop, 0.3f,
            t < dropAt ? kick * (1f - build * 0.6f) : kick);
        if (before < dropAt && t >= dropAt) _world?.SetMusicCue(0.95f, 0f, 0f, true);
        if ((int)(before * 2f) != (int)(t * 2f))
            Console.WriteLine($"EDM | t={t:0.0} build={_director.Build:0.00} drop={_director.DropPulse:0.00} hype={_renderer.HypeScale:0.00} {_renderer.LastEdmFx}");
    }
    private int _cueFrames;
    private float _titleReroll = 22f;
    private float _sinceWorldChange = 99f;
    public MusicDirector Director => _director;
    private Palette _palette = Palette.FromSeed(0xC0FFEE);
    private int _paletteMovement;
    private ulong _paletteSeed;
    private int _menuIndex;
    private Vector2 _lastTitleMouse = new(float.NaN, float.NaN);
    private readonly Coach _coach = new();
    private readonly ClipRecorder _clip = new();
    private int _clipMomentVersion;
    private float _clipDue = -1f;
    private Task<string>? _clipSave;
    private bool _rumbling;

    /// <summary>Gamepad rumble for this frame: the kick thumps the heavy motor, bass notes hum it, snares and hats tick the light one.</summary>
    public static (float Low, float High) RumbleFor(AudioState a, bool playing, float hurt, bool dashing, int percent)
    {
        if (!playing || percent <= 0) return (0f, 0f);
        float s = percent / 100f;
        float low = (MathF.Max(a.KickPulse, a.PercLow * 0.8f) * 0.55f + a.HarmLow * 0.12f + hurt * 0.9f + (dashing ? 0.25f : 0f)) * s;
        float high = (a.SnarePulse * 0.35f + a.HatPulse * 0.12f + hurt * 0.5f) * s;
        return (Math.Clamp(low, 0f, 1f), Math.Clamp(high, 0f, 1f));
    }
    private int _recordedTracks;
    public const int TitleItemCount = 6;
    private int _pauseIndex;
    private int _archiveIndex;
    private int _attuneHover;
    private bool _debug;
    private bool _runRecorded;
    private RecordBreak _records;
    private bool _autoPilot;
    private readonly List<string> _unlocks = new();
    private float _accumulator;
    private PlayerActionBuffer _playerActions;
    private float _elapsed;
    private float _fps = 60f;
    private float _runClock;
    private float _toastTimer;
    private string _toast = "";
    private ulong _seed;
    private bool _daily;
    private Vector2 _mouseUi = new(1100, 420);
    private Vector2 _aimTilt;
    private bool _disposed;

    public AppState State => _state;
    public MetaProgress Meta => _meta;
    public AudioEngine Audio => _audio;
    public Renderer Renderer => _renderer;
    public bool CaptureMode => _capture;
    public string CaptureScenario { get; private set; } = "";
    public string SelectedSeedCode { get; private set; } = "";
    public bool IsEditingSeed { get; private set; }
    public string SeedEditBuffer { get; private set; } = "";
    public GameWorld? ActiveWorld => _world;
    public GameWorld? DemoWorld => _demo;
    public float Fps => _fps;
    /// <summary>--max-fx: captures render with every motion and effect setting at its maximum.</summary>
    public static bool MaxFx;
#if DEBUG
    /// <summary>NULLWAVE_PLAYTEST=1: automated playtest (no Spotify rerouting, score muted, save copy).</summary>
    public static bool Playtest => Environment.GetEnvironmentVariable("NULLWAVE_PLAYTEST") == "1";
#endif
    /// <summary>Set from --app before the game is constructed.</summary>
    public static string? AppOverride;
    /// <summary>Transient smoke/record CLI overrides (never saved to save.json).</summary>
    public static string? ModsOverride;
    public static string? AttuneOverride;
    public static string? SeedOverride;
    public static VisualTheme? ThemeOverride;
    public static string? TrackOverride;
    public static MapKind MapOverride;
    public static bool SmokeMode;
    public MapKind SelectedMap { get; private set; } = MapOverride;

    public void StartSmokeRun(ulong seed)
    {
        ulong effectiveSeed = seed;
        if (!string.IsNullOrWhiteSpace(SeedOverride) && SeedCode.TryParse(SeedOverride, out ulong parsed, out _))
            effectiveSeed = _meta.TryGetSeedInfo(SeedOverride, out _, out _, out _, out _) ? _meta.ResolveSeed(SeedOverride) : parsed;

        HashSet<string>? modOverride = !string.IsNullOrWhiteSpace(ModsOverride)
            ? ModifierCatalog.ResolvePreset(ModsOverride)
            : null;

        StartRun(effectiveSeed, false, modOverride);
        _autoPilot = true;
        if (_world != null)
        {
            _world.Player.InvincibleDemo = true;
            ApplyAttuneOverride(_world.Player, AttuneOverride);
            Console.WriteLine($"SMOKECONFIG | map={_world.MapKind} seed={SeedCode.ToCode(_world.Seed)} mods={_world.Mods.Active.Count} mult=x{_world.Mods.ScoreMultiplier:0.00} bossPhases={_world.Mods.BossPhases} attunes={_world.Player.AttunementCount} synergies={SynergyCatalog.CountActive(_world.Player.ActiveSynergies)}");
        }
    }

    private static void ApplyAttuneOverride(World.Player player, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        string spec = raw.Trim();
        if (spec.Equals("showcase", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var id in new[] { "split", "split", "fan", "pierce", "rapid", "rapid", "homing" })
                AttunementCatalog.Apply(player, id);
        }
        else if (spec.Equals("synergy", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var id in new[] { "split", "split", "homing", "homing", "heavy", "heavy", "rapid", "rapid", "phase", "phase", "grazer", "grazer" })
                AttunementCatalog.Apply(player, id);
        }
        else
        {
            foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var def = AttunementCatalog.Get(part.ToLowerInvariant());
                if (def != null) AttunementCatalog.Apply(player, def.Id);
            }
        }
    }

    public Game(IWindow window, Renderer renderer, InputState? input, bool capture = false)
    {
        _window = window;
        _renderer = renderer;
        _input = input;
        _capture = capture;
        _audio = new AudioEngine(enableOutput: !capture, enableCapture: !capture);
        string captureMeta = Path.Combine(Path.GetTempPath(), "nullwave-capture-meta.json");
        if (capture || SmokeMode)
        {
            try { File.Delete(captureMeta); } catch { /* best-effort disposable capture save */ }
        }
        string? metaPath = capture || SmokeMode ? captureMeta : null;
#if DEBUG
        if (!capture && Playtest)
        {
            // work on a copy of the owner's save so playtests never touch their records
            string playtestMeta = Path.Combine(Path.GetTempPath(), "nullwave-playtest-meta.json");
            try { if (File.Exists(MetaProgress.DefaultSavePath)) File.Copy(MetaProgress.DefaultSavePath, playtestMeta, true); } catch { }
            metaPath = playtestMeta;
        }
#endif
        _meta = MetaProgress.Load(metaPath);
        if (capture) PrepareCaptureMeta();
        _meta.Settings ??= new GameSettings();
        _meta.Settings.Sanitize();
        GameSettings.Current = capture ? new GameSettings() : _meta.Settings;
        if (capture && MaxFx) GameSettings.Current.SetMaxMotionAndFx();
        if (ThemeOverride is { } forcedTheme)
        {
            GameSettings.Current = GameSettings.Current.Clone();
            GameSettings.Current.Theme = forcedTheme;
        }
        ApplyAudioSettings();
        bool startFullscreen = !capture && GameSettings.Current.Fullscreen;
#if DEBUG
        if (Playtest) startFullscreen = false;   // playtests run in a window, never over the owner's screen
#endif
        if (startFullscreen) SetFullscreen(true);
        _settingsMenu.AudioChanged = ApplyAudioSettings;
        _settingsMenu.FullscreenChanged = SetFullscreen;
        _settingsMenu.CycleAudioSource = () =>
        {
            string label = _audio.CycleSource();
            _meta.AudioApp = _audio.PreferredApp;
            return label;
        };
        string app = AppOverride ?? _meta.AudioApp;
        if (!string.IsNullOrWhiteSpace(TrackOverride))
        {
            if (!_audio.LoadLocalTrack(TrackOverride))
            {
                _toast = "TRACK LOAD FAILED // USING SCORE";
                _toastTimer = 4.5f;
            }
        }
        else if (!capture && !string.IsNullOrWhiteSpace(app))
        {
            if (app.Equals("system", StringComparison.OrdinalIgnoreCase)) _meta.AudioApp = "";
            else _audio.SelectApp(app);
            if (AppOverride != null) { _meta.AudioApp = _audio.PreferredApp; _meta.Save(); }
        }
        StartDemo();
    }

    private void PrepareCaptureMeta()
    {
        // Capture mode is a visual review harness; do not mutate the real archive on disk.
        if (_meta.BestWave < 12) _meta.BestWave = 12;
        if (_meta.BestScore < 84500) _meta.BestScore = 84500;
        _meta.TotalBossKills = Math.Max(3, _meta.TotalBossKills);
        _meta.TotalGrazes = Math.Max(214, _meta.TotalGrazes);
        _meta.TotalKills = Math.Max(310, _meta.TotalKills);
        _meta.TotalShatters = Math.Max(41, _meta.TotalShatters);
        _meta.Runs = Math.Max(4, _meta.Runs);
        foreach (var id in new[] { "bass-bloom", "noise-floor", "phase-skin", "blind-choir", "quiet-signal", "overclocked" })
            _meta.UnlockedModifiers.Add(id);
        // flight recorder: a short history so archive/gameover captures show the telemetry; the
        // gameover scenario's chain of 42 beats this best of 36 and shows the NEW BEST callout
        _meta.BestCombo = Math.Max(36, _meta.BestCombo);
        ulong s1 = 0x5EEDC0DEUL;
        ulong s2 = SeedCode.DailySeed(new DateTime(2026, 11, 17));
        ulong s3 = 0xC0FFEE1234UL;
        if (_meta.RecentRuns.Count == 0)
        {
            _meta.RecentRuns.Add(new RunRecord { Seed = SeedCode.ToCode(s1), SeedValue = s1, Wave = 12, Score = 84_500, MaxCombo = 36, Kills = 140, Grazes = 61, Duration = 402f, Movement = "CITY", Attunements = 6, Records = RunRecords.Score | RunRecords.Wave });
            _meta.RecentRuns.Add(new RunRecord { Seed = SeedCode.ToCode(s2), SeedValue = s2, Daily = true, Wave = 9, Score = 41_250, MaxCombo = 28, Kills = 96, Grazes = 40, Duration = 288f, Movement = "SPIRAL", Attunements = 4, Modifiers = 1 });
            _meta.RecentRuns.Add(new RunRecord { Seed = SeedCode.ToCode(s3), SeedValue = s3, Wave = 6, Score = 18_900, MaxCombo = 17, Kills = 58, Grazes = 22, Duration = 175f, Movement = "HENGE", Attunements = 2 });
        }
        _meta.SeedRecords[SeedCode.ToCode(s1)] = new SeedRecord { SeedValue = s1, BestScore = 84_500, BestWave = 12, Runs = 2, LastPlayedUtc = "2026-11-18T02:00:00Z" };
        _meta.SeedRecords[SeedCode.ToCode(s2)] = new SeedRecord { SeedValue = s2, BestScore = 41_250, BestWave = 9, Runs = 1, Daily = true, LastPlayedUtc = "2026-11-17T20:00:00Z" };
        _meta.SeedRecords[SeedCode.ToCode(s3)] = new SeedRecord { SeedValue = s3, BestScore = 18_900, BestWave = 6, Runs = 1, LastPlayedUtc = "2026-11-16T18:00:00Z" };
    }

    /// <summary>Capture: draw only the generated backdrop (no arena, entities or HUD).</summary>
    public bool BackdropOnly { get; private set; }

    public void ConfigureCapture(string scenario)
    {
        if (scenario is "backdrop" or "edmbackdrop")
        {
            BackdropOnly = true;
            _state = AppState.Title;
            if (scenario == "edmbackdrop") _edmScript = 0f;
            return;
        }
        CaptureScenario = scenario;
        _state = AppState.Title;
        _menuIndex = 0;
        if (scenario is "map" or "mapplay")
        {
            StartRun(0x5EEDC0DEUL, false);
            _world!.Player.InvincibleDemo = true;
            _world.BeginWave(4);
            _autoPilot = true;
            return;
        }
        if (scenario == "roster")
        {
            // one of every enemy around a parked ship, for readability checks
            StartRun(0x5EEDC0DEUL, false);
            var w = _world!;
            w.Player.InvincibleDemo = true;
            w.BeginWave(4);
            w.ClearSpawnQueueForCapture();
            var roles = new[] { EnemyRole.Drifter, EnemyRole.Orbiter, EnemyRole.Sniper, EnemyRole.Dasher, EnemyRole.Turret, EnemyRole.Splitter, EnemyRole.Swarm, EnemyRole.Weaver, EnemyRole.Mortar, EnemyRole.Phantom, EnemyRole.Prism };
            for (int i = 0; i < roles.Length; i++)
                w.SpawnForCapture(roles[i], w.Player.Pos + M.FromAngle(i / (float)roles.Length * M.Tau, i % 2 == 0 ? 330f : 420f));
            _rosterHold = true;
            return;
        }
        if (scenario == "songlasers")
        {
            StartRun(0x5EEDC0DEUL, false);
            _autoPilot = true;
            _world!.Player.InvincibleDemo = true;
            _world.BeginWave(3);
            _captureCue = true;
            return;
        }
        if (scenario == "speed")
        {
            StartRun(0x5EEDC0DEUL, false);
            _speedDemo = true;
            _world!.Player.InvincibleDemo = true;
            AttunementCatalog.Apply(_world.Player, "split");
            _world.BeginWave(4);
            _world.PhaseSyncs = 4;
            _world.Player.PhaseSyncs = 4;
            _world.Player.PhaseSyncFlash = 0.85f;
            return;
        }
        if (scenario == "songboss")
        {
            // THE DROP, fought with the Lead ship, for screenshots and reels
            _meta.SelectedShip = "Lead";
            StartRun(0x5EEDC0DEUL, false);
            _autoPilot = true;
            _world!.Player.InvincibleDemo = true;
            _world.BeginWave(4);
            _world.ClearSpawnQueueForCapture();
            _world.SpawnSongBoss();
            return;
        }
        if (scenario == "edm")
        {
            // a scripted build-up into a drop, for reels of the EDM show
            StartRun(0x5EEDC0DEUL, false);
            _autoPilot = true;
            _world!.Player.InvincibleDemo = true;
            AttunementCatalog.Apply(_world.Player, "split");
            _world.BeginWave(4);
            _edmScript = 0f;
            return;
        }
        if (scenario is "drop" or "breakdown")
        {
            StartRun(0x5EEDC0DEUL, false);
            _autoPilot = true;
            _world!.Player.InvincibleDemo = true;
            AttunementCatalog.Apply(_world.Player, "split");
            AttunementCatalog.Apply(_world.Player, "fan");
            _world.BeginWave(4);
            if (scenario == "drop")
                _director.ForceForCapture(0.95f, 0f, 0f, 0.85f, 0.45f, 0.9f);
            else
                _director.ForceForCapture(0.1f, 0f, 1f, 0f, -0.6f, 0.1f);
            return;
        }
        if (scenario is "settings" or "settingsend")
        {
            OpenSettings(AppState.Title);
            if (scenario == "settingsend") _settingsMenu.SelectLastForCapture();
            return;
        }
        if (string.Equals(scenario, "archive", StringComparison.OrdinalIgnoreCase))
        {
            _state = AppState.Archive;
            _archiveReturn = AppState.Title;
            _archiveIndex = 1;
            CycleArchiveSeed(1);
            return;
        }

        if (scenario == "overdrive")
        {
            StartRun(0x5EEDC0DEUL, false);
            _autoPilot = true;
            _world!.Player.InvincibleDemo = true;
            ApplyAttuneOverride(_world.Player, string.IsNullOrWhiteSpace(AttuneOverride) ? "synergy" : AttuneOverride);
            _world.BeginWave(4);
            _world.PhaseSyncs = 3;
            _world.Player.PhaseSyncs = 3;
            _world.Player.PhaseSyncStreak = 3;
            _world.Player.ShotCounter = 3;
            _director.ForceForCapture(0.95f, 0f, 0f, 0.90f, 0.35f, 0.92f);
            return;
        }
        if (scenario == "synergy")
        {
            StartRun(0x5EEDC0DEUL, false);
            _autoPilot = true;
            _world!.Player.InvincibleDemo = true;
            ApplyAttuneOverride(_world.Player, "synergy");
            _world.BeginWave(4);
            _world.Player.VoidPhantomFlash = 0.85f;
            _world.Player.ShotCounter = 3; // next shot fires Rail Cadence bolt alongside Seeker Salvo
            return;
        }
        if (scenario is "combat" or "boss" or "laser" or "hurt")
        {
            StartRun(0x5EEDC0DEUL, false);
            _autoPilot = true;
            _world!.Player.InvincibleDemo = true;
            if (!string.IsNullOrWhiteSpace(AttuneOverride))
            {
                ApplyAttuneOverride(_world.Player, AttuneOverride);
            }
            else
            {
                AttunementCatalog.Apply(_world.Player, "split");
                AttunementCatalog.Apply(_world.Player, "split");
                AttunementCatalog.Apply(_world.Player, "fan");
                AttunementCatalog.Apply(_world.Player, "pierce");
                AttunementCatalog.Apply(_world.Player, "rapid");
            }
            _world.BeginWave(scenario is "combat" or "hurt" ? 4 : 5);
            if (scenario == "hurt")
            {
                _world.Player.Health = 1;
                _world.Player.HealthDisplay = 2.4f;
            }
            if (scenario == "laser")
            {
                Vector2 p = _world.Player.Pos;
                _world.SpawnLaser(p + new Vector2(-900, -540), p + new Vector2(600, 100), 0.4f, 4f, 42f, 0.66f, 0.10f, true);
                _world.SpawnLaser(p + new Vector2(900, -540), p + new Vector2(-600, 100), 0.5f, 4f, 28f, 0.92f, -0.07f, true);
                _world.SpawnLaser(p + new Vector2(-400, -540), p, 0.7f, 4f, 20f, 0.52f, 0f, false);
            }
        }
        else if (scenario == "attune")
        {
            StartRun(0x5EEDC0DEUL, false);
            _world!.Score = 3_200;
            _world.Player.Resonance = 45f;
            _world.Combo = 7;
            AttunementCatalog.Apply(_world!.Player, "split");
            _world.ForceAttunementForTest();
            _state = AppState.Attuning;
            _attuneHover = 0;
        }
        else if (scenario == "pause")
        {
            StartRun(0x5EEDC0DEUL, false);
            _world!.Player.InvincibleDemo = true;
            _world.BeginWave(4);
            _world.Score = 12_500;
            _world.Kills = 41;
            _world.Player.Resonance = 62f;
            if (!string.IsNullOrWhiteSpace(AttuneOverride))
            {
                ApplyAttuneOverride(_world.Player, AttuneOverride);
            }
            else
            {
                AttunementCatalog.Apply(_world.Player, "split");
                AttunementCatalog.Apply(_world.Player, "split");
                AttunementCatalog.Apply(_world.Player, "fan");
                AttunementCatalog.Apply(_world.Player, "pierce");
                AttunementCatalog.Apply(_world.Player, "rapid");
                AttunementCatalog.Apply(_world.Player, "rapid");
                AttunementCatalog.Apply(_world.Player, "homing");
            }
            _state = AppState.Paused;
            _pauseIndex = 0;
        }
        else if (scenario == "gameover")
        {
            StartRun(0xDEADBEEFUL, false);
            _autoPilot = false;
            var w = _world!;
            w.BeginWave(12);
            w.Player.InvincibleDemo = false;
            w.StartGrace = 0f;
            w.Player.Health = 1;
            w.BossKills = 2;
            w.Kills = 137;
            w.Grazes = 88;
            w.Shatters = 19;
            w.Bursts = 3;
            w.MaxCombo = 42;
            w.Score = 73_400;
            AttunementCatalog.Apply(w.Player, "split");
            AttunementCatalog.Apply(w.Player, "rapid");
            w.ForceDeathForTest("laser:LASER CROSS");
        }
    }

    public void CaptureWarmup(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            // "hurt": take a real hit just before the frame, so the hit marker shows where it came from
            if (CaptureScenario == "hurt" && i == frames - 6 && _world != null)
            {
                _world.StartGrace = 0f;
                _world.Player.Invuln = 0f;
                _world.Player.Health = 2;
                _world.Player.InvincibleDemo = false;
                _world.Player.IsDashing = false;
                _world.HurtPlayer(1f, "contact:Dasher", _world.Player.Pos + new Vector2(260f, -180f));
                _world.Player.InvincibleDemo = true;
            }
            if (CaptureScenario == "boss" && i == frames - 20 && _world?.BossEnemy is { Dead: false } boss && boss.BossPhase == 0)
            {
                boss.Spawning = false;
                boss.BossHealthDisplay = MathF.Max(boss.BossHealthDisplay, 0.72f);
                boss.Hp = boss.MaxHp * 0.56f;
            }
            if (CaptureScenario == "overdrive" && i == frames - 18 && _world != null)
            {
                _world.OnMusicDrop(1.0f, false);
                _world.Player.SyncOverdriveTimer = 4.5f;
                _world.Player.PhaseSyncFlash = 0.75f;
            }
            Update(1f / 60f);
        }
    }

    private void StartDemo()
    {
        if (_demo != null) return;
        _demo = new GameWorld(new RunConfig
        {
            Seed = 0xC0FFEE1234UL,
            Map = SelectedMap,
            Demo = true,
            Sfx = _audio.Sfx,
            UnlockedAttunements = new HashSet<string>(_meta.UnlockedAttunements),
        });
        _demo.Player.InvincibleDemo = true;
        AttunementCatalog.Apply(_demo.Player, "split");
        AttunementCatalog.Apply(_demo.Player, "fan");
        _demo.BeginWave(3);
        _palette = Palette.FromSeed(_demo.Seed, _demo.Movement);
        _paletteMovement = _demo.Movement;
        _paletteSeed = _demo.Seed;
        _renderer.Palette = _palette;
    }

    private void ResetDemo()
    {
        _demo = null;
        StartDemo();
    }

    public void StartRun(ulong seed, bool daily, HashSet<string>? modifierOverride = null)
    {
        _seed = seed;
        _daily = daily;
        _recordedTracks = 0;
        _playerActions = default;
        _world = new GameWorld(new RunConfig
        {
            Seed = seed,
            Map = SelectedMap,
            Daily = daily,
            Sfx = _audio.Sfx,
            SelectedModifiers = new HashSet<string>(modifierOverride ?? _meta.SelectedModifiers),
            UnlockedAttunements = new HashSet<string>(_meta.UnlockedAttunements),
            Ship = Ships.Parse(_meta.SelectedShip),
        });
        _state = AppState.Playing;
        _runRecorded = false;
        _records = default;
        _unlocks.Clear();
        _accumulator = 0f;
        _runClock = 0f;
        _attuneHover = 0;
        _autoPilot = false;
        _palette = Palette.FromSeed(seed, 0);
        _paletteMovement = 0;
        _paletteSeed = seed;
        _renderer.Palette = _palette;
        _audio.SetQuiet(_world.Mods.SynthDisabled);
        _renderer.ResetBackdrop();
        _backdropMovement = 0;
        _audio.Sfx.Trigger(SfxType.Wave, 0.6f);
        _toast = "";
        _toastTimer = 0f;
        _coach.Begin(_world, !_capture && GameSettings.Current.TutorialTips && !_meta.TutorialDone);
        _clip.Reset();
        _clipMomentVersion = 0;
        _clipDue = -1f;
#if DEBUG
        if (Playtest && Environment.GetEnvironmentVariable("NULLWAVE_PLAYTEST_GOD") == "1") _world!.PlaytestGod = true;
#endif
    }

    public void Update(float dt)
    {
        if (_disposed) return;
        dt = Math.Clamp(dt, 0.0005f, 0.08f);
        _frameDt = dt;
        _elapsed += dt;
        _fps = _fps <= 1f ? 1f / dt : M.ExpSmooth(_fps, 1f / dt, 3.5f, dt);
        _audio.Update(dt);
        _director.Update(_audio.State, dt);
        if (_edmScript >= 0f) RunEdmScript(dt);
        _renderer.Mood = _director;
        var music = _audio.State;
        bool structured = music.SectionCount >= 2;
        _sinceWorldChange += dt;
        if (!BackdropOnly && GameSettings.Current.MusicFx > 0)
        {
            // each song section gets its own world; a returning chorus brings its world back
            if (music.SectionChanged)
            {
                _renderer.ShowSection(music.SectionLabel, music.SectionRepeat, music.SongEpoch);
                _sinceWorldChange = 0f;
                _titleReroll = 22f;
            }
            // a key change is a big musical turn: new world, unless a section just changed it
            else if (music.KeyChanged && _sinceWorldChange > 8f)
            {
                _renderer.RerollBackdrop();
                _sinceWorldChange = 0f;
            }
            // the texture changed (new instrument, riser, vocal): a shockwave through the world
            if (music.TextureChanged)
                _renderer.PulseBackdrop(_renderer.Camera, 1.0f);
        }
        if (_state == AppState.Title && !BackdropOnly && !structured && (_titleReroll -= dt) <= 0f)
        {
            _titleReroll = 22f;
            _renderer.RerollBackdrop();
        }
        if (_director.DropTriggered && GameSettings.Current.MusicFx > 0)
        {
            // before the song's structure is known, every drop opens onto a new world;
            // after that the section memory decides (so a returning drop gets its world back)
            if (!structured && _sinceWorldChange > 4f) { _renderer.RerollBackdrop(); _sinceWorldChange = 0f; }
            var w = _state == AppState.Playing ? _world : _state == AppState.Title ? _demo : null;
            w?.OnMusicDrop(GameSettings.Current.MusicFx / 100f, GameSettings.Current.ReduceFlashing);
        }
        if (_toastTimer > 0f) _toastTimer -= dt;
        if (_clipSave is { IsCompleted: true })
        {
            _toast = _clipSave.Result;
            _toastTimer = 6f;
            _clipSave = null;
        }
        if (_input != null && !_capture)
        {
            bool playing = _state == AppState.Playing && _world != null;
            var (low, high) = RumbleFor(_audio.State, playing, playing ? _world!.Player.HitFlash : 0f, playing && _world!.Player.IsDashing, GameSettings.Current.Rumble);
            if (playing || _rumbling) _input.SetRumble(low, high);
            _rumbling = playing;
        }

        if (_input != null)
        {
            _input.BeginFrame();
            _mouseUi = WindowMouseToUi(_input.MousePosition);
            if (!_capture)
            {
                if (_input.FullscreenPressed) ToggleFullscreen();
                if (_input.AudioDevicePressed)
                {
                    _toast = "AUDIO // " + _audio.CycleSource();
                    _toastTimer = 3f;
                    _meta.AudioApp = _audio.PreferredApp;
                    _meta.Save();
                }
                if (_input.DebugPressed) _debug = !_debug;
            }
        }

        switch (_state)
        {
            case AppState.Title:
                UpdateDemo(dt);
                UpdateTitle();
                break;
            case AppState.Archive:
                if (_archiveReturn == AppState.Title) UpdateDemo(dt);
                UpdateArchive();
                break;
            case AppState.Playing:
                UpdatePlaying(dt);
                break;
            case AppState.Paused:
                UpdatePaused();
                break;
            case AppState.Attuning:
                UpdateAttuning();
                break;
            case AppState.GameOver:
                UpdateGameOver();
                break;
            case AppState.Settings:
                if (_settingsReturn == AppState.Title) UpdateDemo(dt);
                if (_input != null && _settingsMenu.Update(_input, _mouseUi, _audio.Sfx, dt))
                {
                    _meta.Settings = GameSettings.Current;
                    _meta.Save();
                    _state = _settingsReturn;
                    _audio.Sfx.Trigger(SfxType.Menu, 0.25f);
                }
                break;
        }

        GameWorld? aimWorld = _state is AppState.Playing or AppState.Paused or AppState.Attuning or AppState.GameOver ? _world : _demo;
        if (aimWorld != null)
        {
            Vector2 targetAim = aimWorld.Player.AimDir * 0.65f;
            if (_input != null && _state == AppState.Playing)
            {
                Vector2 mouseNorm = new(
                    (_mouseUi.X - Renderer.DesignWidth * 0.5f) / (Renderer.DesignWidth * 0.5f),
                    (_mouseUi.Y - Renderer.DesignHeight * 0.5f) / (Renderer.DesignHeight * 0.5f));
                targetAim = Vector2.Lerp(aimWorld.Player.AimDir, mouseNorm, 0.55f);
            }
            targetAim = Renderer.ClampAimTilt(targetAim);
            _aimTilt = _capture
                ? targetAim
                : Vector2.Lerp(_aimTilt, targetAim, 1f - MathF.Exp(-dt * 6.5f));
        }
    }

    private void OpenSettings(AppState from)
    {
        _settingsReturn = from;
        _settingsMenu.Open();
        _state = AppState.Settings;
    }

    private void ApplyAudioSettings()
    {
        var s = GameSettings.Current;
        _audio.SetVolumes(s.Music, s.Sfx);
        _audio.SetAppBuffering(s.BufferSpotify);
#if DEBUG
        if (Playtest)
        {
            // never reroute the owner's Spotify or play the score over their music during a playtest
            _audio.SetAppBuffering(false);
            _audio.SetQuiet(true);
        }
#endif
    }

    private void UpdateDemo(float dt)
    {
        if (_demo == null)
        {
            StartDemo();
            return;
        }
        var demo = _demo;
        if (demo.Phase == GamePhase.Attuning)
        {
            if (demo.Offers.Count > 0) demo.ChooseAttunement(0);
            else demo.BeginWave(demo.Wave + 1);
        }
        if (demo.Phase == GamePhase.Dead)
        {
            ResetDemo();
            return;
        }
        _accumulator += dt;
        int steps = 0;
        while (_accumulator >= SimTick && steps++ < MaxSimSteps)
        {
            demo.Step(SimTick, _audio.State, Pilot.Think(demo));
            _accumulator -= SimTick;
            if (demo.Phase == GamePhase.Attuning || demo.Phase == GamePhase.Dead) break;
        }
        if (demo.Phase == GamePhase.Attuning && demo.Offers.Count > 0) demo.ChooseAttunement(0);
        if (demo.Phase == GamePhase.Dead) ResetDemo();
        _paletteMovement = demo.Movement;
        _paletteSeed = demo.Seed;
        _palette = Palette.FromSeed(demo.Seed, demo.Movement);
    }

    private void UpdateTitle()
    {
        if (_input == null) return;
        if (_input.UpPressed) { _menuIndex = (_menuIndex + TitleItemCount - 1) % TitleItemCount; _audio.Sfx.Trigger(SfxType.Menu, 0.22f); }
        if (_input.DownPressed) { _menuIndex = (_menuIndex + 1) % TitleItemCount; _audio.Sfx.Trigger(SfxType.Menu, 0.22f); }
        bool movedMouse = _mouseUi != _lastTitleMouse;
        _lastTitleMouse = _mouseUi;
        int hoveredRow = -1;
        if (_mouseUi.X >= Screens.TitleMenuX && _mouseUi.X < Screens.TitleMenuX + Screens.TitleMenuWidth &&
            _mouseUi.Y >= Screens.TitleMenuY && _mouseUi.Y < Screens.TitleMenuY + TitleItemCount * Screens.TitleMenuRowHeight)
        {
            float offset = _mouseUi.Y - Screens.TitleMenuY;
            if (offset % Screens.TitleMenuRowHeight < Screens.TitleMenuRowHeightVisible)
                hoveredRow = (int)(offset / Screens.TitleMenuRowHeight);
        }
        if (movedMouse && hoveredRow >= 0) _menuIndex = hoveredRow;
        bool titleClick = _input.MouseLeftPressed && hoveredRow >= 0;
        if (titleClick) _menuIndex = hoveredRow;

        bool shipClick = _input.MouseLeftPressed && _mouseUi.Y >= Screens.TitleShipY &&
            _mouseUi.Y < Screens.TitleShipY + Screens.TitleShipButtonHeight;
        bool clickLeft = shipClick && _mouseUi.X >= Screens.TitleShipLeftX && _mouseUi.X < Screens.TitleShipLeftX + Screens.TitleShipButtonWidth;
        bool clickRight = shipClick && _mouseUi.X >= Screens.TitleShipRightX && _mouseUi.X < Screens.TitleShipRightX + Screens.TitleShipButtonWidth;
        if ((_menuIndex != 5 && (_input.LeftPressed || _input.RightPressed)) || clickLeft || clickRight)
        {
            var ships = Ships.All;
            int i = Array.IndexOf(ships, Ships.Parse(_meta.SelectedShip));
            i = (i + ((_input.RightPressed || clickRight) ? 1 : ships.Length - 1)) % ships.Length;
            _meta.SelectedShip = ships[i].ToString();
            _meta.Save();
            _audio.Sfx.Trigger(SfxType.Menu, 0.3f);
        }
        if (_menuIndex == 5 && (_input.LeftPressed || _input.RightPressed))
        {
            SelectedMap = MapCatalog.Next(SelectedMap, _input.LeftPressed ? -1 : 1);
            ResetDemo();
        }
        if (_input.ConfirmPressed || titleClick)
        {
            _audio.Sfx.Trigger(SfxType.Menu, 0.35f);
            switch (_menuIndex)
            {
                case 0:
                    StartRun(SeedCode.NewSeed(), false);
                    break;
                case 1:
                    StartRun(SeedCode.DailySeed(DateTime.Now), true);
                    break;
                case 2:
                    _archiveReturn = AppState.Title;
                    _archiveIndex = 0;
                    _state = AppState.Archive;
                    break;
                case 3:
                    OpenSettings(AppState.Title);
                    break;
                case 5:
                    SelectedMap = MapCatalog.Next(SelectedMap, 1);
                    ResetDemo();
                    break;
                case 4:
                    _window.Close();
                    break;
            }
        }
    }

    public bool CycleArchiveSeed(int dir = 1)
    {
        IsEditingSeed = false;
        var seeds = _meta.GetReplayableSeeds();
        if (seeds.Count == 0) return false;
        int idx = seeds.FindIndex(s => string.Equals(s, SelectedSeedCode, StringComparison.OrdinalIgnoreCase));
        int next = idx < 0
            ? (dir >= 0 ? 0 : seeds.Count - 1)
            : (idx + (dir >= 0 ? 1 : -1) + seeds.Count) % seeds.Count;
        SelectedSeedCode = seeds[next];
        SeedEditBuffer = SelectedSeedCode;
        return true;
    }

    public void BeginSeedEdit()
    {
        if (_archiveReturn == AppState.Paused) return;
        IsEditingSeed = true;
        SeedEditBuffer = SelectedSeedCode;
    }

    public void TypeSeedChar(char ch)
    {
        if (!IsEditingSeed || SeedEditBuffer.Length >= SeedCode.CodeLength) return;
        char norm = SeedCode.NormalizeChar(ch);
        if (norm != '\0') SeedEditBuffer += norm;
    }

    public void BackspaceSeedEdit()
    {
        if (!IsEditingSeed || SeedEditBuffer.Length == 0) return;
        SeedEditBuffer = SeedEditBuffer[..^1];
    }

    public bool CommitSeedEdit()
    {
        if (!IsEditingSeed) return false;
        IsEditingSeed = false;
        if (string.IsNullOrWhiteSpace(SeedEditBuffer))
        {
            SelectedSeedCode = "";
            SeedEditBuffer = "";
            return false;
        }
        if (!SeedCode.TryParse(SeedEditBuffer, out _, out string canonical))
            return false;
        SelectedSeedCode = canonical;
        SeedEditBuffer = canonical;
        return true;
    }

    public void CancelSeedEdit()
    {
        IsEditingSeed = false;
        SeedEditBuffer = SelectedSeedCode;
    }

    public void ClearSelectedSeed()
    {
        IsEditingSeed = false;
        SelectedSeedCode = "";
        SeedEditBuffer = "";
    }

    public bool LaunchSelectedSeed()
    {
        if (_archiveReturn == AppState.Paused) return false;
        if (IsEditingSeed && !CommitSeedEdit()) return false;
        if (string.IsNullOrWhiteSpace(SelectedSeedCode)) return false;
        ulong seed = _meta.ResolveSeed(SelectedSeedCode);
        StartRun(seed, false);
        return true;
    }

    private void UpdateArchive()
    {
        if (_input == null) return;
        if (IsEditingSeed)
        {
            if (_input.BackPressed)
            {
                CancelSeedEdit();
                _audio.Sfx.Trigger(SfxType.Menu, 0.2f);
                return;
            }
            if (_input.ConfirmPressed)
            {
                if (CommitSeedEdit()) _audio.Sfx.Trigger(SfxType.Unlock, 0.35f);
                else _audio.Sfx.Trigger(SfxType.Menu, 0.25f);
                return;
            }
            if (_input.BackspacePressed)
            {
                BackspaceSeedEdit();
                _audio.Sfx.Trigger(SfxType.Menu, 0.18f);
            }
            Span<char> typed = stackalloc char[8];
            int n = _input.ReadTypedSeedChars(typed);
            for (int i = 0; i < n; i++)
            {
                int before = SeedEditBuffer.Length;
                TypeSeedChar(typed[i]);
                if (SeedEditBuffer.Length != before) _audio.Sfx.Trigger(SfxType.Menu, 0.2f);
            }
            return;
        }

        if (_input.BackPressed || _input.TabPressed)
        {
            _state = _archiveReturn;
            if (_state == AppState.Title) _menuIndex = 2;
            _audio.Sfx.Trigger(SfxType.Menu, 0.25f);
            return;
        }
        if (_input.RPressed)
        {
            if (CycleArchiveSeed(1)) _audio.Sfx.Trigger(SfxType.Menu, 0.28f);
            else _audio.Sfx.Trigger(SfxType.Hurt, 0.12f);
            return;
        }
        if (_input.NPressed)
        {
            ClearSelectedSeed();
            _audio.Sfx.Trigger(SfxType.Menu, 0.22f);
            return;
        }
        if (_archiveReturn != AppState.Paused && _input.CPressed)
        {
            BeginSeedEdit();
            _audio.Sfx.Trigger(SfxType.Menu, 0.28f);
            return;
        }
        if (_archiveReturn != AppState.Paused && _input.LPressed)
        {
            if (LaunchSelectedSeed()) return;
            _audio.Sfx.Trigger(SfxType.Hurt, 0.12f);
            return;
        }
        int col = _archiveIndex / 8;
        int row = _archiveIndex % 8;
        if (_input.UpPressed) row = (row + 7) % 8;
        if (_input.DownPressed) row = (row + 1) % 8;
        if (_input.LeftPressed && col > 0) col--;
        if (_input.RightPressed && col < 1) col++;
        _archiveIndex = col * 8 + row;
        if (_input.ConfirmPressed || _input.SpacePressed)
        {
            if (_archiveReturn == AppState.Paused)
            {
                _audio.Sfx.Trigger(SfxType.Hurt, 0.12f);
            }
            else
            {
                var def = ModifierCatalog.All[_archiveIndex];
                if (_meta.ToggleModifier(def.Id))
                {
                    _audio.Sfx.Trigger(SfxType.Menu, 0.3f);
                }
                else
                {
                    _audio.Sfx.Trigger(SfxType.Hurt, 0.12f);
                }
            }
        }
    }

    private void UpdatePlaying(float dt)
    {
        if (_world == null) return;
        if (_input != null && _input.PausePressed)
        {
            _state = AppState.Paused;
            _playerActions = default;
            _pauseIndex = 0;
            return;
        }

        bool playerControlled = false;
        InputFrame frame;
        if (_rosterHold)
        {
            frame = new InputFrame(Vector2.Zero, new Vector2(1, 0), false, false, false, false);
        }
        else if (_speedDemo)
        {
            // capture: carve wide arcs at full speed, firing, with flow pinned high
            float a = _runClock * 0.9f;
            Vector2 move = M.FromAngle(a);
            frame = new InputFrame(move, move, true, false, false, false);
            _world.Flow = MathF.Max(_world.Flow, 0.95f);
            _world.Player.PhaseSyncFlash = MathF.Max(_world.Player.PhaseSyncFlash, 0.85f);
        }
        else if (_autoPilot)
        {
            frame = Pilot.Think(_world);
            if (_world.Phase == GamePhase.Attuning && _world.Offers.Count > 0)
            {
                _world.ChooseAttunement(0);
            }
        }
        else
        {
            frame = BuildInput();
            playerControlled = _input != null;
        }

        if (_captureCue) _world.SetMusicCue(0.95f, 0f, 0f, ++_cueFrames == 40);
        else _world.SetMusicCue(_director.Intensity * (0.7f + _world.Hype * 0.5f), _director.Build, _director.Breakdown, _director.DropTriggered);
        _runClock += dt;
        StepPlaying(_world, _audio.State, dt, frame, playerControlled, ref _accumulator, ref _playerActions);

        if (_world.Phase == GamePhase.Attuning)
        {
            _state = AppState.Attuning;
            _attuneHover = 0;
            return;
        }
        RecordFinishedTracks();
        UpdateCoachAndClips(dt);
        if (_world.Phase == GamePhase.Dead)
        {
            EnterGameOver();
            return;
        }
        if (_world.Movement != _paletteMovement || _world.Seed != _paletteSeed)
        {
            _palette = Palette.FromSeed(_world.Seed, _world.Movement);
            _paletteMovement = _world.Movement;
            _paletteSeed = _world.Seed;
        }
    }

    internal static void StepPlaying(GameWorld world, AudioState audio, float dt, InputFrame frame,
        bool playerControlled, ref float accumulator, ref PlayerActionBuffer playerActions)
    {
        const float tick = SimTick;
        if (playerControlled && world.Phase == GamePhase.Playing) playerActions.Sample(frame);
        else playerActions = default;

        accumulator += dt;
        int steps = 0;
        while (accumulator >= tick && steps++ < MaxSimSteps)
        {
            var tickInput = playerControlled ? playerActions.ForTick(frame, world.WillUpdatePlayer) : frame;
            world.Step(tick, audio, tickInput);
            accumulator -= tick;
            if (world.Phase == GamePhase.Playing) continue;

            playerActions = default;
            if (world.Phase is GamePhase.Attuning or GamePhase.Dead) break;
        }
    }

    private InputFrame BuildInput()
    {
        if (_input == null || _world == null) return InputFrame.Empty;
        Vector2 aim = _world.Player.AimDir;
        if (_input.AimStick.LengthSquared() > 0.04f)
        {
            aim = _input.AimStick;
        }
        else
        {
            Vector2 windowSize = new(_window.Size.X, _window.Size.Y);
            Vector2 mouseWorld = _renderer.ScreenToWorld(_input.MousePosition, windowSize);
            Vector2 delta = mouseWorld - _world.Player.Pos;
            if (delta.LengthSquared() > 18f) aim = delta;
        }
        return new InputFrame(_input.Move, aim, _input.Fire, _input.DashPressed, _input.Focus, _input.BurstPressed);
    }

    private void UpdatePaused()
    {
        if (_input == null) return;
        if (_input.PausePressed || _input.BackPressed)
        {
            _state = AppState.Playing;
            return;
        }
        if (_input.UpPressed) _pauseIndex = (_pauseIndex + 3) % 4;
        if (_input.DownPressed) _pauseIndex = (_pauseIndex + 1) % 4;
        if (_input.ConfirmPressed)
        {
            if (_pauseIndex == 0) _state = AppState.Playing;
            else if (_pauseIndex == 1)
            {
                _archiveReturn = AppState.Paused;
                _archiveIndex = 0;
                _state = AppState.Archive;
            }
            else if (_pauseIndex == 2)
            {
                OpenSettings(AppState.Paused);
            }
            else
            {
                AbandonRun();
            }
        }
    }

    private void UpdateAttuning()
    {
        if (_world == null) return;
        int count = _world.Offers.Count;
        if (count == 0) return;
        if (_input != null)
        {
            Vector2 m = _mouseUi;
            for (int i = 0; i < count; i++)
            {
                float x = 160 + i * 438;
                if (m.X >= x && m.X <= x + 404 && m.Y >= 326 && m.Y <= 656) _attuneHover = i;
            }
            if (_input.LeftPressed) _attuneHover = (_attuneHover + count - 1) % count;
            if (_input.RightPressed) _attuneHover = (_attuneHover + 1) % count;
            bool choose = _input.ConfirmPressed || _input.OnePressed || _input.TwoPressed || _input.ThreePressed;
            int chosen = _attuneHover;
            if (_input.OnePressed) chosen = 0;
            if (_input.TwoPressed) chosen = Math.Min(1, count - 1);
            if (_input.ThreePressed) chosen = Math.Min(2, count - 1);
            if (_input.MouseLeftPressed)
            {
                for (int i = 0; i < count; i++)
                {
                    float x = 160 + i * 438;
                    if (_mouseUi.X >= x && _mouseUi.X <= x + 404 && _mouseUi.Y >= 326 && _mouseUi.Y <= 656)
                    {
                        chosen = i;
                        choose = true;
                        break;
                    }
                }
            }
            if (choose)
            {
                _world.ChooseAttunement(chosen);
                _state = AppState.Playing;
                _attuneHover = 0;
                _audio.Sfx.Trigger(SfxType.Unlock, 0.5f);
            }
        }
        else if (_autoPilot)
        {
            _world.ChooseAttunement(0);
            _state = AppState.Playing;
        }
    }

    private void UpdateCoachAndClips(float dt)
    {
        if (_world == null || _capture) return;
        if (_input != null && _input.TabPressed && _coach.Active)
        {
            _coach.Skip();
            _meta.TutorialDone = true;
            _meta.Save();
        }
        int tipsBefore = _meta.SeenTips.Count;
        if (_coach.Update(_world, dt, _meta.SeenTips, GameSettings.Current.TutorialTips))
        {
            _meta.TutorialDone = true;
            _toast = "TUTORIAL COMPLETE // GOOD LUCK OUT THERE";
            _toastTimer = 3f;
            _audio.Sfx.Trigger(SfxType.Unlock, 0.5f);
            _meta.Save();
        }
        else if (_meta.SeenTips.Count != tipsBefore) _meta.Save();

        // highlight clip: once a new best moment is 4 s old, keep the buffered seconds around it
        var tl = _world.Timeline;
        if (tl.BestMomentVersion != _clipMomentVersion)
        {
            _clipMomentVersion = tl.BestMomentVersion;
            _clipDue = tl.BestMomentTime + ClipRecorder.Seconds * 0.5f;
        }
        if (_clipDue >= 0f && _world.SessionTime >= _clipDue)
        {
            _clipDue = -1f;
            if (GameSettings.Current.HighlightClips) _clip.Snapshot();
        }
    }

    /// <summary>Writes song records for every track the run has finished, with a toast for a new best.</summary>
    private void RecordFinishedTracks()
    {
        if (_world == null || _world.Demo) return;
        bool changed = false;
        for (int i = _recordedTracks; i < _world.Tracks.Count; i++)
        {
            var t = _world.Tracks[i];
            if (!t.Complete) break;
            _recordedTracks = i + 1;
            var before = _meta.GetSong(t.Title);
            bool hadRecord = before != null && before.Plays > 0;
            if (_meta.RecordSong(t))
            {
                changed = true;
                if (hadRecord)
                {
                    _toast = "NEW SONG RECORD // " + t.Title.ToUpperInvariant() + " // RANK " + t.Rank;
                    _toastTimer = 4f;
                    _audio.Sfx.Trigger(SfxType.Unlock, 0.5f);
                }
            }
            else if (t.IsSong && t.Duration >= 30f) changed = true;
        }
        if (changed) _meta.Save();
    }

    private void EnterGameOver()
    {
        if (_world == null) return;
        _state = AppState.GameOver;
        if (_runRecorded) return;
        _runRecorded = true;
        _world.FinishTrack();
        RecordFinishedTracks();
        if (_clipDue >= 0f && GameSettings.Current.HighlightClips) { _clipDue = -1f; _clip.Snapshot(); }
        if (_world.Summary != null)
        {
            if (string.IsNullOrEmpty(_world.Summary.Track) && !string.IsNullOrEmpty(_audio.State.SpotifyTrack))
                _world.Summary.Track = _audio.State.SpotifyTrack;
            _unlocks.Clear();
            _unlocks.AddRange(_meta.RecordRun(_world.Summary));
            _records = _meta.LastRecords;
            if (_unlocks.Count > 0)
            {
                _audio.Sfx.Trigger(SfxType.Unlock, 0.8f);
                _toastTimer = 7f;
                _toast = _unlocks[0];
            }
            else if (_records.Any)
            {
                // a personal best is worth the same chime as an unlock
                _audio.Sfx.Trigger(SfxType.Unlock, 0.6f);
            }
        }
    }

    private void UpdateGameOver()
    {
        if (_input == null) return;
        if (_input.RPressed)
        {
            StartRun(_seed, _daily);
            return;
        }
        if (_input.NPressed)
        {
            StartRun(SeedCode.NewSeed(), false);
            return;
        }
        if (_input.CPressed && _clipSave == null)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "NULLWAVE");
            _clipSave = _clip.SaveAsync(dir);
            _toast = "SAVING CLIP...";
            _toastTimer = 10f;
        }
        if (_input.TabPressed)
        {
            _archiveReturn = AppState.GameOver;
            _archiveIndex = 0;
            _state = AppState.Archive;
            return;
        }
        if (_input.BackPressed)
        {
            GoToTitle();
        }
    }

    private void AbandonRun()
    {
        _world = null;
        _state = AppState.Title;
        _menuIndex = 0;
        _audio.SetQuiet(false);
        _palette = Palette.FromSeed(_demo?.Seed ?? 0xC0FFEE, _demo?.Movement ?? 0);
        ResetDemo();
    }

    private void GoToTitle()
    {
        _state = AppState.Title;
        _menuIndex = 0;
        _audio.SetQuiet(false);
        _runningReset();
        ResetDemo();
    }

    private void _runningReset()
    {
        _world = null;
        _runRecorded = false;
        _accumulator = 0;
    }

    private Vector2 WindowMouseToUi(Vector2 mouse)
    {
        float w = Math.Max(1, _window.Size.X);
        float h = Math.Max(1, _window.Size.Y);
        return new Vector2(mouse.X / w * Renderer.DesignWidth, mouse.Y / h * Renderer.DesignHeight);
    }

    private void ToggleFullscreen()
    {
        bool full = _window.WindowState != WindowState.Fullscreen;
        SetFullscreen(full);
        GameSettings.Current.Fullscreen = full;
        _meta.Save();
    }

    private void SetFullscreen(bool full)
    {
        try
        {
            _window.WindowState = full ? WindowState.Fullscreen : WindowState.Normal;
        }
        catch (Exception ex)
        {
            _toast = "FULLSCREEN: " + ex.Message;
            _toastTimer = 3f;
        }
    }

    public void Render()
    {
        GameWorld? active = _state is AppState.Playing or AppState.Paused or AppState.Attuning or AppState.GameOver
            ? _world
            : null;
        if (_state == AppState.Archive && _archiveReturn is AppState.Paused or AppState.GameOver)
            active = _world;
        if (_state == AppState.Settings && _settingsReturn == AppState.Paused)
            active = _world;
        GameWorld? world = active ?? _demo;
        if (world == null) return;

        if (world.Seed != _paletteSeed || world.Movement != _paletteMovement)
        {
            _palette = Palette.FromSeed(world.Seed, world.Movement);
            _paletteSeed = world.Seed;
            _paletteMovement = world.Movement;
        }
        _renderer.Palette = _palette;
        // camera breathes with the song: a punch on every kick, a slow push-in through a build-up,
        // and a release outward when the drop lands
        // the backdrop and post pass rush with the ship
        _renderer.PlayerSpeed = M.Clamp01(world.Player.SpeedFrac);
        _renderer.PlayerDirection = M.SafeNorm(world.Player.Vel) * _renderer.PlayerSpeed;
        _renderer.Flow = world.Flow;
        _renderer.Hype = world.Demo ? 0.6f : world.Hype;
        _renderer.Overdrive = world.Demo ? 0f : world.OverdriveIntensity;
        if (!BackdropOnly && world.Movement != _backdropMovement)
        {
            _backdropMovement = world.Movement;
            _renderer.RerollBackdrop();
        }
        float fx = GameSettings.Current.MusicFx / 100f;
        fx *= _renderer.HypeScale;
        // EDM camera: a bounce on every kick, a slow pull back through a build, a slam in on the drop
        float edmCam = GameSettings.Current.CameraFx / 100f;
        float punch = Math.Clamp(1f + fx * edmCam * (_director.Kick * 0.035f - _director.Build * 0.05f + _director.DropPulse * 0.06f), 0.6f, 1.6f);
        _renderer.AimTilt = _aimTilt;
        if (CaptureScenario == "map" && world.Map != null)
            _renderer.BeginWorld(Vector2.Zero, MathF.Min(1480f / world.ArenaW, 730f / world.ArenaH));
        else
            _renderer.BeginWorld(world.Cam.Position + world.Cam.ShakeOffset, world.Cam.Zoom * punch);
        if (BackdropOnly)
        {
            _renderer.Palette = _palette;
            _renderer.BeginUi();
            _renderer.Render(_audio.State, _elapsed, 0f, 0.16f + Audio.State.BeatPulse * 0.4f, 0);
            return;
        }
        WorldRenderer.Draw(_renderer, world, _palette, _audio.State, _elapsed);
        _renderer.BeginUi();

        switch (_state)
        {
            case AppState.Title:
                Screens.DrawTitle(_renderer, _palette, _meta, world.Seed, false, _menuIndex, _elapsed, _audio.State, SelectedMap);
                Screens.DrawShipSelect(_renderer, _palette, Ships.Parse(_meta.SelectedShip));
                break;
            case AppState.Archive:
                Screens.DrawArchive(_renderer, _palette, _meta, _archiveIndex, _elapsed, _archiveReturn == AppState.Paused, _archiveReturn == AppState.GameOver, SelectedSeedCode, IsEditingSeed, SeedEditBuffer);
                break;
            case AppState.Playing:
                _coach.Draw(_renderer, _palette, _elapsed);
                Screens.DrawHud(_renderer, world, _palette, _audio.State, _meta, _runClock, _fps, _debug);
                Screens.DrawCrosshair(_renderer, _palette, _mouseUi, _input?.Focus ?? false);
                break;
            case AppState.Paused:
                Screens.DrawHud(_renderer, world, _palette, _audio.State, _meta, _runClock, _fps, _debug, false);
                Screens.DrawPause(_renderer, _palette, world, _pauseIndex, _audio.State);
                break;
            case AppState.Attuning:
                Screens.DrawHud(_renderer, world, _palette, _audio.State, _meta, _runClock, _fps, _debug, false);
                Screens.DrawAttunement(_renderer, _palette, world, _elapsed, _attuneHover);
                break;
            case AppState.GameOver:
                Screens.DrawGameOver(_renderer, _palette, world, _meta, _unlocks, _records, _elapsed);
                break;
            case AppState.Settings:
                _settingsMenu.Draw(_renderer, _palette, _audio.State, Screens.SourceText(_audio.State), _elapsed, _frameDt);
                break;
        }

        if (_toastTimer > 0 && _state == AppState.Playing)
            _renderer.UiTextRight(_toast, 1540, 760, 1.4f, M.WithAlpha(_palette.Accent, 0.8f), true);

        _renderer.Render(_audio.State, _elapsed, world.Flash, 0.16f + Audio.State.BeatPulse * 0.4f, world.DamageFlash > 0.4f ? 1 : 0);
        if (_state == AppState.Playing && !_capture && GameSettings.Current.HighlightClips)
            _clip.Tick(_frameDt, _renderer);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _audio.Dispose();
    }
}
