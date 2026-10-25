using BrutalShore.Voxel;
using BrutalShore.World;

namespace Shorebound;

/// <summary>The animals wandering about near the player.</summary>
/// <remarks>
/// <para>
/// A world that generates for ever and has nothing moving in it reads as a diagram of a world.
/// These are what fix that, and they are kept to exactly that job: they wander, they keep away
/// from whoever is walking towards them, they do not walk off cliffs, and nothing can be done to
/// them. No hunting, no taming, no breeding — those are a different game and each would arrive
/// with its own reasons.
/// </para>
/// <para>
/// They are not saved. An animal is near the player because the player is there: it appears when
/// there is room for it and is forgotten when the player walks away, which is the same bargain
/// the chunks themselves make. What that buys is that nothing about them has to survive a reload
/// or be reconciled with a world that was generated again.
/// </para>
/// </remarks>
public sealed class Grazers
{
    /// <summary>How wide, tall and deep one is, in metres.</summary>
    public static double3 Size { get; } = new(0.7, 1.05, 0.7);

    /// <summary>The most that are kept near the player at once.</summary>
    private const int Most = 10;

    /// <summary>How far away one may appear, in metres.</summary>
    private const double SpawnFromMeters = 18.0;

    /// <summary>And how far out, which has to be inside what is loaded.</summary>
    private const double SpawnToMeters = 48.0;

    /// <summary>How far away one is forgotten, in metres.</summary>
    private const double ForgetMeters = 96.0;

    /// <summary>How often one is looked for, in seconds.</summary>
    private const double SpawnEverySeconds = 2.5;

    /// <summary>How close the player gets before they walk away, in metres.</summary>
    private const double ShyMeters = 7.0;

    private const double WalkSpeedMetersPerSecond = 1.5;
    private const double FleeSpeedMetersPerSecond = 3.4;
    private const double AccelerationMetersPerSecondSquared = 9.0;

    /// <summary>How tall a ledge one walks up rather than stopping at, in metres.</summary>
    private const double StepHeightMeters = 1.05;

    private readonly List<Grazer> _grazers = [];
    private readonly Random _minds;

    private double _sinceSpawnSeconds;

    /// <summary>Starts with an empty field.</summary>
    /// <param name="seed">What their wandering is decided from.</param>
    /// <remarks>
    /// Seeded so a run can be repeated. Unlike the terrain this is genuinely random rather than
    /// hashed from a coordinate, and it can be: nothing depends on where an animal went, because
    /// nothing about one is ever written down.
    /// </remarks>
    public Grazers(int seed) => _minds = new Random(seed);

    /// <summary>How many are about.</summary>
    public int Count => _grazers.Count;

    /// <summary>All of them, for drawing.</summary>
    public IReadOnlyList<Grazer> All => _grazers;

    /// <summary>Moves every animal, finds room for a new one, and forgets the far ones.</summary>
    /// <param name="world">The world they walk on.</param>
    /// <param name="registry">Where a block id is resolved to what it does to a body.</param>
    /// <param name="seconds">How long this step is.</param>
    /// <param name="playerFeetMeters">Where the player is, which is what they keep away from.</param>
    public void Step(VoxelWorld world, BlockRegistry registry, double seconds, double3 playerFeetMeters)
    {
        for (var index = _grazers.Count - 1; index >= 0; index--)
        {
            var grazer = _grazers[index];

            if (Walking.Away(grazer.FeetMeters, playerFeetMeters) > ForgetMeters || grazer.FeetMeters.Y < -8.0)
            {
                _grazers.RemoveAt(index);
                continue;
            }

            _grazers[index] = Move(grazer, world, registry, seconds, playerFeetMeters);
        }

        _sinceSpawnSeconds += seconds;
        if (_sinceSpawnSeconds < SpawnEverySeconds)
            return;

        _sinceSpawnSeconds = 0.0;

        if (_grazers.Count < Most && FindRoom(world, registry, playerFeetMeters) is { } room)
            _grazers.Add(Born(room));
    }

    /// <summary>Forgets every animal, as starting or loading a world does.</summary>
    public void Clear()
    {
        _grazers.Clear();
        _sinceSpawnSeconds = 0.0;
    }

