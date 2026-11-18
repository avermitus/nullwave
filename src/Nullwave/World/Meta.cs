using System.Text.Json;
using Nullwave.Core;

namespace Nullwave.World;

public sealed class SeedRecord
{
    public long BestScore { get; set; }
    public int BestWave { get; set; }
    public int Runs { get; set; }
    public string LastPlayedUtc { get; set; } = "";
    public bool Daily { get; set; }
}

public sealed class MetaProgress
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int Version { get; set; } = 1;
    public int Runs { get; set; }
    public int TotalKills { get; set; }
    public int TotalGrazes { get; set; }
    public int TotalBossKills { get; set; }
    public int TotalShatters { get; set; }
    public int TotalResonanceBursts { get; set; }
    public long TotalScore { get; set; }
    public int BestWave { get; set; }
    public long BestScore { get; set; }
    public int BestGrazesInRun { get; set; }
    public int BestNoHitWaves { get; set; }
    public int BestMovement { get; set; }
    public int BestAttunements { get; set; }
    public int ModifierClears { get; set; }
    public int DailyRuns { get; set; }
    public string DailyDate { get; set; } = "";
    public long DailyBest { get; set; }
    public int DailyBestWave { get; set; }
    public HashSet<string> UnlockedModifiers { get; set; } = new();
    public HashSet<string> SelectedModifiers { get; set; } = new();
    public HashSet<string> UnlockedAttunements { get; set; } = new();
    public Dictionary<string, SeedRecord> SeedRecords { get; set; } = new();
    public Dictionary<string, long> ModifierBestScores { get; set; } = new();
    /// <summary>Process name of the app whose audio drives the game; empty = whole system.</summary>
    public string AudioApp { get; set; } = "";
    public Nullwave.Core.GameSettings Settings { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public string SavePath { get; private set; } = "";

    public static string DefaultSavePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nullwave", "save.json");

    public static MetaProgress Load(string? path = null)
    {
        path ??= DefaultSavePath;
        MetaProgress meta = new();
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                meta = JsonSerializer.Deserialize<MetaProgress>(json, JsonOptions) ?? new MetaProgress();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Save load failed ({ex.Message}); starting a fresh archive.");
            try
            {
                string backup = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(path, backup, true);
            }
            catch
            {
                // ignore backup failure
            }
            meta = new MetaProgress();
        }

        meta.SavePath = path;
        meta.UnlockedModifiers ??= new HashSet<string>();
        meta.SelectedModifiers ??= new HashSet<string>();
        meta.UnlockedAttunements ??= new HashSet<string>();
        meta.SeedRecords ??= new Dictionary<string, SeedRecord>();
        meta.ModifierBestScores ??= new Dictionary<string, long>();
        meta.EnsureStarterUnlocks();
        meta.PruneSelections();
        return meta;
    }

    public void EnsureStarterUnlocks()
    {
        foreach (var id in ModifierCatalog.StarterIds) UnlockedModifiers.Add(id);
    }

    public void PruneSelections()
    {
        SelectedModifiers.RemoveWhere(id => !UnlockedModifiers.Contains(id));
    }

    public List<string> EvaluateUnlocks()
    {
        var fresh = new List<string>();
        foreach (var modifier in ModifierCatalog.All)
        {
            if (UnlockedModifiers.Contains(modifier.Id)) continue;
            if (modifier.Starter) { UnlockedModifiers.Add(modifier.Id); continue; }
            if (modifier.Requirement != null && modifier.Requirement(this))
            {
                UnlockedModifiers.Add(modifier.Id);
                fresh.Add("MODIFIER // " + modifier.Name);
            }
        }
        foreach (var attunement in AttunementCatalog.All)
        {
            if (string.IsNullOrEmpty(attunement.UnlockId)) continue;
            if (UnlockedAttunements.Contains(attunement.UnlockId)) continue;
            if (attunement.UnlockRequirement != null && attunement.UnlockRequirement(this))
            {
                UnlockedAttunements.Add(attunement.UnlockId);
                fresh.Add("ATTUNEMENT // " + attunement.Name);
            }
        }
        return fresh;
    }

    public List<string> RecordRun(RunSummary summary)
    {
        Runs++;
        TotalKills += summary.Kills;
        TotalGrazes += summary.Grazes;
        TotalBossKills += summary.BossKills;
        TotalShatters += summary.Shatters;
        TotalResonanceBursts += summary.Bursts;
        TotalScore += summary.Score;
        BestWave = Math.Max(BestWave, summary.Wave);
        BestScore = Math.Max(BestScore, summary.Score);
        BestGrazesInRun = Math.Max(BestGrazesInRun, summary.Grazes);
        BestNoHitWaves = Math.Max(BestNoHitWaves, summary.NoHitWaves);
        BestMovement = Math.Max(BestMovement, summary.Movement);
        BestAttunements = Math.Max(BestAttunements, summary.Attunements);
        if (summary.Modifiers.Count > 0 && summary.Wave >= 10) ModifierClears++;

        if (summary.Daily)
        {
            string today = SeedCode.DailyLabel(DateTime.Now);
            if (DailyDate != today)
            {
                DailyDate = today;
                DailyBest = 0;
                DailyBestWave = 0;
            }
            DailyRuns++;
            DailyBest = Math.Max(DailyBest, summary.Score);
            DailyBestWave = Math.Max(DailyBestWave, summary.Wave);
        }

        string key = SeedCode.ToCode(summary.Seed);
        if (!SeedRecords.TryGetValue(key, out var record))
        {
            record = new SeedRecord();
            SeedRecords[key] = record;
        }
        record.BestScore = Math.Max(record.BestScore, summary.Score);
        record.BestWave = Math.Max(record.BestWave, summary.Wave);
        record.Runs++;
        record.Daily = summary.Daily;
        record.LastPlayedUtc = DateTime.UtcNow.ToString("O");

        string modKey = summary.Modifiers.Count == 0 ? "VANILLA" : string.Join("+", summary.Modifiers.OrderBy(x => x));
        ModifierBestScores.TryGetValue(modKey, out long existing);
        ModifierBestScores[modKey] = Math.Max(existing, summary.Score);

        while (SeedRecords.Count > 40)
        {
            string oldest = SeedRecords.OrderBy(kv => kv.Value.LastPlayedUtc).First().Key;
            SeedRecords.Remove(oldest);
        }

        var unlocks = EvaluateUnlocks();
        Save();
        return unlocks;
    }

    public bool IsModifierUnlocked(string id) => UnlockedModifiers.Contains(id);

    public bool ToggleModifier(string id)
    {
        if (!UnlockedModifiers.Contains(id)) return false;
        if (!SelectedModifiers.Add(id)) SelectedModifiers.Remove(id);
        Save();
        return true;
    }

    public List<string> ActiveModifierIds() => SelectedModifiers.Where(id => UnlockedModifiers.Contains(id)).OrderBy(x => x).ToList();

    public float SelectedMultiplier()
    {
        float mult = 1f;
        foreach (var id in ActiveModifierIds())
        {
            var def = ModifierCatalog.Get(id);
            if (def != null) mult *= def.Multiplier;
        }
        return mult;
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(SavePath)) SavePath = DefaultSavePath;
        try
        {
            string? dir = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string temp = SavePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, SavePath, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Save failed: {ex.Message}");
        }
    }
}

