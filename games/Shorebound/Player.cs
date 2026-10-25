using BrutalShore.Voxel;
using BrutalShore.World;

namespace Shorebound;

/// <summary>The body the player moves around in, and the rules it moves by.</summary>
/// <remarks>
/// <para>
/// All the numbers here are this game's. The engine knows how to slide a box through voxels and
/// nothing about how fast a person walks, how high they jump or whether they can fly; those are
/// exactly the decisions that would be different in a different game, so they live in this one.
/// </para>
/// <para>
/// Gravity is about three times the real thing. A jump under 9.81 takes most of a second to come
/// down from, which reads as the moon; the number here is tuned so that a jump clears one block
/// and lands quickly, which is what a world built out of one-metre steps needs.
/// </para>
/// </remarks>
public sealed class Player
{
    /// <summary>How far above the feet the eyes are, in metres.</summary>
    public const double EyeHeightMeters = 1.62;

    /// <summary>How far away a block can be broken or placed, in metres.</summary>
    public const double ReachMeters = 5.0;

    private const double GravityMetersPerSecondSquared = 28.0;
    private const double TerminalSpeedMetersPerSecond = 70.0;

    private const double WalkSpeedMetersPerSecond = 4.8;
    private const double SprintFactor = 1.9;

    /// <summary>How fast a jump leaves the ground, in metres per second.</summary>
    /// <remarks>
    /// A jump reaches v squared over twice gravity, so 8.6 against 28 is about 1.32 metres: enough
    /// to step onto a one-block ledge with something to spare, and not enough for two.
    /// </remarks>
    private const double JumpSpeedMetersPerSecond = 8.6;

    private const double FlySpeedMetersPerSecond = 14.0;
    private const double FlySprintFactor = 5.0;

    /// <summary>How much of its weight a body keeps once it is in water.</summary>
    /// <remarks>
    /// A quarter. Enough that letting go still sinks, little enough that a fall into the sea is
    /// survivable rather than a drop to the seabed.
    /// </remarks>
    private const double BuoyancyFactor = 0.25;

    /// <summary>How fast a body can sink through water, in metres per second.</summary>
    private const double SinkSpeedMetersPerSecond = 3.0;

    /// <summary>How fast holding jump swims upwards, in metres per second.</summary>
    private const double SwimSpeedMetersPerSecond = 4.2;

    /// <summary>How much of the walking speed is left when swimming.</summary>
    private const double SwimDragFactor = 0.55;

    /// <summary>The box the player occupies: width, height and depth in metres.</summary>
    private static readonly double3 BodySize = new(0.6, 1.8, 0.6);

    private double _fallSpeed;

    /// <summary>Where the middle of the player's feet is, in world metres.</summary>
    public double3 FeetMeters { get; set; }

    /// <summary>Where the player's eyes are, which is where the camera and any ray start.</summary>
    public double3 EyeMeters => new(FeetMeters.X, FeetMeters.Y + EyeHeightMeters, FeetMeters.Z);

    /// <summary>The box the player occupies: width, height and depth in metres.</summary>
    public static double3 Size => BodySize;

    /// <summary>Whether the player is flying rather than walking.</summary>
    public bool IsFlying { get; set; } = true;

    /// <summary>Whether the player is resting on something solid.</summary>
    public bool IsOnGround { get; private set; }

    /// <summary>Whether the player is in a liquid, and so swimming rather than falling.</summary>
    public bool IsSwimming { get; private set; }

    /// <summary>How fast the player is falling, in metres per second.</summary>
    public double FallSpeedMetersPerSecond => _fallSpeed;

