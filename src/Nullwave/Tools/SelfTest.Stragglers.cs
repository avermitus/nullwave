using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;
using Nullwave.World;
using GameWorld = Nullwave.World.World;

namespace Nullwave.Tools;

public static partial class SelfTest
{
    /// <summary>
    /// Stragglers: a lone Spiraller used to circle a moving ship at the screen edge for a minute or more
    /// (found in an autoplay session). The last enemies of a wave must commit to the hunt after a grace.
    /// </summary>
    private static void TestStragglerHunt()
    {
        const float dt = 1f / 60f;
        var w = new GameWorld(new RunConfig { Seed = 0x57A6UL });
        w.StartGrace = 0f;
        w.Player.InvincibleDemo = true;
        w.BeginWave(1);
        w.ClearSpawnQueueForCapture();
        w.SpawnForCapture(EnemyRole.Orbiter, w.Player.Pos + new Vector2(650f, 0f));
        var orbiter = w.Enemies[^1];
        var a = new AudioState { Bpm = 120f };
        float before = 0f, closest = float.MaxValue, late = 0f;
        bool huntedEarly = false;
        for (int f = 0; f < 60 * 12; f++)
        {
            float t = f * dt;
            // the ship keeps circling, like a player kiting
            var move = new Vector2(MathF.Cos(t * 1.3f), MathF.Sin(t * 1.3f));
            a.Time += dt;
            a.BeatPosition += dt * 2.0;
            w.Step(dt, a, new InputFrame(move, move, false, false, false, false));
            float d = Vector2.Distance(orbiter.Pos, w.Player.Pos);
            if (t > 1.5f && t < GameWorld.StragglerGrace - 0.2f)
            {
                before = MathF.Max(before, d);
                if (w.StragglersHunting) huntedEarly = true;
            }
            if (t > GameWorld.StragglerGrace + GameWorld.StragglerRamp) closest = MathF.Min(closest, d);
            if (t > 9f) late = MathF.Max(late, d);
        }
        Check(!huntedEarly, "a straggler keeps its own movement through the grace period");
        Check(w.StragglersHunting, "the last enemy commits to the hunt after the grace period");
        // the ship is meant to outrun enemies, so the bar is "comes on screen and stays in range", not a catch
        Check(before > 700f, "without the hunt, the straggler drifts off screen (the bug this fixes)");
        Check(closest < 400f && late < 600f, "a hunting straggler comes on screen and stays in fighting range");
        Check(!orbiter.Dead, "the hunt moves the straggler, it does not kill it");

        // a full wave never hunts
        var full = new GameWorld(new RunConfig { Seed = 0x57A7UL });
        full.StartGrace = 0f;
        full.Player.InvincibleDemo = true;
        full.BeginWave(1);
        full.ClearSpawnQueueForCapture();
        for (int i = 0; i < 5; i++)
            full.SpawnForCapture(EnemyRole.Drifter, full.Player.Pos + M.FromAngle(i * 1.25f, 1400f));
        var b = new AudioState { Bpm = 120f };
        for (int f = 0; f < 60 * 8; f++)
        {
            b.Time += dt;
            full.Step(dt, b, InputFrame.Empty);
        }
        Check(!full.StragglersHunting, "a wave with more than two enemies left never hunts");
        Console.WriteLine($"  stragglers: drifted to {before:0}u before the hunt, then held within {late:0}u (closest {closest:0}u)");
    }

    /// <summary>READABILITY GUARD: a soft ceiling on the background's brightness, on by default.</summary>
    private static void TestReadabilityGuard()
    {
        Check(new GameSettings().ReadabilityGuard == 100, "the readability guard is on by default");
        Check(MathF.Abs(Nullwave.Render.Renderer.ReadabilityCeiling(100) - 0.30f) < 1e-5f, "full guard caps the background at 0.30 luma");
        Check(Nullwave.Render.Renderer.ReadabilityCeiling(0) > 5f, "0 % turns the guard off");
        Check(Nullwave.Render.Renderer.ReadabilityCeiling(50) > Nullwave.Render.Renderer.ReadabilityCeiling(100), "a weaker guard allows a brighter background");
        var s = new GameSettings { ReadabilityGuard = 400 };
        s.Sanitize();
        Check(s.ReadabilityGuard == 100, "the guard setting is clamped");
        Check(Nullwave.Render.Shaders.BackgroundFrag.Contains("uniform float uLumaCap;") && Nullwave.Render.Shaders.BackgroundFrag.Contains("readability guard"),
            "the background shader applies the guard");
        Console.WriteLine("  readability guard: background luma ceiling 0.30 at 100 %, off at 0 %");
    }
}
