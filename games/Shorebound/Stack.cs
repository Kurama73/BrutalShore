using BrutalShore.Voxel;

namespace Shorebound;

/// <summary>Some number of one kind of block, off on its own rather than in a slot.</summary>
/// <remarks>
/// <para>
/// What the cursor carries while the inventory screen is open, and what a recipe asks for and
/// gives back. A slot inside <see cref="Inventory" /> stays private to it; this is the same pair
/// of values at the point where it has left one slot and not yet reached another.
/// </para>
/// <para>
/// Nothing is a count of zero rather than a null, so that the common case — moving a stack that
/// may or may not exist — is arithmetic instead of a branch at every call site.
/// </para>
/// </remarks>
/// <param name="Block">Which block.</param>
/// <param name="Count">How many of it; zero means there is nothing here.</param>
public readonly record struct Stack(BlockId Block, int Count)
{
    /// <summary>An empty hand.</summary>
    public static Stack Nothing => default;

    /// <summary>Whether there is nothing here.</summary>
    public bool IsNothing => Count <= 0;
}
