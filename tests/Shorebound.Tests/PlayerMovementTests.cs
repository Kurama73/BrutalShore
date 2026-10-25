namespace Shorebound.Tests;

/// <summary>
/// How the player's movement feels, stated as things that are true of it: it takes a moment to
/// reach speed, it keeps going for a moment after, it climbs a ledge, and crouching stops it at
/// an edge.
/// </summary>
/// <remarks>
/// Feel is not usually testable and these claims are not about feel — they are about the
/// properties feel is made of. Each one is a thing that would silently stop being true if the
/// acceleration, the friction or the step height were rewritten, and each one is something a
/// player would notice within a second of it going.
/// </remarks>
public sealed class PlayerMovementTests
{
    /// <summary>One simulation step, matching the host's fixed rate.</summary>
    private const double Step = 1.0 / 60.0;

    [Fact]
    public void a_standing_player_does_not_reach_full_speed_in_a_single_step()
    {
        var player = Walking();

        player.Step(TestWorlds.Floor, TestWorlds.Walls, Step, Held(forward: 1.0), yaw: 0f);

        // Instant velocity is the thing this replaces. Anything near the walking speed after one
        // sixtieth of a second means the acceleration is not being applied at all.
        Assert.True(
            player.SpeedMetersPerSecond < 2.0,
            $"one step reached {player.SpeedMetersPerSecond:F2} m/s, which is no acceleration at all"
        );
    }

    [Fact]
    public void a_player_holding_forward_settles_at_the_walking_speed()
    {
        var player = Walking();

        for (var tick = 0; tick < 60; tick++)
            player.Step(TestWorlds.Floor, TestWorlds.Walls, Step, Held(forward: 1.0), yaw: 0f);

        Assert.Equal(4.8, player.SpeedMetersPerSecond, 0.01);
    }

    [Fact]
    public void letting_go_slows_the_player_down_over_a_moment_rather_than_at_once()
    {
        var player = Walking();

        for (var tick = 0; tick < 60; tick++)
            player.Step(TestWorlds.Floor, TestWorlds.Walls, Step, Held(forward: 1.0), yaw: 0f);

        player.Step(TestWorlds.Floor, TestWorlds.Walls, Step, Held(), yaw: 0f);
        var afterOneStep = player.SpeedMetersPerSecond;

        for (var tick = 0; tick < 60; tick++)
            player.Step(TestWorlds.Floor, TestWorlds.Walls, Step, Held(), yaw: 0f);

        Assert.True(afterOneStep > 3.0, $"letting go dropped straight to {afterOneStep:F2} m/s");
        Assert.True(player.SpeedMetersPerSecond < 0.05, "a second later the player is still moving");
    }

    [Fact]
    public void the_player_walks_up_a_single_block_ledge_without_jumping()
    {
        // The ledge starts at x = 3, a block tall. The player walks into it from well before it
        // with nothing but a forward key held.
        var world = TestWorlds.FloorWithStep(height: 1, fromX: 3);
        var player = Walking(new double3(0.5, 1.0, 0.5));

        for (var tick = 0; tick < 180; tick++)
            player.Step(world, TestWorlds.Walls, Step, Held(forward: 1.0), yaw: 0f);

        Assert.True(player.FeetMeters.X > 3.5, $"the player stopped at x {player.FeetMeters.X:F2}");
        Assert.Equal(2.0, player.FeetMeters.Y, 0.01);
    }

    [Fact]
    public void the_player_does_not_walk_up_a_ledge_two_blocks_tall()
    {
        var world = TestWorlds.FloorWithStep(height: 2, fromX: 3);
        var player = Walking(new double3(0.5, 1.0, 0.5));

        for (var tick = 0; tick < 180; tick++)
            player.Step(world, TestWorlds.Walls, Step, Held(forward: 1.0), yaw: 0f);

        Assert.Equal(1.0, player.FeetMeters.Y, 0.01);
        Assert.True(player.FeetMeters.X < 3.0, $"the player climbed to x {player.FeetMeters.X:F2}");
    }

