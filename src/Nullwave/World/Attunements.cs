using System.Numerics;
using Nullwave.Core;

namespace Nullwave.World;

[Flags]
public enum SynergyFlags
{
    None = 0,
    SeekerSalvo = 1 << 0, // split >= 2 && homing >= 2
    PrismLance = 1 << 1,  // pierce >= 2 && ricochet >= 2
    RailCadence = 1 << 2, // heavy >= 2 && rapid >= 2
    VoidPhantom = 1 << 3, // phase >= 2 && grazer >= 2
}

public sealed class SynergyDef
{
    public SynergyFlags Flag;
    public string Id = "";
    public string Tag = "";
    public string Name = "";
    public string FirstId = "";
    public string SecondId = "";
    public int MinStacks = 2;
    public string Desc = "";
    public float Hue;
}

public static class SynergyCatalog
{
    public static readonly SynergyDef[] All =
    [
        new()
        {
            Flag = SynergyFlags.SeekerSalvo,
            Id = "seeker-salvo",
            Tag = "SLV",
            Name = "SEEKER SALVO",
            FirstId = "split",
            SecondId = "homing",
            MinStacks = 2,
            Desc = "Fires twin flanking micro-seekers with amplified lock range and turn rate.",
            Hue = 0.84f,
        },
        new()
        {
            Flag = SynergyFlags.PrismLance,
            Id = "prism-lance",
            Tag = "LNC",
            Name = "PRISM LANCE",
            FirstId = "pierce",
            SecondId = "ricochet",
            MinStacks = 2,
            Desc = "Ricochets supercharge bolts with bonus damage, pierce, velocity, and life.",
            Hue = 0.13f,
        },
        new()
        {
            Flag = SynergyFlags.RailCadence,
            Id = "rail-cadence",
            Tag = "RAL",
            Name = "RAIL CADENCE",
            FirstId = "heavy",
            SecondId = "rapid",
            MinStacks = 2,
            Desc = "Every 4th shot launches a hyper-velocity piercing centerline rail bolt.",
            Hue = 0.09f,
        },
        new()
        {
            Flag = SynergyFlags.VoidPhantom,
            Id = "void-phantom",
            Tag = "VPH",
            Name = "VOID PHANTOM",
            FirstId = "phase",
            SecondId = "grazer",
            MinStacks = 2,
            Desc = "Expands dash/graze radii and unleashes a bullet-cancelling shockwave on dash.",
            Hue = 0.78f,
        },
    ];

    public static SynergyFlags Evaluate(Player player)
    {
        SynergyFlags flags = SynergyFlags.None;
        if (player.SplitLevel >= 2 && player.HomingLevel >= 2) flags |= SynergyFlags.SeekerSalvo;
        if (player.PierceLevel >= 2 && player.RicochetLevel >= 2) flags |= SynergyFlags.PrismLance;
        if (player.HeavyLevel >= 2 && player.RapidLevel >= 2) flags |= SynergyFlags.RailCadence;
        if (player.PhaseLevel >= 2 && player.GrazerLevel >= 2) flags |= SynergyFlags.VoidPhantom;
        return flags;
    }

    public static int CountActive(SynergyFlags flags) => BitOperations.PopCount((uint)flags);

    public static SynergyDef? FirstMatch(SynergyFlags flags)
    {
        if (flags == SynergyFlags.None) return null;
        for (int i = 0; i < All.Length; i++)
            if ((flags & All[i].Flag) != 0) return All[i];
        return null;
    }

    public static SynergyDef? WouldUnlock(Player player, string attunementId)
    {
        SynergyFlags current = Evaluate(player);
        for (int i = 0; i < All.Length; i++)
        {
            var def = All[i];
            if ((current & def.Flag) != 0) continue;
            int first = AttunementCatalog.GetLevel(player, def.FirstId) + (attunementId == def.FirstId ? 1 : 0);
            int second = AttunementCatalog.GetLevel(player, def.SecondId) + (attunementId == def.SecondId ? 1 : 0);
            if (first >= def.MinStacks && second >= def.MinStacks) return def;
        }
        return null;
    }

    public static int GetActive(Player player, Span<int> catalogIndices)
    {
        SynergyFlags flags = Evaluate(player);
        int count = 0;
        for (int i = 0; i < All.Length && count < catalogIndices.Length; i++)
        {
            if ((flags & All[i].Flag) != 0)
                catalogIndices[count++] = i;
        }
        return count;
    }
}

public sealed class AttunementDef
{
    public string Id = "";
    public string Tag = "";
    public string Name = "";
    public string Desc = "";
    public int MaxStacks = 4;
    public string? UnlockId;
    public string UnlockText = "";
    public Func<MetaProgress, bool>? UnlockRequirement;
}

/// <summary>One active attunement on the player's ship (unmanaged value type for stackalloc HUD/Pause queries).</summary>
public readonly record struct ActiveAttunement(int Level, int MaxStacks, int CatalogIndex)
{
    public string Id => AttunementCatalog.All[CatalogIndex].Id;
    public string Tag => AttunementCatalog.All[CatalogIndex].Tag;
    public string Name => AttunementCatalog.All[CatalogIndex].Name;
}

public sealed class AttunementOffer
{
    public string Id = "";
    public string Name = "";
    public string Desc = "";
    public int NextLevel;
    public int MaxStacks;
    public int Rarity;
}

