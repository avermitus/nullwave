using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Nullwave.Render;

/// <summary>
/// Material slots baked into procedural meshes. The colour of each slot is chosen per draw,
/// so one mesh serves every hue, hit flash and charge state.
/// </summary>
public enum Mat : byte
{
    Hull = 0,     // dark lit body
    Trim = 1,     // hue-tinted plating
    Glow = 2,     // emissive hue
    Core = 3,     // white-hot emissive
}

[StructLayout(LayoutKind.Sequential)]
public struct Vertex3D
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector4 Color;
    public float Glow;
    public Vector2 UV;
}

public struct MaterialSet
{
    public Vector4 Hull, Trim, Glow, Core;
    public float HullGlow, TrimGlow, GlowGlow, CoreGlow;

    public static MaterialSet ForHue(Vector4 hue, float glow = 1f, float flash = 0f, float alpha = 1f)
    {
        var white = new Vector4(1f, 1f, 1f, alpha);
        Vector4 Mix(Vector4 c) => Vector4.Lerp(c, white, Math.Clamp(flash, 0f, 1f)) with { W = alpha };
        return new MaterialSet
        {
            Hull = Mix(new Vector4(0.10f + hue.X * 0.05f, 0.11f + hue.Y * 0.05f, 0.15f + hue.Z * 0.06f, 1f)),
            Trim = Mix(new Vector4(hue.X * 0.55f + 0.08f, hue.Y * 0.55f + 0.08f, hue.Z * 0.55f + 0.1f, 1f)),
            Glow = Mix(hue),
            Core = white,
            HullGlow = flash * 0.8f,
            TrimGlow = 0.05f + flash,
            GlowGlow = 1.15f * glow + flash,
            CoreGlow = 1.9f * glow + flash,
        };
    }

    public readonly Vector4 Color(Mat m) => m switch
    {
        Mat.Hull => Hull,
        Mat.Trim => Trim,
        Mat.Glow => Glow,
        _ => Core,
    };

    public readonly float Emission(Mat m) => m switch
    {
        Mat.Hull => HullGlow,
        Mat.Trim => TrimGlow,
        Mat.Glow => GlowGlow,
        _ => CoreGlow,
    };
}

/// <summary>Immutable flat-shaded triangle soup in model space (x forward, y side, z up).</summary>
public sealed class Mesh
{
    public readonly Vector3[] Positions;
    public readonly Vector3[] Normals;
    public readonly Mat[] Materials;
    public readonly Vector2[]? UV;
    public int VertexCount => Positions.Length;

    public Mesh(Vector3[] positions, Vector3[] normals, Mat[] materials, Vector2[]? uv = null)
    {
        UV = uv;
        Positions = positions;
        Normals = normals;
        Materials = materials;
    }
}

public sealed class MeshBuilder
{
    private readonly List<Vector3> _p = new();
    private readonly List<Vector3> _n = new();
    private readonly List<Mat> _m = new();
    private Matrix4x4 _xf = Matrix4x4.Identity;

    public int VertexCount => _p.Count;

    /// <summary>Sets the transform applied to every primitive added until the next call.</summary>
    public MeshBuilder At(Matrix4x4 transform)
    {
        _xf = transform;
        return this;
    }

    public MeshBuilder At(Vector3 offset, float yaw = 0f, float pitch = 0f, float roll = 0f, Vector3? scale = null)
    {
        _xf = Matrix4x4.CreateScale(scale ?? Vector3.One)
              * Matrix4x4.CreateRotationX(roll)
              * Matrix4x4.CreateRotationY(pitch)
              * Matrix4x4.CreateRotationZ(yaw)
              * Matrix4x4.CreateTranslation(offset);
        return this;
    }

    public MeshBuilder Reset()
    {
        _xf = Matrix4x4.Identity;
        return this;
    }

