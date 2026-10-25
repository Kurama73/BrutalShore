namespace RustSpace.Voxel;

/// <summary>
/// A block's identity: an index into a <see cref="BlockRegistry"/>, with zero reserved for air.
/// </summary>
/// <remarks>
/// A <see cref="ushort"/> rather than a byte because 256 block types is a ceiling a voxel game
/// reaches, and rather than an int because a chunk stores 32,768 of them: at two bytes a chunk is
/// 64 KiB, and at four it is 128 KiB for a range nothing will use. That factor of two is paid on
/// every chunk resident in memory, which is why it is a decision rather than a default.
/// </remarks>
/// <param name="Value">The registry index.</param>
public readonly record struct BlockId(ushort Value)
{
    /// <summary>Nothing: the absence of a block, which is what a chunk is full of to begin with.</summary>
    public static BlockId Air => new(0);

    /// <summary>Whether this is <see cref="Air"/>.</summary>
    public bool IsAir => Value == 0;

    /// <summary>Whether this is anything other than <see cref="Air"/>.</summary>
    public bool IsSolid => Value != 0;

    /// <inheritdoc />
    public override string ToString() => IsAir ? "air" : $"block {Value}";
}
