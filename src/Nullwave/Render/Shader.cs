using Silk.NET.OpenGL;

namespace Nullwave.Render;

public sealed class Shader : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, int> _uniforms = new();
    public uint Handle { get; }

    public Shader(GL gl, string vertexSource, string fragmentSource, string label = "shader")
    {
        _gl = gl;
        uint vs = Compile(ShaderType.VertexShader, vertexSource, label + ".vert");
        uint fs = Compile(ShaderType.FragmentShader, fragmentSource, label + ".frag");
        Handle = _gl.CreateProgram();
        _gl.AttachShader(Handle, vs);
        _gl.AttachShader(Handle, fs);
        _gl.LinkProgram(Handle);
        _gl.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
            throw new InvalidOperationException($"Link failed for {label}: {_gl.GetProgramInfoLog(Handle)}");
        _gl.DetachShader(Handle, vs);
        _gl.DetachShader(Handle, fs);
        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);
    }

    private uint Compile(ShaderType type, string source, string label)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
            throw new InvalidOperationException($"Compile failed for {label}: {_gl.GetShaderInfoLog(shader)}");
        return shader;
    }

    public void Use() => _gl.UseProgram(Handle);

    private int Loc(string name)
    {
        if (_uniforms.TryGetValue(name, out int loc)) return loc;
        loc = _gl.GetUniformLocation(Handle, name);
        _uniforms[name] = loc;
        return loc;
    }

    public void SetInt(string name, int value)
    {
        int loc = Loc(name);
        if (loc >= 0) _gl.Uniform1(loc, value);
    }

    public void SetFloat(string name, float value)
    {
        int loc = Loc(name);
        if (loc >= 0) _gl.Uniform1(loc, value);
    }

    public void SetVec2(string name, float x, float y)
    {
        int loc = Loc(name);
        if (loc >= 0) _gl.Uniform2(loc, x, y);
    }

    public void SetVec2(string name, System.Numerics.Vector2 v) => SetVec2(name, v.X, v.Y);

    public void SetVec3(string name, float x, float y, float z)
    {
        int loc = Loc(name);
        if (loc >= 0) _gl.Uniform3(loc, x, y, z);
    }

    public void SetVec3(string name, System.Numerics.Vector3 v) => SetVec3(name, v.X, v.Y, v.Z);

    public void SetVec4(string name, float x, float y, float z, float w)
    {
        int loc = Loc(name);
        if (loc >= 0) _gl.Uniform4(loc, x, y, z, w);
    }

    public void SetVec4(string name, System.Numerics.Vector4 v) => SetVec4(name, v.X, v.Y, v.Z, v.W);

    /// <summary>Uploads a System.Numerics (row-vector) matrix so GLSL `m * vec4(p, 1)` matches `p * m` in C#.</summary>
    public unsafe void SetMatrix(string name, System.Numerics.Matrix4x4 m)
    {
        int loc = Loc(name);
        if (loc >= 0) _gl.UniformMatrix4(loc, 1, false, (float*)&m);
    }

    public void Dispose()
    {
        _gl.DeleteProgram(Handle);
    }
}
