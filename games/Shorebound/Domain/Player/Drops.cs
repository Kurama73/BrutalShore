using BrutalShore.Voxel;
using BrutalShore.World;

namespace Shorebound.Domain.Player;

/// <summary>The blocks that have been knocked loose and not yet walked over.</summary>
/// <remarks>
/// <para>
/// What turns breaking a block from a deletion into a transaction. A block that simply vanished
/// would make digging a way of tidying up; a block that falls out, bounces, lies there and is
/// picked up is a block that went somewhere, and the whole of the difference is this list.
/// </para>
/// <para>
/// Each one is a small cube with the same physics the player has — it falls, it lands, it slides
/// off a slope — because <see cref="VoxelBody" /> already knows how to move a box through voxels
/// and a dropped block is a small box. What it does not have is any state of its own beyond a
/// position, a speed and an age, which is why they live in one array rather than as objects.
/// </para>
/// </remarks>
public sealed class Drops
{
    /// <summary>How large a dropped block is drawn, in metres.</summary>
    public const float SizeMeters = 0.3f;

    /// <summary>How close the player has to be for one to jump into their hand, in metres.</summary>
    private const double ReachMeters = 0.9;

    /// <summary>How close the player has to be before a drop starts moving towards them.</summary>
    /// <remarks>
    /// Drops are pulled rather than merely collected. Without it a block that rolled into a hollow
    /// a metre away is a block the player has to work out how to stand on top of, and the work is
    /// not interesting — the block was theirs the moment they broke it.
    /// </remarks>
    private const double AttractMeters = 2.6;

    /// <summary>How hard a drop is pulled towards the player, in metres per second squared.</summary>
    private const double AttractionMetersPerSecondSquared = 26.0;

    /// <summary>How long one has to lie there before it can be picked up, in seconds.</summary>
    /// <remarks>
    /// Long enough to see it fall. Without it a block breaks and is collected on the same frame,
    /// which is indistinguishable from the block simply disappearing into the row — and the
    /// point of a drop is that something visibly happened.
    /// </remarks>
    private const double SettleSeconds = 0.35;

    /// <summary>How long one lasts before it is given up on, in seconds.</summary>
    private const double LifetimeSeconds = 300.0;

    private const double GravityMetersPerSecondSquared = 24.0;
    private const double TerminalSpeedMetersPerSecond = 30.0;

    /// <summary>How quickly a drop turns on the spot, in radians a second.</summary>
    private const double TurnRadiansPerSecond = 1.1;

    /// <summary>How much of its sideways speed a drop keeps each second on the ground.</summary>
    private const double GroundDragPerSecond = 6.0;

    private static readonly double3 BodySize = new(SizeMeters, SizeMeters, SizeMeters);

    private readonly List<Drop> _drops = [];
    private readonly Random _scatter;

    /// <summary>Starts with nothing dropped.</summary>
    /// <param name="seed">What the scatter of a dropped block is decided from.</param>
    /// <remarks>
    /// Seeded, so a run can be repeated. Where a block lands is the only thing in this game that
    /// is random rather than hashed, and it is random because nothing depends on it: a drop is
    /// picked up within a second or two and is never part of the world that has to be rebuilt.
    /// </remarks>
    public Drops(int seed) => _scatter = new Random(seed);

    /// <summary>How many are lying about.</summary>
    public int Count => _drops.Count;

    /// <summary>Everything currently dropped, for drawing.</summary>
    public IReadOnlyList<Drop> All => _drops;

    /// <summary>Throws a block out of the space it used to fill.</summary>
    /// <param name="block">What was broken.</param>
    /// <param name="blockCoordinate">Where it was.</param>
    public void Spawn(BlockId block, int3 blockCoordinate)
    {
        if (block.IsAir)
            return;

        // Out of the middle of where the block was, with a small push in some direction, so two
        // blocks broken side by side do not land on exactly the same spot.
        var middle = new double3(blockCoordinate.X + 0.5, blockCoordinate.Y + 0.5, blockCoordinate.Z + 0.5);
        var feet = middle - new double3(SizeMeters / 2.0, SizeMeters / 2.0, SizeMeters / 2.0);

        _drops.Add(
            new Drop
            {
                Block = block,
                FeetMeters = feet,
                VelocityMeters = new double3(
                    (_scatter.NextDouble() - 0.5) * 1.6,
                    1.8,
                    (_scatter.NextDouble() - 0.5) * 1.6
                ),
                TurnRadians = (float)(_scatter.NextDouble() * double.Tau),
            }
        );
    }

