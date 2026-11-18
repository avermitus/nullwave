using System.Text.Json;
using Nullwave.Core;

namespace Nullwave.World;

public sealed class SeedRecord
{
    public ulong SeedValue { get; set; }
    public long BestScore { get; set; }
    public int BestWave { get; set; }
    public int Runs { get; set; }
    public string LastPlayedUtc { get; set; } = "";
    public bool Daily { get; set; }
}

/// <summary>Which personal bests a run broke.</summary>
[Flags]
public enum RunRecords
{
    None = 0,
    Score = 1,
    Wave = 2,
    Combo = 4,
}

/// <summary>Result of comparing one run against the archive's bests, taken before the bests are updated.</summary>
public readonly record struct RecordBreak(RunRecords Records, long PreviousScore, int PreviousWave, int PreviousCombo)
{
    public bool Any => Records != RunRecords.None;
    public bool Score => (Records & RunRecords.Score) != 0;
    public bool Wave => (Records & RunRecords.Wave) != 0;
    public bool Combo => (Records & RunRecords.Combo) != 0;
}

/// <summary>One entry of the archive's flight recorder (persisted in save.json, newest first).</summary>
public sealed class RunRecord
{
    public string Seed { get; set; } = "";
    public ulong SeedValue { get; set; }
    public bool Daily { get; set; }
    public int Wave { get; set; }
    public long Score { get; set; }
    public int MaxCombo { get; set; }
    public int Kills { get; set; }
    public int Grazes { get; set; }
    public float Duration { get; set; }
    public string Movement { get; set; } = "";
    public int Attunements { get; set; }
    public int Modifiers { get; set; }
    public RunRecords Records { get; set; }
    public string PlayedUtc { get; set; } = "";
    public string Track { get; set; } = "";

    public static RunRecord From(RunSummary s, RunRecords records, string playedUtc) => new()
    {
        Seed = SeedCode.ToCode(s.Seed),
        SeedValue = s.Seed,
        Daily = s.Daily,
        Wave = s.Wave,
        Score = s.Score,
        MaxCombo = s.MaxCombo,
        Kills = s.Kills,
        Grazes = s.Grazes,
        Duration = s.Duration,
        Movement = s.MovementName ?? "",
        Attunements = s.Attunements,
        Modifiers = s.Modifiers?.Count ?? 0,
        Records = records,
        PlayedUtc = playedUtc,
        Track = s.Track ?? "",
    };
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
    /// <summary>Longest kill chain in any run (added in TASK-001; 0 in older saves until the next run).</summary>
    public int BestCombo { get; set; }
    /// <summary>Flight recorder: the last <see cref="RecentRunCap"/> runs, newest first.</summary>
    public List<RunRecord> RecentRuns { get; set; } = new();

    public const int RecentRunCap = 8;

    /// <summary>Personal bests broken by the most recent <see cref="RecordRun"/> (not persisted).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public RecordBreak LastRecords { get; private set; }
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
        meta.RecentRuns ??= new List<RunRecord>();
        meta.RecentRuns.RemoveAll(r => r == null);
        if (meta.RecentRuns.Count > RecentRunCap) meta.RecentRuns.RemoveRange(RecentRunCap, meta.RecentRuns.Count - RecentRunCap);
        if (meta.BestCombo < 0) meta.BestCombo = 0;
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

    /// <summary>
    /// Which personal bests <paramref name="summary"/> would break, against the archive as it is now.
    /// A save written before BestCombo existed (runs recorded but no chain or history) has no chain
    /// baseline, so its first chain is adopted silently instead of being called a record.
    /// </summary>
    public RecordBreak CompareWithBests(RunSummary summary)
    {
        var records = RunRecords.None;
        if (summary.Score > 0 && summary.Score > BestScore) records |= RunRecords.Score;
        if (summary.Wave > 0 && summary.Wave > BestWave) records |= RunRecords.Wave;
        bool comboUntracked = Runs > 0 && BestCombo == 0 && RecentRuns.Count == 0;
        if (!comboUntracked && summary.MaxCombo > 0 && summary.MaxCombo > BestCombo) records |= RunRecords.Combo;
        return new RecordBreak(records, BestScore, BestWave, BestCombo);
    }