public sealed class ModifierDef
{
    public string Id = "";
    public string Name = "";
    public string Desc = "";
    public string UnlockText = "";
    public float Multiplier = 1.2f;
    public bool Starter;
    public Func<MetaProgress, bool>? Requirement;
}

public static class ModifierCatalog
{
    public static readonly string[] StarterIds = ["metronome", "glass-choir"];

    public static readonly ModifierDef[] All =
    [
        new() { Id = "metronome", Name = "METRONOME", Desc = "Enemy fire lands on the grid. Faster bullets.", UnlockText = "Available from the start.", Multiplier = 1.15f, Starter = true },
        new() { Id = "glass-choir", Name = "GLASS CHOIR", Desc = "One heart. No room for error.", UnlockText = "Available from the start.", Multiplier = 1.40f, Starter = true },
        new() { Id = "bass-bloom", Name = "BASS BLOOM", Desc = "Monoliths grow and fire faster.", UnlockText = "Shatter 30 monoliths.", Multiplier = 1.20f, Requirement = m => m.TotalShatters >= 30 },
        new() { Id = "noise-floor", Name = "NOISE FLOOR", Desc = "+30% enemy density.", UnlockText = "Reach wave 7.", Multiplier = 1.25f, Requirement = m => m.BestWave >= 7 },
        new() { Id = "phase-skin", Name = "PHASE SKIN", Desc = "Faster dash. Faster enemies.", UnlockText = "Graze 400 bullets.", Multiplier = 1.20f, Requirement = m => m.TotalGrazes >= 400 },
        new() { Id = "blind-choir", Name = "BLIND CHOIR", Desc = "Bosses gain a fourth phase.", UnlockText = "Kill 3 bosses.", Multiplier = 1.30f, Requirement = m => m.TotalBossKills >= 3 },
        new() { Id = "quiet-signal", Name = "QUIET SIGNAL", Desc = "No fallback score. External audio only.", UnlockText = "Reach wave 12.", Multiplier = 1.25f, Requirement = m => m.BestWave >= 12 },
        new() { Id = "overclocked", Name = "OVERCLOCKED", Desc = "Both sides fire faster.", UnlockText = "Kill 750 enemies.", Multiplier = 1.15f, Requirement = m => m.TotalKills >= 750 },
        new() { Id = "fracture", Name = "FRACTURE", Desc = "Monoliths have 60% health and shatter loudly.", UnlockText = "Shatter 120 monoliths.", Multiplier = 1.15f, Requirement = m => m.TotalShatters >= 120 },
        new() { Id = "mirror-choir", Name = "MIRROR CHOIR", Desc = "Graze harder. Bullets move faster.", UnlockText = "Graze 120 times in one run.", Multiplier = 1.20f, Requirement = m => m.BestGrazesInRun >= 120 },
        new() { Id = "low-ceiling", Name = "LOW CEILING", Desc = "Arena shrinks by 18%.", UnlockText = "Score 50,000.", Multiplier = 1.20f, Requirement = m => m.BestScore >= 50_000 },
        new() { Id = "thin-air", Name = "THIN AIR", Desc = "Two hearts, thinner margins.", UnlockText = "Clear 5 waves without damage.", Multiplier = 1.30f, Requirement = m => m.BestNoHitWaves >= 5 },
        new() { Id = "iron-pulse", Name = "IRON PULSE", Desc = "Start with 50 Resonance. Gain it slower.", UnlockText = "Release 25 Resonance bursts.", Multiplier = 1.10f, Requirement = m => m.TotalResonanceBursts >= 25 },
        new() { Id = "last-light", Name = "LAST LIGHT", Desc = "One heart. Bullets burn faster.", UnlockText = "Reach wave 20.", Multiplier = 1.50f, Requirement = m => m.BestWave >= 20 },
        new() { Id = "nullwave", Name = "NULLWAVE", Desc = "Denser fire, faster bullets, everything at once.", UnlockText = "Reach wave 35.", Multiplier = 1.60f, Requirement = m => m.BestWave >= 35 },
        new() { Id = "hollow-choir", Name = "HOLLOW CHOIR", Desc = "Fewer pickups. Tougher enemies.", UnlockText = "Complete 10 runs.", Multiplier = 1.20f, Requirement = m => m.Runs >= 10 },
    ];

