namespace BrutalShore.Render.Tests;

public sealed class ViewFrustumTests
{
    private const float AspectRatio = 16f / 9f;

    private static ViewFrustum FrustumOf(Camera camera) =>
        ViewFrustum.FromViewProjection(camera.ViewProjection(AspectRatio));

    private static (float3 Minimum, float3 Maximum) BoxAt(float3 centre, float halfSize = 1f) =>
        (centre - new float3(halfSize), centre + new float3(halfSize));

    [Fact]
    public void a_box_straight_ahead_is_visible()
    {
        var (minimum, maximum) = BoxAt(new float3(50f, 0f, 0f));

        Assert.True(FrustumOf(new Camera()).Intersects(minimum, maximum));
    }

    [Fact]
    public void a_box_behind_the_camera_is_not_visible()
    {
        var (minimum, maximum) = BoxAt(new float3(-50f, 0f, 0f));

        Assert.False(FrustumOf(new Camera()).Intersects(minimum, maximum));
    }

    [Fact]
    public void a_box_beyond_the_far_plane_is_not_visible()
    {
        var camera = new Camera { FarPlane = 100f };
        var (minimum, maximum) = BoxAt(new float3(200f, 0f, 0f));

        Assert.False(FrustumOf(camera).Intersects(minimum, maximum));
    }

    [Fact]
    public void a_box_off_to_one_side_is_not_visible()
    {
        // Twenty metres ahead and two hundred to the side is far outside any sane field of view.
        var (minimum, maximum) = BoxAt(new float3(20f, 0f, 200f));

        Assert.False(FrustumOf(new Camera()).Intersects(minimum, maximum));
    }

    [Fact]
    public void a_box_above_the_camera_is_not_visible()
    {
        var (minimum, maximum) = BoxAt(new float3(20f, 200f, 0f));

        Assert.False(FrustumOf(new Camera()).Intersects(minimum, maximum));
    }

    [Fact]
    public void a_box_that_only_partly_overlaps_the_frustum_is_visible()
    {
        // Centred well off to the side, but wide enough to reach into the view. A test against the
        // box's centre alone would wrongly cull this, which is the mistake the corner test avoids.
        var minimum = new float3(20f, -1f, -400f);
        var maximum = new float3(22f, 1f, 1f);

        Assert.True(FrustumOf(new Camera()).Intersects(minimum, maximum));
    }

    [Fact]
    public void turning_the_camera_changes_what_is_visible()
    {
        var camera = new Camera();
        var (minimum, maximum) = BoxAt(new float3(0f, 0f, 50f));

        Assert.False(FrustumOf(camera).Intersects(minimum, maximum));

        // A quarter turn to the right, which is towards positive z.
        camera.Look(float.Pi / 2f / 0.001f, 0.0, 0.001f);

        Assert.True(FrustumOf(camera).Intersects(minimum, maximum));
    }

    [Fact]
    public void the_camera_is_always_inside_its_own_frustum_boundary_box()
    {
        // A box around the eye reaches past the near plane, so it is visible however it is turned.
        var (minimum, maximum) = BoxAt(float3.Zero, 10f);

        Assert.True(FrustumOf(new Camera()).Intersects(minimum, maximum));
    }
}
