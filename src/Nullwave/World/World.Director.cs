using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;

namespace Nullwave.World;

public sealed partial class World
{
    public void SetMovement(int index, bool announce)
    {
        index = Math.Max(0, index);
        if (_lastMovement == index && Movement == index) return;
        Movement = index;
        _lastMovement = index;
        Rules = MovementRules.For(index);
        if (announce)
        {
            GenerateObstacles();
            AddShockwave(Player.Pos, MathF.Max(ArenaW, ArenaH), 1.2f, 8f, new Vector4(0.55f, 0.9f, 1f, 1f));
            Sfx?.Trigger(SfxType.Boss, 0.8f);
            EmitEvent($"MOVEMENT {Roman(index + 1)} // {Rules.Name} // {LayoutName}", 3.2f);
            for (int i = EnemyBullets.Count - 1; i >= 0; i--)
                ReturnBullet(EnemyBullets, i);
        }
    }

    public static readonly string[] LayoutNames = { "SCATTER", "HENGE", "CITY", "SPIRAL", "LANES" };
    public int Layout;
    public string LayoutName => LayoutNames[Layout];

    public Vector2 ClampToArena(Vector2 p, float margin)
    {
        return new Vector2(
            M.Clamp(p.X, -ArenaW * 0.5f + margin, ArenaW * 0.5f - margin),
            M.Clamp(p.Y, -ArenaH * 0.5f + margin, ArenaH * 0.5f - margin));
    }

    /// <summary>
    /// Lays out the monoliths for the current movement. The bigger arena gets a distinct
    /// architecture per movement so each act of the run reads as a new place.
    /// </summary>
    public void GenerateObstacles()
    {
        Obstacles.Clear();
        Layout = Movement < 5 ? Movement % LayoutNames.Length : Rng.Range(0, LayoutNames.Length);
        float area = ArenaW * ArenaH / (3000f * 2000f);
        int budget = (int)MathF.Round((7 + Math.Min(5, Movement)) * MathF.Min(2.6f, area * 0.8f));
        var candidates = new List<(Vector2 pos, float radius)>();
        float hw = ArenaW * 0.5f, hh = ArenaH * 0.5f;
        switch (Layout)
        {
            case 1: // HENGE: concentric broken rings around the centre
                foreach (var (ring, count) in new[] { (0.24f, 8), (0.42f, 13) })
                {
                    float rr = MathF.Min(hw, hh) * ring * 2f;
                    float gap = Rng.Range(0, M.Tau);
                    for (int i = 0; i < count; i++)
                    {
                        float a = i / (float)count * M.Tau + gap;
                        if (M.AngleDelta(a, gap) is > -0.35f and < 0.35f) continue;
                        candidates.Add((new Vector2(MathF.Cos(a) * rr * (hw / hh) * 0.75f, MathF.Sin(a) * rr), Rng.Range(70f, 105f)));
                    }
                }
                break;
            case 2: // CITY: a jittered lattice of blocks with open plazas
                for (float x = -hw + 520f; x < hw - 300f; x += 760f)
                    for (float y = -hh + 480f; y < hh - 300f; y += 720f)
                        if (Rng.Chance(0.72f))
                            candidates.Add((new Vector2(x + Rng.Range(-120f, 120f), y + Rng.Range(-110f, 110f)), Rng.Range(80f, 140f)));
                break;
            case 3: // SPIRAL: two arms winding out from the centre
                for (int arm = 0; arm < 2; arm++)
                    for (int i = 0; i < 12; i++)
                    {
                        float t = 0.18f + i / 12f * 0.82f;
                        float a = arm * MathF.PI + t * M.Tau * 1.15f;
                        candidates.Add((new Vector2(MathF.Cos(a) * hw * 0.88f * t, MathF.Sin(a) * hh * 0.88f * t), Rng.Range(60f, 100f) + t * 30f));
                    }
                break;
            case 4: // LANES: horizontal walls with staggered doors
                for (int row = 0; row < 4; row++)
                {
                    float y = -hh + ArenaH * (row + 1) / 5f;
                    int door = Rng.Range(1, 6);
                    for (int i = 0; i < 8; i++)
                    {
                        if (i == door || i == (door + 3 + row) % 8) continue;
                        float x = -hw + ArenaW * (i + 0.5f) / 8f;
                        candidates.Add((new Vector2(x + Rng.Range(-40f, 40f), y + Rng.Range(-30f, 30f)), Rng.Range(85f, 120f)));
                    }
                }
                break;
        }
        foreach (var (pos, radius) in candidates)
            TryPlaceObstacle(pos, radius);
        // scatter fills whatever the layout left of the budget (and is the whole SCATTER layout)
        int tries = 0;
        while (Obstacles.Count < Math.Max(budget, candidates.Count / 2 + 6) && tries++ < 400)
        {
            Vector2 pos = new(Rng.Range(-hw * 0.9f, hw * 0.9f), Rng.Range(-hh * 0.9f, hh * 0.9f));
            TryPlaceObstacle(pos, Rng.Range(64f, 140f));
        }
    }

