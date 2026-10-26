namespace BrutalShore.World.Tests;

/// <summary>What bending the plane has to do, and what it must not do.</summary>
/// <remarks>
/// A warp is only useful if it is a well-behaved change of coordinates: bounded, so that a field
/// sampled through it still comes from somewhere nearby, and continuous, so that terrain built on
/// it does not tear. Those two are what is asserted. That it makes a field look better is not
/// something a test can say.
/// </remarks>
public sealed class WarpTests
{
    private readonly Warp _warp = new(20_260, scaleBlocks: 220.0f, strengthBlocks: 40.0f);

    [Fact]
    public void a_warp_never_moves_a_point_further_than_it_promised()
    {
        for (var step = 0; step < 3000; step++)
        {
            var x = step * 7.3f;
            var y = step * -3.1f;
            var (warpedX, warpedY) = _warp.Of(x, y);

            Assert.InRange(warpedX - x, -_warp.StrengthBlocks, _warp.StrengthBlocks);
            Assert.InRange(warpedY - y, -_warp.StrengthBlocks, _warp.StrengthBlocks);
        }
    }

    [Fact]
    public void a_warp_moves_the_two_axes_differently()
    {
        // Two offsets out of one field would slide the whole plane along a diagonal, which is a
        // translation and does nothing at all to how the terrain reads.
        var apart = 0;

        for (var step = 0; step < 1000; step++)
        {
            var x = step * 11.7f;
            var (warpedX, warpedY) = _warp.Of(x, x * 0.5f);

            if (float.Abs((warpedX - x) - (warpedY - (x * 0.5f))) > 1.0f)
                apart++;
        }

        Assert.True(apart > 900, $"the two axes moved together at {1000 - apart} places in a thousand");
    }

    [Fact]
    public void a_warp_is_continuous()
    {
        // Terrain sampled through a discontinuous warp tears: two neighbouring columns read from
        // two unrelated places, and the result is a one-block cliff out of nothing.
        var worst = 0.0f;

        for (var step = 0; step < 2000; step++)
        {
            var x = step * 0.5f;
            var (here, _) = _warp.Of(x, 40.0f);
            var (next, _) = _warp.Of(x + 1.0f, 40.0f);

            worst = float.Max(worst, float.Abs(next - here - 1.0f));
        }

        // One block of travel is 1/220th of a warp cell, and the offset is 40 blocks at the most,
        // so a block of movement can bend the sample by well under a block.
        Assert.True(worst < 1.0f, $"a block of travel moved the sample by {worst + 1.0f} blocks");
    }

    [Fact]
    public void a_warp_with_no_strength_is_the_plane_itself()
    {
        var still = new Warp(1, scaleBlocks: 100.0f, strengthBlocks: 0.0f);

        Assert.Equal((12.5f, -3.25f), still.Of(12.5f, -3.25f));
    }

    [Fact]
    public void the_same_seed_bends_the_plane_the_same_way_forever()
    {
        var again = new Warp(20_260, scaleBlocks: 220.0f, strengthBlocks: 40.0f);

        Assert.Equal(_warp.Of(500.0f, -250.0f), again.Of(500.0f, -250.0f));
    }

    [Fact]
    public void a_warp_needs_a_scale()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Warp(1, scaleBlocks: 0.0f, strengthBlocks: 10.0f));
    }
}
