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

    /// <summary>How fast a crouching player walks, in metres per second.</summary>
    private const double CrouchSpeedMetersPerSecond = 1.6;

    /// <summary>How far the eyes drop while crouching, in metres.</summary>
    private const double CrouchDropMeters = 0.32;

    /// <summary>How quickly the eyes get most of the way to their new height, in seconds.</summary>
    /// <remarks>
    /// Short, but not nothing. A camera that snaps down the instant a key goes in reads as the
    /// world jumping rather than as the player ducking.
    /// </remarks>
    private const double CrouchEaseSeconds = 0.09;

    /// <summary>How tall a ledge is walked up rather than stopped at, in metres.</summary>
    /// <remarks>
    /// A whole block and a little over. Every ledge in a voxel world is a multiple of one metre,
    /// so a step of less than that is a step that never fires; the alternative is a player who
    /// has to jump to cross a ploughed field.
    /// </remarks>
    private const double StepHeightMeters = 1.05;

    /// <summary>How hard the ground pushes a body up to speed, in metres per second squared.</summary>
    /// <remarks>
    /// High enough that the first tenth of a second is most of the way to walking pace, which is
    /// what keeps the controls feeling direct, and low enough that there is a moment of weight
    /// at the start of a sprint rather than a teleport into one.
    /// </remarks>
    private const double GroundAccelerationMetersPerSecondSquared = 62.0;

    /// <summary>The same in mid-air, where there is much less to push against.</summary>
    private const double AirAccelerationMetersPerSecondSquared = 14.0;

    /// <summary>How quickly the ground takes speed away when nothing is being asked of it.</summary>
    private const double GroundFrictionPerSecond = 13.0;

    /// <summary>The same in the air, where almost nothing slows a body horizontally.</summary>
    private const double AirFrictionPerSecond = 0.6;

    /// <summary>The same in water, which takes speed away quickly whatever is being asked.</summary>
    private const double WaterFrictionPerSecond = 5.0;

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

    // Horizontal speed in world metres a second, kept between steps. Keeping it is the whole
    // difference between a body that is moved and a body that moves: without it, letting go of a
    // key stops a sprinting player dead, which no amount of animation hides.
    private double _velocityX;
    private double _velocityZ;
    private double _eyeDrop;

    /// <summary>Where the middle of the player's feet is, in world metres.</summary>
    public double3 FeetMeters { get; set; }

    /// <summary>Where the player's eyes are, which is where the camera and any ray start.</summary>
    public double3 EyeMeters =>
        new(FeetMeters.X, FeetMeters.Y + EyeHeightMeters - _eyeDrop, FeetMeters.Z);

    /// <summary>Whether the player is crouching.</summary>
    public bool IsCrouching { get; private set; }

    /// <summary>How fast the player is moving across the ground, in metres per second.</summary>
    public double SpeedMetersPerSecond =>
        double.Sqrt((_velocityX * _velocityX) + (_velocityZ * _velocityZ));

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
        IsCrouching = !IsFlying && intent.Crouch;

        EaseEyeHeight(seconds);

        var (sinYaw, cosYaw) = double.SinCos(yaw);

        // Moving diagonally must not be faster than moving straight, and normalising only when
        // both axes are pushed keeps a half-pressed stick from being scaled up.
        var push = double.Sqrt((intent.Forward * intent.Forward) + (intent.Right * intent.Right));
        var scale = push > 1.0 ? 1.0 / push : 1.0;
        var forward = intent.Forward * scale;
        var right = intent.Right * scale;

        var speed = IsFlying
            ? FlySpeedMetersPerSecond * (intent.Sprint ? FlySprintFactor : 1.0)
            : IsCrouching
                ? CrouchSpeedMetersPerSecond
                : WalkSpeedMetersPerSecond * (intent.Sprint ? SprintFactor : 1.0);

        if (IsSwimming)
            speed *= SwimDragFactor;

        // What the player is asking for, as a velocity rather than a displacement.
        var wantedX = ((forward * cosYaw) + (right * -sinYaw)) * speed;
        var wantedZ = ((forward * sinYaw) + (right * cosYaw)) * speed;

        Accelerate(wantedX, wantedZ, push > 0.0, seconds);

        var horizontal = new double3(_velocityX * seconds, 0.0, _velocityZ * seconds);

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

        var wanted = new double3(horizontal.X, vertical, horizontal.Z);

        // Crouching at an edge stops at it. Done by refusing the move rather than by undoing one,
        // because a body already over the drop has to be put back somewhere, and the only place
        // that is certainly safe is where it came from.
        if (IsCrouching && IsOnGround && vertical <= 0.0)
            wanted = WithoutStepsOffTheEdge(world, registry, wanted);

        var before = FeetMeters;
        var move = VoxelBody.Move(
            world,
            registry,
            FeetMeters,
            BodySize,
            wanted,
            IsFlying || IsSwimming ? 0.0 : StepHeightMeters
        );

        FeetMeters = move.FeetMeters;

        // Walking into a wall loses the speed that was going into it, the way it loses the
        // distance: without this a player held against a wall builds up speed in place and shoots
        // sideways the moment the wall ends.
        if (move.HitWall)
            StopAgainstWall(wanted, before, move.FeetMeters);

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

    /// <summary>Brings the horizontal velocity towards what the player is asking for.</summary>
    /// <remarks>
    /// Acceleration towards the wanted velocity, and friction when nothing is wanted. Both are
    /// per second and applied over the step, so the result does not depend on the frame rate —
    /// which it would if either were a fraction applied per step.
    /// </remarks>
    private void Accelerate(double wantedX, double wantedZ, bool pushing, double seconds)
    {
        if (IsFlying)
        {
            // Flying is direct. There is nothing to push against and nothing to slide on, and a
            // camera that drifts after the key is let go is a camera that overshoots a block.
            _velocityX = wantedX;
            _velocityZ = wantedZ;
            return;
        }

        var friction = IsSwimming
            ? WaterFrictionPerSecond
            : IsOnGround
                ? GroundFrictionPerSecond
                : AirFrictionPerSecond;

        var acceleration = IsOnGround || IsSwimming
            ? GroundAccelerationMetersPerSecondSquared
            : AirAccelerationMetersPerSecondSquared;

        if (pushing)
        {
            // Along the vector, not axis by axis. Per axis, a diagonal reaches each of its two
            // smaller targets sooner than a straight line reaches its one larger one, and the
            // result is that pressing two keys accelerates faster than pressing one.
            var towardX = wantedX - _velocityX;
            var towardZ = wantedZ - _velocityZ;
            var distance = double.Sqrt((towardX * towardX) + (towardZ * towardZ));
            var step = acceleration * seconds;

            if (distance <= step || distance <= 0.0)
            {
                _velocityX = wantedX;
                _velocityZ = wantedZ;
                return;
            }

            _velocityX += towardX / distance * step;
            _velocityZ += towardZ / distance * step;
            return;
        }

        var kept = double.Max(0.0, 1.0 - (friction * seconds));
        _velocityX *= kept;
        _velocityZ *= kept;
    }

    /// <summary>Takes the velocity out of whichever axis a wall stopped.</summary>
    /// <remarks>
    /// Compared against what was asked for rather than against the wall: an axis that covered
    /// less than a tenth of what it was told to is an axis that met something. Asking the world
    /// again would be the other way round, and would cost a second pass over the blocks for an
    /// answer the move already knows.
    /// </remarks>
    private void StopAgainstWall(double3 wanted, double3 before, double3 after)
    {
        if (double.Abs(wanted.X) > 1e-9 && double.Abs(after.X - before.X) < double.Abs(wanted.X) * 0.1)
            _velocityX = 0.0;

        if (double.Abs(wanted.Z) > 1e-9 && double.Abs(after.Z - before.Z) < double.Abs(wanted.Z) * 0.1)
            _velocityZ = 0.0;
    }

    /// <summary>Drops whichever parts of a move would take a crouching player over an edge.</summary>
    private double3 WithoutStepsOffTheEdge(IVoxelSource world, BlockRegistry registry, double3 wanted)
    {
        var alongX = new double3(FeetMeters.X + wanted.X, FeetMeters.Y, FeetMeters.Z);
        var alongZ = new double3(FeetMeters.X, FeetMeters.Y, FeetMeters.Z + wanted.Z);

        // One axis at a time, so edging along a ledge keeps whichever direction still has ground
        // under it — which is what makes crouching along a one-block ledge possible at all.
        var keepX = VoxelBody.IsStandingOn(world, registry, alongX, BodySize);
        var keepZ = VoxelBody.IsStandingOn(world, registry, alongZ, BodySize);

        return new double3(keepX ? wanted.X : 0.0, wanted.Y, keepZ ? wanted.Z : 0.0);
    }

    /// <summary>Moves the eyes towards where crouching or standing puts them.</summary>
    private void EaseEyeHeight(double seconds)
    {
        var target = IsCrouching ? CrouchDropMeters : 0.0;
        var caught = CrouchEaseSeconds <= 0.0 ? 1.0 : 1.0 - double.Exp(-seconds / (CrouchEaseSeconds / 3.0));
        _eyeDrop = double.Lerp(_eyeDrop, target, double.Clamp(caught, 0.0, 1.0));
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
        _velocityX = 0.0;
        _velocityZ = 0.0;
        IsOnGround = false;
    }
}
