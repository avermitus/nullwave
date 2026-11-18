namespace Nullwave.Core;

/// <summary>
/// Player-tunable settings, saved inside save.json. Percent values are stored as 0..N ints so
/// the menu can step them exactly. <see cref="Current"/> is read by the renderer, HUD and sim.
/// </summary>
public sealed class GameSettings
{
    public static GameSettings Current { get; set; } = new();

    // audio
    public int MasterVolume { get; set; } = 80;
    public int MusicVolume { get; set; } = 70;
    public int SfxVolume { get; set; } = 80;
    /// <summary>Shifts the beat grid against the music to compensate output/capture latency.</summary>
    public int BeatOffsetMs { get; set; }
    /// <summary>How hard bullets and enemies surge forward on each beat (0 = constant speed).</summary>
    public int BeatMotion { get; set; } = 100;

    // video
    public bool Fullscreen { get; set; }
    public int BackgroundIntensity { get; set; } = 100;
    public int BackgroundMotion { get; set; } = 100;
    public int Bloom { get; set; } = 100;
    public int ScreenShake { get; set; } = 100;
    public int Aberration { get; set; } = 100;
    public bool ShowFps { get; set; }
    /// <summary>Strength of music-driven grading, kick thump, camera punch and drop effects.</summary>
    public int MusicFx { get; set; } = 100;
    /// <summary>Caps full-screen flashes and glitches (photosensitivity).</summary>
    public bool ReduceFlashing { get; set; }

    // interface
    public bool Minimap { get; set; } = true;
    public bool ThreatArrows { get; set; } = true;
    public bool EnemyHealthBars { get; set; } = true;
    public bool BeatTelegraphs { get; set; } = true;
    public int CrosshairSize { get; set; } = 100;

    [System.Text.Json.Serialization.JsonIgnore] public float Master => MasterVolume / 100f;
    [System.Text.Json.Serialization.JsonIgnore] public float Music => MusicVolume / 100f * Master;
    [System.Text.Json.Serialization.JsonIgnore] public float Sfx => SfxVolume / 100f * Master;

    public GameSettings Clone() => (GameSettings)MemberwiseClone();

    /// <summary>Clamps values loaded from disk into their menu ranges.</summary>
    public void Sanitize()
    {
        MasterVolume = Math.Clamp(MasterVolume, 0, 100);
        MusicVolume = Math.Clamp(MusicVolume, 0, 100);
        SfxVolume = Math.Clamp(SfxVolume, 0, 100);
        BeatOffsetMs = Math.Clamp(BeatOffsetMs, -200, 200);
        BeatMotion = Math.Clamp(BeatMotion, 0, 150);
        BackgroundIntensity = Math.Clamp(BackgroundIntensity, 0, 150);
        BackgroundMotion = Math.Clamp(BackgroundMotion, 0, 200);
        Bloom = Math.Clamp(Bloom, 0, 150);
        ScreenShake = Math.Clamp(ScreenShake, 0, 150);
        Aberration = Math.Clamp(Aberration, 0, 100);
        CrosshairSize = Math.Clamp(CrosshairSize, 50, 200);
        MusicFx = Math.Clamp(MusicFx, 0, 150);
    }
}
