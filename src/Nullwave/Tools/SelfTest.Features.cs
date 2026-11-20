using System.Numerics;
using System.Reflection;
using Nullwave.Audio;
using Nullwave.Core;
using Nullwave.Render;
using Nullwave.World;
using GameWorld = Nullwave.World.World;

namespace Nullwave.Tools;

/// <summary>
/// Self tests for setlist runs, song records, instrument ships, in-key guns, THE DROP, the run recap,
/// rumble, the tutorial and the EDM show.
/// </summary>
public static partial class SelfTest
{
    private const float Dt = 1f / 60f;
    private static readonly InputFrame FireRight = new(Vector2.Zero, new Vector2(1f, 0f), true, false, false, false);

    /// <summary>A world in play, with one enemy far away keeping the wave open.</summary>
    private static GameWorld LiveWorld(ulong seed, ShipKind ship = ShipKind.Classic, int setlist = 0)
    {
        var w = new GameWorld(new RunConfig { Seed = seed, Ship = ship, Setlist = setlist });
        w.StartGrace = 0f;
        w.Player.InvincibleDemo = true;
        w.BeginWave(1);
        w.ClearSpawnQueueForCapture();
        w.SpawnForCapture(EnemyRole.Turret, w.Player.Pos + new Vector2(2600f, 2600f));
        return w;
    }

    /// <summary>Steps the world with a fresh audio frame each step (onsets are read once per audio frame).</summary>
    private static void Play(GameWorld w, AudioState a, int frames, InputFrame input)
    {
        for (int i = 0; i < frames; i++)
        {
            a.Time += Dt;
            a.BeatPosition += Dt * a.Bpm / 60.0;
            w.Step(Dt, a, input);
        }
    }

    private static void KillForTest(GameWorld w, Enemy e) =>
        typeof(GameWorld).GetMethod("KillEnemy", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, [e]);

    /// <summary>Setlists: each new track is a stage with an upgrade; the last one ends with THE DROP; beating it wins.</summary>
    private static void TestSetlistRuns()
    {
        var w = LiveWorld(0x5E71UL, setlist: 2);
        var a = new AudioState { Bpm = 120f, TrackTitle = "ARTIST - FIRST", TrackIsSong = true, TrackIndex = 1 };
        Play(w, a, 30, InputFrame.Empty);
        Check(w.IsSetlist && w.Stage == 1 && !w.FinalStage, "a setlist starts on stage 1");
        Check(w.Tracks.Count == 1 && w.CurrentTrack!.Title == "ARTIST - FIRST", "the first track is followed");
        a.TrackIndex = 2;
        a.TrackTitle = "ARTIST - SECOND";
        Play(w, a, 2, InputFrame.Empty);
        Check(w.Stage == 2 && w.FinalStage, "a new track is the next stage");
        Check(w.Tracks.Count == 2 && w.Tracks[0].Complete && w.CompletedTrackVersion == 1, "the finished track is closed for its record");
        Check(w.Phase == GamePhase.Attuning, "clearing a stage offers an upgrade");
        w.ChooseAttunement(0);
        for (int i = 0; i < 1200 && w.Phase != GamePhase.Playing; i++) Play(w, a, 1, InputFrame.Empty);
        Check(w.Phase == GamePhase.Playing, "play resumes after the pick");
        Check(w.BossEnemy is not { Dead: false }, "no final boss before the drop");
        w.SetMusicCue(0.95f, 0f, 0f, true);
        Play(w, a, 2, InputFrame.Empty);
        var boss = w.BossEnemy;
        Check(boss is { SongBoss: true, Dead: false }, "the final track's drop brings THE DROP");
        RunSummary? summary = null;
        w.RunEnded += s => summary = s;
        KillForTest(w, boss!);
        Check(w.SetComplete && w.Phase == GamePhase.Dead, "beating the final boss ends the run in victory");
        Check(summary is { SetComplete: true } && summary.Tracks.Count == 2 && summary.Stages == 2, "the summary lists the set");
        Console.WriteLine($"  setlist: stage {w.Stage}/{w.SetlistLength}, tracks={w.Tracks.Count}, set complete={w.SetComplete}");
    }

