using System.Numerics;
using Silk.NET.OpenGL;

namespace Nullwave.Render;

/// <summary>
/// A separate RGB / alpha blend function. Kept as data so the render passes and the self test share
/// one definition, and so the blend maths can be checked without a GL context.
/// </summary>
public readonly record struct BlendMode(BlendingFactor SrcColor, BlendingFactor DstColor, BlendingFactor SrcAlphaFactor, BlendingFactor DstAlphaFactor)
{
    /// <summary>Standard "over": colour blends by source alpha, destination alpha accumulates towards 1 (never drops).</summary>
    public static readonly BlendMode Alpha = new(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);

    /// <summary>Additive glow: colour adds, destination alpha is left untouched.</summary>
    public static readonly BlendMode Additive = new(BlendingFactor.SrcAlpha, BlendingFactor.One, BlendingFactor.Zero, BlendingFactor.One);

    /// <summary>The pre-TASK-001 UI blend (plain glBlendFunc): it applied SrcAlpha/OneMinusSrcAlpha to alpha too, so the backbuffer went translucent.</summary>
    public static readonly BlendMode LegacyAlpha = new(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

    /// <summary>The pre-TASK-001 additive blend (plain glBlendFunc).</summary>
    public static readonly BlendMode LegacyAdditive = new(BlendingFactor.SrcAlpha, BlendingFactor.One, BlendingFactor.SrcAlpha, BlendingFactor.One);

    public void Apply(GL gl) => gl.BlendFuncSeparate(SrcColor, DstColor, SrcAlphaFactor, DstAlphaFactor);

    /// <summary>Destination alpha after blending one fragment (clamped like an 8-bit target).</summary>
    public float ResultAlpha(float srcA, float dstA) =>
        Math.Clamp(Factor(SrcAlphaFactor, srcA, dstA) * srcA + Factor(DstAlphaFactor, srcA, dstA) * dstA, 0f, 1f);

    /// <summary>Destination colour after blending one fragment (clamped like an 8-bit target).</summary>
    public Vector3 ResultColor(Vector3 src, float srcA, Vector3 dst, float dstA) =>
        Vector3.Clamp(src * Factor(SrcColor, srcA, dstA) + dst * Factor(DstColor, srcA, dstA), Vector3.Zero, Vector3.One);

    private static float Factor(BlendingFactor f, float srcA, float dstA) => f switch
    {
        BlendingFactor.Zero => 0f,
        BlendingFactor.One => 1f,
        BlendingFactor.SrcAlpha => srcA,
        BlendingFactor.OneMinusSrcAlpha => 1f - srcA,
        BlendingFactor.DstAlpha => dstA,
        BlendingFactor.OneMinusDstAlpha => 1f - dstA,
        _ => throw new NotSupportedException("blend factor " + f),
    };
}

/// <summary>
/// The GL state every pass may assume on entry and must leave on exit: blending on with
/// <see cref="BlendMode.Alpha"/>, depth test off, texture unit 0 active, no 2D texture bound on the
/// sampler units the post chain uses, and no VAO bound.
/// </summary>
public readonly record struct RenderBaseline(bool Blend, bool DepthTest, BlendMode Mode, int ActiveUnit, int SamplerUnits, uint VertexArray)
{
    public static readonly RenderBaseline Default = new(true, false, BlendMode.Alpha, 0, PostFx.SamplerUnits, 0);
}
