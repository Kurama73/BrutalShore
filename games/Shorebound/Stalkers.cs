using BrutalShore.Voxel;
using BrutalShore.World;

namespace Shorebound;

/// <summary>The horde: what comes out of the dark, and what makes more of it come.</summary>
/// <remarks>
/// <para>
/// Three rules, and between them they are the whole of the game's pressure.
/// </para>
/// <para>
/// They come out of the dark. A stalker appears where no sky reaches and no lamp does, which
/// means a cave always has them and the surface only has them at night — and it means light is a
/// defence rather than a convenience. A player who lights the ground around what they built has
/// genuinely made it safer, and did it with the same lanterns they were already carrying.
/// </para>
/// <para>
/// They come for what was built. The number that may exist at once rises with how much machinery
/// is running nearby, so a bigger factory is a louder one. That is the trade the whole middle of
/// the game turns on: every smelter is production and every smelter is another thing to defend.
/// </para>
/// <para>
/// Daylight finishes them. One caught out when the sun comes up burns, which is what stops a
/// night's worth of them accumulating into a permanent siege, and what makes dawn mean something
/// other than a change of colour.
/// </para>
/// </remarks>
public sealed class Stalkers
{
    /// <summary>How wide, tall and deep one is, in metres.</summary>
    public static double3 Size { get; } = new(0.62, 0.9, 0.62);

    /// <summary>How many exist at once before anything was built.</summary>
    private const int FewestAtOnce = 4;

    /// <summary>And the most, however much is running.</summary>
    /// <remarks>
    /// Capped because the cost of them is linear and the fear of them is not: twenty converging
    /// on a doorway is already as frightening as a horde gets, and sixty is the same fight at a
    /// third of the frame rate.
    /// </remarks>
    private const int MostAtOnce = 22;

    /// <summary>How many machines running counts as one more stalker allowed.</summary>
    private const int MachinesPerExtra = 2;

    /// <summary>How far away one may appear, in metres.</summary>
    private const double SpawnFromMeters = 16.0;

    /// <summary>And how far out.</summary>
    private const double SpawnToMeters = 46.0;

    /// <summary>How far away one is forgotten, in metres.</summary>
    private const double ForgetMeters = 110.0;

    /// <summary>How often one is looked for, in seconds.</summary>
    private const double SpawnEverySeconds = 1.4;

    /// <summary>How far a lamp keeps them away, in blocks.</summary>
    /// <remarks>
    /// Six, which is about where a lantern's light has fallen to half. The world does not keep
    /// its light levels around after meshing, so this asks the question the other way — is there
    /// anything bright near here — and gets the same answer for a fraction of the memory. It is
    /// a box rather than a real light calculation, which means a lamp on the far side of a wall
    /// also keeps them off; that is wrong, and it is wrong in the direction of rewarding a
    /// player for lighting the place they built.
    /// </remarks>
    private const int LampReachBlocks = 6;

    /// <summary>How bright a block has to be to count as a lamp.</summary>
    private const int LampEmission = 8;

    private const double WalkSpeedMetersPerSecond = 3.1;
    private const double AccelerationMetersPerSecondSquared = 14.0;
    private const double StepHeightMeters = 1.05;

    /// <summary>How close one gets before it bites, in metres.</summary>
    private const double BiteReachMeters = 1.5;

    /// <summary>How long between one bite and the next, in seconds.</summary>
    private const double BiteEverySeconds = 1.1;

    /// <summary>How much one bite takes.</summary>
    public const float BiteDamage = 2.5f;

    /// <summary>How much one can take before it comes apart.</summary>
    public const float Health = 6f;

    /// <summary>How long one survives in daylight, in seconds.</summary>
    private const double BurnsAfterSeconds = 4.0;

    private readonly List<Stalker> _stalkers = [];
    private readonly Random _minds;

    private double _sinceSpawnSeconds;

