using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;

namespace Nullwave.World;

public sealed class RunConfig
{
    public ulong Seed = 1;
    public bool Daily;
    public bool Demo;
    public HashSet<string> SelectedModifiers = new();
    public HashSet<string> UnlockedAttunements = new();
    public SoundBank? Sfx;
    /// <summary>The instrument ship for this run.</summary>
    public ShipKind Ship;
}

public sealed class RunSummary
{
    public ulong Seed;
    public bool Daily;
    public int Wave;
    public long Score;
    public int Kills;
    public int Grazes;
    public int BossKills;
    public int Shatters;
    public int Bursts;
    public int MaxCombo;
    public float Duration;
    public int NoHitWaves;
    public int Movement;
    public string MovementName = "";
    public ShipKind Ship;
    /// <summary>Every track played this run.</summary>
    public List<TrackStat> Tracks = new();
    /// <summary>Run recap timeline.</summary>
    public RunTimeline? Timeline;
    public int Attunements;
    public int PhaseSyncs;
    public string Track = "";
    public List<string> Modifiers = new();
}

public readonly struct InputFrame
{
    public readonly Vector2 Move;
    public readonly Vector2 Aim;
    public readonly bool Fire;
    public readonly bool Dash;
    public readonly bool Focus;
    public readonly bool Burst;

    public InputFrame(Vector2 move, Vector2 aim, bool fire, bool dash, bool focus, bool burst)
    {
        Move = move;
        Aim = aim;
        Fire = fire;
        Dash = dash;
        Focus = focus;
        Burst = burst;
    }

    public static InputFrame Empty => new(Vector2.Zero, Vector2.Zero, false, false, false, false);
}

public sealed class MovementRules
{
    public string Name = "DRIFT";
    public string Subtitle = "the room breathes";
    public bool MonolithLasers;
    public bool DriftBullets;
    public bool ShatterBurst;
    public bool Regrow;
    public bool DoubleTime;

    public static MovementRules For(int index)
    {
        bool overdrive = index >= 6;
        if (overdrive)
            return new MovementRules
            {
                Name = "OVERDRIVE",
                Subtitle = "every rule at once",
                MonolithLasers = true,
                DriftBullets = true,
                ShatterBurst = true,
                Regrow = true,
                DoubleTime = true,
            };
        return index switch
        {
            0 => new MovementRules { Name = "DRIFT", Subtitle = "the room breathes" },
            1 => new MovementRules { Name = "CROSSFIRE", Subtitle = "the monoliths are listening", MonolithLasers = true },
            2 => new MovementRules { Name = "BLOOM", Subtitle = "stone remembers sound", Regrow = true, ShatterBurst = true },
            3 => new MovementRules { Name = "SPIN", Subtitle = "nothing travels straight", DriftBullets = true },
            4 => new MovementRules { Name = "SHATTER", Subtitle = "break the room before it breaks you", ShatterBurst = true },
            _ => new MovementRules { Name = "DOUBLE TIME", Subtitle = "the pulse is a weapon", DoubleTime = true },
        };
    }
}

public sealed partial class World
{
    public const float ComboDuration = 4.1f;
    public const float BaseArenaW = 5600f;
    public const float BaseArenaH = 3600f;

    public readonly RunConfig Config;
    public readonly RunModifiers Mods;
    public readonly ulong Seed;
    public readonly bool Daily;
    public readonly bool Demo;
    public readonly Rng Rng;
    public readonly Rng FxRng;
    public readonly Player Player = new();
    public readonly Camera Cam = new();

    public readonly List<Enemy> Enemies = new();
    public readonly List<Bullet> PlayerBullets = new();
    public readonly List<Bullet> EnemyBullets = new();
    public readonly List<LaserBeam> Lasers = new();
    public readonly List<Strike> Strikes = new();
    public readonly List<Obstacle> Obstacles = new();
    public readonly List<Particle> Particles = new();
    public readonly List<Shockwave> Shockwaves = new();
    public readonly List<Pickup> Pickups = new();
    public readonly List<FloatText> FloatTexts = new();
    public readonly List<AttunementOffer> Offers = new();

    public GamePhase Phase = GamePhase.Playing;
    public float Time;
    public float SessionTime;
    public int Wave = 1;
    public int Movement;
    public MovementRules Rules = MovementRules.For(0);
    public string EventText = "";
    public float EventTimer;
    public long Score;
    public int Combo;
    public float ComboTimer;
    public int MaxCombo;
    public int Kills;
    public int Grazes;
    public int BossKills;
    public int Shatters;
    public int Bursts;
    public int NoHitWaves;
    public int AttunementsTaken;
    public int PhaseSyncs;
    public float HitStop;
    public float Flash;
    public float DamageFlash;
    public float ArenaW;
    public float ArenaH;
    public float Bass;
    public float Mid;
    public float High;
    public float Energy;
    public float BeatPulse;
    public float Bpm = 126f;
    public bool BeatTick;
    public bool DownbeatTick;
    /// <summary>True on the step an eighth note starts (beat-locked grid).</summary>
    public bool EighthTick;
    /// <summary>Eighth-note position within the bar, 0..7 (even = on the beat).</summary>
    public int EighthInBar;
    /// <summary>Phase within the current beat, 0..1.</summary>
    public float BeatPhase;
    /// <summary>Position within the bar in beats, 0..4 (0 = the downbeat).</summary>
    public float BarPos;
    /// <summary>Bars counted on the beat clock (used for movement that spans several bars).</summary>
    public long BarIndex;
    private int _lastBeatIndex = -1;
    private long _lastEighth = -1;
    private double _beatPos;
    private float _lastNudgeTime = float.NaN;
    private double _lastAudioPos = double.NaN;
    private float _audioPosAge;
    /// <summary>Beat index modulo 4 that is the bar's downbeat (from the tracker).</summary>
    public int DownbeatOffset;
    public float IntermissionTimer;
    public float StartGrace;
    public Enemy? BossEnemy;
    public RunSummary? Summary;
    public SoundBank? Sfx;
    public event Action<RunSummary>? RunEnded;

