using System.Diagnostics;

namespace Nullwave.Audio;

public enum SpotifyPlaybackState
{
    NotRunning,
    Idle,
    Playing,
    Ad,
}

/// <summary>Parsed local Spotify now-playing metadata (zero external dependencies).</summary>
public readonly record struct SpotifyTrackInfo(SpotifyPlaybackState State, string Artist, string Title, string Source, bool IsWebPlayer)
{
    public static readonly SpotifyTrackInfo NotRunning = new(SpotifyPlaybackState.NotRunning, "", "", "", false);

    public bool IsPlaying => State == SpotifyPlaybackState.Playing;

    public string DisplayTrack =>
        !IsPlaying ? "" :
        string.IsNullOrEmpty(Artist) ? Title :
        Artist + " - " + Title;
}

/// <summary>
/// Polls local Spotify desktop and browser Web Player window titles to surface the active track
/// without requiring OAuth credentials or third-party packages.
/// </summary>
public sealed class SpotifyBridge
{
    private static readonly string[] BrowserProcesses = ["chrome", "msedge", "firefox", "brave", "opera"];
    private static readonly string[] BrowserSuffixes =
    [
        " - Google Chrome",
        " - Microsoft Edge",
        " — Mozilla Firefox",
        " - Mozilla Firefox",
        " - Brave",
        " - Opera",
        " - Vivaldi",
    ];
    private static readonly string[] TrackSeparators = [" - ", " – ", " — "];

    private const float PollIntervalSeconds = 1.25f;

    private float _pollTimer;
    private SpotifyTrackInfo _polled = SpotifyTrackInfo.NotRunning;
    private SpotifyTrackInfo? _override;

    public SpotifyTrackInfo Current => _override ?? _polled;

    public void SetOverrideForTest(SpotifyTrackInfo? info) => _override = info;

    public void Update(float dt)
    {
        if (_override.HasValue) return;
        _pollTimer -= dt;
        if (_pollTimer > 0f) return;
        _pollTimer = PollIntervalSeconds;
        _polled = Poll();
    }

    public static SpotifyTrackInfo Poll()
    {
        SpotifyTrackInfo best = SpotifyTrackInfo.NotRunning;
        try
        {
            var procs = Process.GetProcessesByName("Spotify");
            try
            {
                foreach (var proc in procs)
                {
                    string title = SafeTitle(proc);
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    var parsed = ParseWindowTitle(title, isBrowserProcess: false);
                    if (parsed.IsPlaying) return parsed;
                    if (parsed.State != SpotifyPlaybackState.NotRunning && best.State == SpotifyPlaybackState.NotRunning)
                        best = parsed;
                }
            }
            finally
            {
                foreach (var proc in procs) proc.Dispose();
            }

            if (best.IsPlaying) return best;

            foreach (var name in BrowserProcesses)
            {
                var bprocs = Process.GetProcessesByName(name);
                try
                {
                    foreach (var proc in bprocs)
                    {
                        string title = SafeTitle(proc);
                        if (string.IsNullOrWhiteSpace(title)) continue;
                        var parsed = ParseWindowTitle(title, isBrowserProcess: true);
                        if (parsed.IsPlaying) return parsed;
                        if (parsed.State != SpotifyPlaybackState.NotRunning && best.State == SpotifyPlaybackState.NotRunning)
                            best = parsed;
                    }
                }
                finally
                {
                    foreach (var proc in bprocs) proc.Dispose();
                }
            }
        }
        catch
        {
            // Process enumeration can race process exit or deny access on protected handles.
        }
        return best;
    }

    private static string SafeTitle(Process proc)
    {
        try { return proc.MainWindowTitle ?? ""; }
        catch { return ""; }
    }

