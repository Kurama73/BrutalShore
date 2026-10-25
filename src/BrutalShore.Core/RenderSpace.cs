namespace BrutalShore.Core;

/// <summary>
/// The one place where a world position in f64 metres becomes something a GPU can hold.
/// </summary>
/// <remarks>
/// <para>
/// Simulation state is f64 and absolute; vertex data is f32 and relative. The conversion subtracts
/// the observer's position <em>in f64</em> and only then narrows, which is what keeps a one-metre
/// detail stable while the camera sits 4e12 m from the origin. Narrowing first and subtracting
/// afterwards loses the detail entirely: an f32 holds about seven significant decimal digits, so at
/// 1e12 m its spacing is already tens of kilometres.
/// </para>
/// <para>
/// Every conversion from world space to render space goes through here. A second place that does
/// this subtraction by hand is a second place to get it wrong, and the symptom — geometry
/// shimmering only when far from the origin — is one of the harder ones to track down after the
/// fact.
/// </para>
/// </remarks>
public static class RenderSpace
{
    /// <summary>
    /// Converts an absolute world position to a render-space offset from
    /// <paramref name="originMeters"/>, scaled by <paramref name="unitsPerMeter"/>.
    /// </summary>
    public static float3 ToRenderSpace(double3 worldMeters, double3 originMeters, double unitsPerMeter)
    {
        var relative = (worldMeters - originMeters) * unitsPerMeter;
        return new float3((float)relative.X, (float)relative.Y, (float)relative.Z);
    }

    /// <summary>
    /// The largest world-space distance from the origin, in metres, at which an f32 render-space
    /// coordinate still resolves <paramref name="detailMeters"/>.
    /// </summary>
    /// <remarks>
    /// An f32 has 24 bits of mantissa, so the spacing between representable values at magnitude
    /// <c>v</c> is about <c>v * 2^-23</c>. Requiring that spacing to stay below the detail that
    /// must remain visible gives this budget — and the scale factor cancels out of it, which is the
    /// point worth remembering: choosing smaller render units buys no range at all, because it
    /// shrinks the detail by exactly as much as it shrinks the coordinate. Only a floating origin
    /// buys range.
    /// </remarks>
    public static double RenderSpaceBudgetMeters(double detailMeters) => detailMeters * (1 << 23);
}
