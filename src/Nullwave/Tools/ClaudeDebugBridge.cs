#if DEBUG
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Nullwave.Core;
using Nullwave.Render;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace Nullwave.Tools;

/// <summary>
/// DEBUG-only local playtest bridge on http://localhost:5055/ (compiled out of Release).
///   GET  /state       JSON snapshot of the game (screen, player, world, audio, fps, recent exceptions)
///   GET  /screenshot  PNG of the backbuffer, read on the render thread right before SwapBuffers
///   POST /input       {"key":"Space","frames":10} | {"keys":["W","D"],"frames":30}
///                     | {"mouseX":800,"mouseY":450,"click":"left","frames":5}
///                     Injected into InputState, so it works while the window is unfocused.
/// All game access happens on the game thread: state is published as a JSON string each frame,
/// screenshots are requested and fulfilled from OnRender, input is queued and drained in BeginFrame.
/// </summary>
public sealed class ClaudeDebugBridge : IDisposable
{
    public const string Prefix = "http://localhost:5055/";

    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly IWindow _window;
    private volatile string _stateJson = "{\"status\":\"starting\"}";
    private readonly ConcurrentQueue<TaskCompletionSource<(byte[] Rgba, int W, int H)>> _shots = new();
    private readonly ConcurrentQueue<string> _errors = new();
    private long _frame;
    private long _updates;
    private double _uptime;
    private int _publishCountdown;
    private volatile bool _focused = true;

    public ClaudeDebugBridge(IWindow window)
    {
        _window = window;
        _window.FocusChanged += f => _focused = f;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => RecordError("UNHANDLED", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => RecordError("UNOBSERVED TASK", e.Exception);
        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            // swallowed exceptions are bugs too; skip the bridge's own socket noise
            if (e.Exception is HttpListenerException or ObjectDisposedException or OperationCanceledException) return;
            RecordError("FIRST CHANCE", e.Exception);
        };
    }

    // ------------------------------------------------------------------ lifecycle (window callbacks)

    public void Start()
    {
        try
        {
            _listener.Prefixes.Add(Prefix);
            _listener.Start();
            _ = Task.Run(ListenLoop);
            Console.WriteLine($"DEBUGBRIDGE | listening on {Prefix}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DEBUGBRIDGE | failed to start: {ex.Message}");
        }
    }

    /// <summary>Window.Update: counts sim ticks and uptime (the game itself updates inside Render).</summary>
    public void OnUpdate(double dt)
    {
        _updates++;
        _uptime += dt;
    }

    /// <summary>Window.Render, after the frame is drawn and before Silk.NET swaps buffers.</summary>
    public void OnRender(Game game, Renderer renderer)
    {
        _frame++;
        if (--_publishCountdown <= 0)
        {
            _publishCountdown = 6;
            try { _stateJson = BuildState(game, renderer); }
            catch (Exception ex) { RecordError("STATE", ex); }
        }
        while (_shots.TryDequeue(out var tcs))
        {
            try
            {
                var rgba = new byte[renderer.Width * renderer.Height * 4];
                renderer.ReadPixelsInto(rgba);
                tcs.TrySetResult((rgba, renderer.Width, renderer.Height));
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); _listener.Close(); } catch { }
        while (_shots.TryDequeue(out var tcs)) tcs.TrySetCanceled();
    }

    // ------------------------------------------------------------------ HTTP