    /// <summary>Adds a triangle whose normal is oriented away from <paramref name="inside"/> (model space, pre-transform).</summary>
    public void Tri(Vector3 a, Vector3 b, Vector3 c, Mat mat, Vector3 inside)
    {
        a = Vector3.Transform(a, _xf);
        b = Vector3.Transform(b, _xf);
        c = Vector3.Transform(c, _xf);
        inside = Vector3.Transform(inside, _xf);
        Vector3 n = Vector3.Cross(b - a, c - a);
        float len = n.Length();
        if (len < 1e-8f) return;
        n /= len;
        Vector3 centroid = (a + b + c) / 3f;
        if (Vector3.Dot(n, centroid - inside) < 0) n = -n;
        _p.Add(a); _p.Add(b); _p.Add(c);
        _n.Add(n); _n.Add(n); _n.Add(n);
        _m.Add(mat); _m.Add(mat); _m.Add(mat);
    }

    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Mat mat, Vector3 inside)
    {
        Tri(a, b, c, mat, inside);
        Tri(a, c, d, mat, inside);
    }

    /// <summary>
    /// Lofts a closed tube along the x axis through cross sections (x, halfWidth, halfHeight, zOffset).
    /// Capped at both ends. Good for fuselages, barrels and pods.
    /// </summary>
    public void Loft(ReadOnlySpan<Vector4> sections, int sides, Mat side, Mat frontCap, Mat backCap, float twist = 0f)
    {
        if (sections.Length < 2) return;
        var rings = new Vector3[sections.Length][];
        Vector3 center = Vector3.Zero;
        for (int s = 0; s < sections.Length; s++)
        {
            var sec = sections[s];
            rings[s] = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i / (float)sides * MathF.Tau + twist;
                rings[s][i] = new Vector3(sec.X, MathF.Cos(a) * sec.Y, MathF.Sin(a) * sec.Z + sec.W);
            }
            center += new Vector3(sec.X, 0, sec.W);
        }
        center /= sections.Length;
        for (int s = 0; s < sections.Length - 1; s++)
        {
            Vector3 inside = new((sections[s].X + sections[s + 1].X) * 0.5f, 0, (sections[s].W + sections[s + 1].W) * 0.5f);
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                Quad(rings[s][i], rings[s][j], rings[s + 1][j], rings[s + 1][i], side, inside);
            }
        }
        Cap(rings[0], new Vector3(sections[0].X, 0, sections[0].W), backCap, center);
        Cap(rings[^1], new Vector3(sections[^1].X, 0, sections[^1].W), frontCap, center);
    }

    private void Cap(Vector3[] ring, Vector3 mid, Mat mat, Vector3 inside)
    {
        for (int i = 0; i < ring.Length; i++)
            Tri(mid, ring[i], ring[(i + 1) % ring.Length], mat, inside);
    }

    /// <summary>Extrudes a 2D polygon (xy) between z0 and z1 with optional distinct top material.</summary>
    public void Prism(ReadOnlySpan<Vector2> poly, float z0, float z1, Mat side, Mat top, Mat? bottom = null)
    {
        Vector2 c2 = Vector2.Zero;
        foreach (var v in poly) c2 += v;
        c2 /= poly.Length;
        Vector3 inside = new(c2.X, c2.Y, (z0 + z1) * 0.5f);
        for (int i = 0; i < poly.Length; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Length];
            Quad(new Vector3(a, z0), new Vector3(b, z0), new Vector3(b, z1), new Vector3(a, z1), side, inside);
            Tri(new Vector3(c2, z1), new Vector3(a, z1), new Vector3(b, z1), top, inside);
            if (bottom.HasValue) Tri(new Vector3(c2, z0), new Vector3(b, z0), new Vector3(a, z0), bottom.Value, inside);
        }
    }

    /// <summary>Truncated cone / pyramid along z.</summary>
    public void Frustum(int sides, float r0, float r1, float z0, float z1, Mat side, Mat top, Mat bottom, float phase = 0f)
    {
        Vector3 inside = new(0, 0, (z0 + z1) * 0.5f);
        for (int i = 0; i < sides; i++)
        {
            float a0 = i / (float)sides * MathF.Tau + phase;
            float a1 = (i + 1) / (float)sides * MathF.Tau + phase;
            Vector3 p0 = new(MathF.Cos(a0) * r0, MathF.Sin(a0) * r0, z0);
            Vector3 p1 = new(MathF.Cos(a1) * r0, MathF.Sin(a1) * r0, z0);
            Vector3 q0 = new(MathF.Cos(a0) * r1, MathF.Sin(a0) * r1, z1);
            Vector3 q1 = new(MathF.Cos(a1) * r1, MathF.Sin(a1) * r1, z1);
            if (r1 > 0.0001f) Quad(p0, p1, q1, q0, side, inside);
            else Tri(p0, p1, q0, side, inside);
            if (r1 > 0.0001f) Tri(new Vector3(0, 0, z1), q0, q1, top, inside);
            if (r0 > 0.0001f) Tri(new Vector3(0, 0, z0), p1, p0, bottom, inside);
        }
    }

    public void Box(Vector3 size, Mat side, Mat top)
    {
        Vector3 h = size * 0.5f;
        Span<Vector2> poly = stackalloc Vector2[] { new(-h.X, -h.Y), new(h.X, -h.Y), new(h.X, h.Y), new(-h.X, h.Y) };
        Prism(poly, -h.Z, h.Z, side, top, side);
    }

    public void Octahedron(float rx, float ry, float rz, Mat mat, Mat? alt = null)
    {
        Vector3[] v = { new(rx, 0, 0), new(0, ry, 0), new(-rx, 0, 0), new(0, -ry, 0) };
        Vector3 top = new(0, 0, rz), bot = new(0, 0, -rz);
        for (int i = 0; i < 4; i++)
        {
            var a = v[i];
            var b = v[(i + 1) % 4];
            Tri(top, a, b, (i % 2 == 0 || alt == null) ? mat : alt.Value, Vector3.Zero);
            Tri(bot, b, a, (i % 2 == 1 || alt == null) ? mat : alt.Value, Vector3.Zero);
        }
    }

    public void Icosphere(float r, int subdivisions, Mat mat, Mat? alt = null, float altChance = 0f, int seed = 7)
    {
        float t = (1f + MathF.Sqrt(5f)) * 0.5f;
        var verts = new List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        var faces = new List<(int, int, int)>
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11),
            (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
            (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9),
            (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
        };
        for (int s = 0; s < subdivisions; s++)
        {
            var next = new List<(int, int, int)>(faces.Count * 4);
            var cache = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out int idx)) return idx;
                verts.Add((verts[a] + verts[b]) * 0.5f);
                cache[key] = verts.Count - 1;
                return verts.Count - 1;
            }
            foreach (var (a, b, c) in faces)
            {
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.Add((a, ab, ca)); next.Add((b, bc, ab)); next.Add((c, ca, bc)); next.Add((ab, bc, ca));
            }
            faces = next;
        }
        uint h = (uint)seed * 2654435761u;
        foreach (var (a, b, c) in faces)
        {
            h ^= h << 13; h ^= h >> 17; h ^= h << 5;
            bool useAlt = alt.HasValue && (h % 1000) / 1000f < altChance;
            Tri(Vector3.Normalize(verts[a]) * r, Vector3.Normalize(verts[b]) * r, Vector3.Normalize(verts[c]) * r, useAlt ? alt!.Value : mat, Vector3.Zero);
        }
    }

    public void Torus(float major, float minor, int segU, int segV, Mat mat, Mat? stripe = null, int stripeEvery = 0)
    {
        for (int u = 0; u < segU; u++)
        {
            float a0 = u / (float)segU * MathF.Tau, a1 = (u + 1) / (float)segU * MathF.Tau;
            Vector3 c0 = new(MathF.Cos(a0) * major, MathF.Sin(a0) * major, 0);
            Vector3 c1 = new(MathF.Cos(a1) * major, MathF.Sin(a1) * major, 0);
            Mat m = stripe.HasValue && stripeEvery > 0 && u % stripeEvery == 0 ? stripe.Value : mat;
            for (int v = 0; v < segV; v++)
            {
                float b0 = v / (float)segV * MathF.Tau, b1 = (v + 1) / (float)segV * MathF.Tau;
                Vector3 P(float a, float b) => new(
                    MathF.Cos(a) * (major + MathF.Cos(b) * minor),
                    MathF.Sin(a) * (major + MathF.Cos(b) * minor),
                    MathF.Sin(b) * minor);
                Quad(P(a0, b0), P(a1, b0), P(a1, b1), P(a0, b1), m, (c0 + c1) * 0.5f);
            }
        }
    }

    /// <summary>Flat slab from a 2D outline (wings, fins, blades) with the given thickness centred on z.</summary>
    public void Slab(ReadOnlySpan<Vector2> outline, float thickness, Mat side, Mat face)
    {
        Prism(outline, -thickness * 0.5f, thickness * 0.5f, side, face, face);
    }

    public Mesh Build() => new(_p.ToArray(), _n.ToArray(), _m.ToArray());
}