    [Fact]
    public void crouching_stops_the_player_at_the_edge_of_a_drop()
    {
        // A ledge the player is standing on top of, walking towards where it ends.
        var world = new LedgeTo(4);
        var player = Walking(new double3(0.5, 2.0, 0.5));

        for (var tick = 0; tick < 180; tick++)
            player.Step(world, TestWorlds.Walls, Step, Held(forward: 1.0, crouch: true), yaw: 0f);

        Assert.True(player.IsCrouching);
        Assert.Equal(2.0, player.FeetMeters.Y, 0.01);

        // The ledge's last block ends at x = 5, and the body is six tenths across, so a crouching
        // player comes to rest with half of it over the drop and its far edge still on the block.
        // Stopping at the block's centre instead would mean never being able to build outwards.
        Assert.InRange(player.FeetMeters.X, 4.5, 5.31);
    }

    [Fact]
    public void not_crouching_walks_straight_off_the_same_edge()
    {
        // The other half of the claim: the guard is crouching, not the ledge.
        var world = new LedgeTo(4);
        var player = Walking(new double3(0.5, 2.0, 0.5));

        for (var tick = 0; tick < 180; tick++)
            player.Step(world, TestWorlds.Walls, Step, Held(forward: 1.0), yaw: 0f);

        Assert.True(player.FeetMeters.X > 4.5, $"the player stopped at x {player.FeetMeters.X:F2} without crouching");
        Assert.Equal(1.0, player.FeetMeters.Y, 0.01);
    }

    [Fact]
    public void crouching_lowers_the_eyes_and_standing_raises_them_again()
    {
        var player = Walking();
        var standing = player.EyeMeters.Y;

        for (var tick = 0; tick < 60; tick++)
            player.Step(TestWorlds.Floor, TestWorlds.Walls, Step, Held(crouch: true), yaw: 0f);

        var crouched = player.EyeMeters.Y;

        for (var tick = 0; tick < 60; tick++)
            player.Step(TestWorlds.Floor, TestWorlds.Walls, Step, Held(), yaw: 0f);

        Assert.True(crouched < standing - 0.25, $"crouching only dropped the eyes to {crouched:F2}");
        Assert.Equal(standing, player.EyeMeters.Y, 0.01);
    }

    [Fact]
    public void crouching_is_slower_than_walking()
    {
        var crouched = Walking();
        var walking = Walking();

        for (var tick = 0; tick < 60; tick++)
        {
            crouched.Step(TestWorlds.Floor, TestWorlds.Walls, Step, Held(forward: 1.0, crouch: true), yaw: 0f);
            walking.Step(TestWorlds.Floor, TestWorlds.Walls, Step, Held(forward: 1.0), yaw: 0f);
        }

        Assert.True(crouched.SpeedMetersPerSecond < walking.SpeedMetersPerSecond * 0.5);
    }

    [Fact]
    public void a_player_held_against_a_wall_does_not_build_up_speed_behind_it()
    {
        // The failure this guards against is the catapult: velocity accumulating into a wall and
        // all of it arriving at once the moment the wall ends.
        var world = TestWorlds.FloorWithStep(height: 3, fromX: 2);
        var player = Walking(new double3(0.5, 1.0, 0.5));

        for (var tick = 0; tick < 120; tick++)
            player.Step(world, TestWorlds.Walls, Step, Held(forward: 1.0, sprint: true), yaw: 0f);

        Assert.True(
            player.SpeedMetersPerSecond < 1.0,
            $"pressed against a wall the player was still carrying {player.SpeedMetersPerSecond:F2} m/s"
        );
    }

    private static Player Walking() => Walking(new double3(0.5, 1.0, 0.5));

    private static Player Walking(double3 feet)
    {
        var player = new Player { IsFlying = false };
        player.TeleportTo(feet);
        return player;
    }

    private static PlayerIntent Held(
        double forward = 0.0,
        double right = 0.0,
        bool jump = false,
        bool sprint = false,
        bool crouch = false
    ) =>
        new()
        {
            Forward = forward,
            Right = right,
            Jump = jump,
            Sink = false,
            Sprint = sprint,
            Crouch = crouch,
        };

    /// <summary>A floor at y = 0 with a one-block ledge on top of it, ending at an x.</summary>
    private sealed class LedgeTo(int lastX) : IVoxelSource
    {
        public BlockId BlockAt(int x, int y, int z) =>
            y == 0 || (y == 1 && x <= lastX) ? new BlockId(1) : BlockId.Air;
    }
}
