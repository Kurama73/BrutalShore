namespace Shorebound.Tests;

/// <summary>
/// Pins down how the player moves: they fall, they land, and a jump is worth exactly one block.
/// </summary>
/// <remarks>
/// The jump height is the one number here a player would notice being wrong within a minute, and
/// it falls out of two others — the jump speed and the gravity — so it is asserted directly
/// rather than trusted to arithmetic done once in a comment.
/// </remarks>
public sealed class PlayerTests
{
    /// <summary>The step the game runs its simulation at.</summary>
    private const double Step = 1.0 / 60.0;

    [Fact]
    public void a_walking_player_falls_until_something_stops_them()
    {
        var player = Walking(new double3(0.5, 10.0, 0.5));

        Run(player, seconds: 3.0);

        // The floor fills y = 0, so its top is at y = 1 and that is where feet belong.
        Assert.True(player.IsOnGround);
        Assert.Equal(1.0, player.FeetMeters.Y, 1e-3);
    }

    [Fact]
    public void a_flying_player_hangs_where_they_are()
    {
        var player = new Player { IsFlying = true };
        player.TeleportTo(new double3(0.5, 10.0, 0.5));

        Run(player, seconds: 3.0);

        Assert.Equal(10.0, player.FeetMeters.Y, 1e-9);
        Assert.False(player.IsOnGround);
    }

    [Fact]
    public void a_jump_clears_a_one_block_step()
    {
        // The step's top is at y = 2, a metre above where the player starts.
        var world = TestWorlds.FloorWithStep(height: 1, fromX: 2);
        var player = Walking(new double3(0.5, 1.0, 0.5));

        JumpTowardsThenSettle(player, world);

        Assert.Equal(2.0, player.FeetMeters.Y, 1e-2);
        Assert.True(player.FeetMeters.X > 2.0, "the player never made it onto the step");
    }

    [Fact]
    public void a_jump_does_not_clear_a_two_block_step()
    {
        // The other side of the same tuning: a jump worth a block and a bit, and no more.
        var world = TestWorlds.FloorWithStep(height: 2, fromX: 2);
        var player = Walking(new double3(0.5, 1.0, 0.5));

        JumpTowardsThenSettle(player, world);

        Assert.Equal(1.0, player.FeetMeters.Y, 1e-2);
        Assert.True(player.FeetMeters.X < 2.0, "the player climbed a wall they should not have");
    }

    [Fact]
    public void walking_forward_goes_the_way_the_player_is_facing()
    {
        var east = Walking(new double3(0.5, 1.0, 0.5));
        var north = Walking(new double3(0.5, 1.0, 0.5));

        for (var tick = 0; tick < 60; tick++)
        {
            east.Step(TestWorlds.Floor, Step, Held(forward: 1.0), yaw: 0f);
            north.Step(TestWorlds.Floor, Step, Held(forward: 1.0), yaw: float.Pi / 2f);
        }

        Assert.True(east.FeetMeters.X > 4.0 && double.Abs(east.FeetMeters.Z - 0.5) < 1e-6);
        Assert.True(north.FeetMeters.Z > 4.0 && double.Abs(north.FeetMeters.X - 0.5) < 1e-6);
    }

    [Fact]
    public void walking_diagonally_is_no_faster_than_walking_straight()
    {
        var straight = Walking(new double3(0.5, 1.0, 0.5));
        var diagonal = Walking(new double3(0.5, 1.0, 0.5));

        for (var tick = 0; tick < 60; tick++)
        {
            straight.Step(TestWorlds.Floor, Step, Held(forward: 1.0), yaw: 0f);
            diagonal.Step(TestWorlds.Floor, Step, Held(forward: 1.0, right: 1.0), yaw: 0f);
        }

        // Pressing two directions at once must not be a speed bonus; 1e-6 is the drift of sixty
        // accumulated steps in f64.
        Assert.Equal(Travelled(straight), Travelled(diagonal), 1e-6);
    }

    [Fact]
    public void sprinting_covers_more_ground_than_walking()
    {
        var walk = Walking(new double3(0.5, 1.0, 0.5));
        var sprint = Walking(new double3(0.5, 1.0, 0.5));

        for (var tick = 0; tick < 60; tick++)
        {
            walk.Step(TestWorlds.Floor, Step, Held(forward: 1.0), yaw: 0f);
            sprint.Step(TestWorlds.Floor, Step, Held(forward: 1.0, sprint: true), yaw: 0f);
        }

        Assert.True(Travelled(sprint) > Travelled(walk) * 1.5);
    }

    [Fact]
    public void the_eyes_are_above_the_feet_and_not_at_them()
    {
        var player = Walking(new double3(3.0, 1.0, -4.0));

        Assert.Equal(1.0 + Player.EyeHeightMeters, player.EyeMeters.Y, 1e-9);
        Assert.Equal(3.0, player.EyeMeters.X, 1e-9);
        Assert.Equal(-4.0, player.EyeMeters.Z, 1e-9);
    }

    [Fact]
    public void a_block_where_the_player_stands_would_be_inside_them()
    {
        var player = Walking(new double3(0.5, 1.0, 0.5));

        // Feet at y = 1, head at 2.8, so blocks 1 and 2 are occupied and 3 is clear.
        Assert.True(player.WouldBeInsideMe(new int3(0, 1, 0)));
        Assert.True(player.WouldBeInsideMe(new int3(0, 2, 0)));
        Assert.False(player.WouldBeInsideMe(new int3(0, 3, 0)));
        Assert.False(player.WouldBeInsideMe(new int3(0, 0, 0)));
        Assert.False(player.WouldBeInsideMe(new int3(2, 1, 0)));
    }

    private static Player Walking(double3 feet)
    {
        var player = new Player { IsFlying = false };
        player.TeleportTo(feet);
        return player;
    }

    private static PlayerIntent Held(double forward = 0.0, double right = 0.0, bool jump = false, bool sprint = false) =>
        new()
        {
            Forward = forward,
            Right = right,
            Jump = jump,
            Sink = false,
            Sprint = sprint,
        };

    /// <summary>
    /// Walks forward jumping for two seconds, then walks forward for one more without jumping.
    /// </summary>
    /// <remarks>
    /// The second half is what makes the result a height rather than a moment: holding jump makes
    /// the player bounce the instant they land, so a reading taken while the key is down catches
    /// them part-way through whichever hop they happen to be on.
    /// </remarks>
    private static void JumpTowardsThenSettle(Player player, IVoxelSource world)
    {
        for (var tick = 0; tick < 120; tick++)
            player.Step(world, Step, Held(forward: 1.0, jump: true), yaw: 0f);

        for (var tick = 0; tick < 60; tick++)
            player.Step(world, Step, Held(forward: 1.0), yaw: 0f);
    }

    private static void Run(Player player, double seconds)
    {
        for (var tick = 0; tick < (int)(seconds / Step); tick++)
            player.Step(TestWorlds.Floor, Step, PlayerIntent.Still, yaw: 0f);
    }

    private static double Travelled(Player player) =>
        double.Sqrt(
            ((player.FeetMeters.X - 0.5) * (player.FeetMeters.X - 0.5))
            + ((player.FeetMeters.Z - 0.5) * (player.FeetMeters.Z - 0.5))
        );
}
