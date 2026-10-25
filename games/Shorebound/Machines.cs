using BrutalShore.Voxel;
using BrutalShore.World;

namespace Shorebound;

/// <summary>Every smelter standing in the world, and where each of them is.</summary>
/// <remarks>
/// <para>
/// A map from a block coordinate to the machine at it, kept beside the world rather than inside
/// it. The world stores blocks, and a block is one number; a machine has slots and clocks, and
/// pushing that into the voxel grid would make every chunk carry a field that nearly every chunk
/// does not use.
/// </para>
/// <para>
/// It is the same arrangement as the edit log and for the same reason: both are the small, sparse
/// part of a world that cannot be regenerated from the seed, and both are therefore what a save
/// file is made of.
/// </para>
/// <para>
/// Every machine is stepped every frame, including the ones nobody is looking at. That is the
/// whole point of building one — a smelter that stopped when the player walked away would be a
/// slower way of crafting by hand rather than a thing that works while they are elsewhere. The
/// cost is one small loop per frame over however many a player has built, which is the right
/// trade until that number reaches the thousands.
/// </para>
/// </remarks>
public sealed class Machines
{
    private readonly Dictionary<int3, Smelter> _smelters = [];
    private readonly Smelting _smelting;

    /// <summary>Builds an empty set of machines.</summary>
    /// <param name="smelting">The tables each smelter is run against.</param>
    public Machines(Smelting smelting) => _smelting = smelting;

    /// <summary>How many machines there are, for the overlay.</summary>
    public int Count => _smelters.Count;

    /// <summary>Every machine and where it stands, for a save file.</summary>
    public IEnumerable<KeyValuePair<int3, Smelter>> All => _smelters;

    /// <summary>The machine at a block coordinate, or nothing.</summary>
    /// <param name="position">Where to look.</param>
    /// <returns>The smelter there, or <see langword="null" />.</returns>
    public Smelter? At(int3 position) => _smelters.GetValueOrDefault(position);

    /// <summary>Puts an empty machine at a coordinate, replacing whatever was there.</summary>
    /// <param name="position">Where it goes.</param>
    /// <returns>The machine, so that a save file can fill it in.</returns>
    public Smelter Place(int3 position) => _smelters[position] = new Smelter();

    /// <summary>Takes the machine at a coordinate away.</summary>
    /// <param name="position">Where it was.</param>
    /// <returns>It, so that what was inside can be dropped, or <see langword="null" />.</returns>
    public Smelter? Remove(int3 position)
    {
        if (!_smelters.Remove(position, out var machine))
            return null;

        return machine;
    }

    /// <summary>Forgets every machine, as starting a fresh world does.</summary>
    public void Clear() => _smelters.Clear();

    /// <summary>Runs every machine for a slice of time.</summary>
    /// <param name="seconds">How long has passed.</param>
    public void Step(double seconds)
    {
        foreach (var machine in _smelters.Values)
            machine.Step(_smelting, seconds);
    }

    /// <summary>How many of them are alight, for the overlay.</summary>
    public int LitCount
    {
        get
        {
            var lit = 0;
            foreach (var machine in _smelters.Values)
            {
                if (machine.IsLit)
                    lit++;
            }

            return lit;
        }
    }
}
