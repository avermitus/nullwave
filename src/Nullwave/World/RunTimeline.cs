using Nullwave.Audio;

namespace Nullwave.World;

/// <summary>Twice a second during a run: what the recap strip is drawn from.</summary>
public readonly record struct TimelineSample(float Time, long Score, int Kills, int Section, float Energy, float Flow, int Health);

/// <summary>
/// The run recap: score, kills, song sections and energy sampled every half second, plus hits,
/// drops and track starts, and the best moment (the 6-second window that earned the most score).
/// </summary>
public sealed class RunTimeline
{
    public const float SampleInterval = 0.5f;
    private const int MaxSamples = 7200;   // an hour
    private const int MomentSamples = 12;  // 6 s

    public readonly List<TimelineSample> Samples = new();
    public readonly List<float> Hits = new();
    public readonly List<float> Drops = new();
    public readonly List<(float Time, string Title)> TrackStarts = new();
    public float BestMomentTime { get; private set; } = -1f;
    public long BestMomentGain { get; private set; }
    /// <summary>Bumped when a new best moment is found (the clip recorder saves around it).</summary>
    public int BestMomentVersion { get; private set; }
    public float Duration { get; private set; }
    private float _acc;
    private int _trackCount;

    public void Sample(World w, AudioState audio, float dt)
    {
        if (w.Tracks.Count != _trackCount && w.CurrentTrack != null)
        {
            _trackCount = w.Tracks.Count;
            TrackStarts.Add((w.CurrentTrack.StartTime, w.CurrentTrack.Title));
        }
        _acc += dt;
        if (_acc < SampleInterval) return;
        _acc -= SampleInterval;
        if (Samples.Count >= MaxSamples) return;
        Samples.Add(new TimelineSample(w.SessionTime, w.Score, w.Kills, audio.SectionLabel, audio.Energy, w.Flow, w.Player.Health));
        Duration = w.SessionTime;
        if (Samples.Count > MomentSamples)
        {
            long gain = Samples[^1].Score - Samples[^(MomentSamples + 1)].Score;
            if (gain > BestMomentGain && gain > 0)
            {
                BestMomentGain = gain;
                BestMomentTime = Samples[^(MomentSamples / 2)].Time;
                BestMomentVersion++;
            }
        }
    }

    public void AddHit(float time) => Hits.Add(time);

    public void AddDrop(float time)
    {
        if (Drops.Count == 0 || time - Drops[^1] > 4f) Drops.Add(time);
    }

    public void Finish(float time, long score) => Duration = MathF.Max(Duration, time);
}

public sealed partial class World
{
    /// <summary>Recap data for this run.</summary>
    public readonly RunTimeline Timeline = new();
}
