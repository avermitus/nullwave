using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;
using Nullwave.Util;
using Silk.NET.OpenGL;

namespace Nullwave.Render;

public sealed class Renderer : IDisposable
{
    public const int DesignWidth = 1600;
    public const int DesignHeight = 900;

    private readonly GL _gl;
    private readonly Shader _worldShader;
    private readonly Shader _uiShader;
    private readonly Shader _background;
    private readonly Batch _worldAlpha;
    private readonly Batch _worldGlow;
    private readonly Batch _uiAlpha;
    private readonly Batch _uiGlow;
    private readonly Shader _meshShader;
    private readonly MeshBatch _backdropMeshes;
    private readonly MeshBatch _worldMeshes;
    private Matrix4x4 _backdropViewProj = Matrix4x4.Identity;
    private Vector3 _backdropEye = new(0, 0, 1000);
    private Vector3 _fogColor = new(0.01f, 0.012f, 0.03f);

    /// <summary>Screen-space lift per unit of height for the world mesh pass (pseudo 3/4 view).</summary>
    public const float Oblique = 0.42f;
    private readonly uint _fullscreenVao;
    private readonly uint _emptyVao;
    private GlFbo _sceneA;
    private GlFbo _sceneB;
    private bool _useA = true;
    private Vector2 _camera;
    private Vector2 _view;
    private float _zoom = 1f;
    private float _flow;
    private float _lastRenderTime = -1f;
    public PostFx Post { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public Palette Palette { get; set; } = Palette.FromSeed(1);
    public Vector2 Camera => _camera;
    public Vector2 ViewExtents => _view;
    public float Zoom => _zoom;
    public MeshBatch Backdrop => _backdropMeshes;
    public MeshBatch Meshes => _worldMeshes;
    public float BackdropBrightness { get; set; } = 0.5f;
    /// <summary>Song-structure drivers (set by the game each frame); null = neutral.</summary>
    public MusicDirector? Mood { get; set; }
    /// <summary>Player speed 0..1 and screen-space direction, so the backdrop rushes with the ship.</summary>
    public float PlayerSpeed { get; set; }
    public Vector2 PlayerDirection { get; set; }
    /// <summary>Flow state 0..1: pushes speed lines and backdrop rush.</summary>
    public float Flow { get; set; }
    /// <summary>Performance hype 0..1 (set by the game): scales every music-driven effect.</summary>
    public float Hype { get; set; } = 0.5f;
    /// <summary>Effect multiplier from hype: 0.55x when struggling, up to 1.45x when on fire.</summary>
    public float HypeScale => 0.55f + Hype * 0.9f;
    public BackdropGenome Backdrop0 => _genA;
    private BackdropGenome _genA = BackdropGenome.Random(new Random());
    private BackdropGenome? _genB;
    private float _mix;
    private float _travel;
    private readonly Random _genRng = new();

    /// <summary>Starts a transition to a brand new random backdrop (ignored while one is running).</summary>
    public void RerollBackdrop()
    {
        if (_genB != null) return;
        _genB = BackdropGenome.Random(_genRng, _genA.Style);
        _mix = 0f;
    }

    /// <summary>Pins a specific style (capture/debug).</summary>
    public void ForceBackdrop(int style, int seed)
    {
        var rng = new Random(seed);
        BackdropGenome g;
        do g = BackdropGenome.Random(rng); while (g.Style != style);
        _genA = g;
        _genB = null;
        _mix = 0f;
    }

    /// <summary>Replaces the backdrop immediately (new run).</summary>
    public void ResetBackdrop()
    {
        _genA = BackdropGenome.Random(_genRng, _genA.Style);
        _genB = null;
        _mix = 0f;
    }

    private void SetGenome(string suffix, BackdropGenome g)
    {
        _background.SetInt("uStyle" + suffix, g.Style);
        _background.SetVec4("uParam" + suffix, g.Params);
        _background.SetVec4("uMut" + suffix, g.Mutators);
        _background.SetVec3("uPalOff" + suffix, g.PaletteOffset);
        _background.SetVec3("uPalFreq" + suffix, g.PaletteFrequency);
    }

    public Renderer(GL gl, int width, int height)
    {
        _gl = gl;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        _worldShader = new Shader(_gl, Shaders.ColorVert, Shaders.ColorFrag, "world");
        _uiShader = new Shader(_gl, Shaders.UiVert, Shaders.ColorFrag, "ui");
        _background = new Shader(_gl, Shaders.FullscreenVert, Shaders.BackgroundFrag, "background");
        _worldAlpha = new Batch(_gl, 131072);
        _worldGlow = new Batch(_gl, 131072);
        _uiAlpha = new Batch(_gl, 65536);
        _uiGlow = new Batch(_gl, 32768);
        _meshShader = new Shader(_gl, Shaders.MeshVert, Shaders.MeshFrag, "mesh");
        _backdropMeshes = new MeshBatch(_gl, 196608);
        _worldMeshes = new MeshBatch(_gl, 196608);
        _fullscreenVao = _gl.GenVertexArray();
        _emptyVao = _gl.GenVertexArray();
        Post = new PostFx(_gl);
        _sceneA = new GlFbo(_gl, Width, Height, true, true);
        _sceneB = new GlFbo(_gl, Width, Height, true, true);
        _gl.Enable(EnableCap.Blend);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        BlendMode.Alpha.Apply(_gl);
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height) return;
        Width = width;
        Height = height;
        _sceneA.Dispose();
        _sceneB.Dispose();
        _sceneA = new GlFbo(_gl, Width, Height, true, true);
        _sceneB = new GlFbo(_gl, Width, Height, true, true);
        Post.EnsureSize(width, height);
    }