    /// <summary>Song records: ranks, bests across plays, and only real songs played long enough count.</summary>
    private static void TestSongRecords()
    {
        Check(TrackStat.RankFor(30, 0, 60f) == "S", "no hits and a fast kill rate is S");
        Check(TrackStat.RankFor(30, 1, 60f) == "A", "one hit is at best A");
        Check(TrackStat.RankFor(5, 2, 60f) == "B", "two hits is B");
        Check(TrackStat.RankFor(40, 3, 60f) == "C", "three hits is C");
        var meta = new MetaProgress();
        Check(meta.RecordSong(new TrackStat { Title = "Artist - Song", IsSong = true, Duration = 95f, Score = 12000, Kills = 40, Hits = 1 }),
            "a first play sets the best");
        Check(!meta.RecordSong(new TrackStat { Title = " ARTIST - SONG", IsSong = true, Duration = 80f, Score = 9000, Kills = 10, Hits = 4 }),
            "a worse play is not a new best");
        var r = meta.GetSong("artist - song");
        Check(r != null && r.Plays == 2 && r.BestScore == 12000 && r.BestRank == "A" && r.BestKills == 40 && r.FewestHits == 1,
            "a record keeps the best of every play, whatever the title's case");
        Check(!meta.RecordSong(new TrackStat { Title = "Short", IsSong = true, Duration = 12f, Score = 99999 }), "tracks under 30 s do not count");
        Check(!meta.RecordSong(new TrackStat { Title = "GENERATIVE SCORE", IsSong = false, Duration = 200f, Score = 99999 }), "the generative score is not a song");
        Check(meta.GetSong("Short") == null && meta.SongRecords.Count == 1, "skipped tracks leave no record");
        Console.WriteLine("  song records: ranks S/A/B/C, best-of-plays, 30 s minimum");
    }

