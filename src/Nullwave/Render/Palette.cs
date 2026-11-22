using System.Numerics;
using Nullwave.Core;

namespace Nullwave.Render;

public sealed class Palette
{
    public Vector3 BackA;
    public Vector3 BackB;
    public Vector3 BackC;
    public Vector4 Primary;
    public Vector4 Secondary;
    public Vector4 Accent;
    public Vector4 Player;
    public Vector4 Danger;
    public Vector4 Text;
    public Vector4 Dim;
    public float BaseHue;

    public static Palette FromSeed(ulong seed, int movement = 0)
    {
        var rng = new Rng(seed ^ 0xD1B54A32D192ED03UL ^ (ulong)(movement * 7919));
        float baseHue = rng.Range(0.48f, 0.62f) + movement * 0.061f;
        baseHue -= MathF.Floor(baseHue);
        float spread = rng.Range(0.18f, 0.32f);
        var p = new Palette
        {
            BaseHue = baseHue,
            BackA = M.HsvToRgb(baseHue, 0.72f, 0.45f),
            BackB = M.HsvToRgb((baseHue + spread + 0.36f) % 1f, 0.68f, 0.42f),
            BackC = M.HsvToRgb((baseHue + spread * 1.7f + 0.08f) % 1f, 0.58f, 0.52f),
            Primary = M.Hsv(baseHue, 0.72f, 1f),
            Secondary = M.Hsv((baseHue + 0.46f) % 1f, 0.78f, 1f),
            Accent = M.Hsv((baseHue + 0.11f) % 1f, 0.42f, 1f),
            Player = M.Hsv((baseHue + 0.01f) % 1f, 0.38f, 1f),
            // Keep warnings warm even when the seeded arena and player colors rotate.
            Danger = new Vector4(1f, 0.46f, 0.35f, 1f),
            Text = new Vector4(0.90f, 0.96f, 1f, 1f),
            Dim = new Vector4(0.57f, 0.65f, 0.74f, 1f),
        };
        return p;
    }

    public Vector4 Hue(float h, float s = 0.8f, float v = 1f, float a = 1f)
    {
        return M.Hsv((BaseHue + h) % 1f, s, v, a);
    }
}
