namespace BrutalShore.World;

/// <summary>
/// Splitting an absolute block coordinate into the chunk that holds it and the offset within that
/// chunk, and putting it back together.
/// </summary>
/// <remarks>
/// This is the whole reason <see cref="Chunk.Size"/> is a power of two. A shift and a mask are
/// correct for negative coordinates, where integer division and remainder are not: C# rounds
/// division toward zero, so <c>-1 / 32</c> is 0 and <c>-1 % 32</c> is -1, which would put block -1
/// in chunk 0 at offset -1 — a coordinate no chunk has. An arithmetic right shift rounds toward
/// negative infinity, so block -1 lands in chunk -1 at offset 31, which is where it is.
/// </remarks>
public static class WorldCoordinates
{
    /// <summary>Which chunk, along one axis, holds an absolute block coordinate.</summary>
    public static int ToChunk(int blockCoordinate) => blockCoordinate >> Chunk.SizeLog2;

    /// <summary>The offset within its chunk, along one axis, of an absolute block coordinate.</summary>
    public static int ToLocal(int blockCoordinate) => blockCoordinate & (Chunk.Size - 1);

    /// <summary>Which chunk holds an absolute block coordinate.</summary>
    public static int3 ToChunk(int3 blockCoordinate) =>
        new(ToChunk(blockCoordinate.X), ToChunk(blockCoordinate.Y), ToChunk(blockCoordinate.Z));

    /// <summary>The offset within its chunk of an absolute block coordinate.</summary>
    public static int3 ToLocal(int3 blockCoordinate) =>
        new(ToLocal(blockCoordinate.X), ToLocal(blockCoordinate.Y), ToLocal(blockCoordinate.Z));

    /// <summary>The absolute block coordinate of a chunk's minimum corner.</summary>
    public static int3 ChunkOrigin(int3 chunkCoordinate) => chunkCoordinate * Chunk.Size;

    /// <summary>
    /// The absolute block coordinate a world position in metres falls inside, with one block to the
    /// metre.
    /// </summary>
    /// <remarks>
    /// Floored rather than truncated, for the same reason the shift above is: the block a position
    /// is in has to be the block whose cube contains it, on both sides of the origin.
    /// </remarks>
    public static int3 ToBlock(double3 positionMeters) =>
        new(
            (int)double.Floor(positionMeters.X),
            (int)double.Floor(positionMeters.Y),
            (int)double.Floor(positionMeters.Z)
        );
}
