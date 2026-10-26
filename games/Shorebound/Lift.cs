using BrutalShore.Voxel;

namespace Shorebound;

/// <summary>A lift: a belt that goes up instead of along.</summary>
/// <remarks>
/// <para>
/// A voxel world's whole shape is vertical — the ore is down and the player is up — and a factory
/// that could only move things sideways would be a factory that has to be built at the bottom of
/// the shaft it feeds from. A lift is what lets the mine be underground and the works be on the
/// surface, which is the arrangement everybody tries to build first.
/// </para>
/// <para>
/// It is a separate machine rather than a belt with a vertical facing. A belt's items slide along
/// a block and are drawn doing it; a lift's rise through one and are drawn doing that, and the
/// two have different capacities, different speeds and different rules about what is above and
/// below them. One type with a flag in it would be two types sharing a name.
/// </para>
/// </remarks>
public sealed class Lift : IMachine
{
    /// <summary>How many things fit in one lift.</summary>
    public const int Capacity = 3;

    /// <summary>How long one thing takes to rise through one block, in seconds.</summary>
    /// <remarks>
    /// Slower than a belt crosses one, because going up is work. It is also what stops a lift
    /// being strictly better than a belt on flat ground, which it otherwise would be.
    /// </remarks>
    private const double RisingSeconds = 1.0;

    private const double Spacing = 1.0 / Capacity;

    private readonly List<Conveyor.Carried> _rising = [];

    /// <summary>How many things are in it.</summary>
    public int Count => _rising.Count;

    /// <summary>What is in it and how far up each has got, for drawing.</summary>
    public IReadOnlyList<Conveyor.Carried> Items => _rising;

    /// <inheritdoc />
    public void Step(in MachineStep step)
    {
        var above = new int3(step.Position.X, step.Position.Y + 1, step.Position.Z);
        var below = new int3(step.Position.X, step.Position.Y - 1, step.Position.Z);
        var risePerSecond = 1.0 / RisingSeconds;

        for (var index = 0; index < _rising.Count; index++)
        {
            var item = _rising[index];
            var limit = index == 0 ? 1.0 : _rising[index - 1].Along - Spacing;

            item.Along = double.Min(item.Along + (risePerSecond * step.Seconds), limit);
            _rising[index] = item;
        }

        if (_rising.Count > 0 && _rising[0].Along >= 1.0)
        {
            if (step.Machines.At(above)?.Offer(_rising[0].Block) is true)
                _rising.RemoveAt(0);
        }

        PullFromBelow(step, below);
    }

    /// <inheritdoc />
    public bool Offer(BlockId block)
    {
        if (block.IsAir || _rising.Count >= Capacity)
            return false;

        if (_rising.Count > 0 && _rising[^1].Along < Spacing)
            return false;

        _rising.Add(new Conveyor.Carried { Block = block, Along = 0.0 });
        return true;
    }

    /// <inheritdoc />
    public BlockId? TakeOut()
    {
        if (_rising.Count == 0)
            return null;

        var top = _rising[0];
        _rising.RemoveAt(0);
        return top.Block;
    }

    /// <inheritdoc />
    public IEnumerable<Stack> Contents()
    {
        foreach (var item in _rising)
            yield return new Stack(item.Block, 1);
    }

    /// <summary>Puts something in at a point along, for a save file.</summary>
    /// <param name="block">What it is.</param>
    /// <param name="along">How far up it had got, from 0 to 1.</param>
    public void Restore(BlockId block, double along)
    {
        if (block.IsAir || _rising.Count >= Capacity)
            return;

        _rising.Add(new Conveyor.Carried { Block = block, Along = double.Clamp(along, 0.0, 1.0) });
    }

    /// <summary>Takes one out of whatever is underneath, unless that is another lift.</summary>
    /// <remarks>
    /// The same rule a belt follows, and for the same reason: a lift below already pushes
    /// upwards on its own step, so a stack of them that also pulled would move something two
    /// blocks in one step.
    /// </remarks>
    private void PullFromBelow(in MachineStep step, int3 below)
    {
        if (_rising.Count >= Capacity || (_rising.Count > 0 && _rising[^1].Along < Spacing))
            return;

        var under = step.Machines.At(below);
        if (under is null or Lift)
            return;

        if (under.TakeOut() is { } block)
            Offer(block);
    }
}
