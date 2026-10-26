namespace BrutalShore.World;

/// <summary>Every block a player has changed from what the generator would have made.</summary>
/// <remarks>
/// <para>
/// This is what makes the world both generated and editable. A chunk nobody has touched is still
/// regenerated from its coordinate and the seed, costing nothing to store; a chunk somebody has
/// touched is regenerated too, and then the handful of differences are laid over it. The world
/// stays unbounded, and a save is the size of what was done to it rather than the size of what
/// was seen.
/// </para>
/// <para>
/// It is also not optional. Without it a block broken next to a player comes back the moment they
/// walk far enough for the chunk to unload, because unloading throws the chunk away and reloading
/// asks the generator again.
/// </para>
/// <para>
/// Edits are grouped by chunk because that is the only way they are ever read: a chunk has just
/// been generated and wants its own, and asking a flat map for them would mean walking every edit
/// in the world.
/// </para>
/// </remarks>
public sealed class BlockEdits
{
    private static readonly Dictionary<int, BlockId> None = [];

    private readonly Dictionary<int3, Dictionary<int, BlockId>> _byChunk = [];

    /// <summary>How many blocks have been changed.</summary>
    public int Count { get; private set; }

    /// <summary>The chunks that have at least one edit.</summary>
    public IReadOnlyCollection<int3> TouchedChunks => _byChunk.Keys;

    /// <summary>Records that a block is now something other than what was generated.</summary>
    /// <param name="blockCoordinate">Which block, in world coordinates.</param>
    /// <param name="block">What it is now.</param>
    /// <remarks>
    /// Editing the same block twice replaces the first record rather than adding to it: what is
    /// kept is the difference from the generated world, not the history of how it got there.
    /// </remarks>
    public void Record(int3 blockCoordinate, BlockId block)
    {
        var local = WorldCoordinates.ToLocal(blockCoordinate);
        RecordAt(WorldCoordinates.ToChunk(blockCoordinate), Chunk.IndexOf(local.X, local.Y, local.Z), block);
    }

    /// <summary>Lays this chunk's edits over what the generator just made.</summary>
    /// <param name="chunkCoordinate">Which chunk was generated.</param>
    /// <param name="into">The freshly generated chunk.</param>
    /// <returns>How many blocks were changed.</returns>
    public int ApplyTo(int3 chunkCoordinate, Chunk into)
    {
        if (!_byChunk.TryGetValue(chunkCoordinate, out var inChunk))
            return 0;

        var blocks = into.BeginBulkWrite();
        foreach (var (index, block) in inChunk)
            blocks[index] = block;

        into.EndBulkWrite();
        return inChunk.Count;
    }

    /// <summary>The edits in one chunk, as index-into-the-chunk and block.</summary>
    /// <param name="chunkCoordinate">Which chunk.</param>
    /// <returns>Its edits, or an empty collection if it has none.</returns>
    /// <remarks>Exists so a file format can write them without this class knowing about files.</remarks>
    public IReadOnlyDictionary<int, BlockId> InChunk(int3 chunkCoordinate) =>
        _byChunk.GetValueOrDefault(chunkCoordinate) ?? None;

    /// <summary>Records an edit by its index within a chunk, the way a file stores it.</summary>
    /// <param name="chunkCoordinate">Which chunk.</param>
    /// <param name="index">The block's index within it; see <see cref="Chunk.IndexOf" />.</param>
    /// <param name="block">What the block is now.</param>
    /// <exception cref="ArgumentOutOfRangeException">The index is not within a chunk.</exception>
    public void RecordAt(int3 chunkCoordinate, int index, BlockId block)
    {
        if ((uint)index >= Chunk.BlockCount)
            throw new ArgumentOutOfRangeException(nameof(index), index, "That is not an index into a chunk.");

        if (!_byChunk.TryGetValue(chunkCoordinate, out var inChunk))
            _byChunk[chunkCoordinate] = inChunk = [];

        if (inChunk.TryAdd(index, block))
            Count++;
        else
            inChunk[index] = block;
    }

    /// <summary>Forgets everything.</summary>
    public void Clear()
    {
        _byChunk.Clear();
        Count = 0;
    }
}