    public void BeginWorld(Vector2 camera, float zoom, float viewHeight = 900f)
    {
        _camera = camera;
        _zoom = Math.Max(0.2f, zoom);
        float halfH = viewHeight * 0.5f / _zoom;
        float halfW = halfH * Width / Math.Max(1f, Height);
        _view = new Vector2(halfW, halfH);
        _worldAlpha.Clear();
        _worldGlow.Clear();
        _worldMeshes.Clear();
        _backdropMeshes.Clear();
    }

    /// <summary>Perspective camera for the background 3D layer (its own space, z up).</summary>
    public void SetBackdropCamera(Vector3 eye, Vector3 target, float fovDegrees, Vector3 fogColor)
    {
        _backdropEye = eye;
        _fogColor = fogColor;
        float aspect = Width / (float)Math.Max(1, Height);
        var view = Matrix4x4.CreateLookAt(eye, target, new Vector3(0, -1, 0));
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(fovDegrees * MathF.PI / 180f, aspect, 20f, 12000f);
        // Game space has y pointing down the screen, so mirror x to keep the backdrop un-flipped.
        _backdropViewProj = view * proj * Matrix4x4.CreateScale(-1f, 1f, 1f);
    }

    /// <summary>Orthographic oblique projection matching the 2D world camera exactly at z = 0.</summary>
    private Matrix4x4 WorldMeshMatrix()
    {
        float ix = 1f / MathF.Max(1f, _view.X);
        float iy = 1f / MathF.Max(1f, _view.Y);
        return new Matrix4x4(
            ix, 0, 0, 0,
            0, -iy, 0, 0,
            0, Oblique * iy, -1f / 4000f, 0,
            -_camera.X * ix, _camera.Y * iy, 0, 1);
    }

    public void BeginUi()
    {
        _uiAlpha.Clear();
        _uiGlow.Clear();
    }

    public Vector2 ScreenToWorld(Vector2 mouse, Vector2 windowSize)
    {
        float nx = windowSize.X <= 0 ? 0.5f : mouse.X / windowSize.X;
        float ny = windowSize.Y <= 0 ? 0.5f : mouse.Y / windowSize.Y;
        return _camera + new Vector2((nx - 0.5f) * 2f * _view.X, (ny - 0.5f) * 2f * _view.Y);
    }

