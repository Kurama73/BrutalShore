namespace RustSpace.Voxel;

/// <summary>
/// Every block type the game knows, indexed by <see cref="BlockId"/>.
/// </summary>
/// <remarks>
/// <para>
/// One registry for the whole world, not one palette per chunk. A chunk-local palette saves memory
/// when a chunk holds few types, but it makes a block id mean different things in different chunks,
/// so every id that crosses a chunk boundary — a mesher reading a neighbour, a player placing a
/// block, a save file — has to be translated. For a game whose block types are a fixed, authored
/// set, that translation buys nothing and costs correctness.
/// </para>
/// <para>
/// Ids are handed out in registration order and never reused, so a world saved against one registry
/// loads against a later one as long as types were only appended. That is a constraint on how this
/// is populated, which is why it is populated in one place.
/// </para>
/// </remarks>
public sealed class BlockRegistry
{
    private readonly List<BlockType> _types = [new BlockType { Name = "air", Color = new byte3(0, 0, 0) }];

    /// <summary>What a block id that no registered type covers is drawn as: unmistakable magenta.</summary>
    public static BlockType Missing { get; } = new() { Name = "missing", Color = byte3.HexColor(0xFF00FF) };

    /// <summary>How many types are registered, air included.</summary>
    public int Count => _types.Count;

    /// <summary>Every registered type, air first, in id order.</summary>
    public IReadOnlyList<BlockType> Types => _types;

    /// <summary>
    /// Registers a type and returns its id.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the registry is full, which is at 65,535 types.
    /// </exception>
    public BlockId Register(BlockType type)
    {
        if (_types.Count > ushort.MaxValue)
            throw new InvalidOperationException($"the registry is full at {ushort.MaxValue} block types");

        _types.Add(type);
        return new BlockId((ushort)(_types.Count - 1));
    }

    /// <summary>
    /// The type behind an id, or <see cref="Missing"/> when the id is not registered.
    /// </summary>
    /// <remarks>
    /// Returns a visible placeholder rather than throwing, because the caller is usually a mesher
    /// part-way through a chunk: a magenta block is a bug report you can see and walk up to, and a
    /// thrown exception is a black screen.
    /// </remarks>
    public BlockType this[BlockId id] => id.Value < _types.Count ? _types[id.Value] : Missing;

    /// <summary>The id of the type with this name, or <see langword="null"/> if there is none.</summary>
    public BlockId? Find(string name)
    {
        for (var index = 0; index < _types.Count; index++)
            if (_types[index].Name == name)
                return new BlockId((ushort)index);
        return null;
    }
}
