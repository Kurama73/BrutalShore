namespace BrutalShore.World;

/// <summary>Where a body ended up, and what stopped it.</summary>
/// <remarks>
/// The flags matter as much as the position: standing on something is what allows a jump, and
/// hitting a ceiling is what has to cancel the rest of one.
/// </remarks>
public readonly record struct BodyMove
{
    /// <summary>Where the body's feet ended up, in world metres.</summary>
    public required double3 FeetMeters { get; init; }

    /// <summary>Whether something solid stopped it going down.</summary>
    public required bool IsOnGround { get; init; }

    /// <summary>Whether something solid stopped it going up.</summary>
    public required bool HitCeiling { get; init; }

    /// <summary>Whether something solid stopped it sideways.</summary>
    public required bool HitWall { get; init; }
}

/// <summary>Slides an upright box through the voxel grid without letting it into anything.</summary>
/// <remarks>
/// <para>
/// A body is an axis-aligned box standing on its feet: the position given is the middle of its
/// base, and the size is its width, its height and its depth. That is the shape a player is, and
/// it keeps the one coordinate anything else cares about — where the feet are — exact rather than
/// derived.
/// </para>
/// <para>
/// Each axis is resolved on its own, in the order up-down, then the two horizontal ones. Doing
/// them together would need the box's whole swept volume and a decision about which contact came
/// first; doing them apart gives sliding along a wall for free, which is what a player expects
/// from walking into one at an angle.
/// </para>
/// <para>
/// The move is cut into steps no longer than a block so that nothing can pass through anything:
/// a single long step could start on one side of a wall and end on the other with no overlap at
/// either end. Falling is what reaches those speeds first, and falling through the floor is the
/// bug this prevents.
/// </para>
/// </remarks>
public static class VoxelBody
{
    /// <summary>How far a body is kept clear of the block it stopped against, in metres.</summary>
    /// <remarks>
    /// A tenth of a millimetre. Landing exactly on a boundary leaves the box's base on the same
    /// coordinate as the block's top face, and whether that counts as overlapping then comes down
    /// to a rounding mode; standing just clear of it does not.
    /// </remarks>
    private const double Clearance = 1e-4;

    /// <summary>The longest single step taken, in metres.</summary>
    private const double MaxStepMeters = 0.5;

    /// <summary>Moves a body as far along a delta as the world allows.</summary>
    /// <param name="source">The blocks to collide against.</param>
    /// <param name="feetMeters">The middle of the body's base, in world metres.</param>
    /// <param name="sizeMeters">Its width, height and depth, in metres.</param>
    /// <param name="deltaMeters">How far it is trying to move.</param>
    /// <returns>Where it ended up and what stopped it.</returns>
    public static BodyMove Move(IVoxelSource source, double3 feetMeters, double3 sizeMeters, double3 deltaMeters)
    {
        var remaining = deltaMeters;
        var feet = feetMeters;
        var onGround = false;
        var ceiling = false;
        var wall = false;

        var longest = double.Max(
            double.Abs(deltaMeters.X),
            double.Max(double.Abs(deltaMeters.Y), double.Abs(deltaMeters.Z))
        );
        var steps = int.Max(1, (int)double.Ceiling(longest / MaxStepMeters));
        var perStep = new double3(remaining.X / steps, remaining.Y / steps, remaining.Z / steps);

        for (var step = 0; step < steps; step++)
        {
            // Vertical first: landing on something is what the horizontal moves then slide along.
            if (Slide(source, ref feet, sizeMeters, 1, perStep.Y))
            {
                if (perStep.Y < 0.0)
                    onGround = true;
                else
                    ceiling = true;
            }

            wall |= Slide(source, ref feet, sizeMeters, 0, perStep.X);
            wall |= Slide(source, ref feet, sizeMeters, 2, perStep.Z);
        }

        return new BodyMove
        {
            FeetMeters = feet,
            IsOnGround = onGround,
            HitCeiling = ceiling,
            HitWall = wall,
        };
    }