    private readonly Stack<Bullet> _bulletPool = new();
    private readonly Stack<Particle> _particlePool = new();
    private readonly Queue<EnemyRole> _spawnQueue = new();
    private float _spawnTimer;
    private int _waveSpawnTotal;
    private int _waveSpawned;
    private bool _waveActive;
    private bool _waveCleanAnnounced;
    private bool _tookDamageThisWave;
    private bool _awaitingAttunement;
    private int _nextWave;
    private float _echoTimer;
    private int _lastMovement = -1;
    private readonly HashSet<int> _removedEnemies = new();

    public Vector2 ArenaSize => new(ArenaW, ArenaH);
    public float ComboMultiplier => 1f + Combo * 0.035f;
    public int ActiveEnemyCount => Enemies.Count;
    public int QueuedEnemies => _spawnQueue.Count;
    public bool WaveActive => _waveActive;
    public bool IsAttuning => Phase == GamePhase.Attuning;
    public float PlayerHealthFrac => Player.MaxHealth <= 0 ? 0 : (float)Player.Health / Player.MaxHealth;
    public bool IsBossFight => BossEnemy != null && !BossEnemy.Dead;

    public World(RunConfig config)
    {
        Config = config;
        Seed = config.Seed == 0 ? 1 : config.Seed;
        Daily = config.Daily;
        Demo = config.Demo;
        Sfx = config.Sfx;
        Mods = ModifierCatalog.Build(config.SelectedModifiers);
        Rng = new Rng(Seed ^ 0xA0761D6478BD642FUL);
        FxRng = new Rng(Seed ^ 0xE7037ED1A0B428DBUL);
        ArenaW = BaseArenaW * Mods.ArenaScale;
        ArenaH = BaseArenaH * Mods.ArenaScale;
        Player.MaxHealth = Mods.MaxHealth;
        Player.Health = Player.MaxHealth;
        Player.DashCooldownBase = 0.82f * Mods.DashCooldown;
        Player.Resonance = Mods.StartResonance;
        Player.Pos = Vector2.Zero;
        Player.PrevPos = Vector2.Zero;
        Player.InvincibleDemo = Demo;
        Cam.Snap(Player.Pos);
        StartGrace = Demo ? 0 : 1.4f;
        SetMovement(0, false);
        GenerateObstacles();
        BeginWave(1);
        if (Demo)
        {
            Player.SplitLevel = 1;
            Player.FanLevel = 1;
            Player.Health = Player.MaxHealth = 3;
        }
    }

    // Hit-stop skips the player update even on the tick that brings its timer to zero.
    internal bool WillUpdatePlayer => Phase == GamePhase.Playing && HitStop <= 0f;

    public void Step(float dt, AudioState audio, InputFrame input)
    {
        dt = M.Clamp(dt, 0f, 0.05f);
        SessionTime += dt;
        Bass = M.ExpSmooth(Bass, audio.Bass, 18f, dt);
        Mid = M.ExpSmooth(Mid, audio.Mid, 18f, dt);
        High = M.ExpSmooth(High, audio.High, 18f, dt);
        Energy = M.ExpSmooth(Energy, audio.Energy, 8f, dt);
        BeatPulse = audio.BeatPulse;
        Bpm = audio.Bpm;
        AdvanceBeatClock(dt, audio);
        _beatConfidence = M.ExpSmooth(_beatConfidence, audio.BeatLocked ? 1f : 0.3f, 2f, dt);
        Flash = M.ExpSmooth(Flash, 0f, 7f, dt);
        DamageFlash = M.ExpSmooth(DamageFlash, 0f, 4.5f, dt);
        EventTimer = MathF.Max(0, EventTimer - dt);
        UpdateVisuals(dt);

        if (Phase == GamePhase.Dead)
        {
            Cam.Update(Player.Pos, Player.Vel * 0.3f, Player.AimDir, false, 0f, ArenaSize, dt, true);
            return;
        }

        if (Phase == GamePhase.Attuning)
        {
            Cam.Update(Player.Pos, Vector2.Zero, Player.AimDir, false, Energy, ArenaSize, dt);
            return;
        }

        if (HitStop > 0f)
        {
            HitStop -= dt;
            Cam.Update(Player.Pos, Vector2.Zero, Player.AimDir, false, Energy, ArenaSize, dt);
            return;
        }

        Time += dt;
        if (StartGrace > 0) StartGrace -= dt;
        if (DropOverdriveTimer > 0f) DropOverdriveTimer = MathF.Max(0f, DropOverdriveTimer - dt);
        UpdateCombo(dt);

        if (Phase == GamePhase.Intermission)
        {
            IntermissionTimer -= dt;
            if (IntermissionTimer <= 0f)
            {
                if (_awaitingAttunement)
                {
                    BeginAttunement();
                }
                else
                {
                    BeginWave(_nextWave);
                }
            }
            Cam.Update(Player.Pos, Player.Vel, Player.AimDir, false, Energy, ArenaSize, dt);
            return;
        }

        if (Phase != GamePhase.Playing) return;

        UpdatePlayer(dt, input, audio);
        UpdateRails(dt, audio);
        UpdateTracks(dt, audio);
        if (Phase != GamePhase.Playing) return;   // a new stage opened its upgrade pick
        Timeline.Sample(this, audio, dt);
        CacheHarmony(audio);
        UpdateHype(dt);
        UpdateDirector(dt, audio);
        UpdateEnemies(dt, audio);
        UpdateLasers(dt);
        UpdateStrikes(dt);
        UpdateWells(dt, audio);
        UpdateBullets(dt);
        UpdateObstacles(dt, audio);
        UpdatePickups(dt);
        ResolveCollisions(audio);
        CleanupDead();
        CheckWaveEnd();
        UpdateArenaHazards(dt, audio);
        UpdateSongLasers();

        Cam.Update(Player.Pos, Player.Vel, Player.AimDir, Player.IsDashing, Energy, ArenaSize, dt, Phase == GamePhase.Dead);
    }

