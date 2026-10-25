namespace RustSpace.Voxel;

/// <summary>
/// A fixed cube of blocks: the unit the world is stored, generated, meshed and uploaded in.
/// </summary>
/// <remarks>
/// <para>
/// 32 blocks on a side. The size is a power of two so that splitting a world coordinate into a
/// chunk and an offset is a shift and a mask rather than a division with a sign to worry about,
/// and 32 specifically because it is the size where the two costs balance: smaller chunks mean
/// more draw calls and more seams to re-mesh when a block changes, larger ones mean re-meshing more
/// geometry for the same single block change. 32³ is 32,768 blocks, 64 KiB of ids.
/// </para>
/// <para>
/// A chunk holds blocks and nothing else — no mesh, no GPU handle, no neighbour pointers. What is
/// drawn for a chunk is derived from it and cached elsewhere, so that the thing being saved, sent
/// over a network or edited by a command is just the blocks.
/// </para>
/// </remarks>
public sealed class Chunk
{
    /// <summary>Blocks along one edge of a chunk.</summary>
    public const int Size = 32;

    /// <summary>
    /// The power of two <see cref="Size"/> is, so callers can shift instead of divide.
    /// </summary>
    public const int SizeLog2 = 5;

    /// <summary>Blocks in a whole chunk.</summary>
    public const int BlockCount = Size * Size * Size;

    private readonly BlockId[] _blocks = new BlockId[BlockCount];

    /// <summary>How many of the chunk's blocks are not air.</summary>
    /// <remarks>
    /// Kept as a running count so that "is there anything here to draw?" is a comparison rather
    /// than a scan of 32,768 entries. Most chunks in a terrain world are entirely air or entirely
    /// stone, and both answer that question without being meshed at all.
    /// </remarks>
    public int SolidCount { get; private set; }

    /// <summary>Whether the chunk is entirely air, so there is nothing to mesh.</summary>
    public bool IsEmpty => SolidCount == 0;

    /// <summary>Whether the chunk has no air at all, so only its outer faces can ever be seen.</summary>
    public bool IsFull => SolidCount == BlockCount;

    /// <summary>
    /// Increments every time a block changes, so a cached mesh can tell whether it is stale without
    /// comparing 64 KiB of ids.
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>
    /// The flat index of a local coordinate.
    /// </summary>
    /// <remarks>
    /// X varies fastest and Y slowest. X fastest is what the mesher's inner loop and any horizontal
    /// scan walk, and Y slowest keeps a vertical column of a chunk contiguous in strides, which is
    /// the order terrain generation fills it in.
    /// </remarks>
    public static int IndexOf(int x, int y, int z) => x | (z << SizeLog2) | (y << (SizeLog2 * 2));

    /// <summary>Whether a local coordinate is inside the chunk.</summary>
    public static bool Contains(int x, int y, int z) =>
        (uint)x < Size && (uint)y < Size && (uint)z < Size;

    /// <summary>
    /// The block at a local coordinate, which must be inside the chunk.
    /// </summary>
    public BlockId this[int x, int y, int z]
    {
        get => _blocks[IndexOf(x, y, z)];
        set => Set(x, y, z, value);
    }

    /// <summary>The block at a local coordinate, which must be inside the chunk.</summary>
    public BlockId this[int3 local]
    {
        get => _blocks[IndexOf(local.X, local.Y, local.Z)];
        set => Set(local.X, local.Y, local.Z, value);
    }

    /// <summary>
    /// Writes a block, keeping <see cref="SolidCount"/> and <see cref="Revision"/> in step.
    /// </summary>
    /// <returns>Whether anything changed.</returns>
    public bool Set(int x, int y, int z, BlockId block)
    {
        var index = IndexOf(x, y, z);
        var previous = _blocks[index];
        if (previous == block)
            return false;

        if (previous.IsAir && block.IsSolid)
            SolidCount++;
        else if (previous.IsSolid && block.IsAir)
            SolidCount--;

        _blocks[index] = block;
        Revision++;
        return true;
    }

    /// <summary>
    /// Fills the chunk with one block, which is the fast path terrain generation takes for the
    /// chunks that are all air or all stone.
    /// </summary>
    public void Fill(BlockId block)
    {
        Array.Fill(_blocks, block);
        SolidCount = block.IsAir ? 0 : BlockCount;
        Revision++;
    }

    /// <summary>
    /// The blocks, in <see cref="IndexOf"/> order, for a caller that walks all of them — a mesher,
    /// a save file, a generator filling a column.
    /// </summary>
    public ReadOnlySpan<BlockId> Blocks => _blocks;

    /// <summary>
    /// Writes the chunk's blocks directly, then recounts.
    /// </summary>
    /// <remarks>
    /// For a generator that fills a whole chunk at once: going through <see cref="Set"/> 32,768
    /// times would re-derive the solid count on every one of them. The recount at the end is one
    /// pass instead of none, which is the price of not having to trust the caller to count.
    /// </remarks>
    public Span<BlockId> BeginBulkWrite() => _blocks;

    /// <summary>Recounts solid blocks after a <see cref="BeginBulkWrite"/>.</summary>
    public void EndBulkWrite()
    {
        var solid = 0;
        foreach (var block in _blocks)
            if (block.IsSolid)
                solid++;

        SolidCount = solid;
        Revision++;
    }
}