    /// <summary>Instrument ships: the drums fire on drum hits, the bass lobs slow orbs, the lead beam bends with the melody.</summary>
    private static void TestInstrumentShips()
    {
        var hold = FireRight;
        var noFire = new InputFrame(Vector2.Zero, new Vector2(1f, 0f), false, false, false, false);

        // drums
        var w = LiveWorld(0xD8UL, ShipKind.Drums);
        var a = new AudioState { Bpm = 120f };
        Play(w, a, 5, hold);
        w.PlayerBullets.Clear();
        a.KickHit = true; Play(w, a, 1, hold); a.KickHit = false;
        Check(w.PlayerBullets.Count >= 1, "drums: the kick fires a slug");
        float slug = w.PlayerBullets[^1].Damage;
        w.PlayerBullets.Clear();
        a.SnareHit = true; Play(w, a, 1, hold); a.SnareHit = false;
        Check(w.PlayerBullets.Count >= 3, "drums: the snare fires a fan");
        w.PlayerBullets.Clear();
        a.HatHit = true; Play(w, a, 1, hold); a.HatHit = false;
        Check(w.PlayerBullets.Count >= 1 && slug > w.PlayerBullets[^1].Damage * 2f, "drums: kick slugs hit far harder than hat needles");
        w.PlayerBullets.Clear();
        a.KickHit = true; Play(w, a, 1, noFire); a.KickHit = false;
        Check(w.PlayerBullets.Count == 0, "drums: nothing fires unless you hold fire");
        w.PlayerBullets.Clear();
        Play(w, a, 90, hold);
        Check(w.PlayerBullets.Count >= 2, "drums: with no drums to play along to, an eighth-note pulse keeps firing");

        // classic, for reference
        var c = LiveWorld(0xC1UL);
        var ac = new AudioState { Bpm = 120f };
        Play(c, ac, 20, hold);
        Check(c.PlayerBullets.Count > 0, "classic ship auto-fires");
        float classicSpeed = c.PlayerBullets[^1].Vel.Length();

        // bass
        var b = LiveWorld(0xBA55UL, ShipKind.Bass);
        var ab = new AudioState { Bpm = 120f, HarmLow = 0.8f };
        Play(b, ab, 5, hold);
        b.PlayerBullets.Clear();
        Play(b, ab, 70, hold);
        int orbs = 0;
        float orbSpeed = 0f;
        foreach (var x in b.PlayerBullets)
            if (x.SynergyKind == 4) { orbs++; orbSpeed = MathF.Max(orbSpeed, x.Vel.Length()); }
        Check(orbs >= 1 && orbs == b.PlayerBullets.Count, "bass: it fires heavy orbs on the beat, nothing else");
        Check(orbSpeed < classicSpeed * 0.5f, "bass: the orbs are slow");

        // lead
        var l = LiveWorld(0x1EADUL, ShipKind.Lead);
        var al = new AudioState { Bpm = 120f, MelodyConfidence = 0.9f, MelodyPitch = 64f };
        l.SpawnForCapture(EnemyRole.Drifter, l.Player.Pos + new Vector2(380f, 0f));
        var victim = l.Enemies[^1];
        Play(l, al, 3, hold);
        Check(l.LeadBeam.Count == 19 && l.LeadBeamPower > 0.9f, "lead: holding fire draws the beam");
        Vector2 straight = M.SafeNorm(l.LeadBeam[^1] - l.LeadBeam[0]);
        Play(l, al, 40, hold);
        Check(victim.Dead || victim.Hp < victim.MaxHp, "lead: the beam cuts what it touches");
        al.MelodyPitch = 71f;
        Play(l, al, 3, hold);
        Vector2 bent = M.SafeNorm(l.LeadBeam[^1] - l.LeadBeam[0]);
        float turn = MathF.Acos(M.Clamp(Vector2.Dot(straight, bent), -1f, 1f)) * 180f / MathF.PI;
        Check(turn > 10f, "lead: a higher note bends the beam");
        Play(l, al, 1, noFire);
        Check(l.LeadBeam.Count == 0, "lead: letting go drops the beam");
        Console.WriteLine($"  ships: slug {slug:0.0} dmg, bass orbs {orbs} at {orbSpeed:0} (classic {classicSpeed:0}), lead bend {turn:0}deg");
    }

    /// <summary>In-key guns: chord tones stay in the chord, and scheduled notes wait for their sixteenth.</summary>
    private static void TestInKeyNotes()
    {
        Check(GameWorld.ChordTone(0, false, 0, 0) == 72, "C major's root is C5");
        Check(GameWorld.ChordTone(0, false, 1, 0) == 76 && GameWorld.ChordTone(0, true, 1, 0) == 75, "the third follows major and minor");
        Check(GameWorld.ChordTone(9, true, 2, 0) == 72 + 9 + 7 - 12, "high roots fold down an octave");
        Check(GameWorld.ChordTone(2, false, 0, 1) == GameWorld.ChordTone(2, false, 0, 0) + 12, "octave shifts are octaves");
        for (int root = 0; root < 12; root++)
        for (int deg = 0; deg < 8; deg++)
        {
            int pc = ((GameWorld.ChordTone(root, true, deg, 0) % 12) + 12) % 12;
            Check(pc == root || pc == (root + 3) % 12 || pc == (root + 7) % 12, "minor chord tones stay in the chord");
            int pcMaj = ((GameWorld.ChordTone(root, false, deg, 0) % 12) + 12) % 12;
            Check(pcMaj == root || pcMaj == (root + 4) % 12 || pcMaj == (root + 7) % 12, "major chord tones stay in the chord");
        }
        var bank = new SoundBank();
        bank.PlayNote(76f, 0.8f, 0f, 0.1f);
        var buf = new float[4800];   // 50 ms of stereo at 48 kHz
        bank.Mix(buf, 0, buf.Length);
        bool silent = true;
        foreach (float v in buf) if (v != 0f) silent = false;
        Check(silent, "a note scheduled 100 ms ahead is silent at first");
        float peak = 0f;
        for (int k = 0; k < 4; k++)
        {
            Array.Clear(buf);
            bank.Mix(buf, 0, buf.Length);
            foreach (float v in buf) peak = MathF.Max(peak, MathF.Abs(v));
        }
        Check(peak > 0.02f && peak < 1f, "then it sounds, on time and unclipped");
        Console.WriteLine($"  in-key notes: chord tones in every key, scheduled note peak {peak:0.00}");
    }

