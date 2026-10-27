using BrutalShore.Voxel;

namespace Shorebound.Domain.Production;

/// <summary>One thing that can be made, and the arrangement of blocks that makes it.</summary>
/// <remarks>
/// <para>
/// A recipe is a small rectangle of blocks and what comes out. It is shaped rather than a bag of
/// ingredients because the shape is the puzzle: a player who has the blocks and does not know the
/// arrangement has something left to find out, and one who knows the arrangement can lay it out
/// without reading a list.
/// </para>
/// <para>
/// The pattern is stored at its own size, not padded to the grid, so a two-by-two recipe matches
/// anywhere in a three-by-three grid without the recipe having to say so four times. Matching is
/// <see cref="Recipes" />' job; this is only the statement of what the recipe is.
/// </para>
/// </remarks>
public sealed class Recipe
{
    /// <summary>States a recipe.</summary>
    /// <param name="name">What it makes, for the interface to say.</param>
    /// <param name="width">How many columns the pattern is.</param>
    /// <param name="height">How many rows.</param>
    /// <param name="pattern">
    /// The blocks, row by row from the top left. An empty slot is <see cref="BlockId.Air" />.
    /// </param>
    /// <param name="result">What comes out.</param>
    /// <param name="count">How many of it.</param>
    /// <exception cref="ArgumentException">If the pattern is not the size it says it is.</exception>
    public Recipe(string name, int width, int height, BlockId[] pattern, BlockId result, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        if (pattern.Length != width * height)
            throw new ArgumentException($"a {width}x{height} pattern needs {width * height} blocks", nameof(pattern));

        Name = name;
        Width = width;
        Height = height;
        Pattern = pattern;
        Result = result;
        Count = count;
    }

    /// <summary>What it makes, in words.</summary>
    public string Name { get; }

    /// <summary>How many columns the pattern is.</summary>
    public int Width { get; }

    /// <summary>How many rows the pattern is.</summary>
    public int Height { get; }

    /// <summary>The blocks, row by row from the top left; air for an empty slot.</summary>
    public IReadOnlyList<BlockId> Pattern { get; }

    /// <summary>What comes out.</summary>
    public BlockId Result { get; }

    /// <summary>How many come out.</summary>
    public int Count { get; }
}