    public static SpotifyTrackInfo ParseWindowTitle(string? rawTitle, bool isBrowserProcess = false)
    {
        string text = (rawTitle ?? "").Trim();
        if (isBrowserProcess)
        {
            if (text.Length == 0) return SpotifyTrackInfo.NotRunning;
            foreach (var suffix in BrowserSuffixes)
            {
                if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    text = text[..^suffix.Length].Trim();
                    break;
                }
            }

            if (IsIdleSpotifyTitle(text))
                return new SpotifyTrackInfo(SpotifyPlaybackState.Idle, "", "", "SPOTIFY WEB", true);

            bool hasMarker = false;
            foreach (var marker in new[] { " - Spotify", " – Spotify", " — Spotify", " | Spotify" })
            {
                if (text.EndsWith(marker, StringComparison.OrdinalIgnoreCase))
                {
                    text = text[..^marker.Length].Trim();
                    hasMarker = true;
                    break;
                }
            }
            if (!hasMarker)
            {
                foreach (var prefix in new[] { "Spotify - ", "Spotify – ", "Spotify | " })
                {
                    if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        text = text[prefix.Length..].Trim();
                        hasMarker = true;
                        break;
                    }
                }
            }
            if (!hasMarker && text.Contains(" • ", StringComparison.Ordinal) && text.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Replace("Spotify", "", StringComparison.OrdinalIgnoreCase).Trim(' ', '-', '–', '|');
                hasMarker = true;
            }
            if (!hasMarker) return SpotifyTrackInfo.NotRunning;
            if (text.Length == 0 || IsIdleSpotifyTitle(text))
                return new SpotifyTrackInfo(SpotifyPlaybackState.Idle, "", "", "SPOTIFY WEB", true);
            if (text.Equals("Advertisement", StringComparison.OrdinalIgnoreCase))
                return new SpotifyTrackInfo(SpotifyPlaybackState.Ad, "", "", "SPOTIFY WEB", true);

            int bullet = text.IndexOf(" • ", StringComparison.Ordinal);
            if (bullet > 0)
            {
                string titlePart = text[..bullet].Trim();
                string artistPart = text[(bullet + 3)..].Trim();
                if (titlePart.Length > 0 && artistPart.Length > 0)
                    return new SpotifyTrackInfo(SpotifyPlaybackState.Playing, artistPart, titlePart, "SPOTIFY WEB", true);
            }

            if (TrySplitArtistTitle(text, out string webArtist, out string webTitle))
                return new SpotifyTrackInfo(SpotifyPlaybackState.Playing, webArtist, webTitle, "SPOTIFY WEB", true);

            return new SpotifyTrackInfo(SpotifyPlaybackState.Playing, "", text, "SPOTIFY WEB", true);
        }

        if (text.Length == 0 || IsIdleSpotifyTitle(text))
            return new SpotifyTrackInfo(SpotifyPlaybackState.Idle, "", "", "SPOTIFY", false);
        if (text.Equals("Advertisement", StringComparison.OrdinalIgnoreCase))
            return new SpotifyTrackInfo(SpotifyPlaybackState.Ad, "", "", "SPOTIFY", false);
        if (TrySplitArtistTitle(text, out string artist, out string song))
            return new SpotifyTrackInfo(SpotifyPlaybackState.Playing, artist, song, "SPOTIFY", false);
        return new SpotifyTrackInfo(SpotifyPlaybackState.Playing, "", text, "SPOTIFY", false);
    }

    private static bool IsIdleSpotifyTitle(string text)
    {
        if (text.Equals("Spotify", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Spotify Free", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Spotify Premium", StringComparison.OrdinalIgnoreCase))
            return true;
        return text.StartsWith("Spotify – Web Player", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Spotify - Web Player", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TrySplitArtistTitle(string text, out string artist, out string title)
    {
        foreach (var sep in TrackSeparators)
        {
            int idx = text.IndexOf(sep, StringComparison.Ordinal);
            if (idx > 0 && idx + sep.Length < text.Length)
            {
                artist = text[..idx].Trim();
                title = text[(idx + sep.Length)..].Trim();
                if (artist.Length > 0 && title.Length > 0) return true;
            }
        }
        artist = "";
        title = "";
        return false;
    }
}