    /// <summary>THE DROP: rings on the kick, fans on the snare, a new phase per song section, a melody laser.</summary>
    private static void TestSongBoss()
    {
        var w = LiveWorld(0xD209UL);
        w.SpawnSongBoss();
        var boss = w.BossEnemy;
        Check(boss is { SongBoss: true, Boss: true, Dead: false }, "THE DROP spawns as a boss");
        Check(w.BossTitle(boss!, 4).StartsWith("THE DROP"), "its bar is titled THE DROP");
        // drums are present (no beat-grid fallback), but no onsets yet
        var a = new AudioState { Bpm = 120f, PercLow = 0.3f, KickPulse = 0.3f };
        Play(w, a, 180, InputFrame.Empty);
        int n0 = w.EnemyBullets.Count;
        a.KickHit = true; Play(w, a, 1, InputFrame.Empty); a.KickHit = false;
        int ring = w.EnemyBullets.Count - n0;
        Check(ring >= 12, "the kick slams out a ring");
        n0 = w.EnemyBullets.Count;
        a.SnareHit = true; Play(w, a, 1, InputFrame.Empty); a.SnareHit = false;
        Check(w.EnemyBullets.Count - n0 >= 5, "the snare fires a fan");
        a.SectionChanged = true; a.SectionLabel = 2;
        Play(w, a, 1, InputFrame.Empty);
        a.SectionChanged = false;
        Check(boss!.BossPhase == 2 && boss.BossInvuln > 0f, "a new song section is a new phase");
        n0 = w.EnemyBullets.Count;
        a.KickHit = true; Play(w, a, 1, InputFrame.Empty); a.KickHit = false;
        Check(w.EnemyBullets.Count <= n0, "it holds fire while the phase breaks");
        Play(w, a, 75, InputFrame.Empty);
        n0 = w.EnemyBullets.Count;
        a.KickHit = true; Play(w, a, 1, InputFrame.Empty); a.KickHit = false;
        int denser = w.EnemyBullets.Count - n0;
        Check(denser >= 16, "later phases fire denser rings");
        int lasers = w.Lasers.Count;
        a.MelodyConfidence = 0.9f; a.MelodyPitch = 67f;
        Play(w, a, 5, InputFrame.Empty);
        Check(w.Lasers.Count > lasers, "the melody draws the boss's laser");
        Console.WriteLine($"  song boss: ring {ring} -> {denser} bullets, phase {boss.BossPhase + 1}, melody laser on");
    }

    /// <summary>The run recap: a sampled timeline, the best six seconds, track starts and drops.</summary>
    private static void TestRunRecap()
    {
        var w = LiveWorld(0x7173UL);
        var a = new AudioState { Bpm = 120f, TrackTitle = "SONG", TrackIsSong = true, TrackIndex = 1 };
        for (int f = 0; f < 60 * 14; f++)
        {
            float t = f * Dt;
            w.Score += t is > 5f and < 7f ? 400 : 2;   // a two-second burst of scoring
            Play(w, a, 1, InputFrame.Empty);
        }
        var tl = w.Timeline;
        Check(tl.Samples.Count >= 26, "the timeline samples twice a second");
        Check(tl.BestMomentVersion > 0 && tl.BestMomentTime > 3f && tl.BestMomentTime < 9f, "the best moment is the scoring burst");
        Check(tl.TrackStarts.Count == 1 && tl.TrackStarts[0].Title == "SONG", "track starts are marked");
        tl.AddDrop(10f); tl.AddDrop(12f); tl.AddDrop(15f);
        Check(tl.Drops.Count == 2, "drops closer than 4 s together count once");
        Console.WriteLine($"  recap: {tl.Samples.Count} samples, best moment at {tl.BestMomentTime:0.0}s (+{tl.BestMomentGain})");
    }