    /// <summary>Starts with nothing abroad.</summary>
    /// <param name="seed">What their wandering is decided from.</param>
    public Stalkers(int seed) => _minds = new Random(seed ^ 0x51ED270B);

    /// <summary>How many are about.</summary>
    public int Count => _stalkers.Count;

    /// <summary>All of them, for drawing.</summary>
    public IReadOnlyList<Stalker> All => _stalkers;

    /// <summary>Moves the horde, lets it bite, and brings more of it out of the dark.</summary>
    /// <param name="world">The world they walk in.</param>
    /// <param name="registry">Where a block id is resolved to what it does to a body.</param>
    /// <param name="seconds">How long this step is.</param>
    /// <param name="playerFeetMeters">Where the player is, which is what they want.</param>
    /// <param name="vitality">What they bite, which decides for itself whether a bite lands.</param>
    /// <param name="machinesRunning">How much is being run nearby, which is what draws them.</param>
    /// <param name="isDay">Whether the sun is up, which is what finishes the ones caught out.</param>
    public void Step(
        VoxelWorld world,
        BlockRegistry registry,
        double seconds,
        double3 playerFeetMeters,
        Vitality vitality,
        int machinesRunning,
        bool isDay
    )
    {
        for (var index = _stalkers.Count - 1; index >= 0; index--)
        {
            var stalker = _stalkers[index];

            if (Walking.Away(stalker.FeetMeters, playerFeetMeters) > ForgetMeters || stalker.FeetMeters.Y < -8.0)
            {
                _stalkers.RemoveAt(index);
                continue;
            }

            stalker = Burn(stalker, world, seconds, isDay);
            if (stalker.Health <= 0f)
            {
                _stalkers.RemoveAt(index);
                continue;
            }

            _stalkers[index] = Hunt(stalker, world, registry, seconds, playerFeetMeters, vitality);
        }

        _sinceSpawnSeconds += seconds;
        if (_sinceSpawnSeconds < SpawnEverySeconds)
            return;

        _sinceSpawnSeconds = 0.0;

        if (_stalkers.Count < Allowed(machinesRunning)
            && FindDark(world, registry, playerFeetMeters, isDay) is { } room)
            _stalkers.Add(Born(room));
    }

    /// <summary>Hurts the one nearest a point in front of the player, if there is one in reach.</summary>
    /// <param name="fromMeters">Where the blow starts, which is the player's eye.</param>
    /// <param name="alongMeters">Which way they are looking, as a unit direction.</param>
    /// <param name="reachMeters">How far the blow goes.</param>
    /// <param name="damage">How much it takes.</param>
    /// <returns>Whether it landed on anything.</returns>
    /// <remarks>
    /// A cone rather than a ray, because a ray asks a player to be accurate about the middle of a
    /// moving box in the dark and the answer to that is always to widen the ray until it is a
    /// cone with extra steps.
    /// </remarks>
    public bool Strike(double3 fromMeters, double3 alongMeters, double reachMeters, float damage)
    {
        var nearest = -1;
        var nearestAway = reachMeters;

        for (var index = 0; index < _stalkers.Count; index++)
        {
            var middle = _stalkers[index].FeetMeters + new double3(0.0, Size.Y * 0.5, 0.0);
            var toIt = middle - fromMeters;
            var away = double.Sqrt(double3.Dot(toIt, toIt));

            if (away > nearestAway || away < 1e-6)
                continue;

            // Within about forty degrees of where they are looking.
            if (double3.Dot(toIt / away, alongMeters) < 0.76)
                continue;

            nearest = index;
            nearestAway = away;
        }

        if (nearest < 0)
            return false;

        var struck = _stalkers[nearest];
        struck.Health -= damage;

        if (struck.Health <= 0f)
            _stalkers.RemoveAt(nearest);
        else
            _stalkers[nearest] = struck;

        return true;
    }