public static class AttunementCatalog
{
    public static readonly AttunementDef[] All =
    [
        new() { Id = "split", Tag = "SPL", Name = "SPLIT", Desc = "Adds two angled bolts to every shot.", MaxStacks = 4 },
        new() { Id = "fan", Tag = "FAN", Name = "FAN", Desc = "Adds a wide pair of bolts.", MaxStacks = 4 },
        new() { Id = "pierce", Tag = "PRC", Name = "PIERCE", Desc = "Shots pass through one more target.", MaxStacks = 4 },
        new() { Id = "rapid", Tag = "RPD", Name = "RAPID", Desc = "Fire 14% faster.", MaxStacks = 4 },
        new() { Id = "heavy", Tag = "HVY", Name = "HEAVY", Desc = "Shots hit harder and grow larger.", MaxStacks = 4 },
        new() { Id = "long", Tag = "LNG", Name = "LONG WAVE", Desc = "Shots travel farther.", MaxStacks = 4 },
        new() { Id = "homing", Tag = "HOM", Name = "HOMING", Desc = "Shots bend toward nearby enemies.", MaxStacks = 3, UnlockId = "homing", UnlockText = "Kill 150 enemies.", UnlockRequirement = m => m.TotalKills >= 150 },
        new() { Id = "ricochet", Tag = "RIC", Name = "RICOCHET", Desc = "Shots bounce off obstacles and walls.", MaxStacks = 3, UnlockId = "ricochet", UnlockText = "Shatter 40 monoliths.", UnlockRequirement = m => m.TotalShatters >= 40 },
        new() { Id = "rear", Tag = "RER", Name = "REAR ARRAY", Desc = "Fires a pair of bolts backward.", MaxStacks = 3, UnlockId = "rear", UnlockText = "Reach wave 3.", UnlockRequirement = m => m.BestWave >= 3 },
        new() { Id = "siphon", Tag = "SPH", Name = "SIPHON", Desc = "Kills sometimes drop repair fragments.", MaxStacks = 3, UnlockId = "siphon", UnlockText = "Reach wave 6.", UnlockRequirement = m => m.BestWave >= 6 },
        new() { Id = "phase", Tag = "PHS", Name = "PHASE DRIVE", Desc = "Dash recharges faster.", MaxStacks = 3, UnlockId = "phase", UnlockText = "Graze 250 bullets.", UnlockRequirement = m => m.TotalGrazes >= 250 },
        new() { Id = "grazer", Tag = "GRZ", Name = "GRAZER", Desc = "Near-misses build Resonance faster.", MaxStacks = 3, UnlockId = "grazer", UnlockText = "Graze 80 times in one run.", UnlockRequirement = m => m.BestGrazesInRun >= 80 },
    ];

    public static AttunementDef? Get(string id)
    {
        foreach (var a in All) if (a.Id == id) return a;
        return null;
    }

    public static List<AttunementOffer> Generate(Player player, HashSet<string> unlocked, Rng rng, int count = 3)
    {
        var pool = new List<AttunementDef>();
        foreach (var def in All)
        {
            if (def.UnlockId != null && !unlocked.Contains(def.UnlockId)) continue;
            if (GetLevel(player, def.Id) >= def.MaxStacks) continue;
            pool.Add(def);
        }
        rng.Shuffle(pool);
        var result = new List<AttunementOffer>();
        string[] prefixes = ["TIDAL", "HALO", "VOID", "CHROME", "PHASE", "HOLLOW", "PRISM", "NOCTURNE", "SIGNAL", "ASHEN", "LUMEN", "STATIC"];
        for (int i = 0; i < pool.Count && result.Count < count; i++)
        {
            var def = pool[i];
            int level = GetLevel(player, def.Id) + 1;
            string prefix = prefixes[rng.Range(0, prefixes.Length)];
            result.Add(new AttunementOffer
            {
                Id = def.Id,
                Name = $"{prefix} {def.Name}",
                Desc = def.Desc,
                NextLevel = level,
                MaxStacks = def.MaxStacks,
                Rarity = Math.Clamp(1 + (def.UnlockId != null ? 1 : 0) + (level - 1) / 2, 1, 3),
            });
        }
        return result;
    }

    public static int GetLevel(Player player, string id) => id switch
    {
        "split" => player.SplitLevel,
        "fan" => player.FanLevel,
        "pierce" => player.PierceLevel,
        "rapid" => player.RapidLevel,
        "heavy" => player.HeavyLevel,
        "long" => player.LongLevel,
        "homing" => player.HomingLevel,
        "ricochet" => player.RicochetLevel,
        "rear" => player.RearLevel,
        "siphon" => player.SiphonLevel,
        "phase" => player.PhaseLevel,
        "grazer" => player.GrazerLevel,
        _ => 0,
    };

    /// <summary>
    /// Writes all active attunements (in catalog order) into <paramref name="destination"/> without
    /// allocating, and returns the number of active entries written.
    /// </summary>
    public static int GetActive(Player player, Span<ActiveAttunement> destination)
    {
        int count = 0;
        for (int i = 0; i < All.Length && count < destination.Length; i++)
        {
            var def = All[i];
            int level = GetLevel(player, def.Id);
            if (level <= 0) continue;
            destination[count++] = new ActiveAttunement(level, def.MaxStacks, i);
        }
        return count;
    }

    public static void Apply(Player player, string id)
    {
        switch (id)
        {
            case "split": player.SplitLevel++; break;
            case "fan": player.FanLevel++; break;
            case "pierce": player.PierceLevel++; break;
            case "rapid": player.RapidLevel++; break;
            case "heavy": player.HeavyLevel++; break;
            case "long": player.LongLevel++; break;
            case "homing": player.HomingLevel++; break;
            case "ricochet": player.RicochetLevel++; break;
            case "rear": player.RearLevel++; break;
            case "siphon": player.SiphonLevel++; break;
            case "phase": player.PhaseLevel++; break;
            case "grazer": player.GrazerLevel++; break;
        }
        player.ActiveSynergies = SynergyCatalog.Evaluate(player);
    }
}
