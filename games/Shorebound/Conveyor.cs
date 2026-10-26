using BrutalShore.Voxel;

namespace Shorebound;

/// <summary>A belt: a block that carries what is put on it to the block in front.</summary>
/// <remarks>
/// <para>
/// The piece that turns a pile of machines into a factory. On its own a smelter is a slower way
/// of crafting by hand; with a belt taking its output to the next one, a line of them is a thing
/// that runs while the player is a hundred metres down a shaft, and that is the whole promise.
/// </para>
/// <para>
/// One belt is one block and holds a few items, each at a position along it from 0 at the back
/// to 1 at the front. Items move at a fixed speed and stop behind each other, which is what makes
/// a backed-up line read as backed up instead of as items piling into one place.
/// </para>
/// <para>
/// A belt pushes, and pulls only from a machine. Pushing forward into whatever is ahead is what
/// chains belts together; pulling from a machine behind is what empties a smelter without the
/// smelter needing to know a belt exists. A belt does not pull from another belt, because that
/// one already pushes — doing both would move an item two blocks in a step.
/// </para>
/// </remarks>
public sealed class Conveyor : IMachine
{
    /// <summary>How many items fit on one belt.</summary>
    /// <remarks>
    /// Four, which at the spacing below fills the block. More would mean items overlapping, and
    /// a belt that looks full when it is not is a belt a player cannot reason about.
    /// </remarks>
    public const int Capacity = 4;

    /// <summary>How long one item takes to cross one belt, in seconds.</summary>
    /// <remarks>
    /// Two thirds of a second, so a belt moves about one and a half blocks a second — a little
    /// faster than a player walks, which is what makes watching one satisfying rather than
    /// agonising.
    /// </remarks>
    private const double CrossingSeconds = 0.666;

    /// <summary>How close together two items may sit, as a fraction of a belt.</summary>
    private const double Spacing = 1.0 / Capacity;

    private readonly List<Carried> _carried = [];

    /// <summary>Builds a belt pointing somewhere.</summary>
    /// <param name="facing">Which way it carries.</param>
    public Conveyor(Facing facing) => Facing = facing;

    /// <summary>Which way it carries.</summary>
    public Facing Facing { get; }

    /// <summary>How many items are on it.</summary>
    public int Count => _carried.Count;

    /// <summary>What is on it and how far along each is, for drawing.</summary>
    public IReadOnlyList<Carried> Items => _carried;

    /// <inheritdoc />
    public void Step(in MachineStep step)
    {
        var alongPerSecond = 1.0 / CrossingSeconds;
        var ahead = Facings.Ahead(step.Position, Facing);
        var next = step.Machines.At(ahead);

        // Front to back, so that each item is stopped by where the one in front has already moved
        // to this step rather than by where it was last step. Doing it the other way makes a full
        // belt lurch: every item waits a step for the one ahead to leave.
        for (var index = 0; index < _carried.Count; index++)
        {
            var item = _carried[index];
            var limit = index == 0 ? 1.0 : _carried[index - 1].Along - Spacing;

            item.Along = double.Min(item.Along + (alongPerSecond * step.Seconds), limit);
            _carried[index] = item;
        }

        if (_carried.Count == 0)
        {
            PullFromBehind(step);
            return;
        }

        // The one at the front hands itself on when it gets there. Only one a step: a belt that
        // emptied itself into the machine ahead in a single frame would be a belt with no
        // throughput limit at all.
        var front = _carried[0];
        if (front.Along >= 1.0 && next?.Offer(front.Block) is true)
            _carried.RemoveAt(0);

        PullFromBehind(step);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Taken at the back, and only when there is room behind the last item. That is what makes a
    /// belt have a throughput rather than a capacity: a machine feeding a full belt waits.
    /// </remarks>
    public bool Offer(BlockId block)
    {
        if (block.IsAir || _carried.Count >= Capacity)
            return false;

        if (_carried.Count > 0 && _carried[^1].Along < Spacing)
            return false;

        _carried.Add(new Carried { Block = block, Along = 0.0 });
        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// From the front, which is the only end anything should be taken from. A belt is a queue and
    /// taking from the back of one would let a machine behind reclaim what it had already sent.
    /// </remarks>
    public BlockId? TakeOut()
    {
        if (_carried.Count == 0)
            return null;

        var front = _carried[0];
        _carried.RemoveAt(0);
        return front.Block;
    }

    /// <inheritdoc />
    public IEnumerable<Stack> Contents()
    {
        foreach (var item in _carried)
            yield return new Stack(item.Block, 1);
    }

    /// <summary>Puts an item on at a point along, for a save file.</summary>
    /// <param name="block">What it is.</param>
    /// <param name="along">How far along it had got, from 0 to 1.</param>
    public void Restore(BlockId block, double along)
    {
        if (block.IsAir || _carried.Count >= Capacity)
            return;

        _carried.Add(new Carried { Block = block, Along = double.Clamp(along, 0.0, 1.0) });
    }

    /// <summary>Takes one out of the machine behind, if there is room at the back.</summary>
    /// <remarks>
    /// Not from another belt. That one pushes forward on its own step, and a belt that both
    /// pushed and was pulled from would move an item two blocks in one step — which shows up as
    /// a line that runs at double speed wherever two belts meet.
    /// </remarks>
    private void PullFromBehind(in MachineStep step)
    {
        if (_carried.Count >= Capacity || (_carried.Count > 0 && _carried[^1].Along < Spacing))
            return;

        var behind = step.Machines.At(Facings.Behind(step.Position, Facing));
        if (behind is null or Conveyor)
            return;

        if (behind.TakeOut() is { } block)
            Offer(block);
    }

    /// <summary>One thing on a belt: what it is and how far along it has got.</summary>
    /// <param name="Block">What it is.</param>
    /// <param name="Along">How far along the belt, from 0 at the back to 1 at the front.</param>
    public record struct Carried(BlockId Block, double Along);
}
