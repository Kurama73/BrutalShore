namespace BrutalShore.World.Tests;

/// <summary>
/// Pins down how a body meets the world: it lands on things, it slides along them, and it never
/// ends up inside one.
/// </summary>
/// <remarks>
/// The last of those is the one worth the tests. A body that stops a little short of a wall is a
/// visible annoyance; a body that passes through the floor at terminal velocity is the end of the
/// session, and it only happens at speeds a hand test never reaches.
/// </remarks>
public sealed class VoxelBodyTests
{
    /// <summary>A player-shaped box: six tenths across, one and four fifths tall.</summary>
    private static readonly double3 PlayerSize = new(0.6, 1.8, 0.6);

    [Fact]
    public void a_body_in_open_air_moves_the_whole_way()
    {
        var world = new FloorAt(0);

        var move = VoxelBody.Move(world, new double3(0.5, 20.0, 0.5), PlayerSize, new double3(1.0, 0.0, 2.0));

        Assert.Equal(1.5, move.FeetMeters.X, 1e-9);
        Assert.Equal(2.5, move.FeetMeters.Z, 1e-9);
        Assert.False(move.IsOnGround);
    }

    [Fact]
    public void a_body_falling_onto_the_ground_comes_to_rest_on_top_of_it()
    {
        // The floor fills y = 0, so its top face is at y = 1 and that is where feet belong.
        var world = new FloorAt(0);

        var move = VoxelBody.Move(world, new double3(0.5, 6.0, 0.5), PlayerSize, new double3(0.0, -10.0, 0.0));

        Assert.True(move.IsOnGround);
        Assert.Equal(1.0, move.FeetMeters.Y, 1e-3);
        Assert.True(move.FeetMeters.Y >= 1.0, "the body sank into the floor");
    }

    [Fact]
    public void a_body_falling_fast_still_lands_on_the_floor_rather_than_through_it()
    {
        // Terminal velocity in one step. Resolving a move in one go, rather than in pieces no
        // longer than a block, is what lets this start above the floor and end below it with no
        // overlap at either end to notice.
        var world = new FloorAt(0);

        var move = VoxelBody.Move(world, new double3(0.5, 300.0, 0.5), PlayerSize, new double3(0.0, -600.0, 0.0));

        Assert.True(move.IsOnGround);
        Assert.Equal(1.0, move.FeetMeters.Y, 1e-3);
    }

    [Fact]
    public void a_body_jumping_into_a_ceiling_stops_under_it()
    {
        var world = new FloorAndCeiling(floor: 0, ceiling: 5);

        var move = VoxelBody.Move(world, new double3(0.5, 1.0, 0.5), PlayerSize, new double3(0.0, 6.0, 0.0));

        Assert.True(move.HitCeiling);

        // The ceiling's underside is at y = 5, and the body is 1.8 tall, so its feet stop at 3.2.
        Assert.Equal(3.2, move.FeetMeters.Y, 1e-3);
        Assert.True(move.FeetMeters.Y + PlayerSize.Y <= 5.0, "the body's head went into the ceiling");
    }

    [Fact]
    public void a_body_walking_into_a_wall_stops_against_it()
    {
        var world = new WallAt(4);

        var move = VoxelBody.Move(world, new double3(0.5, 1.0, 0.5), PlayerSize, new double3(8.0, 0.0, 0.0));

        Assert.True(move.HitWall);

        // The wall's near face is at x = 4, and the body is 0.6 across, so its middle stops at 3.7.
        Assert.Equal(3.7, move.FeetMeters.X, 1e-3);
    }

    [Fact]
    public void a_body_walking_into_a_wall_at_an_angle_slides_along_it()
    {
        var world = new WallAt(4);

        var move = VoxelBody.Move(world, new double3(0.5, 1.0, 0.5), PlayerSize, new double3(8.0, 0.0, 3.0));

        // Resolving the axes separately is what gives this: the push into the wall is thrown
        // away and the movement along it is kept, which is what walking along a wall feels like.
        Assert.True(move.HitWall);
        Assert.Equal(3.7, move.FeetMeters.X, 1e-3);
        Assert.Equal(3.5, move.FeetMeters.Z, 1e-9);
    }

    [Fact]
    public void standing_on_the_ground_is_reported_without_having_to_move()
    {
        var world = new FloorAt(0);

        // Walking off a ledge has to stop being on the ground with nothing having stopped it, so
        // this cannot be something only a blocked move can report.
        Assert.True(VoxelBody.IsStandingOn(world, new double3(0.5, 1.0, 0.5), PlayerSize));
        Assert.False(VoxelBody.IsStandingOn(world, new double3(0.5, 4.0, 0.5), PlayerSize));
    }

    [Fact]
    public void a_body_that_has_landed_does_not_sink_on_the_next_move()
    {
        var world = new FloorAt(0);
        var feet = new double3(0.5, 1.0, 0.5);

        // Gravity keeps pulling after a landing. A resolution that gave back even a fraction of a
        // millimetre each time would bury a standing player over a few minutes.
        for (var tick = 0; tick < 600; tick++)
            feet = VoxelBody.Move(world, feet, PlayerSize, new double3(0.0, -0.16, 0.0)).FeetMeters;

        Assert.Equal(1.0, feet.Y, 1e-3);
    }

    [Fact]
    public void a_body_far_from_the_origin_lands_just_as_exactly()
    {
        var world = new FloorAt(0);
        const double far = 512_345.5;

        var move = VoxelBody.Move(world, new double3(far, 6.0, -far), PlayerSize, new double3(0.0, -10.0, 0.0));

        // Half a million metres out there is no room left in f32 for a tenth of a millimetre of
        // clearance; the whole resolution stays in f64 for this case.
        Assert.True(move.IsOnGround);
        Assert.Equal(1.0, move.FeetMeters.Y, 1e-3);
    }

    /// <summary>One solid layer of blocks, unbounded sideways.</summary>
    private sealed class FloorAt(int level) : IVoxelSource
    {
        public BlockId BlockAt(int x, int y, int z) => y == level ? new BlockId(1) : BlockId.Air;
    }

    /// <summary>A floor and a ceiling, unbounded sideways.</summary>
    private sealed class FloorAndCeiling(int floor, int ceiling) : IVoxelSource
    {
        public BlockId BlockAt(int x, int y, int z) =>
            y == floor || y == ceiling ? new BlockId(1) : BlockId.Air;
    }

    /// <summary>A floor, and a wall filling everything from one x onwards.</summary>
    private sealed class WallAt(int x) : IVoxelSource
    {
        public BlockId BlockAt(int blockX, int blockY, int blockZ) =>
            blockY == 0 || blockX >= x ? new BlockId(1) : BlockId.Air;
    }
}