    private bool TryPlaceObstacle(Vector2 pos, float radius)
    {
        {
            if (MathF.Abs(pos.X) > ArenaW * 0.5f - radius - 120f || MathF.Abs(pos.Y) > ArenaH * 0.5f - radius - 120f) return false;
            if (M.Dist(pos, Vector2.Zero) < 390f) return false;
            if (M.Dist(pos, Player.Pos) < radius + 300f) return false;
            foreach (var o in Obstacles)
                if (M.Dist(pos, o.Pos) < radius + o.Radius + 85f)
                    return false;

            int sides = Rng.Range(4, 9);
            var shape = new Vector2[sides];
            for (int i = 0; i < sides; i++)
                shape[i] = M.FromAngle(i / (float)sides * M.Tau + Rng.Range(-0.11f, 0.11f), radius * Rng.Range(0.72f, 1.12f));
            float hp = 120f * (1f + Wave * 0.045f) * Mods.ObstacleHp;
            Obstacles.Add(new Obstacle
            {
                Pos = pos,
                Radius = radius,
                Hue = Rng.Range(0f, 1f),
                Rotation = Rng.Range(0, M.Tau),
                RotVel = Rng.Range(-0.11f, 0.11f),
                Sides = sides,
                Shape = shape,
                Hp = hp,
                MaxHp = hp,
                FireBeats = Rng.Range(3, 8),
                CanFire = Rules.MonolithLasers && Rng.Chance(0.82f),
                CanRegrow = Rules.Regrow,
                Phase = Rng.Range(0, M.Tau),
            });
            return true;
        }
    }

    public void ClearSpawnQueueForCapture() => _spawnQueue.Clear();

    private void BuildWaveQueue()
    {
        _spawnQueue.Clear();
        bool bossWave = Wave % 5 == 0;
        if (bossWave)
        {
            _spawnQueue.Enqueue(EnemyRole.Boss);
            int support = 3 + Math.Min(6, Wave / 3);
            for (int i = 0; i < support; i++) _spawnQueue.Enqueue(PickRole());
        }
        else
        {
            int count = 6 + Math.Min(24, (int)MathF.Round(Wave * 1.65f));
            count = Math.Max(5, (int)MathF.Round(count * Mods.SpawnCount));
            if (Demo) count = Math.Min(count, 14);
            for (int i = 0; i < count; i++) _spawnQueue.Enqueue(PickRole());
        }
        _waveSpawnTotal = _spawnQueue.Count;
        _waveSpawned = 0;
    }

    private EnemyRole PickRole()
    {
        float r = Rng.NextFloat();
        if (Wave < 2)
            return r < 0.68f ? EnemyRole.Drifter : EnemyRole.Orbiter;
        if (Wave < 4)
        {
            if (r < 0.34f) return EnemyRole.Drifter;
            if (r < 0.57f) return EnemyRole.Orbiter;
            if (r < 0.78f) return EnemyRole.Sniper;
            if (r < 0.90f) return EnemyRole.Swarm;
            return EnemyRole.Dasher;
        }
        // the newer roles join as the run goes on
        if (Wave >= 4 && r < 0.12f) return EnemyRole.Weaver;
        if (Wave >= 6 && r < 0.21f) return EnemyRole.Phantom;
        if (Wave >= 6 && r < 0.29f) return EnemyRole.Mortar;
        if (Wave >= 8 && r < 0.36f) return EnemyRole.Prism;
        r = Rng.NextFloat();
        if (r < 0.23f) return EnemyRole.Drifter;
        if (r < 0.40f) return EnemyRole.Orbiter;
        if (r < 0.56f) return EnemyRole.Sniper;
        if (r < 0.68f) return EnemyRole.Dasher;
        if (r < 0.80f) return EnemyRole.Turret;
        if (r < 0.91f) return EnemyRole.Splitter;
        return EnemyRole.Swarm;
    }

    public void BeginWave(int wave)
    {
        Wave = Math.Max(1, wave);
        int movement = (Wave - 1) / 5;
        if (movement != Movement)
            SetMovement(movement, true);
        _waveActive = true;
        _waveCleanAnnounced = false;
        _tookDamageThisWave = false;
        _awaitingAttunement = false;
        Offers.Clear();
        Phase = GamePhase.Playing;
        BuildWaveQueue();
        _spawnTimer = 0.55f;
        _nextWave = Wave + 1;
        EmitEvent($"WAVE {Wave:00} // {Rules.Name}", 2.3f);
        Sfx?.Trigger(SfxType.Wave, 0.45f);
    }

