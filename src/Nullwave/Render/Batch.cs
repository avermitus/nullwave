using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Nullwave.Render;

[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    public Vector2 Position;
    public Vector4 Color;
    public Vertex(Vector2 p, Vector4 c) { Position = p; Color = c; }
}

public sealed class Batch : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private Vertex[] _vertices;
    private int _count;
    public int Count => _count;

    public Batch(GL gl, int capacity = 65536)
    {
        _gl = gl;
        _vertices = new Vertex[capacity];
        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);
        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, (uint)sizeof(Vertex), (void*)0);
            _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, (uint)sizeof(Vertex), (void*)(2 * sizeof(float)));
        }
        _gl.EnableVertexAttribArray(0);
        _gl.EnableVertexAttribArray(1);
        _gl.BindVertexArray(0);
    }

    public void Clear() => _count = 0;

    private void Ensure(int extra)
    {
        if (_count + extra <= _vertices.Length) return;
        int newSize = Math.Max(_vertices.Length * 2, _count + extra);
        Array.Resize(ref _vertices, newSize);
    }

    public void AddTriangle(Vector2 a, Vector2 b, Vector2 c, Vector4 color)
    {
        Ensure(3);
        _vertices[_count++] = new Vertex(a, color);
        _vertices[_count++] = new Vertex(b, color);
        _vertices[_count++] = new Vertex(c, color);
    }

    public void AddTriangle(Vector2 a, Vector2 b, Vector2 c, Vector4 ca, Vector4 cb, Vector4 cc)
    {
        Ensure(3);
        _vertices[_count++] = new Vertex(a, ca);
        _vertices[_count++] = new Vertex(b, cb);
        _vertices[_count++] = new Vertex(c, cc);
    }

    public void AddQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector4 color)
    {
        Ensure(6);
        _vertices[_count++] = new Vertex(a, color);
        _vertices[_count++] = new Vertex(b, color);
        _vertices[_count++] = new Vertex(c, color);
        _vertices[_count++] = new Vertex(a, color);
        _vertices[_count++] = new Vertex(c, color);
        _vertices[_count++] = new Vertex(d, color);
    }

    public void AddQuadGradient(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector4 ca, Vector4 cb, Vector4 cc, Vector4 cd)
    {
        Ensure(6);
        _vertices[_count++] = new Vertex(a, ca);
        _vertices[_count++] = new Vertex(b, cb);
        _vertices[_count++] = new Vertex(c, cc);
        _vertices[_count++] = new Vertex(a, ca);
        _vertices[_count++] = new Vertex(c, cc);
        _vertices[_count++] = new Vertex(d, cd);
    }

    public unsafe void Draw()
    {
        if (_count == 0) return;
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (Vertex* ptr = _vertices)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_count * sizeof(Vertex)), ptr, BufferUsageARB.DynamicDraw);
        }
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_count);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
    }
}