    /// <summary>Whether a body standing here has something solid immediately under it.</summary>
    /// <param name="source">The blocks to test against.</param>
    /// <param name="feetMeters">The middle of the body's base, in world metres.</param>
    /// <param name="sizeMeters">Its width, height and depth, in metres.</param>
    /// <returns><see langword="true" /> if it is resting on something.</returns>
    /// <remarks>
    /// Asked separately from <see cref="Move" /> because a body that has not moved this frame
    /// still needs to know whether it may jump, and because walking off a ledge has to stop being
    /// on the ground without anything having stopped it.
    /// </remarks>
    public static bool IsStandingOn(IVoxelSource source, double3 feetMeters, double3 sizeMeters)
    {
        var below = new double3(feetMeters.X, feetMeters.Y - (Clearance * 2.0), feetMeters.Z);
        return Overlaps(source, below, sizeMeters);
    }

    /// <summary>Moves one axis as far as it can; true if something stopped it short.</summary>
    private static bool Slide(IVoxelSource source, ref double3 feet, double3 size, int axis, double delta)
    {
        if (delta == 0.0)
            return false;

        var moved = Offset(feet, axis, delta);
        if (!Overlaps(source, moved, size))
        {
            feet = moved;
            return false;
        }

        // Something is in the way. The step is at most half a block, so the only boundary that
        // can be in between is the one the leading edge has just crossed; snapping back to it is
        // the whole resolution.
        var (low, high) = Extent(moved, size, axis);
        var stopped = delta > 0.0
            ? double.Floor(high) - Clearance - (high - Component(moved, axis))
            : double.Floor(low) + 1.0 + Clearance - (low - Component(moved, axis));

        feet = WithComponent(feet, axis, stopped);
        return true;
    }

    /// <summary>Whether a body standing at a position overlaps anything solid.</summary>
    private static bool Overlaps(IVoxelSource source, double3 feet, double3 size)
    {
        var minimum = new double3(feet.X - (size.X / 2.0), feet.Y, feet.Z - (size.Z / 2.0));
        var maximum = new double3(feet.X + (size.X / 2.0), feet.Y + size.Y, feet.Z + (size.Z / 2.0));

        // The upper bound is nudged inwards: a box whose face lies exactly on a block boundary is
        // touching that block, not inside it.
        var lowX = (int)double.Floor(minimum.X);
        var lowY = (int)double.Floor(minimum.Y);
        var lowZ = (int)double.Floor(minimum.Z);
        var highX = (int)double.Floor(maximum.X - Clearance);
        var highY = (int)double.Floor(maximum.Y - Clearance);
        var highZ = (int)double.Floor(maximum.Z - Clearance);

        for (var y = lowY; y <= highY; y++)
        for (var z = lowZ; z <= highZ; z++)
        for (var x = lowX; x <= highX; x++)
        {
            if (source.BlockAt(x, y, z).IsSolid)
                return true;
        }

        return false;
    }

    /// <summary>The body's extent along one axis, as a pair of world coordinates.</summary>
    private static (double Low, double High) Extent(double3 feet, double3 size, int axis) =>
        axis switch
        {
            0 => (feet.X - (size.X / 2.0), feet.X + (size.X / 2.0)),
            1 => (feet.Y, feet.Y + size.Y),
            _ => (feet.Z - (size.Z / 2.0), feet.Z + (size.Z / 2.0)),
        };

    private static double Component(double3 position, int axis) =>
        axis switch
        {
            0 => position.X,
            1 => position.Y,
            _ => position.Z,
        };

    private static double3 WithComponent(double3 position, int axis, double value) =>
        axis switch
        {
            0 => new double3(value, position.Y, position.Z),
            1 => new double3(position.X, value, position.Z),
            _ => new double3(position.X, position.Y, value),
        };

    private static double3 Offset(double3 position, int axis, double delta) =>
        WithComponent(position, axis, Component(position, axis) + delta);
}