    /// <summary>
    /// Beat clock phase-locked to detected beats. It free-runs at the tracked BPM and is nudged
    /// toward every real onset, so enemy fire lands on the music's beat rather than on a grid
    /// that merely shares its tempo. Ticks are monotonic: corrections never replay a beat.
    /// </summary>
    private void AdvanceBeatClock(float dt, AudioState audio)
    {
        _beatPos += dt * MathF.Max(30f, Bpm) / 60.0;
        if (audio.BeatLocked)
        {
            // Follow the tracker's continuous beat position. It only updates once per rendered
            // frame while the sim steps several times, so extrapolate from when it last changed.
            if (audio.BeatPosition != _lastAudioPos)
            {
                _lastAudioPos = audio.BeatPosition;
                _audioPosAge = 0f;
            }
            else
            {
                _audioPosAge += dt;
            }
            double follow = _lastAudioPos + _audioPosAge * MathF.Max(30f, Bpm) / 60.0;
            double err = follow - _beatPos;
            if (Math.Abs(err) > 1.5)
            {
                // new source or a re-lock: jump, and restart the tick counters at the new spot
                _beatPos = follow;
                _lastBeatIndex = (int)Math.Floor(follow) - (follow - Math.Floor(follow) < 0.05 ? 1 : 0);
                _lastEighth = (long)Math.Floor(follow * 2.0);
            }
            else
            {
                _beatPos += err * Math.Min(1.0, dt * 12.0);
            }
            DownbeatOffset = audio.DownbeatOffset;
        }
        else if (audio.Beat && audio.Time != _lastNudgeTime)
        {
            // no lock yet: free-run at the current tempo, gently nudged by onsets
            _lastNudgeTime = audio.Time;
            double frac = _beatPos - Math.Floor(_beatPos);
            double err = frac > 0.5 ? frac - 1.0 : frac;
            _beatPos -= err * 0.15;
        }
        // positive offset = fire later than the detected beat (compensates output latency)
        double pos = _beatPos - GameSettings.Current.BeatOffsetMs / 1000.0 * MathF.Max(30f, Bpm) / 60.0;
        int beatIndex = (int)Math.Floor(pos);
        BeatTick = beatIndex > _lastBeatIndex;
        if (BeatTick) _lastBeatIndex = beatIndex;
        DownbeatTick = BeatTick && ((beatIndex - DownbeatOffset) % 4 + 4) % 4 == 0;
        long eighth = (long)Math.Floor(pos * 2.0);
        EighthTick = eighth > _lastEighth;
        if (EighthTick) _lastEighth = eighth;
        EighthInBar = (int)((((eighth - DownbeatOffset * 2) % 8) + 8) % 8);
        BeatPhase = (float)(pos - Math.Floor(pos));
        SetBarPos(pos);
        float target = Player.Health;
        Player.HealthDisplay = Player.HealthDisplay > target
            ? MathF.Max(target, Player.HealthDisplay - dt * 1.4f)
            : target;
    }

    /// <summary>
    /// Speed multiplier that throbs with the beat: a sharp surge on the beat that relaxes until the
    /// next one. Normalised so its average over a beat is exactly 1, so difficulty is unchanged.
    /// </summary>
    public float BeatSurge(float amplitude)
    {
        const float mean = 0.16625f; // average of exp(-6x) over [0,1]
        // capped where the coast would hit the 0.12 floor, so the average stays exactly 1
        float s = MathF.Min(amplitude * GameSettings.Current.BeatMotion / 100f * _beatConfidence, 0.88f / mean);
        return MathF.Max(0.12f, 1f + s * (MathF.Exp(-BeatPhase * 6f) - mean));
    }

    private float _beatConfidence = 1f;

    private void SetBarPos(double pos)
    {
        double bars = (pos - DownbeatOffset) / 4.0;
        BarIndex = (long)Math.Floor(bars);
        BarPos = (float)((bars - Math.Floor(bars)) * 4.0);
    }

    /// <summary>
    /// Like <see cref="BeatSurge"/>, but for one part of the bar. <paramref name="pocket"/> is a
    /// 16-bit mask of the sixteenth notes the motion hits on (bit 0 = the downbeat). The surge
    /// is sharpest right after each hit and relaxes until the next. Each gap between hits averages
    /// exactly 1, so an enemy covers the same ground as before, just in the rhythm of its pocket.
    /// <paramref name="envelope"/> returns 1 on the hit, falling to 0.
    /// The surge is integrated over the step (<paramref name="dt"/>), splitting it at a hit it crosses,
    /// so even a sharp sixteenth-note pulse averages exactly 1 at the 60 Hz tick.
    /// </summary>
    public float PocketSurge(float dt, ushort pocket, float amplitude, float decayPerBeat, out float envelope)
    {
        envelope = 0f;
        if (pocket == 0) return 1f;
        var (since, gap) = PocketPhase(pocket, BarPos * 4f);
        envelope = MathF.Exp(-decayPerBeat * since);
        float h = MathF.Max(1e-4f, dt * MathF.Max(30f, Bpm) / 60f);
        float toNext = gap - since;
        float area;
        if (h <= toNext)
        {
            area = PulseArea(decayPerBeat, gap, since, since + h);
        }
        else
        {
            float nextS16 = ((BarPos + toNext) * 4f + 0.001f) % 16f;
            float nextGap = PocketPhase(pocket, nextS16).Gap;
            area = PulseArea(decayPerBeat, gap, since, gap) +
                   PulseArea(decayPerBeat, nextGap, 0f, MathF.Min(h - toNext, nextGap));
        }
        // at high BEAT MOTION the coast nearly stops; capped there so the average stays exactly 1
        float k = decayPerBeat * gap;
        float maxA = 0.9f * k / (1f - MathF.Exp(-k));
        float a = MathF.Min(amplitude * GameSettings.Current.BeatMotion / 100f * _beatConfidence, maxA);
        return MathF.Max(0.08f, 1f + a * area / h);
    }