    private async Task ListenLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        try
        {
            string path = req.Url?.AbsolutePath.TrimEnd('/') ?? "";
            if (req.HttpMethod == "GET" && path == "/state")
            {
                await Send(res, 200, "application/json", Encoding.UTF8.GetBytes(_stateJson));
            }
            else if (req.HttpMethod == "GET" && path == "/screenshot")
            {
                var tcs = new TaskCompletionSource<(byte[] Rgba, int W, int H)>(TaskCreationOptions.RunContinuationsAsynchronously);
                _shots.Enqueue(tcs);
                var done = await Task.WhenAny(tcs.Task, Task.Delay(3000));
                if (done != tcs.Task) { await Send(res, 504, "text/plain", "render loop did not respond (window minimised?)"u8.ToArray()); return; }
                var (rgba, w, h) = await tcs.Task;
                for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;   // UI blending leaves alpha < 1
                await Send(res, 200, "image/png", EncodePng(w, h, rgba));
            }
            else if (req.HttpMethod == "POST" && path == "/input")
            {
                using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
                string body = await reader.ReadToEndAsync();
                string result = QueueInput(body);
                await Send(res, result.StartsWith("ok") ? 200 : 400, "application/json",
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { result })));
            }
            else
            {
                await Send(res, 404, "text/plain", "endpoints: GET /state, GET /screenshot, POST /input"u8.ToArray());
            }
        }
        catch (Exception ex)
        {
            RecordError("BRIDGE", ex);
            try { await Send(res, 500, "text/plain", Encoding.UTF8.GetBytes(ex.Message)); } catch { }
        }
    }

    private static async Task Send(HttpListenerResponse res, int code, string type, byte[] body)
    {
        res.StatusCode = code;
        res.ContentType = type;
        res.ContentLength64 = body.Length;
        await res.OutputStream.WriteAsync(body);
        res.Close();
    }

    // ------------------------------------------------------------------ input

    private static string QueueInput(string body)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = doc.RootElement;
        int frames = root.TryGetProperty("frames", out var f) && f.TryGetInt32(out int fv) ? Math.Clamp(fv, 1, 6000) : 1;
        var keys = new List<Key>();
        if (root.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String)
        {
            if (!ParseKey(k.GetString()!, out var key)) return "unknown key: " + k.GetString();
            keys.Add(key);
        }
        if (root.TryGetProperty("keys", out var ks) && ks.ValueKind == JsonValueKind.Array)
            foreach (var item in ks.EnumerateArray())
            {
                if (!ParseKey(item.GetString() ?? "", out var key)) return "unknown key: " + item.GetString();
                keys.Add(key);
            }
        float? mx = root.TryGetProperty("mouseX", out var x) && x.TryGetSingle(out float xv) ? xv : null;
        float? my = root.TryGetProperty("mouseY", out var y) && y.TryGetSingle(out float yv) ? yv : null;
        // "click": "left" or "click": true
        bool click = root.TryGetProperty("click", out var c) &&
                     (c.ValueKind == JsonValueKind.True ||
                      (c.ValueKind == JsonValueKind.String && c.GetString()!.Equals("left", StringComparison.OrdinalIgnoreCase)));
        bool release = root.TryGetProperty("release", out var rl) && rl.ValueKind == JsonValueKind.True;
        if (keys.Count == 0 && mx == null && my == null && !click && !release) return "nothing to inject";
        InputState.Virtual.Enqueue(new InputState.VirtualCommand(keys.ToArray(), frames, mx, my, click, release));
        return $"ok: {string.Join("+", keys)}{(click ? " +click" : "")}{(mx != null ? $" mouse=({mx},{my})" : "")} for {frames} frames";
    }

    private static bool ParseKey(string name, out Key key)
    {
        name = name.Trim();
        string alias = name.ToLowerInvariant() switch
        {
            "space" => "Space", "enter" or "return" => "Enter", "esc" or "escape" => "Escape",
            "shift" => "ShiftLeft", "up" => "Up", "down" => "Down", "left" => "Left", "right" => "Right",
            "tab" => "Tab", "backspace" => "Backspace",
            _ => name.Length == 1 && char.IsDigit(name[0]) ? "Number" + name : name,
        };
        return Enum.TryParse(alias, true, out key);
    }

    // ------------------------------------------------------------------ state

    private string BuildState(Game game, Renderer renderer)
    {
        var w = game.ActiveWorld;
        var shown = w ?? game.DemoWorld;
        var a = game.Audio.State;
        var p = shown?.Player;
        var errors = _errors.ToArray();
        var state = new
        {
            screen = game.State.ToString(),
            frame = _frame,
            windowUpdates = _updates,
            uptimeSeconds = Math.Round(_uptime, 2),
            fps = Math.Round(game.Fps, 1),
            window = new { width = renderer.Width, height = renderer.Height, mouseWidth = _window.Size.X, mouseHeight = _window.Size.Y, focused = _focused, title = _window.Title },
            world = shown == null ? null : new
            {
                isDemo = w == null,
                phase = shown.Phase.ToString(),
                wave = shown.Wave,
                score = shown.Score,
                flow = Math.Round(shown.Flow, 3),
                hype = Math.Round(shown.Hype, 3),
                enemies = shown.Enemies.Count(e => !e.Dead),
                boss = shown.BossEnemy is { Dead: false } b ? new { hp = Math.Round(b.Hp), maxHp = Math.Round(b.MaxHp) } : null,
                enemyBullets = shown.EnemyBullets.Count,
                playerBullets = shown.PlayerBullets.Count,
                gravityWells = shown.Wells.Count,
                melodyRails = shown.Rails.Count,
                arena = new { width = shown.ArenaW, height = shown.ArenaH },
                camera = new { x = Math.Round(renderer.Camera.X), y = Math.Round(renderer.Camera.Y), zoom = Math.Round(renderer.Zoom, 3) },
                // nearest enemies relative to the ship, and where each is in window (mouse) pixels
                nearestEnemies = shown.Enemies.Where(e => !e.Dead)
                    .OrderBy(e => System.Numerics.Vector2.DistanceSquared(e.Pos, shown.Player.Pos)).Take(8)
                    .Select(e =>
                    {
                        var ui = renderer.WorldToUi(e.Pos);
                        return new
                        {
                            role = e.Role.ToString(), dx = Math.Round(e.Pos.X - shown.Player.Pos.X), dy = Math.Round(e.Pos.Y - shown.Player.Pos.Y),
                            hp = Math.Round(e.Hp), spawning = e.Spawning,
                            mouseX = Math.Round(ui.X / Renderer.DesignWidth * _window.Size.X), mouseY = Math.Round(ui.Y / Renderer.DesignHeight * _window.Size.Y),
                        };
                    }).ToArray(),
            },
            player = p == null ? null : new
            {
                x = Math.Round(p.Pos.X, 1), y = Math.Round(p.Pos.Y, 1),
                vx = Math.Round(p.Vel.X, 1), vy = Math.Round(p.Vel.Y, 1),
                speedFrac = Math.Round(p.SpeedFrac, 3),
                health = p.Health, maxHealth = p.MaxHealth,
                lastHit = shown.LastHitCause, hitsTaken = shown.HitsTaken,
                lastHitDx = (int)(shown.LastHitFrom.X - p.Pos.X), lastHitDy = (int)(shown.LastHitFrom.Y - p.Pos.Y),
                invuln = Math.Round(p.Invuln, 2),
                dashing = p.IsDashing,
                resonance = Math.Round(p.Resonance, 1),
                railRiding = p.RailRiding, railBoost = Math.Round(p.RailBoost, 2), slingBoost = Math.Round(p.SlingBoost, 2),
            },
            audio = new
            {
                source = a.SourceName, external = a.External, buffered = a.AppBuffered,
                bpm = Math.Round(a.Bpm, 1), beatLocked = a.BeatLocked, beat = Math.Round(a.BeatPosition, 2),
                energy = Math.Round(a.Energy, 2), section = a.Section, sectionLabel = a.SectionLabel,
                chordRoot = a.ChordRoot, chordMinor = a.ChordMinor, key = a.Key,
                melodyPitch = Math.Round(a.MelodyPitch, 1), melodyConfidence = Math.Round(a.MelodyConfidence, 2),
            },
            input = InputState.DebugLast,
            backdrop = renderer.BackdropName,
            recentExceptions = errors,
        };
        return JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
    }

    private void RecordError(string kind, Exception? ex)
    {
        if (ex == null) return;
        string top = ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "";
        _errors.Enqueue($"{DateTime.Now:HH:mm:ss} {kind} {ex.GetType().Name}: {ex.Message} {top}");
        while (_errors.Count > 30) _errors.TryDequeue(out _);
    }

    // ------------------------------------------------------------------ PNG (in memory; the project's PngWriter only writes files)

    private static byte[] EncodePng(int width, int height, byte[] rgba)
    {
        using var ms = new MemoryStream();
        ms.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var ihdr = new byte[13];
        BE(ihdr, 0, (uint)width); BE(ihdr, 4, (uint)height);
        ihdr[8] = 8; ihdr[9] = 6;
        Chunk(ms, "IHDR", ihdr);
        using var raw = new MemoryStream(height * (width * 4 + 1));
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgba, (height - 1 - y) * width * 4, width * 4);   // GL rows are bottom-up
        }
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Fastest, true)) { raw.Position = 0; raw.CopyTo(zs); }
        Chunk(ms, "IDAT", z.ToArray());
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, (uint)data.Length); s.Write(len);
        var t = Encoding.ASCII.GetBytes(type); s.Write(t); s.Write(data);
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in t) crc = Crc(crc, b);
        foreach (byte b in data) crc = Crc(crc, b);
        var cb = new byte[4]; BE(cb, 0, crc ^ 0xFFFFFFFFu); s.Write(cb);
    }

    private static uint Crc(uint crc, byte b)
    {
        crc ^= b;
        for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        return crc;
    }

    private static void BE(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
}
#endif
