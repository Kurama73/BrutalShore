namespace BrutalShore.World.Tests;

/// <summary>
/// Pins down what a ray reports: which block, which face, which empty space in front of it, and
/// how far away the surface is.
/// </summary>
/// <remarks>
/// Every one of those is something a player can see being wrong — breaking the block behind the
/// one they aimed at, placing one inside themselves, or reaching through a corner — and none of
/// them needs a window to check.
/// </remarks>
public sealed class VoxelRayTests
{
    private static readonly BlockId Ground = new(1);

    [Fact]
    public void a_ray_pointing_down_at_a_floor_lands_on_its_top_face()
    {
        var world = Floor(surfaceHeight: 10);

        var hit = VoxelRay.Cast(world, new double3(4.5, 14.0, 4.5), new double3(0, -1, 0), 32.0);

        Assert.NotNull(hit);
        Assert.Equal(new int3(4, 10, 4), hit.Value.Block);
        Assert.Equal(VoxelFace.PositiveY, hit.Value.Face);
    }

    [Fact]
    public void the_block_just_before_the_hit_is_where_a_new_one_would_go()
    {
        var world = Floor(surfaceHeight: 10);

        var hit = VoxelRay.Cast(world, new double3(4.5, 14.0, 4.5), new double3(0, -1, 0), 32.0);

        // Placing against a face means filling the empty block the ray was last in, and working
        // that out afterwards from the face means working the face out twice.
        Assert.Equal(new int3(4, 11, 4), hit!.Value.Before);
    }

    [Fact]
    public void the_distance_is_measured_to_the_surface_and_not_to_the_block()
    {
        var world = Floor(surfaceHeight: 10);

        var hit = VoxelRay.Cast(world, new double3(4.5, 14.0, 4.5), new double3(0, -1, 0), 32.0);

        // The top of block 10 is at y = 11, so a ray from 14 travels exactly 3 metres. 1e-9 is
        // several orders inside f64 for numbers this size; anything looser would hide a
        // half-block error.
        Assert.Equal(3.0, hit!.Value.DistanceMeters, 1e-9);
    }

    [Fact]
    public void a_ray_that_reaches_nothing_comes_back_empty()
    {
        var world = Floor(surfaceHeight: 10);

        Assert.Null(VoxelRay.Cast(world, new double3(4.5, 40.0, 4.5), new double3(0, -1, 0), 8.0));
        Assert.Null(VoxelRay.Cast(world, new double3(4.5, 40.0, 4.5), new double3(0, 1, 0), 1000.0));
    }

    [Fact]
    public void a_ray_stops_one_block_short_of_the_range_it_was_given()
    {
        var world = Floor(surfaceHeight: 10);

        // The surface is 3 metres down. Reaching exactly that far must hit; stopping just short
        // must not, or the reach a player is told they have is not the reach they get.
        Assert.NotNull(VoxelRay.Cast(world, new double3(4.5, 14.0, 4.5), new double3(0, -1, 0), 3.0));
        Assert.Null(VoxelRay.Cast(world, new double3(4.5, 14.0, 4.5), new double3(0, -1, 0), 2.99));
    }

    [Theory]
    [InlineData(1, 0, 0, VoxelFace.NegativeX)]
    [InlineData(-1, 0, 0, VoxelFace.PositiveX)]
    [InlineData(0, 0, 1, VoxelFace.NegativeZ)]
    [InlineData(0, 0, -1, VoxelFace.PositiveZ)]
    public void a_ray_enters_through_the_face_it_is_travelling_towards(int x, int y, int z, VoxelFace expected)
    {
        var world = SingleBlock(new int3(0, 0, 0));
        var origin = new double3(0.5 - (x * 4.0), 0.5 - (y * 4.0), 0.5 - (z * 4.0));

        var hit = VoxelRay.Cast(world, origin, new double3(x, y, z), 32.0);

        Assert.Equal(expected, hit!.Value.Face);
    }