/// <summary>Per-frame dynamic triangle list of transformed mesh instances.</summary>
public sealed class MeshBatch : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private Vertex3D[] _vertices;
    private int _count;
    public int Count => _count;

    public MeshBatch(GL gl, int capacity = 131072)
    {
        _gl = gl;
        _vertices = new Vertex3D[capacity];
        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);
        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            uint stride = (uint)sizeof(Vertex3D);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
            _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
            _gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, stride, (void*)(10 * sizeof(float)));
            _gl.VertexAttribPointer(4, 2, VertexAttribPointerType.Float, false, stride, (void*)(11 * sizeof(float)));
        }
        for (uint i = 0; i < 5; i++) _gl.EnableVertexAttribArray(i);
        _gl.BindVertexArray(0);
    }

    public void Clear() => _count = 0;

    private void Ensure(int extra)
    {
        if (_count + extra <= _vertices.Length) return;
        Array.Resize(ref _vertices, Math.Max(_vertices.Length * 2, _count + extra));
    }

    public void Add(Mesh mesh, in Matrix4x4 transform, in MaterialSet mats)
    {
        int n = mesh.VertexCount;
        Ensure(n);
        Span<Vector4> colors = stackalloc Vector4[4] { mats.Hull, mats.Trim, mats.Glow, mats.Core };
        Span<float> glows = stackalloc float[4] { mats.HullGlow, mats.TrimGlow, mats.GlowGlow, mats.CoreGlow };
        var pos = mesh.Positions;
        var nor = mesh.Normals;
        var mat = mesh.Materials;
        for (int i = 0; i < n; i++)
        {
            int m = (int)mat[i];
            ref var v = ref _vertices[_count++];
            v.Position = Vector3.Transform(pos[i], transform);
            Vector3 nn = Vector3.TransformNormal(nor[i], transform);
            float l = nn.Length();
            v.Normal = l > 1e-6f ? nn / l : Vector3.UnitZ;
            v.Color = colors[m];
            v.Glow = glows[m];
            v.UV = mesh.UV == null ? new Vector2(-1f) : mesh.UV[i];
        }
    }

    public unsafe void Draw()
    {
        if (_count == 0) return;
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (Vertex3D* ptr = _vertices)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_count * sizeof(Vertex3D)), ptr, BufferUsageARB.DynamicDraw);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_count);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
    }
}