    public Vector2 WorldToUi(Vector2 world)
    {
        return new Vector2(
            DesignWidth * 0.5f + (world.X - _camera.X) / MathF.Max(0.001f, _view.X * 2f) * DesignWidth,
            DesignHeight * 0.5f + (world.Y - _camera.Y) / MathF.Max(0.001f, _view.Y * 2f) * DesignHeight);
    }

    public bool InView(Vector2 world, float margin = 140f)
    {
        return MathF.Abs(world.X - _camera.X) < _view.X + margin && MathF.Abs(world.Y - _camera.Y) < _view.Y + margin;
    }

    public void Render(AudioState audio, float time, float flash, float aberration, int damage)
    {
        var scene = _useA ? _sceneA : _sceneB;
        scene.Bind();
        _gl.Disable(EnableCap.Blend);

        // "Flow time" runs faster when the music is louder, so the liquid surges with the track
        // instead of jumping: every motion in the backdrop is driven by this smooth clock.
        float frameDt = _lastRenderTime < 0 ? 1f / 60f : Math.Clamp(time - _lastRenderTime, 0f, 0.1f);
        _lastRenderTime = time;
        var settings = GameSettings.Current;
        var mood = Mood;
        float fx = settings.MusicFx / 100f * HypeScale;
        float drop = (mood?.DropPulse ?? 0f) * fx;
        float breakdown = (mood?.Breakdown ?? 0f) * fx;
        float build = (mood?.Build ?? 0f) * fx;
        // breakdowns slow the liquid right down, drops make it surge
        _flow += frameDt * (0.35f + audio.Energy * 1.6f + audio.BeatPulse * 0.9f + drop * 4f) * (1f - breakdown * 0.65f) * settings.BackgroundMotion / 100f;

        _background.Use();
        _background.SetVec2("uResolution", Width, Height);
        _background.SetVec2("uCam", _camera);
        _background.SetFloat("uTime", time);
        _background.SetFloat("uFlow", _flow);
        _background.SetFloat("uIntensity", settings.BackgroundIntensity / 100f * (1f - breakdown * 0.35f + (mood?.DropGlow ?? 0f) * fx * 0.25f));
        _background.SetFloat("uDrop", drop);
        _background.SetFloat("uBuild", build);
        _background.SetFloat("uKick", (mood?.Kick ?? audio.BeatPulse) * fx);
        _background.SetFloat("uBass", audio.Bass);
        _background.SetFloat("uLowMid", audio.LowMid);
        _background.SetFloat("uMid", audio.Mid);
        _background.SetFloat("uHigh", audio.High);
        _background.SetFloat("uEnergy", audio.Energy);
        _background.SetFloat("uBeat", audio.BeatPulse);
        _background.SetFloat("uDown", audio.DownPulse);
        _background.SetFloat("uSeed", Palette.BaseHue * 31.7f);
        _background.SetVec3("uColA", Palette.BackA);
        _background.SetVec3("uColB", Palette.BackB);
        _background.SetVec3("uColC", Palette.BackC);
        _background.SetVec3("uPrim", new Vector3(Palette.Primary.X, Palette.Primary.Y, Palette.Primary.Z));
        _background.SetVec3("uSec", new Vector3(Palette.Secondary.X, Palette.Secondary.Y, Palette.Secondary.Z));
        // travel: how far the ship has "flown" - drives tunnels and fields rushing past
        _travel += frameDt * (0.15f + PlayerSpeed * 1.4f + Flow * 0.8f + drop * 2f) * settings.BackgroundMotion / 100f;
        _background.SetFloat("uTravel", _travel);
        _background.SetFloat("uSpeed", PlayerSpeed);
        _background.SetVec2("uVel", PlayerDirection.X, -PlayerDirection.Y);
        if (_genB != null)
        {
            _mix += frameDt / 1.8f;
            if (_mix >= 1f) { _genA = _genB; _genB = null; _mix = 0f; }
        }
        SetGenome("A", _genA);
        SetGenome("B", _genB ?? _genA);
        _background.SetFloat("uMix", _genB != null ? _mix : 0f);
        _gl.BindVertexArray(_fullscreenVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        if (_backdropMeshes.Count > 0)
        {
            _gl.Clear(ClearBufferMask.DepthBufferBit);
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Less);
            _meshShader.Use();
            _meshShader.SetMatrix("uViewProj", _backdropViewProj);
            _meshShader.SetInt("uPerspective", 1);
            _meshShader.SetVec3("uEye", _backdropEye);
            _meshShader.SetVec3("uLightDir", new Vector3(-0.35f, -0.55f, 0.75f));
            _meshShader.SetVec3("uViewDir", new Vector3(0, 0.3f, 1f));
            _meshShader.SetVec3("uRimColor", new Vector3(Palette.Primary.X, Palette.Primary.Y, Palette.Primary.Z));
            _meshShader.SetVec3("uFill", Palette.BackB);
            _meshShader.SetFloat("uRim", 0.18f + audio.BeatPulse * 0.25f);
            _meshShader.SetFloat("uBrightness", BackdropBrightness);
            _meshShader.SetFloat("uGlowScale", 0.5f + audio.Energy * 0.3f);
            _meshShader.SetVec3("uFogColor", _fogColor);
            _meshShader.SetFloat("uFogNear", 1800f);
            _meshShader.SetFloat("uFogFar", 6200f);
            _backdropMeshes.Draw();
            _gl.Disable(EnableCap.DepthTest);
        }

        _worldShader.Use();
        _worldShader.SetVec2("uCam", _camera);
        _worldShader.SetVec2("uView", _view);
        _worldAlpha.Draw();

        if (_worldMeshes.Count > 0)
        {
            _gl.Clear(ClearBufferMask.DepthBufferBit);
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Less);
            _meshShader.Use();
            _meshShader.SetMatrix("uViewProj", WorldMeshMatrix());
            _meshShader.SetInt("uPerspective", 0);
            _meshShader.SetVec3("uLightDir", new Vector3(-0.45f, -0.6f, 0.85f));
            _meshShader.SetVec3("uViewDir", new Vector3(0, Oblique, 1f));
            _meshShader.SetVec3("uRimColor", new Vector3(Palette.Accent.X, Palette.Accent.Y, Palette.Accent.Z) * 0.8f);
            _meshShader.SetVec3("uFill", Palette.BackB * 1.5f);
            _meshShader.SetFloat("uRim", 0.42f + audio.BeatPulse * 0.25f);
            _meshShader.SetFloat("uBrightness", 1f);
            _meshShader.SetFloat("uGlowScale", 1f);
            _worldMeshes.Draw();
            _gl.Disable(EnableCap.DepthTest);
            _worldShader.Use();
        }

        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
        _worldGlow.Draw();

