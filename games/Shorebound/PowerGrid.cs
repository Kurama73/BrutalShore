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

    // Kept between rebuilds rather than allocated in one. A rebuild happens on every placement
    // while a player lays cable, which is several a second, and a few thousand entries of garbage
    // each time is work for the collector to do in the middle of them building something.
    private readonly HashSet<int3> _seen = [];
    private readonly Queue<int3> _pending = new();
    private readonly List<Network> _spare = [];

    /// <summary>How many separate grids there are, for the overlay.</summary>
    public int NetworkCount => _networks.Count;

    /// <summary>How much every grid is putting out altogether, in watts.</summary>
    public double DeliveredWatts { get; private set; }

    /// <summary>How much every grid was asked for altogether, in watts.</summary>
    public double DemandedWatts { get; private set; }

    /// <summary>Works out which machines are wired together.</summary>
    /// <param name="machines">Everything standing in the world.</param>
    /// <param name="cables">
    /// Where the cable is, kept up to date by the caller. Gathering it here instead meant walking
    /// every machine in the world on every placement, which measured at twelve times the cost of
    /// an ordinary frame.
    /// </param>
    /// <remarks>
    /// A flood fill over cable, then one pass collecting whatever touches each run. A machine
    /// touching two runs joins them in practice, because the runs are only separate if no cable
    /// of one is beside a cable of the other — a machine between them is not a conductor, which
    /// is the rule a player can see by looking at where they put cable.
    /// </remarks>
    public void Rebuild(Machines machines, IReadOnlySet<int3> cables)
    {
        // The networks themselves are pooled too, because a player laying a line rebuilds a
        // couple of dozen of them several times a second and each one owns three lists.
        foreach (var network in _networks)
        {
            network.Clear();
            _spare.Add(network);
        }

        _networks.Clear();
        _networkOf.Clear();
        _seen.Clear();

        Span<int3> around = stackalloc int3[6];

        foreach (var start in cables)
        {
            if (!_seen.Add(start))
                continue;

            var network = Fresh();
            _pending.Clear();
            _pending.Enqueue(start);

            while (_pending.Count > 0)
            {
                var at = _pending.Dequeue();
                network.Cables.Add(at);
                Around(at, around);

                foreach (var beside in around)
                {
                    if (cables.Contains(beside) && _seen.Add(beside))
                        _pending.Enqueue(beside);
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
            {
                Around(cable, around);

                foreach (var beside in around)
                {
                    // The machine is looked up first because nearly every neighbour of a cable
                    // is air or more cable, and that case then costs one lookup instead of three.
                    var machine = machines.At(beside);
                    if (machine is null or Cable || _networkOf.ContainsKey(beside))
                        continue;

                    switch (machine)
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

    /// <summary>A network to fill, reused if one has been returned.</summary>
    private Network Fresh()
    {
        if (_spare.Count == 0)
            return new Network();

        var network = _spare[^1];
        _spare.RemoveAt(_spare.Count - 1);
        return network;
    }

    /// <summary>Writes the six blocks around one into a span.</summary>
    /// <remarks>
    /// <para>
    /// Six and not twenty-six. Cable that joined round a corner diagonally would be cable whose
    /// shape a player cannot predict from looking at it, and the whole appeal of a wire is that
    /// you can see where it goes.
    /// </para>
    /// <para>
    /// A span rather than an iterator, which is not a style preference. Written with
    /// <c>yield return</c> this is called about thirteen thousand times per rebuild per pass and
    /// allocates an enumerator every time; measured, that was two thirds of the whole cost of
    /// rebuilding a grid.
    /// </para>
    /// </remarks>
    private static void Around(int3 at, Span<int3> into)
    {
        into[0] = new int3(at.X + 1, at.Y, at.Z);
        into[1] = new int3(at.X - 1, at.Y, at.Z);
        into[2] = new int3(at.X, at.Y + 1, at.Z);
        into[3] = new int3(at.X, at.Y - 1, at.Z);
        into[4] = new int3(at.X, at.Y, at.Z + 1);
        into[5] = new int3(at.X, at.Y, at.Z - 1);
    }

    /// <summary>One grid: its cable, what feeds it and what draws from it.</summary>
    private sealed class Network
    {
        public List<int3> Cables { get; } = [];

        public List<Generator> Generators { get; } = [];

        public List<IPowered> Consumers { get; } = [];

        public void Clear()
        {
            Cables.Clear();
            Generators.Clear();
            Consumers.Clear();
        }
    }
}
