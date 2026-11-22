using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;
using Nullwave.World;
using GameWorld = Nullwave.World.World;

namespace Nullwave.Tools;

public static partial class SelfTest
{
    /// <summary>
    /// The game now simulates 120 steps a second (it used to be 60). The same play must come out the same
    /// at either rate: movement, dashing, fire rate, enemy pursuit and Flow.
    /// </summary>
    private static void TestSimRateIndependence()
    {
        Check(MathF.Abs(Game.SimTick - 1f / 120f) < 1e-7f, "the simulation runs at 120 Hz");
        var r60 = SimRateRun(1f / 60f);
        var r120 = SimRateRun(1f / 120f);
        static bool Near(float a, float b, float tol) => MathF.Abs(a - b) <= tol * MathF.Max(MathF.Abs(a), MathF.Abs(b)) + 1e-3f;
        Check(Near(r60.Travel, r120.Travel, 0.04f), $"flying is the same at 60 and 120 Hz ({r60.Travel:0} vs {r120.Travel:0})");
        Check(Near(r60.Dash, r120.Dash, 0.06f), $"a dash covers the same ground ({r60.Dash:0} vs {r120.Dash:0})");
        Check(Near(r60.Shots, r120.Shots, 0.12f), $"the guns fire at the same rate ({r60.Shots} vs {r120.Shots})");
        Check(Near(r60.Chase, r120.Chase, 0.06f), $"enemies close in at the same speed ({r60.Chase:0} vs {r120.Chase:0})");
        Check(Near(r60.Flow, r120.Flow, 0.08f), $"Flow builds at the same rate ({r60.Flow:0.00} vs {r120.Flow:0.00})");
        Console.WriteLine($"  sim rate: travel {r60.Travel:0}/{r120.Travel:0}, dash {r60.Dash:0}/{r120.Dash:0}, shots {r60.Shots}/{r120.Shots}, chase {r60.Chase:0}/{r120.Chase:0}, flow {r60.Flow:0.00}/{r120.Flow:0.00} (60/120 Hz)");
    }

    private readonly record struct SimRateResult(float Travel, float Dash, int Shots, float Chase, float Flow);

    private static SimRateResult SimRateRun(float dt)
    {
        GameWorld Fresh(ulong seed)
        {
            var w = new GameWorld(new RunConfig { Seed = seed });
            w.StartGrace = 0f;
            w.Player.InvincibleDemo = true;
            w.BeginWave(1);
            w.ClearSpawnQueueForCapture();
            return w;
        }
        void Run(GameWorld w, AudioState a, float seconds, Func<int, InputFrame> input)
        {
            int steps = (int)MathF.Round(seconds / dt);
            for (int i = 0; i < steps; i++)
            {
                a.Time += dt;
                a.BeatPosition += dt * 2.0;
                w.Step(dt, a, input(i));
            }
        }
        var right = new Vector2(1f, 0f);
        var far = new Vector2(0f, 2600f);

        // flying: hold right for two seconds (one far enemy keeps the wave open)
        var w1 = Fresh(0x5170UL);
        w1.SpawnForCapture(EnemyRole.Turret, w1.Player.Pos + far);
        var a1 = new AudioState { Bpm = 120f };
        Vector2 start = w1.Player.Pos;
        Run(w1, a1, 2f, _ => new InputFrame(right, right, false, false, false, false));
        float travel = Vector2.Distance(start, w1.Player.Pos);
        float flow = w1.Flow;

        // dashing: one tap from rest, then let it play out
        var w2 = Fresh(0x5171UL);
        w2.SpawnForCapture(EnemyRole.Turret, w2.Player.Pos + far);
        var a2 = new AudioState { Bpm = 120f };
        Vector2 dashStart = w2.Player.Pos;
        Run(w2, a2, 0.6f, i => new InputFrame(right, right, false, i == 0, false, false));
        float dash = Vector2.Distance(dashStart, w2.Player.Pos);

        // firing: the classic ship holding the trigger; count bolts as they appear
        var w3 = Fresh(0x5172UL);
        w3.SpawnForCapture(EnemyRole.Turret, w3.Player.Pos + far);
        var a3 = new AudioState { Bpm = 120f };
        var seen = new HashSet<Bullet>(ReferenceEqualityComparer.Instance);
        int stepsFire = (int)MathF.Round(1.5f / dt);
        for (int i = 0; i < stepsFire; i++)
        {
            a3.Time += dt;
            a3.BeatPosition += dt * 2.0;
            w3.Step(dt, a3, new InputFrame(Vector2.Zero, right, true, false, false, false));
            foreach (var b in w3.PlayerBullets) seen.Add(b);
        }

        // pursuit: a Hunter starting 1200 units away, the ship holding still
        var w4 = Fresh(0x5173UL);
        w4.SpawnForCapture(EnemyRole.Drifter, w4.Player.Pos + new Vector2(1200f, 0f));
        var hunter = w4.Enemies[^1];
        var a4 = new AudioState { Bpm = 120f };
        Run(w4, a4, 1.5f, _ => InputFrame.Empty);
        float chase = 1200f - Vector2.Distance(hunter.Pos, w4.Player.Pos);

        return new SimRateResult(travel, dash, seen.Count, chase, flow);
    }

    /// <summary>Every hit is named for the HUD marker and the game-over line.</summary>
    private static void TestDamageSources()
    {
        Check(GameWorld.DescribeHit("laser:LASER CROSS") == "LASER CROSS", "song lasers keep their volley name");
        Check(GameWorld.DescribeHit("contact:Dasher") == "POUNCER RAM", "rams name the enemy");
        Check(GameWorld.DescribeHit("contact:Boss") == "THE CHOIR", "the Choir is named");
        Check(GameWorld.DescribeHit("contact:THE DROP") == "THE DROP", "THE DROP is named");
        Check(GameWorld.DescribeHit("bullet:Needle 420") == "NEEDLE SHOT", "shots name the bullet");
        Check(GameWorld.DescribeHit("strike") == "SIEGE STRIKE", "mortar strikes are named");
        Check(GameWorld.DescribeHit("") == "", "no hit, no name");
        foreach (EnemyRole role in Enum.GetValues<EnemyRole>())
            Check(GameWorld.RoleName(role).Length > 0, "every enemy role has a name");

        // a real laser hit carries its name to the game-over screen
        var w = new GameWorld(new RunConfig { Seed = 0xDA4AUL });
        w.StartGrace = 0f;
        w.BeginWave(5);
        w.ClearSpawnQueueForCapture();
        w.SpawnForCapture(EnemyRole.Turret, w.Player.Pos + new Vector2(0f, 2600f));   // keeps the wave open
        w.Player.Health = 1;
        w.Player.Invuln = 0f;
        w.SpawnLaser(w.Player.Pos + new Vector2(-400f, 0f), w.Player.Pos, 0.01f, 1f, 40f, 0.5f);
        var a = new AudioState { Bpm = 120f };
        for (int i = 0; i < 30 && w.Phase != GamePhase.Dead; i++) { a.Time += 1f / 120f; w.Step(1f / 120f, a, InputFrame.Empty); }
        Check(w.Phase == GamePhase.Dead && w.Summary?.KilledBy == "LASER", "the run summary says what ended the run");
        Check(w.SessionTime - w.LastHitTime < 1f && w.LastHitFrom != w.Player.Pos, "the hit marker knows when and where the hit came from");
        Console.WriteLine("  damage sources: lasers, rams, shots and strikes are named; KilledBy reaches the summary");
    }
}
