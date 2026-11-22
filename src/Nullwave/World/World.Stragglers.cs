using Nullwave.Core;

namespace Nullwave.World;

/// <summary>
/// Stragglers: when a wave is down to its last one or two enemies and nothing is left to spawn, they
/// stop keeping their distance after a few seconds and come for you. Without this, a Spiraller or
/// Sentinel circling at the edge of the screen could hold a wave open for a minute or more.
/// </summary>
public sealed partial class World
{
    /// <summary>Seconds the last enemies keep their own style before they commit to the hunt.</summary>
    public const float StragglerGrace = 5f;
    /// <summary>Seconds over which they blend from their own movement into the hunt.</summary>
    public const float StragglerRamp = 1.5f;
    public const int StragglerMax = 2;

    private float _stragglerTime;
    private bool _stragglerAnnounced;

    /// <summary>The wave's last enemies have stopped keeping their distance.</summary>
    public bool StragglersHunting => _stragglerTime >= StragglerGrace;

    /// <summary>0 before the hunt, rising to 1 over <see cref="StragglerRamp"/> seconds.</summary>
    public float StragglerHunt => M.Clamp01((_stragglerTime - StragglerGrace) / StragglerRamp);

    private void UpdateStragglers(float dt)
    {
        int alive = 0;
        bool boss = false;
        if (_waveActive && _spawnQueue.Count == 0)
            foreach (var e in Enemies)
            {
                if (e.Dead) continue;
                if (e.Boss) boss = true;
                else alive++;
            }
        if (!boss && alive is > 0 and <= StragglerMax)
            _stragglerTime += dt;
        else
        {
            _stragglerTime = 0f;
            _stragglerAnnounced = false;
        }
        if (StragglersHunting && !_stragglerAnnounced)
        {
            _stragglerAnnounced = true;
            EmitEvent(alive == 1 ? "LAST SIGNAL // IT IS COMING FOR YOU" : "LAST SIGNALS // THEY ARE COMING FOR YOU", 1.8f);
        }
    }
}
