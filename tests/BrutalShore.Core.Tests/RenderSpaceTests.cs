namespace BrutalShore.Core.Tests;

/// <summary>
/// Pins down the boundary between the world's f64 coordinates and the f32 the GPU is given.
/// </summary>
/// <remarks>
/// This is the one routine in the engine where the order of two operations is the whole point. A
/// world coordinate a hundred kilometres out has no room left in f32 for a centimetre; the same
/// point measured from the camera has room for a micrometre. Subtracting first is what buys that,
/// and these tests are what stop someone tidying the subtraction to the other side of the cast.
/// </remarks>
public sealed class RenderSpaceTests
{
    [Fact]
    public void the_origin_of_render_space_is_wherever_the_camera_is()
    {
        var camera = new double3(123456.75, -8901.5, 42.25);

        var rendered = RenderSpace.ToRenderSpace(camera, camera, 1.0);

        Assert.Equal(0f, rendered.X);
        Assert.Equal(0f, rendered.Y);
        Assert.Equal(0f, rendered.Z);
    }

    [Fact]
    public void a_point_a_metre_away_is_a_metre_away_however_far_from_the_origin_it_is()
    {
        // A hundred thousand metres out, f32 resolves about eight millimetres. If the narrowing
        // happened before the subtraction, this would come back wrong by several of those.
        var camera = new double3(100_000.0, 0.0, 100_000.0);
        var nearby = new double3(100_001.0, 0.0, 100_000.0);

        var rendered = RenderSpace.ToRenderSpace(nearby, camera, 1.0);

        Assert.Equal(1f, rendered.X, 1e-6f);
    }

    [Fact]
    public void a_centimetre_still_reads_as_a_centimetre_a_hundred_kilometres_out()
    {
        var camera = new double3(100_000.0, 64.0, -100_000.0);
        var nudged = new double3(100_000.01, 64.0, -100_000.0);

        var rendered = RenderSpace.ToRenderSpace(nudged, camera, 1.0);

        // 1e-9 is far inside what f32 holds for a number this small; the point is that the
        // magnitude of the camera's position has dropped out entirely before the cast.
        Assert.Equal(0.01f, rendered.X, 1e-9f);
    }

    [Fact]
    public void the_scale_is_applied_before_the_narrowing_too()
    {
        var camera = new double3(1000.0, 0.0, 0.0);
        var point = new double3(1002.0, 0.0, 0.0);

        Assert.Equal(2f, RenderSpace.ToRenderSpace(point, camera, 1.0).X, 1e-6f);
        Assert.Equal(200f, RenderSpace.ToRenderSpace(point, camera, 100.0).X, 1e-4f);
    }

    [Fact]
    public void the_budget_says_how_far_render_space_reaches_for_a_wanted_detail()
    {
        // f32 has twenty-three bits of mantissa, so a wanted detail of one millimetre holds out
        // to about eight kilometres, and a wanted metre holds out to over eight thousand.
        Assert.Equal(8388.608, RenderSpace.RenderSpaceBudgetMeters(0.001), 1e-3);
        Assert.Equal(8_388_608.0, RenderSpace.RenderSpaceBudgetMeters(1.0), 1e-3);
    }

    [Fact]
    public void the_budget_is_an_honest_bound_on_what_f32_actually_holds()
    {
        // Claimed and measured: at the far edge of the budget, the wanted detail is still the
        // step between two neighbouring f32 values rather than something smaller than one.
        const double detail = 0.01;
        var reach = RenderSpace.RenderSpaceBudgetMeters(detail);

        var atTheEdge = (float)reach;
        var oneStepOn = float.BitIncrement(atTheEdge);

        Assert.True(oneStepOn - atTheEdge <= detail, "f32 cannot resolve the detail the budget promises");
    }
}
