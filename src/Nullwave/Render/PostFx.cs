using Silk.NET.OpenGL;

namespace Nullwave.Render;

/// <summary>Music-driven grading parameters for the composite pass.</summary>
public struct PostMood
{
    public float Hue, Saturation, Contrast, Thump, Glitch, Vignette, SpeedLines, DarkTheme;
    public System.Numerics.Vector2 SpeedDir;
    public System.Numerics.Vector3 Tint;
    public static PostMood Neutral => new() { Saturation = 1f, Contrast = 1f, Vignette = 1f, Tint = System.Numerics.Vector3.One };
}

public sealed class PostFx : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly Shader _bright;
    private readonly Shader _blur;
    private readonly Shader _down;
    private readonly Shader _composite;
    private readonly GlFbo[] _downs = new GlFbo[3];
    private readonly GlFbo[] _temps = new GlFbo[3];
    private int _width;
    private int _height;

    public PostFx(GL gl)
    {
        _gl = gl;
        _vao = _gl.GenVertexArray();
        _bright = new Shader(_gl, Shaders.FullscreenVert, Shaders.BrightFrag, "bright");
        _blur = new Shader(_gl, Shaders.FullscreenVert, Shaders.BlurFrag, "blur");
        _down = new Shader(_gl, Shaders.FullscreenVert, Shaders.DownFrag, "down");
        _composite = new Shader(_gl, Shaders.FullscreenVert, Shaders.CompositeFrag, "composite");
    }

    public void EnsureSize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == _width && height == _height) return;
        _width = width;
        _height = height;
        for (int i = 0; i < 3; i++)
        {
            _downs[i]?.Dispose();
            _temps[i]?.Dispose();
            int w = Math.Max(1, width >> (i + 1));
            int h = Math.Max(1, height >> (i + 1));
            _downs[i] = new GlFbo(_gl, w, h);
            _temps[i] = new GlFbo(_gl, w, h);
        }
    }

    /// <summary>
    /// Bloom + composite into the default framebuffer. Always exits in <see cref="RenderBaseline.Default"/>
    /// (see <see cref="RestoreBaseline"/>), so callers can draw on top without re-deriving state.
    /// </summary>
    public void Process(uint sceneTexture, int width, int height, float time, float bloom, float aberration, float flash, float beat, int damage, PostMood mood)
    {
        EnsureSize(width, height);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.DepthTest);
        _gl.BindVertexArray(_vao);

        bool abyssal = mood.DarkTheme > 0.5f;
        _downs[0].Bind();
        _bright.Use();
        _bright.SetInt("uScene", 0);
        _bright.SetFloat("uThreshold", abyssal ? 0.72f : 0.62f);
        _bright.SetFloat("uKnee", abyssal ? 0.32f : 0.42f);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, sceneTexture);
        DrawFullscreen();

        for (int i = 0; i < 3; i++)
        {
            if (i > 0)
            {
                _downs[i].Bind();
                _down.Use();
                _down.SetInt("uTex", 0);
                _down.SetVec2("uTexel", 1f / _downs[i - 1].Width, 1f / _downs[i - 1].Height);
                _gl.ActiveTexture(TextureUnit.Texture0);
                _gl.BindTexture(TextureTarget.Texture2D, _downs[i - 1].Texture);
                DrawFullscreen();
            }

            _temps[i].Bind();
            _blur.Use();
            _blur.SetInt("uTex", 0);
            _blur.SetVec2("uTexel", 1f / _downs[i].Width, 1f / _downs[i].Height);
            _blur.SetVec2("uDir", 1f, 0f);
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, _downs[i].Texture);
            DrawFullscreen();

            _downs[i].Bind();
            _blur.Use();
            _blur.SetInt("uTex", 0);
            _blur.SetVec2("uTexel", 1f / _downs[i].Width, 1f / _downs[i].Height);
            _blur.SetVec2("uDir", 0f, 1f);
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, _temps[i].Texture);
            DrawFullscreen();
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _composite.Use();
        _composite.SetInt("uScene", 0);
        _composite.SetInt("uBloom0", 1);
        _composite.SetInt("uBloom1", 2);
        _composite.SetInt("uBloom2", 3);
        _composite.SetVec2("uResolution", width, height);
        _composite.SetFloat("uTime", time);
        _composite.SetFloat("uExposure", 1.08f);
        _composite.SetFloat("uBloomStrength", bloom);
        _composite.SetFloat("uAberration", aberration);
        _composite.SetFloat("uFlash", flash);
        _composite.SetFloat("uBeat", beat);
        _composite.SetInt("uDamage", damage);
        _composite.SetFloat("uHue", mood.Hue);
        _composite.SetFloat("uSat", mood.Saturation);
        _composite.SetFloat("uContrast", mood.Contrast);
        _composite.SetFloat("uThump", mood.Thump);
        _composite.SetFloat("uGlitch", mood.Glitch);
        _composite.SetFloat("uVignette", mood.Vignette);
        _composite.SetVec3("uTint", mood.Tint);
        _composite.SetFloat("uSpeedLines", mood.SpeedLines);
        _composite.SetVec2("uSpeedDir", mood.SpeedDir.X, -mood.SpeedDir.Y);
        _composite.SetFloat("uDarkTheme", mood.DarkTheme);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, sceneTexture);
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, _downs[0].Texture);
        _gl.ActiveTexture(TextureUnit.Texture2);
        _gl.BindTexture(TextureTarget.Texture2D, _downs[1].Texture);
        _gl.ActiveTexture(TextureUnit.Texture3);
        _gl.BindTexture(TextureTarget.Texture2D, _downs[2].Texture);
        DrawFullscreen();
        RestoreBaseline(_gl);
    }

    /// <summary>Texture units the post chain binds (scene + three bloom levels).</summary>
    public const int SamplerUnits = 4;

    /// <summary>
    /// Leaves <see cref="RenderBaseline.Default"/>: the passes above disable blending and park bloom
    /// textures on units 1-3 with unit 3 active. Without this, anything drawn after the composite
    /// (a second render in the same frame, the recorder, tools) inherited that state.
    /// </summary>
    public static void RestoreBaseline(GL gl)
    {
        var b = RenderBaseline.Default;
        for (int unit = b.SamplerUnits - 1; unit >= 0; unit--)
        {
            gl.ActiveTexture(TextureUnit.Texture0 + unit);
            gl.BindTexture(TextureTarget.Texture2D, 0);
        }
        gl.ActiveTexture(TextureUnit.Texture0 + b.ActiveUnit);
        if (b.Blend) gl.Enable(EnableCap.Blend); else gl.Disable(EnableCap.Blend);
        if (b.DepthTest) gl.Enable(EnableCap.DepthTest); else gl.Disable(EnableCap.DepthTest);
        b.Mode.Apply(gl);
        gl.BindVertexArray(b.VertexArray);
    }

    /// <summary>Reads the live GL state back and compares it with <see cref="RenderBaseline.Default"/>. Debug/smoke only (glGet stalls).</summary>
    public static bool VerifyBaseline(GL gl, out string detail)
    {
        var b = RenderBaseline.Default;
        var problems = new List<string>();
        if (gl.IsEnabled(EnableCap.Blend) != b.Blend) problems.Add("blend");
        if (gl.IsEnabled(EnableCap.DepthTest) != b.DepthTest) problems.Add("depth");
        if (gl.GetInteger(GetPName.BlendSrcRgb) != (int)b.Mode.SrcColor) problems.Add("srcRgb");
        if (gl.GetInteger(GetPName.BlendDstRgb) != (int)b.Mode.DstColor) problems.Add("dstRgb");
        if (gl.GetInteger(GetPName.BlendSrcAlpha) != (int)b.Mode.SrcAlphaFactor) problems.Add("srcAlpha");
        if (gl.GetInteger(GetPName.BlendDstAlpha) != (int)b.Mode.DstAlphaFactor) problems.Add("dstAlpha");
        if (gl.GetInteger(GetPName.VertexArrayBinding) != (int)b.VertexArray) problems.Add("vao");
        int active = gl.GetInteger(GetPName.ActiveTexture);
        for (int unit = 0; unit < b.SamplerUnits; unit++)
        {
            gl.ActiveTexture(TextureUnit.Texture0 + unit);
            if (gl.GetInteger(GetPName.TextureBinding2D) != 0) problems.Add("tex" + unit);
        }
        gl.ActiveTexture((TextureUnit)active);
        if (active != (int)(TextureUnit.Texture0 + b.ActiveUnit)) problems.Add("activeUnit");
        detail = problems.Count == 0 ? "baseline" : "leak:" + string.Join(",", problems);
        return problems.Count == 0;
    }

    private void DrawFullscreen()
    {
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    public void Dispose()
    {
        foreach (var f in _downs) f?.Dispose();
        foreach (var f in _temps) f?.Dispose();
        _bright.Dispose();
        _blur.Dispose();
        _down.Dispose();
        _composite.Dispose();
        _gl.DeleteVertexArray(_vao);
    }
}
