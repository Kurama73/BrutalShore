using BrutalShore.Voxel;

namespace Shorebound;

/// <summary>A length of cable: a block that does nothing but join its neighbours.</summary>
/// <remarks>
/// <para>
/// It holds nothing, makes nothing and steps to nothing. It exists so that two machines some way
/// apart can be on the same grid, and so that running power somewhere is a thing a player builds
/// rather than a radius they are given.
/// </para>
/// <para>
/// It is a machine for one reason: machines are what the world keeps a map of, and a cable has to
/// be findable at a coordinate. Giving it an empty body is cheaper than a second map that would
/// have to be saved, loaded and kept in step with the first.
/// </para>
/// </remarks>
public sealed class Cable : IMachine
{
    /// <inheritdoc />
    public void Step(in MachineStep step)
    {
        // Nothing. The grid is worked out by whatever walks the cables, not by the cables.
    }

    /// <inheritdoc />
    public bool Offer(BlockId block) => false;

    /// <inheritdoc />
    public BlockId? TakeOut() => null;

    /// <inheritdoc />
    public IEnumerable<Stack> Contents() => [];
}
