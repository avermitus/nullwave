using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;
using Nullwave.World;
using GameWorld = Nullwave.World.World;
namespace Nullwave.Tools;
public static partial class SelfTest
{
    private static void TestExperimentalMaps()
    {
        foreach (string bad in new[] { "999", "-1", "1", "missing", "" })
            Check(!MapCatalog.TryParse(bad, out var k) && k == MapKind.Arena, "reject undefined map " + bad);
        foreach (string good in new[] { "HALO CIRCUIT", "halo", "Cathedral", "open arena" })
            Check(MapCatalog.TryParse(good, out _), "parse map " + good);
        Check(new GameWorld(new RunConfig()).Map == null, "arena remains default");
        foreach (var kind in new[] { MapKind.Halo, MapKind.Cathedral })
        foreach (float scale in new[] { 1f, 0.82f })
        {
            var map = MapCatalog.Build(kind, scale)!;
            Check(map.SegmentCount > 20 && map.FloorQuads.Count > 20, "nonrectangular footprint");
            Check(map.IsBodyValid(map.Spawn, 48), "safe initial spawn");
            Check(!map.TryProjectToFloor(Vector2.Zero, 10000, out _), "impossible margin explicit failure");
            Check(map.NearestNodeBrute(Vector2.Zero, 10000) == -1, "no relaxed node clearance");
            for (int v = 0; v < map.SegmentCount; v++)
            {
                Vector2 vertex = map.SegA[v];
                foreach (float eps in new[] { -0.1f, 0f, 0.1f })
                {
                    Vector2 p = vertex + new Vector2(eps, eps * 0.37f);
                    Check(map.IsFloor(p) == map.IsFloorBrute(p), "vertex half-open floor parity");
                }
                Vector2 start = (map.SegA[v] + map.SegB[v]) * 0.5f + map.SegN[v] * 120f;
                if (!map.IsBodyValid(start, 18)) continue;
                Vector2 end = start;
                map.MoveCircle(ref end, -map.SegN[v] * 1200f, 18);
                Check(map.IsBodyValid(end, 18), "fast dash final clearance");
                for (int step = 0; step < 30; step++)
                {
                    Vector2 previous = start;
                    map.MoveCircle(ref start, -map.SegN[v] * 40, 18);
                    Check(map.IsBodyValid(start, 18) && map.SegmentClear(previous, start), "dash sweep remains on floor");
                }
            }
            foreach (float radius in new[] { 18f, 44.93f, 96f })
            foreach (var room in map.Rooms)
            {
                Vector2 goal = map.ProjectToFloor(room.Center, radius + 8);
                var field = new MapGeometry.FlowField(map, MathF.Max(48f, radius));
                field.Build(map.NearestNodeBrute(goal, field.Radius));
                foreach (var origin in map.Rooms)
                {
                    Vector2 p = map.ProjectToFloor(origin.Center, radius + 8);
                    Check(field.PathLength(p) >= 0, $"{kind}/{scale}/{radius} connected rooms");
                    for (int t = 0; t < 2200 && Vector2.Distance(p, goal) > 35; t++)
                    {
                        Vector2 d = field.Direction(p, goal, radius);
                        map.MoveCircle(ref p, d * 12f, radius);
                        Check(map.IsBodyValid(p, radius), "routed body stays valid");
                    }
                    Check(Vector2.Distance(p, goal) <= 35, $"{kind}/{scale}/{radius} route reaches {room.Name} from {origin.Name}: {p}");
                }
            }
            if (kind == MapKind.Cathedral && scale < 1)
            {
                Vector2 boss = new Vector2(-600, 0) * scale;
                map.MoveCircle(ref boss, new Vector2(0, -450) * scale, 96);
                Check(map.IsBodyValid(boss, 96), "boss cannot squeeze through undersized outer nave lane");
            }
            for (ulong seed = 1; seed <= 3; seed++)
            {
                var w = new GameWorld(new RunConfig { Seed = seed, Map = kind, SelectedModifiers = scale < 1 ? ["low-ceiling"] : [] });
                w.Player.InvincibleDemo = true;
                w.BeginWave(4);
                w.ClearSpawnQueueForCapture();
                foreach (EnemyRole role in Enum.GetValues<EnemyRole>())
                {
                    w.SpawnEnemyForTest(role, new Vector2(9000, 9000));
                    var e = w.Enemies[^1];
                    Check(w.Map!.IsBodyValid(e.Pos, e.Radius), "all role spawn paths safe");
                }
                for (int i = 0; i < 80; i++)
                    Check(w.Map!.IsBodyValid(w.MapSpawnPointForTest(45), 45), "seeded map spawn safe");
                var audio = new AudioState { Bpm = 120, Energy = 0.6f, Rms = 0.2f, HarmLow = 0.8f };
                var synth = new SynthVisual();
                for (int t = 0; t < 900; t++)
                {
                    audio.ClearBeatFlags(); audio.Time += 1f / 30f; synth.Advance(1f / 30f, audio);
                    w.Step(1f / 30f, audio, Pilot.Think(w));
                    Check(w.Map!.IsBodyValid(w.Player.Pos, w.Player.Radius), "full update player valid");
                    foreach (var e in w.Enemies)
                        if (!e.Dead) Check(w.Map.IsBodyValid(e.Pos, e.Radius), "full update enemy/boss valid");
                    foreach (var pk in w.Pickups) Check(w.Map.IsBodyValid(pk.Pos, 10), "pickup stays reachable");
                }
                Check(w.MapRescues == 0, "no off-floor rescues needed");
                var same = w.Map;
                w.BeginWave(6); w.SetMovement(1, true);
                w.BeginWave(11); w.SetMovement(2, true);
                Check(ReferenceEquals(same, w.Map) && w.Obstacles.Count == 0, "movement transitions retain map and routes");
                if (seed == 1)
                {
                    w.SpawnEnemyForTest(EnemyRole.Boss, w.Map!.Spawn);
                    var boss = w.BossEnemy!;
                    foreach (var room in w.Map!.Rooms)
                    {
                        Vector2 target = w.Map.ProjectToFloor(room.Center, 116);
                        for (int tick = 0; tick < 1200 && Vector2.Distance(boss.Pos, target) > 40; tick++)
                        {
                            w.MoveBossInMap(boss, target, 1.4f, 0.05f);
                            Check(w.Map.IsBodyValid(boss.Pos, boss.Radius), "boss integration crosses rooms safely");
                        }
                        Check(Vector2.Distance(boss.Pos, target) <= 40, "boss integration reaches target room");
                    }
                }
                w.Enemies.Clear(); w.BossEnemy = null;
                w.SpawnSongBoss();
                for (int t = 0; t < 240; t++)
                {
                    w.Step(0.05f, audio, Pilot.Think(w));
                    Check(w.Map!.IsBodyValid(w.BossEnemy!.Pos, 96), "song boss full update valid");
                }
            }
            var clearWorld = new GameWorld(new RunConfig { Seed = 101, Map = kind, SelectedModifiers = scale < 1 ? ["low-ceiling"] : [] });
            clearWorld.Player.InvincibleDemo = true;
            var clearAudio = new AudioState();
            var clearSynth = new SynthVisual();
            for (int tick = 0; tick < 12000 && clearWorld.Wave < 3; tick++)
            {
                const float dt = 1f / 60f;
                clearAudio.ClearBeatFlags(); clearAudio.Time += dt; clearSynth.Advance(dt, clearAudio);
                if (clearWorld.Phase == GamePhase.Attuning) clearWorld.ChooseAttunement(0);
                clearWorld.Step(dt, clearAudio, Pilot.Think(clearWorld));
            }
            Check(clearWorld.Wave >= 3, $"{kind}/{scale} pilot completes waves (wave={clearWorld.Wave}, kills={clearWorld.Kills})");
            foreach (var room in clearWorld.Map!.Rooms)
            {
                clearWorld.Player.Pos = clearWorld.Map.ProjectToFloor(room.Center, 30);
                clearWorld.SpawnPickup(room.Center + new Vector2(700, 200), PickupKind.Health);
                for (int tick = 0; tick < 60; tick++) clearWorld.UpdatePickupsForTest(0.05f);
                foreach (var pk in clearWorld.Pickups) Check(clearWorld.Map.IsBodyValid(pk.Pos, GameWorld.PickupRadius), "drifting pickup fits floor");
                var rail = clearWorld.StartRailForTest(clearAudio);
                for (int tick = 0; tick < 150; tick++) clearWorld.GrowRailForTest(rail, 0.02f, clearAudio);
                for (int j = 1; j < rail.Points.Count; j++)
                    Check(clearWorld.Map.CapsuleClear(rail.Points[j-1], rail.Points[j], 18), "music rail never bridges wall");
            }
            // Fire across the first wall in one update, then verify ricochet and laser clipping.
            var shotWorld = new GameWorld(new RunConfig { Seed = 17, Map = kind });
            var geo = shotWorld.Map!;
            int wall = Enumerable.Range(0, geo.SegmentCount).First(i => geo.IsBodyValid((geo.SegA[i] + geo.SegB[i]) * 0.5f + geo.SegN[i] * 80, 18));
            Vector2 mid = (geo.SegA[wall] + geo.SegB[wall]) * 0.5f, n = geo.SegN[wall];
            Vector2 from = mid + n * 80;
            shotWorld.Player.Pos = from;
            shotWorld.SpawnPlayerBullet(from, -n * 2400, 3, 10, 3, 0);
            shotWorld.UpdateBulletsForTest(0.05f);
            Check(shotWorld.PlayerBullets.Count == 0, "fast player bullet consumed at wall");
            shotWorld.SpawnPlayerBullet(from, -n * 2400, 3, 10, 3, 0, bounces: 2);
            shotWorld.UpdateBulletsForTest(0.05f);
            Check(shotWorld.PlayerBullets.Count == 1 && shotWorld.PlayerBullets[0].Bounces == 1 && Vector2.Dot(shotWorld.PlayerBullets[0].Vel, n) > 0, "ricochet reflects once");
            shotWorld.SpawnEnemyBullet(from, -n * 2400, 3, 0.5f, BulletKind.Needle);
            shotWorld.UpdateBulletsForTest(0.05f);
            Check(shotWorld.EnemyBullets.Count == 0, "fast enemy bullet consumed at wall");
            shotWorld.SpawnLaser(from, mid - n * 300, 0, 5, 12, 0.5f, length: 1000);
            var laser = shotWorld.Lasers[^1];
            Check(laser.BeamLength < 82 && laser.BeamLength > 78, "laser first wall clipping");
            laser.Angle += MathF.PI;
            shotWorld.UpdateLasersForTest(0.01f);
            Check(laser.BeamLength > 82, "rotating clipped laser can extend");
            Console.WriteLine($"  maps: {kind} scale={scale} geometry, routing, simulation, projectiles PASS");
        }
    }
}