    /// <summary>Beats since the pocket's last hit, and the gap (beats) from that hit to the next.</summary>
    private static (float Since, float Gap) PocketPhase(ushort pocket, float s16)
    {
        int cur = Math.Clamp((int)s16, 0, 15);
        int last = cur;
        while ((pocket & (1 << last)) == 0) last = (last + 15) % 16;
        int next = (cur + 1) % 16;
        while ((pocket & (1 << next)) == 0) next = (next + 1) % 16;
        return ((s16 - last + 16f) % 16f / 4f, ((next - last + 15) % 16 + 1) / 4f);
    }

    /// <summary>The integral of (pulse - its mean over the gap) from s0 to s1 beats after a hit.</summary>
    private static float PulseArea(float decay, float gap, float s0, float s1)
    {
        float k = decay * gap;
        float mean = (1f - MathF.Exp(-k)) / k;
        return (MathF.Exp(-decay * s0) - MathF.Exp(-decay * s1)) / decay - mean * (s1 - s0);
    }

    /// <summary>The parts of the bar enemies move on, as sixteenth-note masks (bit 0 = beat 1).</summary>
    public static class Pocket
    {
        public const ushort Downbeat = 0x0001;   // 1
        public const ushort HalfNotes = 0x0101;  // 1 and 3
        public const ushort Beats = 0x1111;      // every beat
        public const ushort Backbeat = 0x1010;   // 2 and 4
        public const ushort OffBeats = 0x4444;   // every "and"
        public const ushort Pickups = 0x8888;    // the "a" just before each beat
        public const ushort Sixteenths = 0xFFFF; // every sixteenth
        public const ushort AndOfFour = 0x4000;  // the "and" of 4, leading into the bar
    }

    public const float PhaseSyncWindowMs = 65f;
    public const float PhaseSyncMaxPhase = 0.18f;

    /// <summary>
    /// Returns true when the beat clock is inside the positive Phase Sync window around the beat
    /// boundary (±65 ms, capped at 0.18 beat phase), outputting the timing delta in milliseconds.
    /// </summary>
    public bool IsInPhaseSyncWindow(out float errorMs)
    {
        float phaseDist = MathF.Min(BeatPhase, 1f - BeatPhase);
        float beatMs = 60_000f / MathF.Max(30f, Bpm);
        errorMs = phaseDist * beatMs;
        return errorMs <= PhaseSyncWindowMs && phaseDist <= PhaseSyncMaxPhase;
    }

    /// <summary>Sets the beat clock state directly for deterministic tests.</summary>
    public void SetBeatClockForTest(double beatPos, float bpm = 120f)
    {
        _beatPos = beatPos;
        Bpm = bpm;
        BeatPhase = (float)(beatPos - Math.Floor(beatPos));
        SetBarPos(beatPos);
        _lastBeatIndex = (int)Math.Floor(beatPos);
        _lastEighth = (long)Math.Floor(beatPos * 2.0);
    }

    // ---------------------------------------------------------------- song lasers

    private float _cueIntensity, _cueBuild, _cueBreakdown;
    private bool _cueDrop;
    private int _songBar;
    public int SongLaserVolleys { get; private set; }

    /// <summary>Song-structure cue from the music director (set by the game every frame).</summary>
    public void SetMusicCue(float intensity, float build, float breakdown, bool drop)
    {
        _cueIntensity = intensity;
        _cueBuild = build;
        _cueBreakdown = breakdown;
        if (drop) { _cueDrop = true; Timeline.AddDrop(SessionTime); }
    }

    /// <summary>
    /// Beams that answer the song's sections. Everything is decided on a downbeat and fires two
    /// beats later, so the warning always lands in time with the music:
    ///   drop        STARBURST  - eight beams radiating from near the player, slowly rotating
    ///   build-up    SCAN       - dual opposing beams sweeping across the player's area (every 2 bars)
    ///   high energy GATE/CROSS/CAGE - 4-lane gate, 3-beam star cross, or rotating 4-beam prism cage
    ///   breakdown   nothing    - the room goes quiet with the song
    /// </summary>
    private void UpdateSongLasers()
    {
        if (!DownbeatTick || Phase != GamePhase.Playing || Demo || Wave < 2) return;
        _songBar++;
        if (Lasers.Count > 24 || _cueBreakdown > 0.5f) { _cueDrop = false; return; }
        float beat = 60f / MathF.Max(60f, Bpm);
        float warm = beat * 2f;
        float hue = (0.92f + _songBar * 0.037f) % 1f;
        Vector2 p = Player.Pos;
        if (_cueDrop)
        {
            _cueDrop = false;
            Vector2 c = ClampToArena(p + M.FromAngle(Rng.Range(0, M.Tau), Rng.Range(380f, 520f)), 100f);
            float a0 = Rng.Range(0, M.Tau);
            float spin = Rng.Chance(0.5f) ? 0.32f : -0.32f;
            for (int i = 0; i < 8; i++)
                SpawnLaser(c, c + M.FromAngle(a0 + i * M.Tau / 8f, 100f), warm, beat * 3f, 24f, hue, spin, true, 2600f);
            EmitEvent("// STARBURST //", 1.4f);
            SongLaserVolleys++;
            return;
        }
        if (_cueBuild > 0.5f && _songBar % 2 == 0)
        {
            Vector2 n = M.FromAngle(Rng.Range(0, M.Tau));
            float sweep = (Rng.Chance(0.5f) ? 1f : -1f) * 0.22f;
            Vector2 originA = p + n * 1100f;
            Vector2 originB = p - n * 1100f;
            SpawnLaser(originA, p + M.Perp(n) * 380f * MathF.Sign(sweep), warm, beat * 2.5f, 20f, hue, -sweep, true);
            SpawnLaser(originB, p - M.Perp(n) * 380f * MathF.Sign(sweep), warm, beat * 2.5f, 20f, (hue + 0.08f) % 1f, sweep, true);
            SongLaserVolleys++;
            return;
        }
        bool barReady = Wave >= 3 ? _songBar % 2 == 0 : _songBar % 4 == 0;
        if (_cueIntensity > 0.55f && barReady)
        {
            int kind = (_songBar / 2) % 3;
            if (kind == 0)
            {
                // GATE: four parallel beams with clear navigable lanes
                Vector2 axis = Rng.Chance(0.5f) ? new Vector2(1, 0) : new Vector2(0, 1);
                Vector2 across = M.Perp(axis);
                float shift = Rng.Range(-90f, 90f);
                for (int k = 0; k < 4; k++)
                {
                    float offset = (k - 1.5f) * 300f + shift;
                    Vector2 mid = p + across * offset;
                    SpawnLaser(mid - axis * 3200f, mid, warm, beat * 2f, 22f, hue, 0f, true, 6400f);
                }
                EmitEvent("LASER GATE", 1.0f);
            }
            else if (kind == 1)
            {
                // CROSS: 3-beam symmetric star/cross through the player's neighbourhood (60° spacing)
                Vector2 c = p + M.FromAngle(Rng.Range(0, M.Tau), Rng.Range(60f, 160f));
                float a = Rng.Range(0, MathF.PI);
                for (int k = 0; k < 3; k++)
                {
                    Vector2 d = M.FromAngle(a + k * (MathF.PI / 3f));
                    SpawnLaser(c - d * 3200f, c, warm, beat * 2f, 22f, (hue + k * 0.04f) % 1f, 0f, true, 6400f);
                }
                EmitEvent("LASER CROSS", 1.0f);
            }
            else
            {
                // PRISM CAGE: 4 rotating boundary beams framing an arena box around the player
                float spin = (_songBar % 4 == 0 ? 1f : -1f) * 0.16f;
                float a0 = Rng.Range(0, MathF.PI * 0.5f);
                for (int k = 0; k < 4; k++)
                {
                    float sideA = a0 + k * MathF.PI * 0.5f;
                    Vector2 radial = M.FromAngle(sideA);
                    Vector2 tangent = M.Perp(radial);
                    Vector2 mid = p + radial * 340f;
                    SpawnLaser(mid - tangent * 1500f, mid, warm, beat * 2.5f, 20f, (hue + 0.12f) % 1f, spin, true, 3000f);
                }
                EmitEvent("PRISM CAGE", 1.0f);
            }
            SongLaserVolleys++;
        }
    }

