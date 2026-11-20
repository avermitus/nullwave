using System.Diagnostics;

namespace Nullwave.Render;

/// <summary>
/// Keeps the last few seconds of play as small frames (426x240 at 20 fps) so the run's best
/// moment can be saved as an MP4 from the game-over screen. Frames come from a GPU blit to a small
/// framebuffer, so the cost is a tiny readback; encoding uses ffmpeg if it is installed.
/// </summary>
public sealed class ClipRecorder
{
    public const int Width = 426;
    public const int Height = 240;
    public const int Fps = 20;
    public const float Seconds = 6f;
    private static readonly int FrameBytes = Width * Height * 3;
    private readonly int _capacity = (int)(Fps * Seconds);
    private readonly byte[][] _ring;
    private int _head;
    private int _count;
    private float _acc;
    private List<byte[]>? _best;

    public ClipRecorder()
    {
        _ring = new byte[_capacity][];
    }

    public bool HasClip => _best is { Count: > 0 };
    public int BufferedFrames => _count;

    public void Reset()
    {
        _count = 0;
        _head = 0;
        _acc = 0f;
        _best = null;
    }

    /// <summary>Call once per rendered frame after the frame is complete.</summary>
    public void Tick(float dt, Renderer renderer)
    {
        _acc += dt;
        if (_acc < 1f / Fps) return;
        _acc = MathF.Min(_acc - 1f / Fps, 1f / Fps);
        var frame = _ring[_head] ??= new byte[FrameBytes];
        if (!renderer.BlitSmall(frame, Width, Height)) return;
        _head = (_head + 1) % _capacity;
        _count = Math.Min(_count + 1, _capacity);
    }

    /// <summary>Keeps what is buffered right now as the run's best clip (oldest first).</summary>
    public void Snapshot()
    {
        if (_count == 0) return;
        var clip = new List<byte[]>(_count);
        int start = (_head - _count + _capacity) % _capacity;
        for (int i = 0; i < _count; i++)
        {
            var src = _ring[(start + i) % _capacity];
            var copy = new byte[FrameBytes];
            Buffer.BlockCopy(src, 0, copy, 0, FrameBytes);
            clip.Add(copy);
        }
        _best = clip;
    }

    /// <summary>Encodes the best clip to MP4 with ffmpeg (or a PNG still if ffmpeg is missing). Returns a status line.</summary>
    public Task<string> SaveAsync(string directory)
    {
        var clip = _best;
        if (clip == null || clip.Count == 0) return Task.FromResult("NO CLIP YET // PLAY A LITTLE LONGER");
        return Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string mp4 = Path.Combine(directory, "nullwave_" + stamp + ".mp4");
                var psi = new ProcessStartInfo("ffmpeg")
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                foreach (string a in new[] { "-y", "-v", "error", "-f", "rawvideo", "-pixel_format", "rgb24", "-video_size", $"{Width}x{Height}",
                             "-framerate", Fps.ToString(), "-i", "pipe:0", "-vf", "vflip,scale=852:480:flags=lanczos", "-c:v", "libx264",
                             "-pix_fmt", "yuv420p", "-crf", "20", "-movflags", "+faststart", mp4 })
                    psi.ArgumentList.Add(a);
                Process? p;
                try { p = Process.Start(psi); }
                catch { p = null; }
                if (p == null)
                {
                    string png = Path.Combine(directory, "nullwave_" + stamp + ".png");
                    var mid = clip[clip.Count / 2];
                    var rgba = new byte[Width * Height * 4];
                    for (int i = 0, j = 0; i < mid.Length; i += 3, j += 4) { rgba[j] = mid[i]; rgba[j + 1] = mid[i + 1]; rgba[j + 2] = mid[i + 2]; rgba[j + 3] = 255; }
                    Util.PngWriter.Write(png, Width, Height, rgba);
                    return "FFMPEG NOT FOUND // SAVED A STILL: " + png.ToUpperInvariant();
                }
                using (var stdin = p.StandardInput.BaseStream)
                    foreach (var f in clip) stdin.Write(f, 0, f.Length);
                p.WaitForExit(30000);
                return p.ExitCode == 0 ? "CLIP SAVED // " + mp4.ToUpperInvariant() : "CLIP FAILED // FFMPEG EXIT " + p.ExitCode;
            }
            catch (Exception ex)
            {
                return "CLIP FAILED // " + ex.Message.ToUpperInvariant();
            }
        });
    }
}
