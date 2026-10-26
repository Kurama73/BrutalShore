namespace BrutalShore.Render;

/// <summary>Fits the box the shadow map is rendered through around the camera.</summary>
/// <remarks>
/// <para>
/// One map covering a radius around the camera, not a cascade. A cascade buys range, and range is
/// what fog is already throwing away: everything past the fog's end is sky-coloured, so a second
/// map would be spent on geometry nobody can make out. If the view distance ever grows past what
/// one map can cover at a usable texel size, that is the moment to split it — and the rule here
/// is that a second use case comes first.
/// </para>
/// <para>
/// The box is centred on the camera rather than pushed ahead of it. Pushing it ahead would almost
/// double the useful coverage, at the cost of every shadow in view sliding when the player turns
/// around; a world made of hard-edged blocks shows that sliding clearly.
/// </para>
/// <para>
/// Everything is in render space, where the camera sits at the origin — so the box never moves
/// and the world slides through it. That is exactly the case that makes shadow edges crawl, which
/// is what the snapping in <see cref="LightViewProjection" /> is for.
/// </para>
/// </remarks>
public static class SunShadow
{
    /// <summary>The light direction at which the up vector has to be swapped for another.</summary>
    /// <remarks>
    /// A look-to matrix degenerates when the direction and the up vector line up. The sun's arc is
    /// tilted away from the zenith precisely so that cannot happen, but the shadow light is given
    /// from outside and a straight-overhead one must not produce a matrix full of NaN.
    /// </remarks>
    private const float VerticalLimit = 0.995f;

    /// <summary>Builds the matrix the shadow map is rendered with and sampled through.</summary>
    /// <param name="cameraMeters">
    /// Where the camera is in the world. Only its position on the light's texel grid is used, so
    /// the result is in render space like everything else.
    /// </param>
    /// <param name="lightDirection">The unit direction towards the light.</param>
    /// <param name="radiusMeters">How far from the camera the map reaches, in every direction.</param>
    /// <param name="depthMeters">
    /// How far the box extends along the light, each way from the camera. It has to clear the
    /// tallest thing that can cast into the box, or a mountain behind the camera stops casting.
    /// </param>
    /// <param name="resolution">The shadow map's size in texels, which sets the snapping grid.</param>
    /// <returns>A view-projection mapping render space into the shadow map's clip space.</returns>
    public static float4x4 LightViewProjection(
        double3 cameraMeters,
        float3 lightDirection,
        float radiusMeters,
        float depthMeters,
        int resolution
    )
    {
        var direction = float3.Normalize(lightDirection);
        var up = float.Abs(direction.Y) > VerticalLimit ? new float3(0f, 0f, 1f) : new float3(0f, 1f, 0f);

        // The eye is at the render-space origin looking away from the light, so the box's depth
        // runs symmetrically either side of the camera and needs no separate centring.
        var view = float4x4.CreateLookTo(float3.Zero, -direction, up);

        // The light's own axes, read back off the matrix so the two cannot disagree: under the
        // row-vector convention this project uses, a point's light-space x is its dot with the
        // first column.
        var across = new float3(view.M11, view.M21, view.M31);
        var alongUp = new float3(view.M12, view.M22, view.M32);

        var texelMeters = 2f * radiusMeters / resolution;
        var shiftAcross = SnapOffset(cameraMeters, across, radiusMeters, texelMeters);
        var shiftUp = SnapOffset(cameraMeters, alongUp, radiusMeters, texelMeters);

        // Near is negative because the view looks down -Z: a point towards the light has a
        // positive light-space z, and the box has to reach past the camera in both directions.
        var projection = float4x4.CreateOrthographicOffCenter(
            -radiusMeters - shiftAcross,
            radiusMeters - shiftAcross,
            -radiusMeters - shiftUp,
            radiusMeters - shiftUp,
            -depthMeters,
            depthMeters
        );

        return view * projection;
    }

    /// <summary>How far to slide the box so its edge lands on a world-fixed texel boundary.</summary>
    /// <remarks>
    /// Without this the box is pinned to the camera and the world slides through it by fractions
    /// of a texel every frame, so every shadow edge crawls. The arithmetic stays in f64 because
    /// the world coordinate it reduces can be far from the origin, and the fraction is all that
    /// survives — narrowing first would quantise exactly the digits being asked for.
    /// </remarks>
    private static float SnapOffset(double3 cameraMeters, float3 axis, float radiusMeters, float texelMeters)
    {
        var alongAxis =
            (cameraMeters.X * axis.X) + (cameraMeters.Y * axis.Y) + (cameraMeters.Z * axis.Z) - radiusMeters;
        var inTexels = alongAxis / texelMeters;
        return (float)((inTexels - double.Floor(inTexels)) * texelMeters);
    }
}