    private void UpdateStrikes(float dt)
    {
        for (int i = Strikes.Count - 1; i >= 0; i--)
        {
            var s = Strikes[i];
            s.Timer -= dt;
            if (s.Timer > 0f) continue;
            Strikes.RemoveAt(i);
            AddShockwave(s.Pos, s.Radius * 2.2f, 0.5f, 10f, M.Hsv(s.Hue, 0.6f, 1f, 1f), true);
            BurstParticles(s.Pos, M.Hsv(s.Hue, 0.6f, 1f, 0.9f), 40, 520f, 3f, 0.6f, ParticleKind.Spark);
            Cam.AddShake(5f);
            Sfx?.Trigger(SfxType.Explosion, 0.45f);
            for (int k = 0; k < 6; k++)
                SpawnEnemyBullet(s.Pos, M.FromAngle(k / 6f * M.Tau + s.Hue * 7f, 150f), 5f, s.Hue, BulletKind.Shard, 3f, 1f);
            if (M.Dist(Player.Pos, s.Pos) < s.Radius + Player.Radius * 0.5f) HurtPlayer();
        }
    }

    private void UpdateCombo(float dt)
    {
        if (Combo <= 0) return;
        ComboTimer -= dt;
        if (ComboTimer <= 0)
        {
            Combo = 0;
            ComboTimer = 0;
        }
    }

    public void AddCombo(int amount)
    {
        Combo = Math.Min(999, Combo + amount);
        ComboTimer = ComboDuration;
        MaxCombo = Math.Max(MaxCombo, Combo);
    }

    public float DropOverdriveTimer;
    public bool IsDropOverdrive => DropOverdriveTimer > 0f;
    public float OverdriveFireScale => (IsDropOverdrive ? 0.80f : 1f) * (Player.IsSyncOverdrive ? 0.87f : 1f);
    public float OverdriveDamageScale => IsDropOverdrive ? 1.18f : 1f;
    public float OverdriveResonanceScale => Player.IsSyncOverdrive ? 1.30f : 1f;
    public float OverdriveScoreBonus => (IsDropOverdrive ? 0.50f : 0f) + (Player.IsSyncOverdrive ? 0.35f : 0f);
    public float OverdriveIntensity => M.Clamp01(MathF.Max(DropOverdriveTimer / 4.0f, Player.SyncOverdriveTimer / 5.0f));

    public void AddMicroHitStop(float seconds) =>
        HitStop = MathF.Max(HitStop, MathF.Min(seconds, 0.09f));

    public void AddScore(int value, Vector2? at = null, string? label = null, Vector4? color = null)
    {
        long added = (long)(value * ComboMultiplier * Mods.ScoreMultiplier * (1f + Flow * 1.5f + OverdriveScoreBonus));
        Score += added;
        if (at.HasValue && (label != null || value >= 200))
        {
            FloatTexts.Add(new FloatText
            {
                Text = label ?? ("+" + added),
                Pos = at.Value,
                Vel = new Vector2(FxRng.Signed() * 8, -34),
                Life = 0.9f,
                MaxLife = 0.9f,
                Color = color ?? new Vector4(0.9f, 1f, 1f, 1f),
                Size = value >= 1000 ? 2.4f : 1.8f,
            });
        }
    }

