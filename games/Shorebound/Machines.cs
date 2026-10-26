using BrutalShore.Voxel;
using BrutalShore.World;

namespace Shorebound;

/// <summary>Every machine standing in the world, and where each of them is.</summary>
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
    private readonly Dictionary<int3, IMachine> _machines = [];
    private readonly Smelting _smelting;
    private readonly List<int3> _order = [];

    /// <summary>Whether the grid has to be worked out again before the next step.</summary>
    private bool _wiringChanged = true;

    /// <summary>
    /// Where every cable is, kept up to date rather than gathered when the grid is rebuilt.
    /// </summary>
    /// <remarks>
    /// Measured: rebuilding gathered this by walking every machine in the world and allocating a
    /// fresh set, and a frame that rebuilt cost twelve times one that did not. Keeping it is two
    /// lines in the two places a machine is placed or removed.
    /// </remarks>
    private readonly HashSet<int3> _cables = [];

    /// <summary>How long it has been since the grid was last worked out, in seconds.</summary>
    private double _sinceRebuildSeconds = RebuildEverySeconds;

    /// <summary>The shortest gap between two rebuilds, in seconds.</summary>
    /// <remarks>
    /// A tenth of a second. Laying a line of cable invalidates the grid on every single block,
    /// and a player laying one places several a second; without this, each of those pays for a
    /// whole rebuild. With it, a burst of placements costs one. An occasional placement is still
    /// instant, because the gap since the last rebuild is already longer than this.
    ///
    /// What it costs is that a cable just laid may take up to a tenth of a second to start
    /// carrying power, which is below what anyone can see.
    /// </remarks>
    private const double RebuildEverySeconds = 0.1;

    /// <summary>Builds an empty set of machines.</summary>
    /// <param name="smelting">The tables each smelter is run against.</param>
    public Machines(Smelting smelting) => _smelting = smelting;

    /// <summary>How many machines there are, for the overlay.</summary>
    public int Count => _machines.Count;

    /// <summary>Every machine and where it stands, for a save file.</summary>
    public IEnumerable<KeyValuePair<int3, IMachine>> All => _machines;

    /// <summary>The machine at a block coordinate, or nothing.</summary>
    /// <param name="position">Where to look.</param>
    /// <returns>What is there, or <see langword="null" />.</returns>
    public IMachine? At(int3 position) => _machines.GetValueOrDefault(position);

    /// <summary>The smelter at a block coordinate, or nothing if it is something else.</summary>
    /// <param name="position">Where to look.</param>
    /// <returns>The smelter there, or <see langword="null" />.</returns>
    /// <remarks>
    /// Asked for separately because one caller genuinely needs the type back: the screen that
    /// shows a smelter's three slots and two gauges is a smelter's screen, and a belt has nothing
    /// to show it.
    /// </remarks>
    public Smelter? SmelterAt(int3 position) => _machines.GetValueOrDefault(position) as Smelter;

    /// <summary>Puts a machine at a coordinate, replacing whatever was there.</summary>
    /// <param name="position">Where it goes.</param>
    /// <param name="machine">What goes there.</param>
    /// <returns>The machine, so that a save file can fill it in.</returns>
    public IMachine Place(int3 position, IMachine machine)
    {
        if (!_machines.ContainsKey(position))
            _order.Add(position);

        _machines[position] = machine;

        if (machine is Cable)
            _cables.Add(position);
        else
            _cables.Remove(position);

        _wiringChanged = true;
        return machine;
    }

    /// <summary>Takes the machine at a coordinate away.</summary>
    /// <param name="position">Where it was.</param>
    /// <returns>It, so that what was inside can be dropped, or <see langword="null" />.</returns>
    public IMachine? Remove(int3 position)
    {
        if (!_machines.Remove(position, out var machine))
            return null;

        _order.Remove(position);
        _cables.Remove(position);
        _wiringChanged = true;
        return machine;
    }

    /// <summary>Forgets every machine, as starting a fresh world does.</summary>
    public void Clear()
    {
        _machines.Clear();
        _order.Clear();
        _cables.Clear();
        _wiringChanged = true;
    }

    /// <summary>The grids the cables make, worked out again only when something is built.</summary>
    public PowerGrid Power { get; } = new();

    /// <summary>Runs every machine for a slice of time.</summary>
    /// <param name="seconds">How long has passed.</param>
    /// <param name="world">The blocks, for a machine that reads what it stands on.</param>
    /// <remarks>
    /// Walked in the order they were built rather than in whatever order the dictionary happens
    /// to hold them. A belt handing on to the belt in front of it gives a different throughput
    /// depending on which of the two steps first, and a factory whose speed changed when a world
    /// was reloaded would be a factory nobody could tune.
    /// </remarks>
    public void Step(double seconds, VoxelWorld world)
    {
        // The wiring first, so that a machine stepped this frame already knows what it got. The
        // rebuild is skipped on nearly every frame: the answer only changes when something is
        // placed or broken, and walking every cable sixty times a second would be the most
        // expensive thing in this program.
        _sinceRebuildSeconds += seconds;

        if (_wiringChanged && _sinceRebuildSeconds >= RebuildEverySeconds)
        {
            Power.Rebuild(this, _cables);
            _wiringChanged = false;
            _sinceRebuildSeconds = 0.0;
        }

        Power.Step(seconds);

        // Copied because a machine's step can remove another one — a belt carrying the last item
        // out of a smelter does not, but the next machine written might.
        foreach (var position in _order.ToArray())
        {
            if (!_machines.TryGetValue(position, out var machine))
                continue;

            machine.Step(
                new MachineStep
                {
                    Seconds = seconds,
                    Position = position,
                    Machines = this,
                    World = world,
                    Smelting = _smelting,
                }
            );
        }
    }

    /// <summary>How many things are riding on belts, for the overlay.</summary>
    public int CarriedCount
    {
        get
        {
            var riding = 0;
            foreach (var machine in _machines.Values)
            {
                if (machine is Conveyor belt)
                    riding += belt.Count;
            }

            return riding;
        }
    }

    /// <summary>How many of them are alight, for the overlay and for what the dark hears.</summary>
    public int LitCount
    {
        get
        {
            var lit = 0;
            foreach (var machine in _machines.Values)
            {
                if (machine is Smelter { IsLit: true })
                    lit++;
            }

            return lit;
        }
    }
}
