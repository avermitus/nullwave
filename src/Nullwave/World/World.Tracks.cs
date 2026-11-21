using Nullwave.Audio;

namespace Nullwave.World;

/// <summary>How one track went: the per-song record and the run recap are built from these.</summary>
public sealed class TrackStat
{
    public string Title = "";
    public bool IsSong;
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
    /// <summary>Every track played this run, oldest first; the last one is still playing.</summary>
    public readonly List<TrackStat> Tracks = new();
    public TrackStat? CurrentTrack => Tracks.Count > 0 ? Tracks[^1] : null;
    /// <summary>Bumped whenever a track is finished (the game writes song records then).</summary>
    public int CompletedTrackVersion { get; private set; }
    private int _trackIndexSeen;

    /// <summary>Follows the music's tracks: per-track stats for song records and the run recap.</summary>
    private void UpdateTracks(float dt, AudioState audio)
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
        }
    }

    private void StartTrack(AudioState audio)
    {
        Tracks.Add(new TrackStat
        {
            Title = string.IsNullOrWhiteSpace(audio.TrackTitle) ? "GENERATIVE SCORE" : audio.TrackTitle,
            IsSong = audio.TrackIsSong,
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
}
