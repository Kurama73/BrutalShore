using BrutalShore.Voxel;
using BrutalShore.World;

namespace Shorebound;

/// <summary>Something standing in the world that does work, holds blocks, and can be fed.</summary>
/// <remarks>
/// <para>
/// Introduced once there were two of them. A smelter and a conveyor have almost nothing in
/// common inside — one burns fuel on a clock, the other slides items along — but everything in
/// common at their edges: they occupy a block, they step, they can be handed a block and may
/// refuse it, something can be taken out of them, and when the block is broken they give back
/// what was inside.
/// </para>
/// <para>
/// Those five things are exactly what automation needs and nothing more. A belt does not know
/// what a smelter is; it knows that whatever is in front of it either takes a block or does not,
/// which is the whole of what makes a chain of machines a chain rather than a special case.
/// </para>
/// </remarks>
public interface IMachine
{
    /// <summary>Does whatever this machine does for a slice of time.</summary>
    /// <param name="step">Everything it needs to know about the world around it.</param>
    void Step(in MachineStep step);

    /// <summary>Tries to put one block into it.</summary>
    /// <param name="block">What is being handed over.</param>
    /// <returns>Whether it was taken.</returns>
    /// <remarks>
    /// One block rather than a stack, because that is the unit everything downstream moves in and
    /// a partial refusal would be a thing every caller had to handle. Taking nothing is a refusal
    /// and the caller keeps what it had.
    /// </remarks>
    bool Offer(BlockId block);

    /// <summary>Takes one block out of whatever this machine has finished with.</summary>
    /// <returns>What came out, or nothing.</returns>
    /// <remarks>
    /// Only from its output. A machine that let its input be taken back out would be a machine
    /// that a belt behind it could empty while it worked.
    /// </remarks>
    BlockId? TakeOut();

    /// <summary>Everything inside, for when the block is broken.</summary>
    /// <returns>Each non-empty stack.</returns>
    IEnumerable<Stack> Contents();
}

/// <summary>What a machine is told about the world when it steps.</summary>
/// <remarks>
/// A struct passed by reference rather than five parameters, so that adding something a machine
/// needs is a field here and not a change to every machine's signature. It is also what lets a
/// conveyor reach the machine in front of it without conveyors knowing where machines are kept.
/// </remarks>
public readonly ref struct MachineStep
{
    /// <summary>How long this step is, in seconds.</summary>
    public required double Seconds { get; init; }

    /// <summary>Where this machine stands, in blocks.</summary>
    public required int3 Position { get; init; }

    /// <summary>Everything else standing in the world, for handing blocks on.</summary>
    public required Machines Machines { get; init; }

    /// <summary>The blocks, for a machine that reads what it is standing on.</summary>
    public required VoxelWorld World { get; init; }

    /// <summary>The tables a smelter is run against.</summary>
    public required Smelting Smelting { get; init; }
}
