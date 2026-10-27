using BrutalShore.Voxel;
using BrutalShore.World;

namespace Shorebound.Domain.Movement;

/// <summary>Where a creature's body is between one step and the next.</summary>
/// <remarks>
/// The part of a creature that is the same whatever it wants. A grazer wanders and a stalker
/// hunts, and neither of those has an opinion about gravity, ledges or how far a leg has swung.
/// </remarks>
public readonly record struct Footing
{
    /// <summary>The middle of its feet, in world metres.</summary>
    public required double3 FeetMeters { get; init; }

    /// <summary>How fast it is moving, in metres a second.</summary>
    public double3 VelocityMeters { get; init; }

    /// <summary>Whether it is resting on something solid.</summary>
    public bool IsOnGround { get; init; }

    /// <summary>
    /// How far through its stride it is, in radians, which is what swings its legs.
    /// </summary>
    /// <remarks>
    /// Kept rather than derived from the clock, so a creature that stops mid-step starts again
    /// from where it stopped instead of snapping to wherever the world's clock had got to.
    /// </remarks>
    public float StrideRadians { get; init; }

    /// <summary>Whether something solid stopped it sideways on the last step.</summary>
    public bool HitWall { get; init; }
}

/// <summary>Walking a creature's body around the world: gravity, ledges and legs.</summary>
/// <remarks>
/// <para>
/// Separated from any particular creature once there were two of them. The physics is identical
/// and the steering is not, so what is shared is a function from "where it is and how fast it
/// wants to go" to "where it ended up" — and what is not shared stays in whatever decided the
/// wanting.
/// </para>
/// <para>
/// Nothing here is the player's movement. The player accelerates along a vector, has a step
/// height that interacts with crouching, swims, flies and is steered by a keyboard; a creature
/// does none of that, and one routine trying to be both would be a routine full of flags.
/// </para>
/// </remarks>
public static class Walking
{
    /// <summary>How hard a creature is pulled down, in metres a second squared.</summary>
    private const double GravityMetersPerSecondSquared = 24.0;

    /// <summary>The fastest one falls, in metres a second.</summary>
    private const double TerminalSpeedMetersPerSecond = 40.0;

    /// <summary>How far the legs swing per metre walked, in radians.</summary>
    private const double StrideRadiansPerMeter = 3.4;

    /// <summary>Moves a creature one step towards the speed it is asking for.</summary>
    /// <param name="footing">Where it is now.</param>
    /// <param name="world">The blocks it walks on.</param>
    /// <param name="registry">Where a block id is resolved to what it does to a body.</param>
    /// <param name="seconds">How long this step is.</param>
    /// <param name="sizeMeters">Its width, height and depth.</param>
    /// <param name="wantedMetersPerSecond">How fast it is trying to move along the ground.</param>
    /// <param name="accelerationMetersPerSecondSquared">How quickly it gets up to that.</param>
    /// <param name="stepHeightMeters">How tall a ledge it climbs rather than stops at.</param>
    /// <param name="mindsTheEdge">
    /// Whether it refuses to walk off a drop. True for something grazing, which would otherwise
    /// spend the day falling into the sea; false for something chasing, which should follow the
    /// player off a ledge rather than stopping politely at the top of it.
    /// </param>
    /// <param name="yawRadians">Which way its rendered body faces, in radians.</param>
    /// <returns>Where it ended up.</returns>
    public static Footing Step(
        in Footing footing,
        IVoxelSource world,
        BlockRegistry registry,
        double seconds,
        double3 sizeMeters,
        double3 wantedMetersPerSecond,
        double accelerationMetersPerSecondSquared,
        double stepHeightMeters,
        bool mindsTheEdge,
        float yawRadians
    )
    {
        var collisionSize = OrientedSize(sizeMeters, yawRadians);
        var velocity = footing.VelocityMeters;
        var change = accelerationMetersPerSecondSquared * seconds;

        velocity.X = Approach(velocity.X, wantedMetersPerSecond.X, change);
        velocity.Z = Approach(velocity.Z, wantedMetersPerSecond.Z, change);
        velocity.Y = double.Max(
            velocity.Y - (GravityMetersPerSecondSquared * seconds),
            -TerminalSpeedMetersPerSecond
        );

        var wantedMove = new double3(velocity.X * seconds, velocity.Y * seconds, velocity.Z * seconds);
        var stopped = false;

        if (mindsTheEdge && footing.IsOnGround && velocity.Y <= 0.0
            && !KeepsItsFooting(world, registry, footing.FeetMeters, collisionSize, wantedMove))
        {
            wantedMove = new double3(0.0, wantedMove.Y, 0.0);
            velocity.X = 0.0;
            velocity.Z = 0.0;
            stopped = true;
        }

        var before = footing.FeetMeters;
        var move = VoxelBody.Move(world, registry, before, collisionSize, wantedMove, stepHeightMeters);

        if (move.IsOnGround || move.HitCeiling)
            velocity.Y = 0.0;

        var travelled = move.FeetMeters - before;
        var alongGround = double.Sqrt((travelled.X * travelled.X) + (travelled.Z * travelled.Z));

        return new Footing
        {
            FeetMeters = move.FeetMeters,
            VelocityMeters = velocity,
            IsOnGround = VoxelBody.IsStandingOn(world, registry, move.FeetMeters, collisionSize),
            StrideRadians = (float)(footing.StrideRadians + (alongGround * StrideRadiansPerMeter)),

            // A creature that stopped itself at a ledge has the same problem as one that walked
            // into a wall — it wants to be somewhere it cannot go — so both report the same thing
            // and whatever is steering can think again.
            HitWall = move.HitWall || stopped,
        };
    }