        Post.Process(scene.Texture, Width, Height, time, (1.18f + audio.Energy * 0.32f + drop * 0.8f) * settings.Bloom / 100f,
            (aberration + build * 0.5f + drop * 0.6f) * settings.Aberration / 100f,
            settings.ReduceFlashing ? MathF.Min(flash, 0.15f) : flash, audio.BeatPulse, damage, WithSpeed(BuildPostMood(mood, fx, settings.ReduceFlashing)));

        // Post.Process leaves RenderBaseline.Default bound to the default framebuffer. The composite is
        // opaque (alpha 1); the UI blends colour normally but must not pull destination alpha below 1,
        // or screenshots/recorder frames read back translucent.
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
        _gl.Enable(EnableCap.Blend);
        _uiShader.Use();
        _uiShader.SetVec2("uSize", DesignWidth, DesignHeight);
        BlendMode.Alpha.Apply(_gl);
        _uiAlpha.Draw();
        BlendMode.Additive.Apply(_gl);
        _uiGlow.Draw();
        // exit in the same baseline PostFx guarantees, so a second Render() in one frame starts clean
        PostFx.RestoreBaseline(_gl);

        _useA = !_useA;
    }

    /// <summary>Live GL state check after <see cref="Render"/> (smoke/tools only; glGet stalls the pipeline).</summary>
    public bool VerifyBaselineState(out string detail) => PostFx.VerifyBaseline(_gl, out detail);

    /// <summary>
    /// Maps the song's state to grading: breakdowns go cool, dark and desaturated with a heavy
    /// vignette; builds tighten the vignette and add contrast; drops burst saturated and wide open.
    /// </summary>
    private PostMood WithSpeed(PostMood m)
    {
        // speed lines appear past ~55% speed and roar in flow state
        m.SpeedLines = M.Clamp01((PlayerSpeed - 0.55f) * 2.2f) * (0.5f + Flow * 0.9f);
        m.SpeedDir = PlayerDirection;
        m.Thump += Flow * 0.004f * PlayerSpeed;
        return m;
    }

    private static PostMood BuildPostMood(MusicDirector? m, float fx, bool reduceFlashing)
    {
        if (m == null || fx <= 0f) return PostMood.Neutral;
        float intensity = m.Intensity, breakdown = m.Breakdown, build = m.Build, drop = m.DropPulse, glow = m.DropGlow;
        var mood = new PostMood
        {
            Hue = m.MoodHue * 0.55f * fx,
            Saturation = 1f + fx * (intensity * 0.25f - breakdown * 0.55f + drop * 0.5f + glow * 0.15f),
            Contrast = 1f + fx * (build * 0.15f + intensity * 0.08f - breakdown * 0.1f + drop * 0.1f),
            Thump = fx * (m.Kick * 0.010f + drop * 0.05f),
            Glitch = reduceFlashing ? 0f : fx * MathF.Min(1f, m.Sparkle * 0.3f * intensity + drop * 0.8f),
            Vignette = Math.Clamp(1f + fx * (breakdown * 0.45f + build * 0.35f - m.Kick * 0.25f - drop * 0.6f), 0.2f, 1.8f),
            Tint = Vector3.Lerp(Vector3.One, new Vector3(0.82f, 0.9f, 1.1f), breakdown * fx * 0.8f),
        };
        mood.Saturation = MathF.Max(0.15f, mood.Saturation);
        return mood;
    }

    public void Line(Vector2 a, Vector2 b, float width, Vector4 color, bool glow = false)
    {
        Vector2 d = b - a;
        float len = d.Length();
        if (len < 0.0001f) return;
        Vector2 n = new(-d.Y / len * width * 0.5f, d.X / len * width * 0.5f);
        var batch = glow ? _worldGlow : _worldAlpha;
        batch.AddQuad(a + n, b + n, b - n, a - n, color);
    }

    public void GradientLine(Vector2 a, Vector2 b, float width, Vector4 ca, Vector4 cb, bool glow = false)
    {
        Vector2 d = b - a;
        float len = d.Length();
        if (len < 0.0001f) return;
        Vector2 n = new(-d.Y / len * width * 0.5f, d.X / len * width * 0.5f);
        var batch = glow ? _worldGlow : _worldAlpha;
        batch.AddQuadGradient(a + n, b + n, b - n, a - n, ca, cb, cb, ca);
    }

    public void Polyline(IReadOnlyList<Vector2> points, float width, Vector4 color, bool closed = false, bool glow = false)
    {
        if (points.Count < 2) return;
        for (int i = 0; i < points.Count - 1; i++) Line(points[i], points[i + 1], width, color, glow);
        if (closed) Line(points[^1], points[0], width, color, glow);
    }

    public void FillPolygon(IReadOnlyList<Vector2> points, Vector4 color, bool glow = false)
    {
        if (points.Count < 3) return;
        var batch = glow ? _worldGlow : _worldAlpha;
        for (int i = 1; i < points.Count - 1; i++)
            batch.AddTriangle(points[0], points[i], points[i + 1], color);
    }

    public void FillCircle(Vector2 center, float radius, Vector4 color, int segments = 24, bool glow = false)
    {
        if (radius <= 0) return;
        var batch = glow ? _worldGlow : _worldAlpha;
        float step = M.Tau / segments;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * step, a1 = (i + 1) * step;
            batch.AddTriangle(center, center + M.FromAngle(a0, radius), center + M.FromAngle(a1, radius), color);
        }
    }

    public void Circle(Vector2 center, float radius, float width, Vector4 color, int segments = 32, bool glow = false)
    {
        if (radius <= 0) return;
        Vector2 prev = center + new Vector2(radius, 0);
        for (int i = 1; i <= segments; i++)
        {
            Vector2 cur = center + M.FromAngle(M.Tau * i / segments, radius);
            Line(prev, cur, width, color, glow);
            prev = cur;
        }
    }

    public void Arc(Vector2 center, float radius, float start, float sweep, float width, Vector4 color, int segments = 24, bool glow = false)
    {
        Vector2 prev = center + M.FromAngle(start, radius);
        for (int i = 1; i <= segments; i++)
        {
            Vector2 cur = center + M.FromAngle(start + sweep * i / segments, radius);
            Line(prev, cur, width, color, glow);
            prev = cur;
        }
    }

    public void Rect(Vector2 center, Vector2 size, float angle, Vector4 color, bool glow = false)
    {
        Vector2 hx = M.FromAngle(angle, size.X * 0.5f);
        Vector2 hy = M.FromAngle(angle + MathF.PI * 0.5f, size.Y * 0.5f);
        Vector2 a = center - hx - hy;
        Vector2 b = center + hx - hy;
        Vector2 c = center + hx + hy;
        Vector2 d = center - hx + hy;
        (glow ? _worldGlow : _worldAlpha).AddQuad(a, b, c, d, color);
    }

    public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector4 color, bool glow = false)
    {
        (glow ? _worldGlow : _worldAlpha).AddQuad(a, b, c, d, color);
    }

    public void Triangle(Vector2 a, Vector2 b, Vector2 c, Vector4 color, bool glow = false)
    {
        (glow ? _worldGlow : _worldAlpha).AddTriangle(a, b, c, color);
    }

    public void Capsule(Vector2 a, Vector2 b, float radius, Vector4 color, bool glow = false)
    {
        Line(a, b, radius * 2f, color, glow);
        FillCircle(a, radius, color, 12, glow);
        FillCircle(b, radius, color, 12, glow);
    }

    public void UiRect(float x, float y, float w, float h, Vector4 color, bool glow = false)
    {
        (glow ? _uiGlow : _uiAlpha).AddQuad(new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h), color);
    }

    public void UiFrame(float x, float y, float w, float h, float thickness, Vector4 color, bool glow = false)
    {
        UiRect(x, y, w, thickness, color, glow);
        UiRect(x, y + h - thickness, w, thickness, color, glow);
        UiRect(x, y, thickness, h, color, glow);
        UiRect(x + w - thickness, y, thickness, h, color, glow);
    }

    public void UiLine(float x1, float y1, float x2, float y2, float width, Vector4 color, bool glow = false)
    {
        float dx = x2 - x1, dy = y2 - y1;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.001f) return;
        float nx = -dy / len * width * 0.5f;
        float ny = dx / len * width * 0.5f;
        (glow ? _uiGlow : _uiAlpha).AddQuad(
            new Vector2(x1 + nx, y1 + ny), new Vector2(x2 + nx, y2 + ny),
            new Vector2(x2 - nx, y2 - ny), new Vector2(x1 - nx, y1 - ny), color);
    }

    public void UiCircle(float cx, float cy, float r, float width, Vector4 color, int segments = 24, bool glow = false)
    {
        float px = cx + r, py = cy;
        for (int i = 1; i <= segments; i++)
        {
            float a = M.Tau * i / segments;
            float x = cx + MathF.Cos(a) * r;
            float y = cy + MathF.Sin(a) * r;
            UiLine(px, py, x, y, width, color, glow);
            px = x;
            py = y;
        }
    }

    public int UiTextWidth(string text, float size, float tracking = 1f) => PixelFont.Width(text, size, tracking);

    public void UiText(string text, float x, float y, float size, Vector4 color, bool glow = false, float tracking = 1f)
    {
        text = PixelFont.Normalize(text);
        float cursor = x;
        bool shadow = size >= 2 && !glow;
        for (int ci = 0; ci < text.Length; ci++)
        {
            char ch = text[ci];
            var glyph = PixelFont.Get(ch);
            if (glyph != null)
            {
                for (int row = 0; row < 7; row++)
                {
                    string line = glyph[row];
                    int col = 0;
                    while (col < 5)
                    {
                        if (line[col] != '#')
                        {
                            col++;
                            continue;
                        }
                        int run = 1;
                        while (col + run < 5 && line[col + run] == '#') run++;
                        if (shadow)
                            _uiAlpha.AddQuad(
                                new Vector2(cursor + col * size + size * 0.14f, y + row * size + size * 0.18f),
                                new Vector2(cursor + (col + run) * size + size * 0.14f, y + row * size + size * 0.18f),
                                new Vector2(cursor + (col + run) * size + size * 0.14f, y + (row + 1) * size + size * 0.18f),
                                new Vector2(cursor + col * size + size * 0.14f, y + (row + 1) * size + size * 0.18f),
                                new Vector4(0, 0, 0, 0.65f));
                        var batch = glow ? _uiGlow : _uiAlpha;
                        batch.AddQuad(
                            new Vector2(cursor + col * size, y + row * size),
                            new Vector2(cursor + (col + run) * size, y + row * size),
                            new Vector2(cursor + (col + run) * size, y + (row + 1) * size),
                            new Vector2(cursor + col * size, y + (row + 1) * size),
                            color);
                        col += run;
                    }
                }
            }
            cursor += 6 * size * tracking;
        }
    }

    public void UiTextCentered(string text, float centerX, float y, float size, Vector4 color, bool glow = false, float tracking = 1f)
    {
        float w = UiTextWidth(text, size, tracking);
        UiText(text, centerX - w * 0.5f, y, size, color, glow, tracking);
    }

    public void UiTextRight(string text, float right, float y, float size, Vector4 color, bool glow = false, float tracking = 1f)
    {
        float w = UiTextWidth(text, size, tracking);
        UiText(text, right - w, y, size, color, glow, tracking);
    }

    public byte[] ReadPixels(out int width, out int height)
    {
        width = Width;
        height = Height;
        byte[] data = new byte[width * height * 4];
        ReadPixelsInto(data);
        return data;
    }

    public void ReadPixelsInto(byte[] data)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        unsafe
        {
            fixed (byte* ptr = data)
                _gl.ReadPixels(0, 0, (uint)Width, (uint)Height, PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
        }
    }

    public void SaveScreenshot(string path)
    {
        var pixels = ReadPixels(out int w, out int h);
        // UI blending leaves destination alpha < 1; the window ignores it but image viewers do not
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        PngWriter.Write(path, w, h, pixels);
    }

    public void Dispose()
    {
        _worldAlpha.Dispose();
        _worldGlow.Dispose();
        _uiAlpha.Dispose();
        _uiGlow.Dispose();
        _worldShader.Dispose();
        _uiShader.Dispose();
        _background.Dispose();
        _meshShader.Dispose();
        _backdropMeshes.Dispose();
        _worldMeshes.Dispose();
        _sceneA.Dispose();
        _sceneB.Dispose();
        Post.Dispose();
        _gl.DeleteVertexArray(_fullscreenVao);
        _gl.DeleteVertexArray(_emptyVao);
    }
}
