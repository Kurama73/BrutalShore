using System.Runtime.InteropServices;

namespace BrutalShore.Render;

/// <summary>Everything the shaders read once per frame rather than once per draw.</summary>
/// <remarks>
/// <para>
/// The order of these fields is the layout. It is duplicated by hand at the top of every shader
/// that reads it —
/// <c>voxel.vert</c>, <c>voxel.frag</c> and <c>sky.frag</c>. Shaders are compiled from source at
/// startup with no include path, so there is nowhere to put one shared declaration; the comment
/// in each shader names this struct, and <c>SceneUniformsTests</c> reads both and fails if the
/// two ever stop agreeing — because they did once, and two swapped members produced a picture
/// that looked merely wrong rather than broken.
/// </para>
/// <para>
/// Every member is sixteen bytes or a multiple of it, which is what makes the plain sequential C#
/// layout and std140 agree. A <c>float</c> or a <c>float3</c> added here would not: pack it into
/// the spare <c>w</c> of a neighbour instead, the way the directions carry their factors.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct SceneUniforms
{
    /// <summary>The camera's view-projection, in render space.</summary>
    public readonly float4x4 ViewProjection;

    /// <summary>The inverse of <see cref="ViewProjection" />, which turns a pixel into a ray.</summary>
    public readonly float4x4 InverseViewProjection;

    /// <summary>Render space to the shadow map's clip space; see <see cref="SunShadow" />.</summary>
    public readonly float4x4 LightViewProjection;

    /// <summary>xyz: the unit direction towards the sun. w: the daylight factor.</summary>
    public readonly float4 SunDirection;

    /// <summary>rgb: the sunlight, zero once the sun is down.</summary>
    public readonly float4 SunColor;

    /// <summary>xyz: the unit direction towards the moon. w: how visible the stars are.</summary>
    public readonly float4 MoonDirection;

    /// <summary>rgb: the moonlight, zero once the moon is down.</summary>
    public readonly float4 MoonColor;

    /// <summary>rgb: the light the sky bounces back into faces nothing reaches.</summary>
    public readonly float4 AmbientColor;

    /// <summary>rgb: the sky's colour straight overhead.</summary>
    public readonly float4 ZenithColor;

    /// <summary>rgb: the sky's colour at the horizon.</summary>
    public readonly float4 HorizonColor;

    /// <summary>x: where fog starts, in metres. y: where it is total. zw: spare.</summary>
    public readonly float4 Fog;

    /// <summary>
    /// x: how much direct light a shadow takes away. y: how far along its normal a surface is
    /// nudged before being looked up, in metres. z: one shadow map texel in map coordinates.
    /// w: how much every light is scaled by before anything is drawn with it.
    /// </summary>
    public readonly float4 Shadow;

    /// <summary>
    /// rgb: the colour everything turns while the eye is inside a liquid. w: how much of it
    /// applies, 0 above the surface and 1 below it.
    /// </summary>
    /// <remarks>
    /// Being underwater is drawn as a change to the fog rather than as a pane over the screen.
    /// There is no water geometry between an eye inside a sea and the seabed — the faces inside a
    /// body of water are the ones the mesher throws away — so the only thing left to tint with is
    /// the distance, which is also the thing that makes being underwater feel like being
    /// underwater.
    /// </remarks>
    public readonly float4 Submerged;

    /// <summary>Packs one frame's view and sky into the block the shaders read.</summary>
    /// <param name="viewProjection">The camera's view-projection, in render space.</param>
    /// <param name="lightViewProjection">Render space to the shadow map's clip space.</param>
    /// <param name="sky">The sun, the moon and the sky's colours at this moment.</param>
    /// <param name="fogStartMeters">How far away fog begins.</param>
    /// <param name="fogEndMeters">How far away fog is total.</param>
    /// <param name="shadow">The shadow terms, laid out as <see cref="Shadow" /> describes.</param>
    /// <param name="submerged">The liquid the eye is in, laid out as <see cref="Submerged" /> describes.</param>
    public SceneUniforms(
        in float4x4 viewProjection,
        in float4x4 lightViewProjection,
        in SkyLight sky,
        float fogStartMeters,
        float fogEndMeters,
        float4 shadow,
        float4 submerged
    )
    {
        ViewProjection = viewProjection;
        LightViewProjection = lightViewProjection;

        // A camera matrix is always invertible, so a failure here would mean the caller built
        // something that is not one; leaving the identity behind makes that obvious on screen.
        InverseViewProjection = float4x4.Invert(viewProjection, out var inverse) ? inverse : float4x4.Identity;

        SunDirection = new float4(sky.SunDirection, sky.Daylight);
        SunColor = new float4(sky.SunColor, 0f);
        MoonDirection = new float4(sky.MoonDirection, sky.StarOpacity);
        MoonColor = new float4(sky.MoonColor, 0f);
        AmbientColor = new float4(sky.AmbientColor, 0f);
        ZenithColor = new float4(sky.ZenithColor, 0f);
        HorizonColor = new float4(sky.HorizonColor, 0f);
        Fog = new float4(fogStartMeters, fogEndMeters, 0f, 0f);
        Shadow = shadow;
        Submerged = submerged;
    }
}
