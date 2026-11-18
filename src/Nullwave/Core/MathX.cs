using System.Numerics;
using System.Runtime.CompilerServices;

namespace Nullwave.Core;

public static class M
{
    public const float Tau = MathF.PI * 2f;
    public const float Deg = MathF.PI / 180f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => a + (b - a) * t;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float InvLerp(float a, float b, float v) => b == a ? 0 : Clamp01((v - a) / (b - a));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ExpSmooth(float current, float target, float rate, float dt) =>
        Lerp(current, target, 1f - MathF.Exp(-rate * dt));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Approach(float value, float target, float delta) =>
        value < target ? MathF.Min(value + delta, target) : MathF.Max(value - delta, target);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Len(Vector2 v) => v.Length();
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DistSq(Vector2 a, Vector2 b) => Vector2.DistanceSquared(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dist(Vector2 a, Vector2 b) => Vector2.Distance(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 SafeNorm(Vector2 v)
    {
        float l = v.LengthSquared();
        return l > 1e-8f ? v / MathF.Sqrt(l) : Vector2.Zero;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 FromAngle(float angle, float length = 1f) =>
        new(MathF.Cos(angle) * length, MathF.Sin(angle) * length);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float AngleOf(Vector2 v) => MathF.Atan2(v.Y, v.X);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Rotate(Vector2 v, float angle)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Perp(Vector2 v) => new(-v.Y, v.X);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float WrapAngle(float a)
    {
        a = (a + MathF.PI) % Tau;
        if (a < 0) a += Tau;
        return a - MathF.PI;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float AngleDelta(float from, float to) => WrapAngle(to - from);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float MoveTowardsAngle(float from, float to, float maxDelta)
    {
        float d = AngleDelta(from, to);
        return from + Clamp(d, -maxDelta, maxDelta);
    }
    public static Vector2 ClampLength(Vector2 v, float max)
    {
        float l = v.Length();
        return l > max ? v * (max / l) : v;
    }
    public static Vector2 Clamp(Vector2 v, Vector2 min, Vector2 max) =>
        new(Clamp(v.X, min.X, max.X), Clamp(v.Y, min.Y, max.Y));

    public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.LengthSquared();
        float t = len2 <= 1e-6f ? 0 : Clamp01(Vector2.Dot(p - a, ab) / len2);
        return Vector2.Distance(p, a + ab * t);
    }

    public static float DistanceToSegmentSq(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.LengthSquared();
        float t = len2 <= 1e-6f ? 0 : Clamp01(Vector2.Dot(p - a, ab) / len2);
        return Vector2.DistanceSquared(p, a + ab * t);
    }

    public static bool SegmentIntersectsCircle(Vector2 a, Vector2 b, Vector2 c, float radius) =>
        DistanceToSegmentSq(c, a, b) <= radius * radius;

    public static float Hash01(int x, int y, int seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / 16777215f;
    }

    public static Vector3 HsvToRgb(float h, float s, float v)
    {
        h = h - MathF.Floor(h);
        float r = MathF.Abs(h * 6f - 3f) - 1f;
        float g = 2f - MathF.Abs(h * 6f - 2f);
        float b = 2f - MathF.Abs(h * 6f - 4f);
        Vector3 rgb = new(Clamp01(r), Clamp01(g), Clamp01(b));
        return Vector3.Lerp(Vector3.One, rgb, s) * v;
    }

    public static Vector4 Hsv(float h, float s, float v, float a = 1f)
    {
        Vector3 c = HsvToRgb(h, s, v);
        return new Vector4(c, a);
    }

    public static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);
    public static Vector4 Scale(Vector4 c, float m) => new(c.X * m, c.Y * m, c.Z * m, c.W);
    public static Vector4 Add(Vector4 a, Vector4 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);

    public static float Pulse(float phase, float sharpness = 8f) => MathF.Exp(-phase * sharpness);

    public static float Sqrt(float v) => MathF.Sqrt(MathF.Max(0, v));
    public static float Sin01(float v) => MathF.Sin(v) * 0.5f + 0.5f;

    public static float EaseOutCubic(float t)
    {
        t = Clamp01(t);
        float u = 1 - t;
        return 1 - u * u * u;
    }
    public static float EaseInOut(float t) => t * t * (3 - 2 * t);
    public static float EaseOutBack(float t)
    {
        const float c = 1.70158f;
        t -= 1;
        return 1 + (c + 1) * t * t * t + c * t * t;
    }

    public static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, float t)
    {
        float u = 1 - t;
        return a * (u * u) + b * (2 * u * t) + c * (t * t);
    }
}