    /// <summary>Where the nearest one is, for something that is looking for a target.</summary>
    /// <param name="fromMeters">Where to look from.</param>
    /// <param name="withinMeters">How far to look.</param>
    /// <returns>The middle of the nearest one, or nothing.</returns>
    /// <remarks>
    /// The middle rather than the feet, because whatever is aiming at one wants to point at the
    /// body and not at the ground under it.
    /// </remarks>
    public double3? NearestTo(double3 fromMeters, double withinMeters)
    {
        double3? nearest = null;
        var nearestAway = withinMeters;

        foreach (var stalker in _stalkers)
        {
            var middle = stalker.FeetMeters + new double3(0.0, Size.Y * 0.5, 0.0);
            var apart = middle - fromMeters;
            var away = double.Sqrt(double3.Dot(apart, apart));

            if (away > nearestAway)
                continue;

            nearest = middle;
            nearestAway = away;
        }

        return nearest;
    }

    /// <summary>Forgets the whole horde, as starting or loading a world does.</summary>
    public void Clear()
    {
        _stalkers.Clear();
        _sinceSpawnSeconds = 0.0;
    }

    /// <summary>How many may exist at once, given how much is running nearby.</summary>
    /// <param name="machinesRunning">How many machines are alight.</param>
    /// <returns>The cap on the horde.</returns>
    public static int Allowed(int machinesRunning) =>
        int.Min(MostAtOnce, FewestAtOnce + (machinesRunning / MachinesPerExtra));

    private Stalker Born(double3 feet) =>
        new()
        {
            Footing = new Footing { FeetMeters = feet },
            Yaw = (float)(_minds.NextDouble() * double.Tau),
            Health = Health,
            SidleSide = _minds.NextDouble() < 0.5 ? 1.0 : -1.0,
        };

    /// <summary>Burns one that is standing in daylight, and leaves the ones underground alone.</summary>
    private static Stalker Burn(Stalker stalker, VoxelWorld world, double seconds, bool isDay)
    {
        var head = stalker.FeetMeters + new double3(0.0, Size.Y, 0.0);
        var position = new int3(
            (int)double.Floor(head.X),
            (int)double.Floor(head.Y),
            (int)double.Floor(head.Z)
        );

        // Under open sky, which is the one question the world can answer cheaply: a column's
        // highest block is kept by the chunk it is in.
        var exposed = isDay && world.HighestBlock(position.X, position.Z) <= position.Y;
        if (!exposed)
            return stalker with { BurningSeconds = 0.0 };

        stalker.BurningSeconds += seconds;
        if (stalker.BurningSeconds >= BurnsAfterSeconds)
            stalker.Health = 0f;

        return stalker;
    }

    /// <summary>
    /// Looks for somewhere dark enough and far enough away for one to come out of.
    /// </summary>
    private double3? FindDark(VoxelWorld world, BlockRegistry registry, double3 playerFeetMeters, bool isDay)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var angle = _minds.NextDouble() * double.Tau;
            var away = SpawnFromMeters + (_minds.NextDouble() * (SpawnToMeters - SpawnFromMeters));

            var x = (int)double.Floor(playerFeetMeters.X + (double.Sin(angle) * away));
            var z = (int)double.Floor(playerFeetMeters.Z + (double.Cos(angle) * away));

            // Searched downwards from a little above the player rather than from the sky, so that
            // a player underground gets the cave they are in and not the hillside over it.
            var ceiling = (int)double.Floor(playerFeetMeters.Y) + 6;
            if (Walking.StandingHeight(world, registry, x, z, ceiling) is not { } y)
                continue;

            var feet = new double3(x + 0.5, y, z + 0.5);
            var at = new int3(x, (int)y, z);

            // Daylight where the sky reaches, and a lamp anywhere, both keep them off.
            if (isDay && world.HighestBlock(x, z) <= at.Y)
                continue;

            // Room for the whole thing, not just for its feet. Before the lamp test because it
            // is far cheaper and rules out as much.
            if (VoxelBody.Move(world, registry, feet, Size, double3.Zero).FeetMeters != feet)
                continue;