    /// <summary>Moves the player for one simulation step.</summary>
    /// <param name="world">The blocks to move through.</param>
    /// <param name="registry">Where a block id is resolved to what it does to a body.</param>
    /// <param name="seconds">How long the step is.</param>
    /// <param name="intent">What the player is asking for.</param>
    /// <param name="yaw">Which way the player is facing, in radians.</param>
    public void Step(
        IVoxelSource world,
        BlockRegistry registry,
        double seconds,
        in PlayerIntent intent,
        float yaw
    )
    {
        IsSwimming = !IsFlying && IsInLiquid(world, registry);

        var (sinYaw, cosYaw) = double.SinCos(yaw);

        // Moving diagonally must not be faster than moving straight, and normalising only when
        // both axes are pushed keeps a half-pressed stick from being scaled up.
        var push = double.Sqrt((intent.Forward * intent.Forward) + (intent.Right * intent.Right));
        var scale = push > 1.0 ? 1.0 / push : 1.0;
        var forward = intent.Forward * scale;
        var right = intent.Right * scale;

        var speed = IsFlying
            ? FlySpeedMetersPerSecond * (intent.Sprint ? FlySprintFactor : 1.0)
            : WalkSpeedMetersPerSecond * (intent.Sprint ? SprintFactor : 1.0);

        if (IsSwimming)
            speed *= SwimDragFactor;

        var horizontal = new double3(
            ((forward * cosYaw) + (right * -sinYaw)) * speed * seconds,
            0.0,
            ((forward * sinYaw) + (right * cosYaw)) * speed * seconds
        );

        double vertical;
        if (IsFlying)
        {
            _fallSpeed = 0.0;
            var rise = (intent.Jump ? 1.0 : 0.0) - (intent.Sink ? 1.0 : 0.0);
            vertical = rise * speed * seconds;
        }
        else if (IsSwimming)
        {
            // Holding jump swims; letting go sinks, slowly. Both are a speed rather than an
            // acceleration, because water has far too much drag for anything else to read right.
            _fallSpeed = intent.Jump
                ? -SwimSpeedMetersPerSecond
                : double.Min(
                    _fallSpeed + (GravityMetersPerSecondSquared * BuoyancyFactor * seconds),
                    SinkSpeedMetersPerSecond
                );

            vertical = -_fallSpeed * seconds;
        }
        else
        {
            if (IsOnGround && intent.Jump)
                _fallSpeed = -JumpSpeedMetersPerSecond;

            _fallSpeed = double.Min(
                _fallSpeed + (GravityMetersPerSecondSquared * seconds),
                TerminalSpeedMetersPerSecond
            );
            vertical = -_fallSpeed * seconds;
        }

        var move = VoxelBody.Move(
            world,
            registry,
            FeetMeters,
            BodySize,
            new double3(horizontal.X, vertical, horizontal.Z)
        );

        FeetMeters = move.FeetMeters;

        if (IsFlying)
        {
            IsOnGround = false;
            return;
        }

        // Landing and hitting a ceiling both end the fall, in opposite directions: one because
        // there is nowhere further down, the other so a jump into a block does not keep rising.
        if (move.IsOnGround || move.HitCeiling)
            _fallSpeed = 0.0;

        // Asked rather than taken from the move, because walking off a ledge leaves the ground
        // with nothing having stopped the body.
        IsOnGround = VoxelBody.IsStandingOn(world, registry, FeetMeters, BodySize);
    }

    /// <summary>Whether a block placed here would be inside the player.</summary>
    /// <param name="blockCoordinate">Where the block would go.</param>
    /// <returns><see langword="true" /> if placing it would trap the player inside it.</returns>
    /// <remarks>
    /// Checked at the moment of placing rather than resolved afterwards: a body pushed out of a
    /// block it is already inside has to be pushed somewhere, and every choice is wrong.
    /// </remarks>
    public bool WouldBeInsideMe(int3 blockCoordinate)
    {
        var minimum = new double3(
            FeetMeters.X - (BodySize.X / 2.0),
            FeetMeters.Y,
            FeetMeters.Z - (BodySize.Z / 2.0)
        );
        var maximum = new double3(
            FeetMeters.X + (BodySize.X / 2.0),
            FeetMeters.Y + BodySize.Y,
            FeetMeters.Z + (BodySize.Z / 2.0)
        );

        return blockCoordinate.X < double.Ceiling(maximum.X)
            && blockCoordinate.X + 1 > double.Floor(minimum.X)
            && blockCoordinate.Y < double.Ceiling(maximum.Y)
            && blockCoordinate.Y + 1 > double.Floor(minimum.Y)
            && blockCoordinate.Z < double.Ceiling(maximum.Z)
            && blockCoordinate.Z + 1 > double.Floor(minimum.Z);
    }

    /// <summary>Whether the block at the player's feet is something to swim in.</summary>
    /// <remarks>
    /// The feet rather than the eyes: stepping into shallow water should slow the player down
    /// immediately, and a body up to its waist in the sea is already swimming in it.
    /// </remarks>
    private bool IsInLiquid(IVoxelSource world, BlockRegistry registry)
    {
        var at = WorldCoordinates.ToBlock(new double3(FeetMeters.X, FeetMeters.Y + 0.1, FeetMeters.Z));
        return registry[world.BlockAt(at.X, at.Y, at.Z)].IsLiquid;
    }

    /// <summary>Puts the player somewhere, standing still.</summary>
    /// <param name="feetMeters">Where the middle of their feet goes.</param>
    public void TeleportTo(double3 feetMeters)
    {
        FeetMeters = feetMeters;
        _fallSpeed = 0.0;
        IsOnGround = false;
    }
}
