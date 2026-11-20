using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;

namespace Nullwave.World;

/// <summary>How one track went: the per-song record and the run recap are built from these.</summary>
public sealed class TrackStat
{
    public string Title = "";
    public bool IsSong;
    public int Stage;
    public float StartTime;
    public long StartScore;
    public long Score;
    public int Kills;
    public int Hits;
    public float Duration;
    public bool Complete;
    public string Rank => RankFor(Kills, Hits, Duration);

    /// <summary>S: no hits and a fast kill rate; A: at most one hit; B: at most two; C otherwise.</summary>
    public static string RankFor(int kills, int hits, float duration)
    {
        float kpm = kills / MathF.Max(duration / 60f, 0.25f);
        if (hits == 0 && kpm >= 22f) return "S";
        if (hits <= 1 && kpm >= 12f) return "A";
        if (hits <= 2) return "B";
        return "C";
    }
}

public sealed partial class World
{
    /// <summary>Tracks in a setlist run (0 = endless run).</summary>
    public int SetlistLength => Config.Setlist;
    public bool IsSetlist => SetlistLength > 0;
    /// <summary>Current stage (1-based): each track of the setlist is a stage.</summary>
    public int Stage { get; private set; } = 1;
    public bool FinalStage => IsSetlist && Stage >= SetlistLength;
    /// <summary>The set's final boss went down: the run ends in victory.</summary>
    public bool SetComplete { get; private set; }
    /// <summary>Every track played this run, oldest first; the last one is still playing.</summary>
    public readonly List<TrackStat> Tracks = new();
    public TrackStat? CurrentTrack => Tracks.Count > 0 ? Tracks[^1] : null;
    /// <summary>Bumped whenever a track is finished (the game writes song records then).</summary>
    public int CompletedTrackVersion { get; private set; }
    private bool _finalBossSpawned;
    private bool _setlistDropCue;
    private int _trackIndexSeen;

    /// <summary>Follows the music's tracks: per-track stats, setlist stages and the final boss.</summary>
    private void UpdateSetlist(float dt, AudioState audio)
    {
        if (Demo) return;
        if (Tracks.Count == 0) { StartTrack(audio); _trackIndexSeen = audio.TrackIndex; }
        var t = CurrentTrack!;
        t.Duration += dt;
        t.Score = Score - t.StartScore;
        // by track number, not the one-frame flag: a change during an upgrade pick is still caught
        if (audio.TrackIndex != _trackIndexSeen)
        {
            _trackIndexSeen = audio.TrackIndex;
            FinishTrack();
            StartTrack(audio);
            if (IsSetlist && Phase == GamePhase.Playing) AdvanceStage();
        }
        // the last track's boss arrives with its first drop (or 45 s in, if the song never drops)
        if (FinalStage && !_finalBossSpawned && Phase == GamePhase.Playing &&
            (_setlistDropCue || CurrentTrack!.Duration > 45f))
        {
            _finalBossSpawned = true;
            SpawnSongBoss(final: true);
        }
        _setlistDropCue = false;
    }

    private void StartTrack(AudioState audio)
    {
        Tracks.Add(new TrackStat
        {
            Title = string.IsNullOrWhiteSpace(audio.TrackTitle) ? "GENERATIVE SCORE" : audio.TrackTitle,
            IsSong = audio.TrackIsSong,
            Stage = Stage,
            StartTime = SessionTime,
            StartScore = Score,
        });
    }

    /// <summary>Closes the current track (also called when the run ends).</summary>
    public void FinishTrack()
    {
        var t = CurrentTrack;
        if (t == null || t.Complete) return;
        t.Score = Score - t.StartScore;
        t.Complete = true;
        CompletedTrackVersion++;
    }

    private void AdvanceStage()
    {
        var done = Tracks.Count >= 2 ? Tracks[^2] : null;
        int bonus = 1500 * Stage + (done?.Kills ?? 0) * 25 + (done?.Hits == 0 ? 2500 : 0);
        AddScore(bonus, Player.Pos + new Vector2(0, -60), "STAGE " + Stage + " CLEAR", new Vector4(0.7f, 1f, 1f, 1f));
        Stage++;
        CurrentTrack!.Stage = Stage;
        // the floor is swept clean for the new track
        for (int i = EnemyBullets.Count - 1; i >= 0; i--) ReturnBullet(EnemyBullets, i);
        int count = Enemies.Count;
        for (int i = 0; i < count; i++)
            if (!Enemies[i].Dead && !Enemies[i].Boss) KillEnemy(Enemies[i]);
        AddShockwave(Player.Pos, 1600f, 1.6f, 26f, new Vector4(0.6f, 1f, 1f, 1f), filled: true, heavy: true);
        Flash = MathF.Max(Flash, 0.5f);
        string title = CurrentTrack!.Title;
        EmitEvent(FinalStage ? "FINAL TRACK // " + title : "STAGE " + Stage + " OF " + SetlistLength + " // " + title, 3.4f);
        Sfx?.Trigger(SfxType.Unlock, 0.7f);
        // a new track earns an upgrade
        if (_waveActive || Phase == GamePhase.Playing) BeginAttunement();
    }

    /// <summary>The set's final boss is down: victory.</summary>
    private void CompleteSet()
    {
        if (SetComplete || Phase == GamePhase.Dead) return;
        SetComplete = true;
        FinishTrack();
        AddScore(10000 * SetlistLength, Player.Pos + new Vector2(0, -80), "SET COMPLETE", new Vector4(1f, 0.9f, 0.5f, 1f));
        AddShockwave(Player.Pos, 2400f, 2.2f, 40f, new Vector4(1f, 0.85f, 0.45f, 1f), filled: true, heavy: true);
        BurstParticles(Player.Pos, new Vector4(1f, 0.9f, 0.5f, 0.9f), 220, 900f, 4f, 1.8f, ParticleKind.Spark);
        Sfx?.Trigger(SfxType.Unlock, 1f);
        Phase = GamePhase.Dead;
        Summary = MakeSummary();
        Summary.SetComplete = true;
        RunEnded?.Invoke(Summary);
    }
}
