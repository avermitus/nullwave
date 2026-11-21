namespace Nullwave.Core;

public enum VisualTheme
{
    Standard = 0,
    Abyssal = 1,
}

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
    /// <summary>Delay Spotify 0.75 s through the game so visuals and enemies see the music coming.</summary>
    public bool BufferSpotify { get; set; } = true;
    /// <summary>Kills and hits play notes in the song's key.</summary>
    public bool InKeySounds { get; set; } = true;
    /// <summary>Gamepad rumble on the kick and bass (percent).</summary>
    public int Rumble { get; set; } = 80;
    /// <summary>Keep the last seconds of play so the best moment can be saved as a clip.</summary>
    public bool HighlightClips { get; set; } = true;
    /// <summary>First-run tutorial and one-time tips.</summary>
    public bool TutorialTips { get; set; } = true;
    /// <summary>EDM camera effects (roll on drops, zoom punch, letterbox on build-ups), percent.</summary>
    public int CameraFx { get; set; } = 100;

    // video
    public bool Fullscreen { get; set; }
    /// <summary>Visual aesthetic: Standard neon synthwave or Abyssal OLED-black void noir.</summary>
    public VisualTheme Theme { get; set; } = VisualTheme.Standard;
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
    public bool ShowAttunements { get; set; } = true;
    public int CrosshairSize { get; set; } = 100;

    [System.Text.Json.Serialization.JsonIgnore] public bool IsAbyssal => Theme == VisualTheme.Abyssal;
    [System.Text.Json.Serialization.JsonIgnore] public float Master => MasterVolume / 100f;
    [System.Text.Json.Serialization.JsonIgnore] public float Music => MusicVolume / 100f * Master;
    [System.Text.Json.Serialization.JsonIgnore] public float Sfx => SfxVolume / 100f * Master;

    public static bool TryParseTheme(string? raw, out VisualTheme theme)
    {
        theme = VisualTheme.Standard;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        switch (raw.Trim().ToLowerInvariant())
        {
            case "standard":
            case "default":
            case "0":
                theme = VisualTheme.Standard;
                return true;
            case "abyssal":
            case "dark":
            case "noir":
            case "void":
            case "1":
                theme = VisualTheme.Abyssal;
                return true;
            default:
                return false;
        }
    }

    public GameSettings Clone() => (GameSettings)MemberwiseClone();

    /// <summary>Clamps values loaded from disk into their menu ranges.</summary>
    public void Sanitize()
    {
        if (!Enum.IsDefined(Theme)) Theme = VisualTheme.Standard;
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
