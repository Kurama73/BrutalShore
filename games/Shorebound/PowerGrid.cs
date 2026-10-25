using BrutalShore.World;

namespace Shorebound;

/// <summary>Who is wired to whom, and how much of what they asked for they get.</summary>
/// <remarks>
/// <para>
/// A grid is a connected run of cable plus everything touching it. There can be any number of
/// them and they are entirely separate, which is what lets a player run one generator for the
/// smelters and a second for the turrets and have the smelters not take the turrets' power when
/// a fight starts. Building that separation is a decision they make with where they lay cable.
/// </para>
/// <para>
/// Shortfall is shared rather than queued. When a grid is asked for more than it has, everything
/// on it gets the same fraction of what it wanted and runs that much slower — a brownout — rather
/// than some machines running and others stopping. The reason is legibility: a factory that is
/// visibly struggling everywhere tells a player to build another generator, and one where an
/// arbitrary third of the machines have stopped tells them nothing.
/// </para>
/// <para>
/// The networks are worked out again only when the machines change, not every frame. Walking
/// every cable in a built-up world sixty times a second would be the single most expensive thing
/// in this program, and the answer only changes when something is placed or broken.
/// </para>
/// </remarks>
public sealed class PowerGrid
{
    private readonly List<Network> _networks = [];
    private readonly Dictionary<int3, int> _networkOf = [];

    /// <summary>How many separate grids there are, for the overlay.</summary>
    public int NetworkCount => _networks.Count;

    /// <summary>How much every grid is putting out altogether, in watts.</summary>
    public double DeliveredWatts { get; private set; }

    /// <summary>How much every grid was asked for altogether, in watts.</summary>
    public double DemandedWatts { get; private set; }

    /// <summary>Works out which machines are wired together.</summary>
    /// <param name="machines">Everything standing in the world.</param>
    /// <remarks>
    /// A flood fill over cable, then one pass collecting whatever touches each run. A machine
    /// touching two runs joins them in practice, because the runs are only separate if no cable
    /// of one is beside a cable of the other — a machine between them is not a conductor, which
    /// is the rule a player can see by looking at where they put cable.
    /// </remarks>
    public void Rebuild(Machines machines)
    {
        _networks.Clear();
        _networkOf.Clear();

        var cables = new HashSet<int3>();
        foreach (var (position, machine) in machines.All)
        {
            if (machine is Cable)
                cables.Add(position);
        }

        var seen = new HashSet<int3>();
        var pending = new Queue<int3>();

        foreach (var start in cables)
        {
            if (!seen.Add(start))
                continue;

            var network = new Network();
            pending.Clear();
            pending.Enqueue(start);

            while (pending.Count > 0)
            {
                var at = pending.Dequeue();
                network.Cables.Add(at);

                foreach (var beside in Around(at))
                {
                    if (cables.Contains(beside) && seen.Add(beside))
                        pending.Enqueue(beside);
                }
            }

            _networks.Add(network);
        }

        // Everything touching a run joins it. Done after the fill rather than during, so that a
        // machine beside two cables of the same run is not collected twice.
        for (var index = 0; index < _networks.Count; index++)
        {
            var network = _networks[index];

            foreach (var cable in network.Cables)
            foreach (var beside in Around(cable))
            {
                if (_networkOf.ContainsKey(beside) || cables.Contains(beside))
                    continue;

                switch (machines.At(beside))
                {
                    case Generator generator:
                        network.Generators.Add(generator);
                        _networkOf[beside] = index;
                        break;

                    case IPowered powered:
                        network.Consumers.Add(powered);
                        _networkOf[beside] = index;
                        break;
                }
            }
        }
    }

    /// <summary>Runs every grid for a slice of time, telling each machine what it got.</summary>
    /// <param name="seconds">How long has passed.</param>
    public void Step(double seconds)
    {
        DeliveredWatts = 0.0;
        DemandedWatts = 0.0;

        foreach (var network in _networks)
        {
            var demand = 0.0;
            foreach (var consumer in network.Consumers)
                demand += double.Max(0.0, consumer.DemandWatts);

            DemandedWatts += demand;

            if (demand <= 0.0)
            {
                foreach (var consumer in network.Consumers)
                    consumer.Supply(0.0);

                continue;
            }

            var available = 0.0;
            foreach (var generator in network.Generators)
                available += generator.AvailableWatts;

            var share = double.Min(1.0, available / demand);
            var delivered = demand * share;
            DeliveredWatts += delivered;

            foreach (var consumer in network.Consumers)
                consumer.Supply(share);

            if (available <= 0.0)
                continue;

            // Each generator gives the same fraction of its own capacity, so two of different
            // sizes on one grid wear down together rather than one carrying the other.
            var load = delivered / available;
            foreach (var generator in network.Generators)
                generator.Draw(load, seconds);
        }
    }

    /// <summary>Whether a machine at a coordinate is wired to anything.</summary>
    /// <param name="position">Where it stands.</param>
    /// <returns><see langword="true" /> if a cable reaches it.</returns>
    public bool IsWired(int3 position) => _networkOf.ContainsKey(position);

    /// <summary>The six blocks around one.</summary>
    /// <remarks>
    /// Six and not twenty-six. Cable that joined round a corner diagonally would be cable whose
    /// shape a player cannot predict from looking at it, and the whole appeal of a wire is that
    /// you can see where it goes.
    /// </remarks>
    private static IEnumerable<int3> Around(int3 at)
    {
        yield return new int3(at.X + 1, at.Y, at.Z);
        yield return new int3(at.X - 1, at.Y, at.Z);
        yield return new int3(at.X, at.Y + 1, at.Z);
        yield return new int3(at.X, at.Y - 1, at.Z);
        yield return new int3(at.X, at.Y, at.Z + 1);
        yield return new int3(at.X, at.Y, at.Z - 1);
    }

    /// <summary>One grid: its cable, what feeds it and what draws from it.</summary>
    private sealed class Network
    {
        public List<int3> Cables { get; } = [];

        public List<Generator> Generators { get; } = [];

        public List<IPowered> Consumers { get; } = [];
    }
}