    /// <summary>Rumble follows the drums and hits; the tutorial advances when you do the thing; tips show once.</summary>
    private static void TestRumbleAndCoach()
    {
        var kick = new AudioState { KickPulse = 1f };
        var (lo, hi) = Game.RumbleFor(kick, true, 0f, false, 100);
        Check(lo > 0.4f && hi < lo, "the kick thumps the heavy motor");
        Check(Game.RumbleFor(kick, false, 0f, false, 100) == (0f, 0f), "no rumble outside play");
        Check(Game.RumbleFor(kick, true, 0f, false, 0) == (0f, 0f), "0% turns rumble off");
        var hurt = Game.RumbleFor(new AudioState(), true, 1f, false, 100);
        Check(hurt.Low > 0.8f && hurt.High > 0.4f, "taking a hit shakes both motors");
        var loud = Game.RumbleFor(new AudioState { KickPulse = 5f, SnarePulse = 5f, HatPulse = 5f, PercLow = 5f, HarmLow = 5f }, true, 1f, true, 150);
        Check(loud.Low <= 1f && loud.High <= 1f, "motor speeds stay in range");

        var w = LiveWorld(0xC0ACUL);
        var coach = new Coach();
        var seen = new HashSet<string>();
        coach.Begin(w, true);
        Check(coach.Active && coach.StepIndex == 0, "the tutorial starts with MOVE");
        for (int i = 0; i < 60; i++)
        {
            w.Player.Pos += new Vector2(20f, 0f);
            coach.Update(w, Dt, seen, true);
        }
        for (int i = 0; i < 60; i++) coach.Update(w, Dt, seen, true);
        Check(coach.StepIndex == 1, "flying around finishes MOVE");
        coach.Skip();
        Check(!coach.Active, "Tab skips the tutorial");
        w.SpawnSongBoss();
        coach.Update(w, Dt, seen, true);
        Check(seen.Contains("drop"), "THE DROP gets a one-time tip");
        int count = seen.Count;
        coach.Update(w, Dt, seen, true);
        Check(seen.Count == count, "a tip is only queued once");
        var quiet = new HashSet<string>();
        var w2 = LiveWorld(0xC0ADUL);
        w2.SpawnSongBoss();
        new Coach().Update(w2, Dt, quiet, false);
        Check(quiet.Count == 0, "TUTORIAL & TIPS off shows no tips");
        Console.WriteLine("  rumble + coach: kick/hurt motors, MOVE step, one-time tips");
    }