    /// <summary>
    /// The song just dropped: the arena answers with a shockwave from the player, a burst of light
    /// and a resonance charge, so the biggest moment in the track is the biggest moment on screen.
    /// </summary>
    public void OnMusicDrop(float strength, bool reduceFlashing)
    {
        if (Phase == GamePhase.Dead) return;
        strength = MathF.Max(0.2f, strength);
        // MUSIC FX above 150 % makes the drop look and feel bigger, not hit harder
        float reach = MathF.Min(strength, 1.5f);
        Vector4 hot = new(1f, 0.55f, 0.95f, 1f);
        AddShockwave(Player.Pos, 1400f * reach, 1.4f, 22f * strength, hot, filled: true, heavy: true);
        AddShockwave(Player.Pos, 820f * reach, 0.9f, 8f, new Vector4(0.6f, 1f, 1f, 1f));
        BurstParticles(Player.Pos, new Vector4(1f, 0.7f, 1f, 0.85f), (int)(140 * MathF.Min(strength, 3f)), 900f, 3.4f, 1.1f, ParticleKind.Spark);
        CollapseWells(reach);
        foreach (var e in Enemies) if (!e.Dead) e.HitFlash = MathF.Max(e.HitFlash, 0.6f);
        foreach (var o in Obstacles) if (!o.Dead) o.HitFlash = MathF.Max(o.HitFlash, 0.5f);
        Cam.AddShake(12f * strength);
        // the flash never passes its 150 % level (photosensitivity)
        Flash = MathF.Max(Flash, reduceFlashing ? 0.12f : 0.55f * reach);
        if (Phase == GamePhase.Playing)
        {
            Player.Resonance = MathF.Min(Player.ResonanceMax, Player.Resonance + 20f * OverdriveResonanceScale);
            DropOverdriveTimer = MathF.Max(DropOverdriveTimer, 4.0f);
            EmitEvent("// DROP OVERDRIVE //", 2.2f);
        }
        else
        {
            EmitEvent("// DROP //  RESONANCE SURGE", 1.8f);
        }
    }

    /// <summary>
    /// FLOW: stay fast, graze shots and keep killing and it climbs; slow down or get hit and it
    /// drains. It multiplies score and pushes the visuals (speed lines, trails, backdrop rush).
    /// </summary>
    public float Flow;
    private bool _flowAnnounced;

    private void UpdateFlow(float dt)
    {
        float s = Player.SpeedFrac;
        if (s > 0.7f || Player.IsDashing) Flow += dt * 0.16f * MathF.Min(1.5f, s) * (IsDropOverdrive ? 1.5f : 1f);
        else if (s < 0.35f) Flow -= dt * 0.22f;
        else Flow -= dt * 0.03f;
        Flow = M.Clamp01(Flow);
        if (Flow > 0.85f && !_flowAnnounced)
        {
            _flowAnnounced = true;
            EmitEvent("// FLOW STATE //", 1.6f);
            AddShockwave(Player.Pos, 520f, 0.7f, 6f, new Vector4(0.5f, 1f, 0.95f, 1f));
            Sfx?.Trigger(SfxType.Unlock, 0.45f);
        }
        else if (Flow < 0.5f) _flowAnnounced = false;
    }

    public void FlowGain(float amount) => Flow = M.Clamp01(Flow + amount);

    /// <summary>
    /// HYPE 0..1: how well you are playing right now (flow, combo, time since last hit). It scales
    /// every music-driven effect, so the show escalates as you play better and calms when you slip.
    /// </summary>
    public float Hype;
    public int HypeLevel => Hype >= 0.8f ? 4 : Hype >= 0.6f ? 3 : Hype >= 0.4f ? 2 : Hype >= 0.2f ? 1 : 0;
    private float _sinceHit = 0f;
    private int _lastHypeLevel;

    private void UpdateHype(float dt)
    {
        _sinceHit += dt;
        float target = 0.45f * Flow + 0.3f * M.Clamp01(Combo / 45f) + 0.25f * M.Clamp01(_sinceHit / 50f);
        Hype = M.ExpSmooth(Hype, target, target > Hype ? 0.8f : 2.5f, dt);
        int level = HypeLevel;
        if (level > _lastHypeLevel && level >= 2)
        {
            EmitEvent("HYPE " + new string('|', level + 1), 1.2f);
            AddShockwave(Player.Pos, 300f + level * 120f, 0.6f, 4f, M.Hsv(0.85f - level * 0.1f, 0.6f, 1f, 1f));
        }
        _lastHypeLevel = level;
    }

    public void EmitEvent(string text, float duration = 2.4f)
    {
        EventText = text;
        EventTimer = duration;
    }

    private void UpdateVisuals(float dt)
    {
        for (int i = Particles.Count - 1; i >= 0; i--)
        {
            var p = Particles[i];
            p.Life -= dt;
            if (p.Life <= 0)
            {
                Particles.RemoveAt(i);
                if (_particlePool.Count < 2500) _particlePool.Push(p);
                continue;
            }
            p.PrevPos = p.Pos;
            p.Pos += p.Vel * dt;
            p.Vel *= MathF.Max(0, 1f - p.Drag * dt);
            p.Rot += p.RotVel * dt;
        }

        for (int i = Shockwaves.Count - 1; i >= 0; i--)
        {
            var s = Shockwaves[i];
            s.Life -= dt;
            if (s.Life <= 0)
            {
                Shockwaves.RemoveAt(i);
                continue;
            }
            s.Radius = M.Lerp(s.Radius, s.MaxRadius, 1f - MathF.Exp(-dt * 3.6f));
        }

        for (int i = FloatTexts.Count - 1; i >= 0; i--)
        {
            var f = FloatTexts[i];
            f.Life -= dt;
            if (f.Life <= 0) { FloatTexts.RemoveAt(i); continue; }
            f.Pos += f.Vel * dt;
            f.Vel *= 1f - dt * 1.5f;
        }
    }

    public void SpawnParticle(Vector2 pos, Vector2 vel, float life, float size, Vector4 color, ParticleKind kind = ParticleKind.Dot, float drag = 1.4f, float endSize = 0f)
    {
        if (Particles.Count >= 4200) return;
        if (!_particlePool.TryPop(out var p))
            p = new Particle();
        p.Kind = kind;
        p.Pos = pos;
        p.PrevPos = pos;
        p.Vel = vel;
        p.Life = life;
        p.MaxLife = life;
        p.Size = size;
        p.EndSize = endSize;
        p.Color = color;
        p.Drag = drag;
        p.Rot = FxRng.Range(0, M.Tau);
        p.RotVel = FxRng.Signed() * 3f;
        Particles.Add(p);
    }