    public static ModifierDef? Get(string id)
    {
        foreach (var m in All) if (m.Id == id) return m;
        return null;
    }

    public static RunModifiers Build(HashSet<string> selected)
    {
        var run = new RunModifiers();
        foreach (var id in selected)
        {
            switch (id)
            {
                case "glass-choir":
                    run.MaxHealth = Math.Min(run.MaxHealth, 1);
                    break;
                case "metronome":
                    run.EnemyBulletSpeed *= 1.15f;
                    run.EnemyFireInterval *= 1.25f;
                    break;
                case "bass-bloom":
                    run.ObstacleRate *= 1.5f;
                    break;
                case "noise-floor":
                    run.SpawnCount *= 1.30f;
                    break;
                case "phase-skin":
                    run.DashCooldown *= 0.75f;
                    run.EnemySpeed *= 1.12f;
                    break;
                case "blind-choir":
                    run.BossPhases = 4;
                    break;
                case "quiet-signal":
                    run.SynthDisabled = true;
                    break;
                case "overclocked":
                    run.PlayerFireRate *= 1.15f;
                    run.EnemyFireInterval *= 0.90f;
                    break;
                case "fracture":
                    run.ObstacleHp *= 0.60f;
                    break;
                case "mirror-choir":
                    run.GrazeGain *= 1.6f;
                    run.EnemyBulletSpeed *= 1.10f;
                    break;
                case "low-ceiling":
                    run.ArenaScale *= 0.82f;
                    break;
                case "thin-air":
                    run.MaxHealth = Math.Min(run.MaxHealth, 2);
                    break;
                case "iron-pulse":
                    run.ResonanceGain *= 0.75f;
                    run.StartResonance = Math.Max(run.StartResonance, 50f);
                    break;
                case "last-light":
                    run.MaxHealth = 1;
                    run.EnemyBulletSpeed *= 1.15f;
                    break;
                case "nullwave":
                    run.SpawnCount *= 1.20f;
                    run.EnemyBulletSpeed *= 1.15f;
                    break;
                case "hollow-choir":
                    run.PickupRate *= 0.65f;
                    run.EnemyHealth *= 1.15f;
                    break;
            }
            run.Active.Add(id);
            var def = Get(id);
            if (def != null) run.ScoreMultiplier *= def.Multiplier;
        }
        run.ScoreMultiplier = Math.Clamp(run.ScoreMultiplier, 1f, 25f);
        return run;
    }
}

public sealed class RunModifiers
{
    public List<string> Active { get; } = new();
    public float ScoreMultiplier = 1f;
    public int MaxHealth = 3;
    public float EnemySpeed = 1f;
    public float EnemyBulletSpeed = 1f;
    public float EnemyHealth = 1f;
    public float SpawnCount = 1f;
    public float EnemyFireInterval = 1f;
    public float PlayerFireRate = 1f;
    public float DashCooldown = 1f;
    public float ArenaScale = 1f;
    public float GrazeGain = 1f;
    public float ResonanceGain = 1f;
    public float StartResonance;
    public float ObstacleHp = 1f;
    public float ObstacleRate = 1f;
    public float PickupRate = 1f;
    public int BossPhases = 3;
    public bool SynthDisabled;
}