    /// <summary>The EDM show: build-up and drop effects, settings, flashing limits, and aim under the camera roll.</summary>
    private static void TestEdmShow()
    {
        var off = Renderer.EdmFxFor(0f, 0.0, 1f, 1f, 0f, 1f, 0.01f, 0f, 0f, false);
        Check(off.Laser == 0f && off.Kaleido == 0f && off.HueStep == 0f && off.Pump == 0f && off.Strobe == 0f, "MUSIC FX 0% turns the show off");
        Check(off.Vertigo == 0f && off.Dive == 0f && off.CamKick == 0f && off.Roll == 0f && off.Letterbox == 0f, "CAMERA FX 0% keeps the camera still");

        var calm = Renderer.EdmFxFor(1000f, 10.5, 0.5f, 0f, 0f, 0f, 0f, 1f, 1f, false);
        var build = Renderer.EdmFxFor(1000f, 10.5, 0.5f, 0.95f, 0f, 0f, 0f, 1f, 1f, false);
        Check(calm.Kaleido == 0f && calm.Letterbox == 0f && calm.Vertigo == 0f, "no build-up, no build-up effects");
        Check(build.Kaleido > 0.7f && build.Letterbox > 0.05f && build.Vertigo > 0.9f, "a build-up folds the world, lifts the camera and closes the bars");

        var hit = Renderer.EdmFxFor(0.02f, 64.02, 0.8f, 0f, 0f, 0f, 0f, 1f, 1f, false);
        var later = Renderer.EdmFxFor(3f, 67.0, 0.8f, 0f, 0f, 0f, 0f, 1f, 1f, false);
        var gone = Renderer.EdmFxFor(20f, 84.0, 0.8f, 0f, 0f, 0f, 0f, 1f, 1f, false);
        Check(hit.Dive > 0.35f && later.Dive < 0.05f, "the drop slams the camera in and it eases back");
        Check(hit.Strobe > 0f && gone.Strobe == 0f, "the drop pulses only in its first bar");
        Check(Renderer.EdmFxFor(0.5f, 64.5, 0.8f, 0f, 0f, 0f, 0f, 1f, 1f, false).Strobe == 0f, "the pulse sits on the beat");
        Check(hit.Pump > 0.8f && gone.Pump == 0f && gone.HueStep == 0f, "pump and hue steps last four bars");
        Check(hit.Laser >= later.Laser && hit.Laser > gone.Laser, "lasers flare on the drop");
        Check(MathF.Abs(hit.Roll) > 0.03f && MathF.Abs(gone.Roll) < 0.001f, "the drop rocks the frame, then it settles");
        Check(Renderer.EdmFxFor(1000f, 1.0, 0.9f, 0f, 1f, 0f, 0f, 1f, 1f, false).Laser == 0f, "breakdowns turn the lasers off");

        // photosensitivity: at 174 BPM the drop pulses at most 4 times, under 3 per second
        int pulses = 0, firstSecond = 0;
        bool on = false;
        for (int i = 0; i < 480; i++)
        {
            double beats = i / 240.0 * 174.0 / 60.0;
            bool s = Renderer.EdmFxFor((float)beats, 100.0 + beats, 0.8f, 0f, 0f, 0f, 0f, 1.4f, 1.5f, false).Strobe > 0f;
            if (s && !on) { pulses++; if (i < 240) firstSecond++; }
            on = s;
        }
        Check(pulses <= 4 && firstSecond <= 3, "the drop never strobes faster than 3 Hz");
        var safe = Renderer.EdmFxFor(0.02f, 64.02, 0.8f, 0.95f, 0f, 0f, 0f, 1f, 1f, true);
        Check(safe.Strobe == 0f && safe.HueStep == 0f && safe.Kaleido < build.Kaleido, "REDUCE FLASHING drops the pulse and hue steps and softens the rest");

        var nan = Renderer.EdmFxFor(float.NaN, double.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, false);
        Check(float.IsFinite(nan.Laser + nan.Kaleido + nan.Vertigo + nan.Dive + nan.CamKick + nan.HueStep + nan.Pump + nan.Strobe + nan.Roll + nan.Letterbox),
            "bad input stays finite");

        // aim under the roll: the mouse maps through the same transform the composite uses
        foreach (float aspect in new[] { 16f / 9f, 21f / 9f, 9f / 16f, 1f })
        foreach (float roll in new[] { -0.07f, -0.01f, 0.03f, 0.075f })
        {
            foreach (var pt in new[] { new Vector2(0.1f, 0.2f), new Vector2(0.5f, 0.5f), new Vector2(0.93f, 0.71f) })
            {
                var back = Renderer.RollScreen(Renderer.UnrollScreen(pt, roll, aspect), roll, aspect);
                Check(Vector2.Distance(back, pt) < 1e-4f, "roll and unroll are inverses");
            }
            Check(Vector2.Distance(Renderer.UnrollScreen(new Vector2(0.5f, 0.5f), roll, aspect), new Vector2(0.5f, 0.5f)) < 1e-6f, "the roll turns about the centre");
            foreach (var corner in new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) })
            {
                var s = Renderer.UnrollScreen(corner, roll, aspect);
                Check(s.X >= -1e-4f && s.X <= 1.0001f && s.Y >= -1e-4f && s.Y <= 1.0001f, "a rolled frame never shows its edge");
            }
        }
        Console.WriteLine($"  edm show: build kaleido {build.Kaleido:0.00}, drop dive {hit.Dive:0.00}, roll {hit.Roll * 57.3f:0.0}deg, {pulses} pulses");
    }
}