    public void BurstParticles(Vector2 pos, Vector4 color, int count, float speed, float size, float life, ParticleKind kind = ParticleKind.Dot)
    {
        for (int i = 0; i < count; i++)
        {
            float a = FxRng.Range(0, M.Tau);
            float v = speed * FxRng.Range(0.2f, 1f);
            SpawnParticle(pos, M.FromAngle(a, v), life * FxRng.Range(0.55f, 1.2f), size * FxRng.Range(0.55f, 1.35f), color, kind, FxRng.Range(0.7f, 2.4f));
        }
    }

    public void AddShockwave(Vector2 pos, float maxRadius, float life, float width, Vector4 color, bool filled = false, bool heavy = false)
    {
        Shockwaves.Add(new Shockwave
        {
            Pos = pos,
            Radius = width * 0.5f,
            MaxRadius = maxRadius,
            Life = life,
            MaxLife = life,
            Width = width,
            Color = color,
            Filled = filled,
            Heavy = heavy,
        });
    }

    public void SpawnPickup(Vector2 pos, PickupKind kind, float value = 1f, float hue = 0.5f)
    {
        Pickups.Add(new Pickup
        {
            Kind = kind,
            Pos = pos,
            Vel = FxRng.InCircle(70),
            Life = 15f,
            Phase = FxRng.Range(0, M.Tau),
            Value = value,
            Hue = hue,
        });
    }

    private void UpdatePickups(float dt)
    {
        for (int i = Pickups.Count - 1; i >= 0; i--)
        {
            var p = Pickups[i];
            p.Life -= dt;
            p.Phase += dt * 2.7f;
            if (p.Life <= 0) { Pickups.RemoveAt(i); continue; }
            Vector2 toPlayer = Player.Pos - p.Pos;
            float d = toPlayer.Length();
            if (d < 260f && d > 0.01f)
            {
                float pull = (1f - d / 260f) * 2600f;
                p.Vel += toPlayer / d * pull * dt;
            }
            p.Vel *= MathF.Max(0f, 1f - dt * 1.2f);
            p.Pos += p.Vel * dt;
            if (d < Player.Radius + 24f && Phase == GamePhase.Playing)
            {
                CollectPickup(p);
                Pickups.RemoveAt(i);
            }
        }
    }

    private void CollectPickup(Pickup p)
    {
        Sfx?.Trigger(SfxType.Pickup, 0.5f);
        AddShockwave(p.Pos, 70, 0.35f, 2f, new Vector4(0.8f, 1f, 1f, 0.9f));
        switch (p.Kind)
        {
            case PickupKind.Health:
                if (Player.Health < Player.MaxHealth)
                {
                    Player.Health++;
                    AddScore(50, p.Pos, "REPAIR", new Vector4(0.5f, 1f, 0.75f, 1f));
                }
                else
                {
                    AddScore(300, p.Pos, "FULL", new Vector4(0.5f, 1f, 0.75f, 1f));
                }
                break;
            case PickupKind.Resonance:
                Player.Resonance = MathF.Min(Player.ResonanceMax, Player.Resonance + 28f * p.Value);
                AddScore(80, p.Pos, "PHASE", new Vector4(0.45f, 0.9f, 1f, 1f));
                break;
            case PickupKind.Score:
                AddScore(500, p.Pos, "SIGNAL", new Vector4(1f, 0.85f, 0.4f, 1f));
                break;
            case PickupKind.Core:
                AddScore(2400, p.Pos, "CORE", new Vector4(1f, 0.45f, 0.8f, 1f));
                Player.Resonance = MathF.Min(Player.ResonanceMax, Player.Resonance + 35f);
                break;
        }
    }

    public void Burst()
    {
        if (Phase != GamePhase.Playing || Player.Resonance < Player.ResonanceMax) return;
        Player.Resonance = 0;
        Bursts++;
        Player.Invuln = MathF.Max(Player.Invuln, 0.7f);
        Flash = MathF.Max(Flash, 0.65f);
        Cam.AddShake(17f);
        AddMicroHitStop(0.032f);
        AddShockwave(Player.Pos, 760, 0.8f, 18, new Vector4(0.6f, 0.95f, 1f, 1f), filled: true, heavy: true);
        AddShockwave(Player.Pos, 430, 0.55f, 4, new Vector4(1f, 1f, 1f, 1f));
        Sfx?.Trigger(SfxType.Burst, 0.9f);
        BurstParticles(Player.Pos, new Vector4(0.65f, 1f, 1f, 0.8f), 100, 700, 3.5f, 0.9f, ParticleKind.Spark);
        for (int i = EnemyBullets.Count - 1; i >= 0; i--)
        {
            var b = EnemyBullets[i];
            if (M.DistSq(b.Pos, Player.Pos) < 760 * 760)
            {
                SpawnParticle(b.Pos, FxRng.InCircle(80), 0.35f, 2.2f, new Vector4(0.5f, 1f, 1f, 0.8f), ParticleKind.Spark);
                ReturnBullet(EnemyBullets, i);
            }
        }
        for (int i = Enemies.Count - 1; i >= 0; i--)
        {
            var e = Enemies[i];
            if (M.DistSq(e.Pos, Player.Pos) < 560 * 560) DamageEnemy(e, 80f, Player.Pos);
        }
        for (int i = Obstacles.Count - 1; i >= 0; i--)
        {
            var o = Obstacles[i];
            if (M.DistSq(o.Pos, Player.Pos) < 520 * 520) DamageObstacle(o, 90f);
        }
        for (int i = 0; i < 32; i++)
        {
            float a = i / 32f * M.Tau + FxRng.Signed() * 0.03f;
            SpawnPlayerBullet(Player.Pos + M.FromAngle(a, 24), M.FromAngle(a, 1100), 3.5f, 24f, 1.1f, 3, 0, 0);
        }
        AddScore(500);
        EmitEvent("RESONANCE CASCADE", 1.8f);
    }

