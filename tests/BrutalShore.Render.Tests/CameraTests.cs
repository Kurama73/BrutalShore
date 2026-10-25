namespace BrutalShore.Render.Tests;

public sealed class CameraTests
{
    private const float AspectRatio = 16f / 9f;

    [Fact]
    public void an_unturned_camera_looks_along_positive_x()
    {
        var camera = new Camera();

        Assert.Equal(1f, camera.Forward.X, 1e-5f);
        Assert.Equal(0f, camera.Forward.Y, 1e-5f);
        Assert.Equal(0f, camera.Forward.Z, 1e-5f);
    }

    [Fact]
    public void looking_up_or_down_stops_short_of_straight_up()
    {
        var camera = new Camera();

        camera.Look(0.0, -100_000.0, 0.01f);
        Assert.True(camera.Pitch < float.Pi / 2f);
        Assert.True(camera.Forward.Y < 1f);

        camera.Look(0.0, 200_000.0, 0.01f);
        Assert.True(camera.Pitch > -float.Pi / 2f);
        Assert.True(camera.Forward.Y > -1f);
    }

    [Fact]
    public void moving_forward_follows_the_yaw_and_ignores_the_pitch()
    {
        var camera = new Camera();

        // A quarter turn to the right, and as far down as the camera will look.
        camera.Look(float.Pi / 2f / 0.001f, 100_000.0, 0.001f);
        camera.Move(10.0, 0.0, 0.0);

        Assert.Equal(0.0, camera.Position.X, 1e-4);
        Assert.Equal(0.0, camera.Position.Y, 1e-9);
        Assert.Equal(10.0, camera.Position.Z, 1e-4);
    }

    [Fact]
    public void a_point_ahead_of_the_camera_lands_in_the_middle_of_the_screen()
    {
        var camera = new Camera();
        var clip = float4.Transform(new float3(20f, 0f, 0f), camera.ViewProjection(AspectRatio));

        // A positive w is what "in front of the camera" means in clip space.
        Assert.True(clip.W > 0f);
        Assert.Equal(0f, clip.X / clip.W, 1e-5f);
        Assert.Equal(0f, clip.Y / clip.W, 1e-5f);
    }

    [Fact]
    public void a_point_above_the_camera_lands_in_the_upper_half_of_the_screen()
    {
        var camera = new Camera();
        var clip = float4.Transform(new float3(20f, 5f, 0f), camera.ViewProjection(AspectRatio));

        // Vulkan's clip space has y pointing down, so the upper half of the screen is negative y.
        // This is the whole reason the projection carries a y flip, and getting it wrong renders
        // the world upside down.
        Assert.True(clip.Y / clip.W < 0f);
    }

    [Fact]
    public void a_point_to_the_right_of_the_camera_lands_in_the_right_half_of_the_screen()
    {
        var camera = new Camera();

        // The camera looks along positive x with y up, so its right is positive z.
        var clip = float4.Transform(new float3(20f, 0f, 5f), camera.ViewProjection(AspectRatio));

        Assert.True(clip.X / clip.W > 0f);
    }

    [Fact]
    public void a_point_behind_the_camera_is_behind_the_eye()
    {
        var camera = new Camera();
        var clip = float4.Transform(new float3(-20f, 0f, 0f), camera.ViewProjection(AspectRatio));

        Assert.True(clip.W < 0f);
    }

    [Fact]
    public void depth_runs_from_zero_at_the_near_plane_to_one_at_the_far_plane()
    {
        var camera = new Camera { NearPlane = 1f, FarPlane = 100f };
        var projection = camera.ViewProjection(AspectRatio);

        var near = float4.Transform(new float3(1f, 0f, 0f), projection);
        var far = float4.Transform(new float3(100f, 0f, 0f), projection);

        Assert.Equal(0f, near.Z / near.W, 1e-4f);
        Assert.Equal(1f, far.Z / far.W, 1e-4f);
    }
}