    private void UpdateDirector(float dt, AudioState audio)
    {
        if (!_waveActive) return;
        if (_spawnQueue.Count == 0) return;
        if (Enemies.Count >= 28) return;
        _spawnTimer -= dt;
        if (_spawnTimer <= 0f && (BeatTick || _spawnTimer < -0.7f))
        {
            SpawnNext();
            _spawnTimer = (0.82f - MathF.Min(0.34f, Energy * 0.25f)) * Rng.Range(0.8f, 1.2f);
        }
    }

    private void SpawnNext()
    {
        if (_spawnQueue.Count == 0) return;
        var role = _spawnQueue.Dequeue();
        _waveSpawned++;
        Vector2 pos = role == EnemyRole.Boss
            ? ClampToArena(Player.Pos + new Vector2(0f, -560f), 300f)
            : EdgeSpawnPoint();
        // every second boss wave brings THE DROP instead of THE CHOIR
        if (role == EnemyRole.Boss && Wave % 10 == 0) SpawnSongBoss();
        else SpawnEnemyAt(role, pos, true);
    }

    /// <summary>
    /// Spawns arrive from just beyond the player's view rather than the arena edge: the map is
    /// big enough that edge spawns would take half a wave to reach the fight.
    /// </summary>
    private Vector2 EdgeSpawnPoint()
    {
        Vector2 fallback = ClampToArena(-Player.Pos * 0.55f, 120f);
        for (int attempt = 0; attempt < 12; attempt++)
        {
            float a = Rng.Range(0, M.Tau);
            Vector2 p = ClampToArena(Player.Pos + M.FromAngle(a, Rng.Range(950f, 1350f)), 90f);
            if (M.Dist(p, Player.Pos) < 560f) continue;
            bool blocked = false;
            foreach (var o in Obstacles)
                if (!o.Dead && M.Dist(p, o.Pos) < o.Radius + 60f) { blocked = true; break; }
            if (!blocked) return p;
            fallback = p;
        }
        return fallback;
    }

    private void CheckWaveEnd()
    {
        // only while playing: a stage-clear upgrade pick, a won set or a death mid-step must not be
        // turned into an intermission by the wave emptying in the same step
        if (Phase != GamePhase.Playing || !_waveActive || _spawnQueue.Count > 0) return;
        foreach (var e in Enemies)
            if (!e.Dead) return;

        _waveActive = false;
        if (_waveCleanAnnounced) return;
        _waveCleanAnnounced = true;
        int bonus = 700 + Wave * 130 + (int)(NoHitWaves * 45);
        AddScore(bonus, Player.Pos + new Vector2(0, -32), "WAVE CLEAR", new Vector4(0.75f, 1f, 1f, 1f));
        if (!_tookDamageThisWave) NoHitWaves++;
        for (int i = EnemyBullets.Count - 1; i >= 0; i--)
        {
            var b = EnemyBullets[i];
            SpawnParticle(b.Pos, FxRng.InCircle(30), 0.24f, 1.8f, new Vector4(0.7f, 0.9f, 1f, 0.55f), ParticleKind.Dot, 3f);
            ReturnBullet(EnemyBullets, i);
        }
        Strikes.Clear();
        Sfx?.Trigger(SfxType.Wave, 0.7f);
        EmitEvent("SIGNAL CLEARED", 1.6f);
        _awaitingAttunement = Wave % 2 == 0 || Wave % 5 == 0;
        _nextWave = Wave + 1;
        Phase = GamePhase.Intermission;
        IntermissionTimer = _awaitingAttunement ? 1.45f : 2.15f;
        if (Player.Health < Player.MaxHealth && FxRng.Chance(0.28f))
            SpawnPickup(Player.Pos + new Vector2(0, -80), PickupKind.Health, 1, 0.4f);
    }

    private void BeginAttunement()
    {
        var offers = AttunementCatalog.Generate(Player, Config.UnlockedAttunements, Rng, 3);
        if (offers.Count == 0)
        {
            _awaitingAttunement = false;
            Phase = GamePhase.Intermission;
            IntermissionTimer = 0.6f;
            NextWaveAfterAttunement();
            return;
        }
        Offers.Clear();
        Offers.AddRange(offers);
        _awaitingAttunement = false;
        Phase = GamePhase.Attuning;
        EmitEvent("SIGNAL PENDING // CHOOSE", 1.4f);
    }

    private void NextWaveAfterAttunement()
    {
        _nextWave = Wave + 1;
    }

    private static string Roman(int n) => n switch
    {
        1 => "I",
        2 => "II",
        3 => "III",
        4 => "IV",
        5 => "V",
        6 => "VI",
        7 => "VII",
        8 => "VIII",
        9 => "IX",
        _ => "X",
    };
}