            // The last and by far the dearest test, so it is the one the search stops after
            // whichever way it goes.
            //
            // Measured: this reads about two thousand blocks, and a player who has lit the
            // ground they built on makes every candidate reach it. Running it ten times in one
            // frame showed up in the overlay as creatures costing ten times their average at the
            // ninety-ninth percentile — a hitch that appeared exactly as a player made progress.
            // Stopping after one costs nothing anybody can see: spawning is on a timer of over a
            // second, and a lit base is meant to refuse most attempts anyway.
            return IsNearALamp(world, registry, at) ? null : feet;
        }

        return null;
    }

    /// <summary>Whether anything bright is within a lamp's reach of a point.</summary>
    /// <remarks>
    /// <para>
    /// Every block in the box, not a sample of them. Sampling every other block was tried and is
    /// wrong in a way that is invisible until it matters: a lattice of lamps the player has laid
    /// out can land entirely on the offsets the sample skips, and the ground they lit stays a
    /// spawning ground. A lamp is one block, so the only correct answer is to look at all of
    /// them.
    /// </para>
    /// <para>
    /// That is about two thousand block reads, and it happens at most a handful of times a
    /// second and only for a candidate that has already passed every cheaper test. It also
    /// returns the moment it finds something, so the lit case — the one a player who has built
    /// anything spends their time in — costs almost nothing.
    /// </para>
    /// </remarks>
    private static bool IsNearALamp(VoxelWorld world, BlockRegistry registry, int3 at)
    {
        for (var y = -LampReachBlocks; y <= LampReachBlocks; y++)
        for (var z = -LampReachBlocks; z <= LampReachBlocks; z++)
        for (var x = -LampReachBlocks; x <= LampReachBlocks; x++)
        {
            if (registry[world.BlockAt(at.X + x, at.Y + y, at.Z + z)].Emission >= LampEmission)
                return true;
        }

        return false;
    }

    private Stalker Hunt(
        Stalker stalker,
        IVoxelSource world,
        BlockRegistry registry,
        double seconds,
        double3 playerFeetMeters,
        Vitality vitality
    )
    {
        var toPlayer = playerFeetMeters - stalker.FeetMeters;
        var away = Walking.Away(stalker.FeetMeters, playerFeetMeters);

        stalker.BiteLeftSeconds = double.Max(0.0, stalker.BiteLeftSeconds - seconds);
        stalker.SidleLeftSeconds = double.Max(0.0, stalker.SidleLeftSeconds - seconds);

        if (away > 1e-6)
            stalker.Yaw = (float)double.Atan2(toPlayer.X, toPlayer.Z);

        if (away <= BiteReachMeters && stalker.BiteLeftSeconds <= 0.0 && vitality.Hurt(BiteDamage))
            stalker.BiteLeftSeconds = BiteEverySeconds;

        // Straight at them, unless something got in the way recently, in which case along the
        // wall for a moment. Committing to a side for a second is what turns bumping into a
        // doorway from a stutter into a creature going round.
        var forward = stalker.Forward;
        var heading = stalker.SidleLeftSeconds > 0.0
            ? new double3(forward.Z * stalker.SidleSide, 0.0, -forward.X * stalker.SidleSide)
            : forward;

        var walked = Walking.Step(
            stalker.Footing,
            world,
            registry,
            seconds,
            Size,
            heading * WalkSpeedMetersPerSecond,
            AccelerationMetersPerSecondSquared,
            StepHeightMeters,

            // It follows the player off a ledge. Something hunting that stopped politely at the
            // top of a drop would be something a player beats by standing on a rock.
            mindsTheEdge: false
        );

        if (walked.HitWall && stalker.SidleLeftSeconds <= 0.0)
        {
            stalker.SidleLeftSeconds = 0.9 + (_minds.NextDouble() * 0.8);
            stalker.SidleSide = -stalker.SidleSide;
        }

        return stalker with { Footing = walked };
    }
}
