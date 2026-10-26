namespace BrutalShore.Render.Tests;

/// <summary>
/// Pins down the shadow box: where render space lands in the map, which way depth runs, and that
/// the box stays on a world-fixed grid as the camera moves.
/// </summary>
/// <remarks>
/// Between them these fix the whole convention — the handedness, the depth direction and the
/// texel grid — which is the part of a shadow map that cannot be checked by looking at a picture,
/// because every one of those being wrong produces a plausible-looking wrong picture.
/// </remarks>
public sealed class SunShadowTests
{
    private const float Radius = 100f;
    private const float Depth = 400f;
    private const int Resolution = 1024;

    /// <summary>
    /// A light straight up, for which the light's own axes can be worked out by hand: looking
    /// down from directly overhead puts the map's x along world -x and its y along world +z.
    /// </summary>
    private static readonly float3 Overhead = new(0f, 1f, 0f);

    private static readonly float3 MapAcross = new(-1f, 0f, 0f);
    private static readonly float3 MapUp = new(0f, 0f, 1f);

    [Fact]
    public void the_camera_lands_in_the_middle_of_the_map()
    {
        var matrix = SunShadow.LightViewProjection(double3.Zero, Overhead, Radius, Depth, Resolution);
        var clip = Project(matrix, float3.Zero);

        // The camera is the render-space origin, so this is the one point whose place in the map
        // is not a matter of taste. 1e-5 is the drift of a chain of four f32 multiplies.
        Assert.Equal(0f, clip.X, 1e-5f);
        Assert.Equal(0f, clip.Y, 1e-5f);
        Assert.Equal(0.5f, clip.Z, 1e-5f);
    }

    [Fact]
    public void the_radius_reaches_exactly_to_the_edge_of_the_map()
    {
        var matrix = SunShadow.LightViewProjection(double3.Zero, Overhead, Radius, Depth, Resolution);

        Assert.Equal(1f, Project(matrix, MapAcross * Radius).X, 1e-5f);
        Assert.Equal(-1f, Project(matrix, MapAcross * -Radius).X, 1e-5f);
        Assert.Equal(1f, Project(matrix, MapUp * Radius).Y, 1e-5f);
        Assert.Equal(-1f, Project(matrix, MapUp * -Radius).Y, 1e-5f);
    }

    [Fact]
    public void depth_runs_from_nothing_at_the_light_to_one_away_from_it()
    {
        var matrix = SunShadow.LightViewProjection(double3.Zero, Overhead, Radius, Depth, Resolution);

        // Getting this backwards is the classic shadow bug: everything shadows everything, or
        // nothing shadows anything, and the picture looks explicable either way.
        Assert.Equal(0f, Project(matrix, Overhead * Depth).Z, 1e-5f);
        Assert.Equal(1f, Project(matrix, Overhead * -Depth).Z, 1e-5f);
    }

    [Fact]
    public void something_nearer_the_light_is_nearer_in_the_map()
    {
        var light = float3.Normalize(new float3(0.3f, 0.8f, 0.5f));
        var matrix = SunShadow.LightViewProjection(double3.Zero, light, Radius, Depth, Resolution);

        var high = Project(matrix, new float3(4f, 40f, -7f));
        var low = Project(matrix, new float3(4f, 10f, -7f));

        Assert.True(high.Z < low.Z, "the higher point should have been written nearer the light");
    }

    [Fact]
    public void a_point_beyond_the_radius_falls_outside_the_map()
    {
        var matrix = SunShadow.LightViewProjection(double3.Zero, Overhead, Radius, Depth, Resolution);

        // This is what the shader's fade at the map's edge relies on being able to detect.
        Assert.True(float.Abs(Project(matrix, MapAcross * (Radius * 1.5f)).X) > 1f);
    }

    [Fact]
    public void moving_the_camera_a_whole_texel_leaves_the_map_exactly_where_it_was()
    {
        var texelMeters = 2.0 * Radius / Resolution;
        var moved = new double3(MapAcross.X * texelMeters, 0.0, 0.0);

        var before = SunShadow.LightViewProjection(double3.Zero, Overhead, Radius, Depth, Resolution);
        var after = SunShadow.LightViewProjection(moved, Overhead, Radius, Depth, Resolution);

        // The snapping is the whole reason shadow edges do not crawl, and a whole texel of camera
        // movement is the case where it has to land back on the same grid line.
        Assert.Equal(Project(before, float3.Zero).X, Project(after, float3.Zero).X, 1e-5f);
        Assert.Equal(Project(before, float3.Zero).Y, Project(after, float3.Zero).Y, 1e-5f);
    }

    [Fact]
    public void moving_the_camera_half_a_texel_slides_the_map_by_half_a_texel()
    {
        var texelMeters = 2.0 * Radius / Resolution;
        var moved = new double3(MapAcross.X * texelMeters * 0.5, 0.0, 0.0);

        var matrix = SunShadow.LightViewProjection(moved, Overhead, Radius, Depth, Resolution);

        // Half a texel in metres is half of 2/Resolution in clip units, which is 1/Resolution.
        Assert.Equal(1f / Resolution, Project(matrix, float3.Zero).X, 1e-5f);
    }

    [Fact]
    public void the_matrix_survives_a_light_that_is_straight_overhead()
    {
        // A look-to matrix degenerates when the direction and the up vector line up, and the
        // result is a map full of NaN that silently shadows nothing.
        var matrix = SunShadow.LightViewProjection(double3.Zero, new float3(0f, 1f, 0f), Radius, Depth, Resolution);
        var clip = Project(matrix, new float3(12f, 30f, -5f));

        Assert.False(float.IsNaN(clip.X) || float.IsNaN(clip.Y) || float.IsNaN(clip.Z));
    }

    [Fact]
    public void a_world_far_from_the_origin_still_snaps_to_the_grid()
    {
        var texelMeters = 2.0 * Radius / Resolution;

        // Half a million metres out, an f32 would no longer resolve a tenth of a texel; the
        // offset has to be reduced in f64 before anything narrows, and this is that claim.
        var far = new double3(-512345.0, 0.0, 0.0);
        var farPlusTexel = new double3(far.X + (MapAcross.X * texelMeters), 0.0, 0.0);

        var before = SunShadow.LightViewProjection(far, Overhead, Radius, Depth, Resolution);
        var after = SunShadow.LightViewProjection(farPlusTexel, Overhead, Radius, Depth, Resolution);

        Assert.Equal(Project(before, float3.Zero).X, Project(after, float3.Zero).X, 1e-4f);
    }

    /// <summary>
    /// Transforms a render-space point by a view-projection the way the shader does.
    /// </summary>
    /// <remarks>
    /// Written out rather than taken from a helper because the convention is the thing under
    /// test: this project builds matrices for row vectors, <c>p * M</c>, and the shaders get the
    /// column-vector reading of the same bytes for free because C# lays the struct out in rows
    /// and GLSL reads a <c>mat4</c> in columns.
    /// </remarks>
    private static float3 Project(in float4x4 matrix, float3 point)
    {
        var x = (point.X * matrix.M11) + (point.Y * matrix.M21) + (point.Z * matrix.M31) + matrix.M41;
        var y = (point.X * matrix.M12) + (point.Y * matrix.M22) + (point.Z * matrix.M32) + matrix.M42;
        var z = (point.X * matrix.M13) + (point.Y * matrix.M23) + (point.Z * matrix.M33) + matrix.M43;
        var w = (point.X * matrix.M14) + (point.Y * matrix.M24) + (point.Z * matrix.M34) + matrix.M44;
        return new float3(x / w, y / w, z / w);
    }
}