    /// <summary>Moves everything, picks up what the player is standing over, and forgets the rest.</summary>
    /// <param name="world">The blocks to fall through.</param>
    /// <param name="registry">Where a block id is resolved to whether it stops a body.</param>
    /// <param name="seconds">How long this step is.</param>
    /// <param name="playerFeetMeters">Where the player's feet are.</param>
    /// <param name="inventory">Where a collected block goes.</param>
    /// <returns>How many were picked up.</returns>
    public int Step(
        IVoxelSource world,
        BlockRegistry registry,
        double seconds,
        double3 playerFeetMeters,
        Inventory inventory
    )
    {
        // Measured to the player's middle rather than their feet, so a block beside them on flat
        // ground and a block they are standing on are the same distance away.
        var middle = playerFeetMeters + new double3(0.0, Player.Size.Y / 2.0, 0.0);
        var collected = 0;

        for (var index = _drops.Count - 1; index >= 0; index--)
        {
            var drop = Attract(_drops[index], middle, seconds);
            drop = Move(drop, world, registry, seconds);

            if (drop.AgeSeconds > LifetimeSeconds)
            {
                _drops.RemoveAt(index);
                continue;
            }

            if (drop.AgeSeconds >= SettleSeconds && Near(drop, middle) && inventory.Add(drop.Block, 1) > 0)
            {
                _drops.RemoveAt(index);
                collected++;
                continue;
            }

            _drops[index] = drop;
        }

        return collected;
    }

    /// <summary>Forgets everything dropped, as starting or loading a world does.</summary>
    public void Clear() => _drops.Clear();

    private static bool Near(in Drop drop, double3 playerMiddle) =>
        Distance(drop, playerMiddle) <= ReachMeters;

    private static double Distance(in Drop drop, double3 playerMiddle)
    {
        var toPlayer = playerMiddle - drop.MiddleMeters;
        return double.Sqrt(double3.Dot(toPlayer, toPlayer));
    }

    /// <summary>Pulls a drop towards the player once it is close enough to be theirs.</summary>
    /// <remarks>
    /// Applied to the speed rather than to the position, so a drop under a ledge is pulled into
    /// the ledge and stays there rather than passing through it — the move that follows is the
    /// same one everything else in this world is stopped by.
    /// </remarks>
    private static Drop Attract(Drop drop, double3 playerMiddle, double seconds)
    {
        if (drop.AgeSeconds < SettleSeconds)
            return drop;

        var toPlayer = playerMiddle - drop.MiddleMeters;
        var distance = double.Sqrt(double3.Dot(toPlayer, toPlayer));
        if (distance > AttractMeters || distance <= 1e-6)
            return drop;

        // Stronger the closer it is, so the last half metre is quick and the first two are a
        // drift: a drop that accelerated evenly would overshoot the player and come back.
        var pull = AttractionMetersPerSecondSquared * (1.0 - (distance / AttractMeters));

        return drop with { VelocityMeters = drop.VelocityMeters + (toPlayer / distance * pull * seconds) };
    }

    private static Drop Move(Drop drop, IVoxelSource world, BlockRegistry registry, double seconds)
    {
        var velocity = drop.VelocityMeters;
        velocity.Y = double.Max(
            velocity.Y - (GravityMetersPerSecondSquared * seconds),
            -TerminalSpeedMetersPerSecond
        );

        var move = VoxelBody.Move(world, registry, drop.FeetMeters, BodySize, velocity * seconds);

        if (move.IsOnGround || move.HitCeiling)
            velocity.Y = 0.0;

        if (move.IsOnGround && velocity.Y <= 0.0)
        {
            // Drops come to rest rather than sliding for ever. Nothing about a block says it
            // should keep moving once it has landed, and one that did would end up under the
            // floor of a slope.
            var kept = double.Max(0.0, 1.0 - (GroundDragPerSecond * seconds));
            velocity.X *= kept;
            velocity.Z *= kept;
        }

        return drop with
        {
            FeetMeters = move.FeetMeters,
            VelocityMeters = velocity,
            TurnRadians = (float)(drop.TurnRadians + (TurnRadiansPerSecond * seconds)),
            AgeSeconds = drop.AgeSeconds + seconds,
        };
    }
}

/// <summary>One block lying in the world, waiting to be picked up.</summary>
public readonly record struct Drop
{
    /// <summary>Which block it is.</summary>
    public required BlockId Block { get; init; }

    /// <summary>The middle of its base, in world metres.</summary>
    public required double3 FeetMeters { get; init; }

    /// <summary>How fast it is moving, in metres a second.</summary>
    public double3 VelocityMeters { get; init; }

    /// <summary>How far it has turned about its own upright axis, in radians.</summary>
    public float TurnRadians { get; init; }

    /// <summary>How long it has been lying there, in seconds.</summary>
    public double AgeSeconds { get; init; }

    /// <summary>Where its middle is, which is what a cube is drawn about.</summary>
    public double3 MiddleMeters =>
        FeetMeters + new double3(Drops.SizeMeters / 2.0, Drops.SizeMeters / 2.0, Drops.SizeMeters / 2.0);
}
