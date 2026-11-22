namespace Nullwave.World;

/// <summary>
/// Player-facing names for whatever hits the ship, so the HUD can point at it and the game-over
/// screen can say what ended the run.
/// </summary>
public sealed partial class World
{
    /// <summary>Session time of the last hit that landed (or was absorbed by playtest god mode).</summary>
    public float LastHitTime { get; private set; } = -100f;

    /// <summary>Name given to lasers spawned right now (song lasers, bosses, Sentinels, pylons).</summary>
    private string? _laserSource;

    /// <summary>The enemy names players see (each role's movement name).</summary>
    public static string RoleName(EnemyRole role) => role switch
    {
        EnemyRole.Drifter => "HUNTER",
        EnemyRole.Orbiter => "SPIRALLER",
        EnemyRole.Sniper => "STALKER",
        EnemyRole.Dasher => "POUNCER",
        EnemyRole.Turret => "HOPPER",
        EnemyRole.Splitter => "JUGGERNAUT",
        EnemyRole.Swarm => "PACK",
        EnemyRole.Weaver => "SERPENT",
        EnemyRole.Mortar => "SIEGE",
        EnemyRole.Phantom => "BLINKER",
        EnemyRole.Prism => "SENTINEL",
        EnemyRole.Boss => "THE CHOIR",
        _ => role.ToString().ToUpperInvariant(),
    };

    /// <summary>
    /// Turns a recorded hit cause ("laser:LASER CROSS", "contact:Dasher", "bullet:Needle 420", "strike")
    /// into what the player reads: "LASER CROSS", "POUNCER RAM", "NEEDLE SHOT", "SIEGE STRIKE".
    /// </summary>
    public static string DescribeHit(string cause)
    {
        if (string.IsNullOrWhiteSpace(cause)) return "";
        int colon = cause.IndexOf(':');
        string kind = colon < 0 ? cause : cause[..colon];
        string rest = colon < 0 ? "" : cause[(colon + 1)..];
        switch (kind)
        {
            case "laser":
                return rest.Length > 0 ? rest : "LASER";
            case "contact":
                if (rest == "THE DROP") return "THE DROP";
                return Enum.TryParse<EnemyRole>(rest, out var role) ? RoleName(role) + (role == EnemyRole.Boss ? "" : " RAM") : "COLLISION";
            case "bullet":
            {
                string b = rest.Split(' ')[0].ToUpperInvariant();
                return b.Length > 0 ? b + " SHOT" : "SHOT";
            }
            case "strike":
                return "SIEGE STRIKE";
            default:
                return cause.ToUpperInvariant();
        }
    }

    /// <summary>What ended the run, for the game-over screen ("" if unknown).</summary>
    public string KilledBy => DescribeHit(LastHitCause);
}