    private void UpdateArenaHazards(float dt, AudioState audio)
    {
        if (!Rules.DoubleTime) return;
        // Double Time hazards are carried by the enemies; this keeps the beat pulse dangerous.
        _echoTimer -= dt;
        if (_echoTimer <= 0f && audio.Downbeat && EnemyBullets.Count < 1800)
        {
            _echoTimer = 0.4f;
            Vector2 edge = ClampToArena(Player.Pos + M.FromAngle(FxRng.Range(0, M.Tau), 1250f), 20f);
            Vector2 target = Player.Pos;
            float a = M.AngleOf(target - edge);
            for (int i = -1; i <= 2; i++)
                SpawnEnemyBullet(edge, M.FromAngle(a + i * 0.045f, 200f), 5f, 0.08f, BulletKind.Orb, 8f, 1);
        }
    }

    public void HurtPlayer(float damage = 1f)
    {
        if (Phase != GamePhase.Playing || Player.InvincibleDemo) return;
        if (Player.Invuln > 0f || Player.IsDashing || StartGrace > 0f) return;
        Player.Health--;
        if (CurrentTrack != null) CurrentTrack.Hits++;
        Timeline.AddHit(SessionTime);
        Player.PhaseSyncStreak = 0;
        Flow *= 0.35f;
        _sinceHit = 0f;
        Hype *= 0.5f;
        Player.Invuln = 1.35f;
        Player.HitFlash = 1f;
        HitStop = 0.075f;
        Flash = 0.75f;
        DamageFlash = 1f;
        Cam.AddShake(15f);
        Sfx?.Trigger(SfxType.Hurt, 0.8f);
        _tookDamageThisWave = true;
        BurstParticles(Player.Pos, new Vector4(1f, 0.22f, 0.3f, 0.9f), 32, 260, 3f, 0.7f, ParticleKind.Spark);
        AddShockwave(Player.Pos, 170, 0.4f, 4f, new Vector4(1f, 0.25f, 0.3f, 1f));
        if (Player.Health <= 0) Die();
    }

    private void Die()
    {
        if (Phase == GamePhase.Dead) return;
        Player.Health = 0;
        Phase = GamePhase.Dead;
        Cam.AddShake(28f);
        Flash = 1f;
        AddShockwave(Player.Pos, 720, 1.4f, 18, new Vector4(1f, 0.2f, 0.35f, 1f), true);
        BurstParticles(Player.Pos, new Vector4(1f, 0.3f, 0.4f, 0.9f), 110, 520, 3.5f, 1.4f, ParticleKind.Spark);
        Sfx?.Trigger(SfxType.Explosion, 1f);
        FinishTrack();
        Summary = MakeSummary();
        if (!Demo) RunEnded?.Invoke(Summary);
    }

    private RunSummary MakeSummary()
    {
        Timeline.Finish(SessionTime, Score);
        return new RunSummary
        {
            Ship = Ship,
            Tracks = new List<TrackStat>(Tracks),
            Timeline = Timeline,
            Seed = Seed,
            Daily = Daily,
            Wave = Wave,
            Score = Score,
            Kills = Kills,
            Grazes = Grazes,
            BossKills = BossKills,
            Shatters = Shatters,
            Bursts = Bursts,
            MaxCombo = MaxCombo,
            Duration = SessionTime,
            NoHitWaves = NoHitWaves,
            Movement = Movement,
            MovementName = Rules.Name,
            Attunements = Player.AttunementCount,
            PhaseSyncs = PhaseSyncs,
            Modifiers = new List<string>(Mods.Active),
        };
    }

    public void ForceDeathForTest() => HurtPlayer(99);

    public void ForceAttunementForTest()
    {
        Offers.Clear();
        Offers.AddRange(AttunementCatalog.Generate(Player, Config.UnlockedAttunements, Rng, 3));
        Phase = GamePhase.Attuning;
    }

    private void CleanupDead()
    {
        for (int i = Enemies.Count - 1; i >= 0; i--)
        {
            var e = Enemies[i];
            if (e.MarkedForRemoval || (e.Dead && e.DeathPulse <= 0))
            {
                if (BossEnemy == e) BossEnemy = null;
                Enemies.RemoveAt(i);
            }
        }
        for (int i = Obstacles.Count - 1; i >= 0; i--)
            if (Obstacles[i].Dead) Obstacles.RemoveAt(i);
    }

    public void ChooseAttunement(int index)
    {
        if (Phase != GamePhase.Attuning || index < 0 || index >= Offers.Count) return;
        var offer = Offers[index];
        SynergyFlags before = Player.ActiveSynergies;
        AttunementCatalog.Apply(Player, offer.Id);
        SynergyFlags unlocked = Player.ActiveSynergies & ~before;
        AttunementsTaken++;
        Player.FireTimer = MathF.Min(Player.FireTimer, 0.04f);
        Offers.Clear();
        _awaitingAttunement = false;
        Phase = GamePhase.Intermission;
        IntermissionTimer = 1.15f;
        NextWaveAfterAttunement();
        if (SynergyCatalog.FirstMatch(unlocked) is { } syn)
        {
            Sfx?.Trigger(SfxType.Unlock, 0.85f);
            EmitEvent("OVERCLOCK // " + syn.Name, 2.6f);
            AddShockwave(Player.Pos, 380f, 0.75f, 6f, new Vector4(1f, 0.85f, 0.35f, 1f));
            BurstParticles(Player.Pos, new Vector4(1f, 0.88f, 0.42f, 0.92f), 28, 460f, 3.2f, 0.75f, ParticleKind.Spark);
        }
        else
        {
            Sfx?.Trigger(SfxType.Unlock, 0.6f);
            EmitEvent("ATTUNED // " + offer.Name, 2.0f);
            AddShockwave(Player.Pos, 220, 0.6f, 3f, new Vector4(0.6f, 1f, 1f, 1f));
        }
    }

    public float GetDifficultyHealthScale()
    {
        float w = Math.Max(1, Wave);
        return 1f + (w - 1) * 0.17f + MathF.Pow(w, 1.45f) * 0.006f;
    }

    public float GetDifficultySpeedScale()
    {
        return MathF.Min(1.65f, 1f + (Wave - 1) * 0.011f);
    }

    public float GetDifficultyBulletScale()
    {
        return MathF.Min(1.55f, 1f + (Wave - 1) * 0.008f) * Mods.EnemyBulletSpeed;
    }
}