    [Fact]
    public void a_ray_that_starts_inside_a_block_has_already_hit_it()
    {
        var world = SingleBlock(new int3(3, 3, 3));

        var hit = VoxelRay.Cast(world, new double3(3.5, 3.5, 3.5), new double3(1, 0, 0), 32.0);

        Assert.Equal(new int3(3, 3, 3), hit!.Value.Block);
        Assert.Equal(0.0, hit.Value.DistanceMeters);
    }

    [Fact]
    public void a_diagonal_ray_enters_the_block_it_passes_through()
    {
        // The line y = x - 0.5 clips the lower corner of the block at the origin. Stepping along
        // a ray in fixed increments is what steps over a clip like this, and the result is a
        // player able to reach through a wall at one particular angle.
        var world = SingleBlock(new int3(0, 0, 0));

        var hit = VoxelRay.Cast(world, new double3(-2.0, -2.5, 0.5), new double3(1, 1, 0), 32.0);

        Assert.Equal(new int3(0, 0, 0), hit!.Value.Block);
    }

    [Fact]
    public void a_diagonal_ray_that_passes_beside_a_block_misses_it()
    {
        // The line y = x - 1.5, which is past the block's far corner by half a metre.
        var world = SingleBlock(new int3(0, 0, 0));

        Assert.Null(VoxelRay.Cast(world, new double3(-2.0, -3.5, 0.5), new double3(1, 1, 0), 32.0));
    }

    [Fact]
    public void a_diagonal_ray_through_an_exact_corner_does_not_slip_past()
    {
        // The line y = x, which meets the block exactly at its corner. Advancing one axis at a
        // time is what makes this land inside rather than between: a traversal that moved both at
        // once would pass through the seam and come out the other side.
        var world = SingleBlock(new int3(0, 0, 0));

        var hit = VoxelRay.Cast(world, new double3(-2.0, -2.0, 0.5), new double3(1, 1, 0), 32.0);

        Assert.Equal(new int3(0, 0, 0), hit!.Value.Block);
    }

    [Fact]
    public void a_ray_far_from_the_origin_still_lands_on_the_right_block()
    {
        // Half a million metres out, an f32 would no longer resolve a block at all. The whole
        // cast stays in f64 for exactly this case.
        const int far = 512_345;
        var world = SingleBlock(new int3(far, 40, -far));

        var hit = VoxelRay.Cast(world, new double3(far + 0.5, 48.0, -far + 0.5), new double3(0, -1, 0), 32.0);

        Assert.Equal(new int3(far, 40, -far), hit!.Value.Block);
    }

    [Fact]
    public void a_ray_with_no_direction_is_rejected()
    {
        Assert.Throws<ArgumentException>(
            () => VoxelRay.Cast(Floor(10), double3.Zero, double3.Zero, 8.0)
        );
    }

    private static VoxelWorld Floor(int surfaceHeight)
    {
        var registry = new BlockRegistry();
        var ground = registry.Register(new BlockType { Name = "ground", Color = new byte3(128, 128, 128) });
        var world = new VoxelWorld(registry, new FlatGenerator(ground, surfaceHeight));

        for (var z = -1; z <= 1; z++)
        for (var x = -1; x <= 1; x++)
        for (var y = 0; y < VoxelWorld.ChunksTall; y++)
            world.Load(new int3(x, y, z));

        return world;
    }

    private static IVoxelSource SingleBlock(int3 at) => new OneBlockWorld(at, Ground);

    /// <summary>A world with exactly one solid block in it, wherever the test wants it.</summary>
    /// <remarks>
    /// Unbounded, so a test can put its block half a million metres out without loading anything.
    /// </remarks>
    private sealed class OneBlockWorld(int3 at, BlockId block) : IVoxelSource
    {
        public BlockId BlockAt(int x, int y, int z) =>
            x == at.X && y == at.Y && z == at.Z ? block : BlockId.Air;
    }
}