    /// <summary>Projects a creature's local box into the axis-aligned bounds of its rendered yaw.</summary>
    /// <param name="sizeMeters">Its width, height and depth before rotation.</param>
    /// <param name="yawRadians">Its rotation around the upright axis, in radians.</param>
    /// <returns>The smallest upright box containing the rotated horizontal footprint.</returns>
    public static double3 OrientedSize(double3 sizeMeters, float yawRadians)
    {
        var (sine, cosine) = double.SinCos(yawRadians);
        var sin = double.Abs(sine);
        var cos = double.Abs(cosine);

        return new double3(
            (cos * sizeMeters.X) + (sin * sizeMeters.Z),
            sizeMeters.Y,
            (sin * sizeMeters.X) + (cos * sizeMeters.Z)
        );
    }

    /// <summary>The height a creature would stand at in a column, or nothing if it could not.</summary>
    /// <param name="world">The world to look in.</param>
    /// <param name="registry">Where a block id is resolved to what it does to a body.</param>
    /// <param name="x">The column's x, in blocks.</param>
    /// <param name="z">Its z.</param>
    /// <param name="highest">The altitude to start looking down from.</param>
    /// <returns>Where its feet would be, or <see langword="null" />.</returns>
    /// <remarks>
    /// Searched from the top down, so what is found is the surface rather than the roof of the
    /// first cave. A caller that wants a cave passes a lower ceiling to start from.
    /// </remarks>
    public static double? StandingHeight(
        VoxelWorld world,
        BlockRegistry registry,
        int x,
        int z,
        int highest
    )
    {
        for (var y = int.Min(highest, VoxelWorld.HeightBlocks - 2); y > 1; y--)
        {
            var type = registry[world.BlockAt(x, y, z)];
            if (!type.Collides)
                continue;

            // Standing on it, and not in whatever is above it: a block at the bottom of a lake is
            // solid ground with three metres of water on top.
            if (registry[world.BlockAt(x, y + 1, z)].IsLiquid)
                return null;

            return y + 1.0;
        }

        return null;
    }

    /// <summary>How far apart two points are along the ground, in metres.</summary>
    /// <param name="from">One of them.</param>
    /// <param name="to">The other.</param>
    /// <returns>The distance, ignoring height.</returns>
    public static double Away(double3 from, double3 to)
    {
        var apart = to - from;
        return double.Sqrt((apart.X * apart.X) + (apart.Z * apart.Z));
    }

    private static bool KeepsItsFooting(
        IVoxelSource world,
        BlockRegistry registry,
        double3 feet,
        double3 size,
        double3 move
    ) => VoxelBody.IsStandingOn(world, registry, new double3(feet.X + move.X, feet.Y, feet.Z + move.Z), size);

    private static double Approach(double value, double target, double by) =>
        value < target ? double.Min(value + by, target) : double.Max(value - by, target);
}