    private Grazer Born(double3 feet) =>
        new()
        {
            Footing = new Footing { FeetMeters = feet },
            Yaw = (float)(_minds.NextDouble() * double.Tau),
            MindLeftSeconds = 1.0 + (_minds.NextDouble() * 3.0),
            IsWalking = _minds.NextDouble() < 0.6,
        };

    /// <summary>
    /// Looks for somewhere an animal could stand: solid ground, open air above it, not water, and
    /// far enough from the player not to appear in front of them.
    /// </summary>
    /// <remarks>
    /// A handful of tries rather than a search. Somewhere in a ring forty metres across there is
    /// almost always a patch of ground, and a try that lands in a lake or in an unloaded chunk
    /// costs a few block reads and is simply not taken.
    /// </remarks>
    private double3? FindRoom(VoxelWorld world, BlockRegistry registry, double3 playerFeetMeters)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var angle = _minds.NextDouble() * double.Tau;
            var away = SpawnFromMeters + (_minds.NextDouble() * (SpawnToMeters - SpawnFromMeters));

            var x = playerFeetMeters.X + (double.Sin(angle) * away);
            var z = playerFeetMeters.Z + (double.Cos(angle) * away);

            var highest = VoxelWorld.HeightBlocks - 2;
            if (Walking.StandingHeight(world, registry, (int)double.Floor(x), (int)double.Floor(z), highest) is not { } y)
                continue;

            var feet = new double3(double.Floor(x) + 0.5, y, double.Floor(z) + 0.5);

            // Room for the whole animal, not just for its feet: a spot under an overhang would
            // put it inside the rock the moment it was born.
            if (VoxelBody.Move(world, registry, feet, Size, double3.Zero).FeetMeters != feet)
                continue;

            return feet;
        }

        return null;
    }

    private Grazer Move(
        Grazer grazer,
        IVoxelSource world,
        BlockRegistry registry,
        double seconds,
        double3 playerFeetMeters
    )
    {
        var toPlayer = playerFeetMeters - grazer.FeetMeters;
        var distance = double.Sqrt(double3.Dot(toPlayer, toPlayer));
        var shy = distance < ShyMeters && distance > 1e-6;

        if (shy)
        {
            // Turned away from the player rather than merely moved away, so the animal is seen
            // to be leaving instead of sliding backwards.
            grazer.Yaw = (float)double.Atan2(-toPlayer.X, -toPlayer.Z);
            grazer.IsWalking = true;
            grazer.MindLeftSeconds = double.Max(grazer.MindLeftSeconds, 0.5);
        }
        else
        {
            grazer.MindLeftSeconds -= seconds;
            if (grazer.MindLeftSeconds <= 0.0)
                grazer = ChangeItsMind(grazer);
        }

        var speed = shy ? FleeSpeedMetersPerSecond : WalkSpeedMetersPerSecond;
        var wanted = grazer.IsWalking ? grazer.Forward * speed : double3.Zero;

        var walked = Walking.Step(
            grazer.Footing,
            world,
            registry,
            seconds,
            Size,
            wanted,
            AccelerationMetersPerSecondSquared,
            StepHeightMeters,

            // An animal that walked off every ledge it came to would spend the day falling into
            // the sea.
            mindsTheEdge: true
        );

        // Being stopped — by a wall or by a drop — is a reason to think again, which is what
        // keeps one from standing against a cliff pushing for ever.
        if (walked.HitWall)
            grazer = grazer with { MindLeftSeconds = 0.0 };

        return grazer with { Footing = walked };
    }

    private Grazer ChangeItsMind(Grazer grazer)
    {
        var walking = _minds.NextDouble() < 0.65;

        return grazer with
        {
            IsWalking = walking,

            // Turned by some amount rather than pointed somewhere new, so a path wanders instead
            // of being a sequence of unrelated directions.
            Yaw = walking ? grazer.Yaw + (float)((_minds.NextDouble() - 0.5) * 2.4) : grazer.Yaw,
            MindLeftSeconds = walking ? 1.5 + (_minds.NextDouble() * 4.0) : 1.0 + (_minds.NextDouble() * 4.0),
        };
    }


    private static double Away(double3 from, double3 to)
    {
        var between = to - from;
        return double.Sqrt(double3.Dot(between, between));
    }

    private static double Approach(double current, double target, double step) =>
        current < target
            ? double.Min(current + step, target)
            : double.Max(current - step, target);
}
