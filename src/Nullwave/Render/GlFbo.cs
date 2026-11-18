using Silk.NET.OpenGL;

namespace Nullwave.Render;

public sealed class GlFbo : IDisposable
{
    private readonly GL _gl;
    public uint Fbo { get; private set; }
    public uint Texture { get; private set; }
    public uint Depth { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public GlFbo(GL gl, int width, int height, bool hdr = true, bool depth = false)
    {
        _gl = gl;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Create(hdr, depth);
    }

    private unsafe void Create(bool hdr, bool depth)
    {
        Fbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, Fbo);
        Texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, Texture);
        if (hdr)
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, (uint)Width, (uint)Height, 0, PixelFormat.Rgba, PixelType.HalfFloat, (void*)0);
        else
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)Width, (uint)Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, (void*)0);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, Texture, 0);
        if (depth)
        {
            Depth = _gl.GenRenderbuffer();
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, Depth);
            _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, (uint)Width, (uint)Height);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, Depth);
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
        }
        _gl.DrawBuffer(DrawBufferMode.ColorAttachment0);
        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if ((uint)status != (uint)FramebufferStatus.Complete)
            throw new InvalidOperationException($"Framebuffer incomplete: {status}");
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Bind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, Fbo);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    public void Dispose()
    {
        if (Fbo != 0) _gl.DeleteFramebuffer(Fbo);
        if (Texture != 0) _gl.DeleteTexture(Texture);
        if (Depth != 0) _gl.DeleteRenderbuffer(Depth);
        Depth = 0;
        Fbo = 0;
        Texture = 0;
    }
}
