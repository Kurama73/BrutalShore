namespace BrutalShore.World;

/// <summary>What a ray ran into, and where.</summary>
/// <remarks>
/// <see cref="Before" /> is the whole reason a hit carries more than a coordinate: breaking a
/// block needs the one that was hit, placing one needs the empty space in front of it, and
/// working the second out from the first afterwards means working out the face again.
/// </remarks>
public readonly record struct VoxelHit
{
    /// <summary>The solid block the ray entered.</summary>
    public required int3 Block { get; init; }

    /// <summary>The empty block the ray was in immediately before, where a new one would go.</summary>
    public required int3 Before { get; init; }

    /// <summary>Which face of <see cref="Block" /> the ray came through.</summary>
    public required VoxelFace Face { get; init; }

    /// <summary>How far along the ray the surface is, in metres.</summary>
    public required double DistanceMeters { get; init; }
}

/// <summary>Walks a ray through the voxel grid and reports the first solid block it enters.</summary>
/// <remarks>
/// <para>
/// Amanatides and Woo, "A Fast Voxel Traversal Algorithm for Ray Tracing" (Eurographics 1987).
/// It visits every block the ray passes through, in order, and no others — which is what makes it
/// right rather than merely fast: stepping along a ray in small increments can step over the
/// corner of a block, and a player who can shoot through a wall notices.
/// </para>
/// <para>
/// Everything is in f64 world metres. A ray is cast from wherever the player is, which can be a
/// long way from the origin, and the block it lands on has to be exact.
/// </para>
/// </remarks>
public static class VoxelRay
{
    /// <summary>Casts a ray and returns the first solid block it enters.</summary>
    /// <param name="source">The blocks to walk through.</param>
    /// <param name="originMeters">Where the ray starts, in world metres.</param>
    /// <param name="direction">Which way it goes; need not be a unit vector, but must not be zero.</param>
    /// <param name="maxDistanceMeters">How far to walk before giving up.</param>
    /// <returns>The first hit, or <see langword="null" /> if the ray reached nothing in range.</returns>
    /// <exception cref="ArgumentException">The direction has no length.</exception>
    public static VoxelHit? Cast(
        IVoxelSource source,
        double3 originMeters,
        double3 direction,
        double maxDistanceMeters
    )
    {
        var length = double.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z)
        );
        if (length <= 0.0)
            throw new ArgumentException("A ray needs a direction with some length.", nameof(direction));

        var unit = new double3(direction.X / length, direction.Y / length, direction.Z / length);
        var block = WorldCoordinates.ToBlock(originMeters);

        // A ray that starts inside something has already hit it; there is no face to report, so
        // the one it would have come through if it were travelling backwards is the honest answer.
        if (source.BlockAt(block.X, block.Y, block.Z).IsSolid)
        {
            return new VoxelHit
            {
                Block = block,
                Before = block,
                Face = FaceOf(0, unit.X),
                DistanceMeters = 0.0,
            };
        }

        Span<int> step = [Sign(unit.X), Sign(unit.Y), Sign(unit.Z)];
        Span<double> nextCrossing =
        [
            FirstCrossing(originMeters.X, unit.X, block.X),
            FirstCrossing(originMeters.Y, unit.Y, block.Y),
            FirstCrossing(originMeters.Z, unit.Z, block.Z),
        ];
        Span<double> perBlock = [BlockCrossing(unit.X), BlockCrossing(unit.Y), BlockCrossing(unit.Z)];

        var travelled = 0.0;
        while (travelled <= maxDistanceMeters)
        {
            // Step along whichever axis reaches its next block boundary first. That is the whole
            // algorithm: it is exactly the order the ray crosses them in.
            var axis = nextCrossing[0] < nextCrossing[1]
                ? (nextCrossing[0] < nextCrossing[2] ? 0 : 2)
                : (nextCrossing[1] < nextCrossing[2] ? 1 : 2);

            travelled = nextCrossing[axis];
            if (travelled > maxDistanceMeters)
                return null;

            var before = block;
            block = Advance(block, axis, step[axis]);
            nextCrossing[axis] += perBlock[axis];

            if (source.BlockAt(block.X, block.Y, block.Z).IsSolid)
            {
                return new VoxelHit
                {
                    Block = block,
                    Before = before,
                    Face = FaceOf(axis, step[axis]),
                    DistanceMeters = travelled,
                };
            }
        }

        return null;
    }

    private static int Sign(double value) => value > 0.0 ? 1 : value < 0.0 ? -1 : 0;

    /// <summary>How far along the ray the first block boundary on an axis is.</summary>
    private static double FirstCrossing(double origin, double unit, int block)
    {
        if (unit == 0.0)
            return double.PositiveInfinity;

        var boundary = unit > 0.0 ? block + 1 : block;
        return (boundary - origin) / unit;
    }

    /// <summary>How far along the ray one whole block on an axis is.</summary>
    private static double BlockCrossing(double unit) =>
        unit == 0.0 ? double.PositiveInfinity : double.Abs(1.0 / unit);

    private static int3 Advance(int3 block, int axis, int step) =>
        axis switch
        {
            0 => new int3(block.X + step, block.Y, block.Z),
            1 => new int3(block.X, block.Y + step, block.Z),
            _ => new int3(block.X, block.Y, block.Z + step),
        };

    /// <summary>The face a ray stepping this way on this axis came through.</summary>
    /// <remarks>
    /// Stepping in the positive direction enters through the block's negative face, which is why
    /// the sign reads backwards here.
    /// </remarks>
    private static VoxelFace FaceOf(int axis, double step) =>
        axis switch
        {
            0 => step > 0.0 ? VoxelFace.NegativeX : VoxelFace.PositiveX,
            1 => step > 0.0 ? VoxelFace.NegativeY : VoxelFace.PositiveY,
            _ => step > 0.0 ? VoxelFace.NegativeZ : VoxelFace.PositiveZ,
        };
}