    public List<string> RecordRun(RunSummary summary)
    {
        LastRecords = CompareWithBests(summary);
        Runs++;
        TotalKills += summary.Kills;
        TotalGrazes += summary.Grazes;
        TotalBossKills += summary.BossKills;
        TotalShatters += summary.Shatters;
        TotalResonanceBursts += summary.Bursts;
        TotalScore += summary.Score;
        BestWave = Math.Max(BestWave, summary.Wave);
        BestScore = Math.Max(BestScore, summary.Score);
        BestCombo = Math.Max(BestCombo, summary.MaxCombo);
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
        string playedUtc = DateTime.UtcNow.ToString("O");
        record.SeedValue = summary.Seed;
        record.BestScore = Math.Max(record.BestScore, summary.Score);
        record.BestWave = Math.Max(record.BestWave, summary.Wave);
        record.Runs++;
        record.Daily = summary.Daily;
        record.LastPlayedUtc = playedUtc;

        RecentRuns.Insert(0, RunRecord.From(summary, LastRecords.Records, playedUtc));
        if (RecentRuns.Count > RecentRunCap) RecentRuns.RemoveRange(RecentRunCap, RecentRuns.Count - RecentRunCap);

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

    /// <summary>Distinct replayable 8-char seed codes: RecentRuns (newest first) followed by SeedRecords (by LastPlayedUtc desc).</summary>
    public List<string> GetReplayableSeeds()
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in RecentRuns)
        {
            if (SeedCode.TryParse(r.Seed, out _, out string code) && seen.Add(code))
                result.Add(code);
        }
        foreach (var kv in SeedRecords.OrderByDescending(kv => kv.Value.LastPlayedUtc))
        {
            if (SeedCode.TryParse(kv.Key, out _, out string code) && seen.Add(code))
                result.Add(code);
        }
        return result;
    }

    /// <summary>Looks up historical best wave, best score, run count, and daily flag for a seed code.</summary>
    public bool TryGetSeedInfo(string? code, out int bestWave, out long bestScore, out int runs, out bool daily)
    {
        bestWave = 0;
        bestScore = 0;
        runs = 0;
        daily = false;
        if (!SeedCode.TryParse(code, out _, out string canonical)) return false;
        bool found = false;
        if (SeedRecords.TryGetValue(canonical, out var rec))
        {
            bestWave = rec.BestWave;
            bestScore = rec.BestScore;
            runs = rec.Runs;
            daily = rec.Daily;
            found = true;
        }
        int recentMatches = 0;
        foreach (var r in RecentRuns)
        {
            if (!string.Equals(r.Seed, canonical, StringComparison.OrdinalIgnoreCase)) continue;
            found = true;
            recentMatches++;
            bestWave = Math.Max(bestWave, r.Wave);
            bestScore = Math.Max(bestScore, r.Score);
            daily |= r.Daily;
        }
        if (found && runs == 0) runs = Math.Max(1, recentMatches);
        return found;
    }

    /// <summary>Resolves a seed code to its exact recorded 64-bit seed when known, or decodes the 8-char code via <see cref="SeedCode.FromCode"/>.</summary>
    public ulong ResolveSeed(string? code)
    {
        if (!SeedCode.TryParse(code, out ulong parsed, out string canonical)) return SeedCode.NewSeed();
        if (SeedRecords.TryGetValue(canonical, out var rec) && rec.SeedValue != 0) return rec.SeedValue;
        foreach (var r in RecentRuns)
        {
            if (string.Equals(r.Seed, canonical, StringComparison.OrdinalIgnoreCase) && r.SeedValue != 0)
                return r.SeedValue;
        }
        return parsed;
    }

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

    /// <summary>
    /// Resolves a CLI modifier preset ("hard" = all 15 except quiet-signal, "all" = all 16, or CSV IDs).
    /// </summary>
    public static HashSet<string> ResolvePreset(string? spec)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(spec)) return set;
        string s = spec.Trim();
        if (s.Equals("hard", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var m in All)
                if (!m.Id.Equals("quiet-signal", StringComparison.Ordinal)) set.Add(m.Id);
            return set;
        }
        if (s.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var m in All) set.Add(m.Id);
            return set;
        }
        foreach (var part in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var def = Get(part.ToLowerInvariant());
            if (def != null) set.Add(def.Id);
        }
        return set;
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
