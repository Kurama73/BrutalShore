using BrutalShore.Voxel;
using BrutalShore.World;

namespace Shorebound;

/// <summary>A works already standing when the run starts, so the loop can be watched running.</summary>
/// <remarks>
/// <para>
/// Everything this game is about happens over minutes: a drill fills a belt, a belt feeds a fire,
/// a fire turns rock into metal, and the noise of all three brings things out of the dark. None of
/// that can be seen in a screenshot of the first morning, and none of it can be driven from a
/// script either — placing a drill by aiming at a block and clicking is a minute of mouse work to
/// set up ten seconds of watching.
/// </para>
/// <para>
/// So a scenario builds the works with the same calls a player's click makes, and then gets out of
/// the way. What it proves is the thing no unit test can: that the machines, the grid, the
/// renderer and the dark all run together in one process, against the real world, for as long as
/// the run lasts. What it reports at the end is what actually happened — ore dug, metal made,
/// watts drawn, and what came for it.
/// </para>
/// <para>
/// Two of them, which is why this is a base class and not a switch: a works by day, and the same
/// works at night with something standing watch over it.
/// </para>
/// </remarks>
public abstract class Scenario
{
    /// <summary>How far the works reaches from its corner, in blocks, on each horizontal axis.</summary>
    /// <remarks>Enough for the drill, two belts, the fire, the wire and whatever stands beyond them.</remarks>
    private const int Reach = 8;

    /// <summary>The scenario of that name, or nothing.</summary>
    /// <param name="name">What was asked for on the command line.</param>
    /// <returns>The scenario, or <see langword="null" /> if there is no such thing.</returns>
    public static Scenario? Named(string name) =>
        name switch
        {
            "works" => new Works(),
            "night" => new Night(),
            _ => null,
        };

    /// <summary>The names that can be asked for, for an error message.</summary>
    public static IReadOnlyList<string> Names => ["works", "night"];

    /// <summary>What this one is called.</summary>
    public abstract string Name { get; }

    /// <summary>What time of day it starts at, from 0 to 24.</summary>
    public abstract double StartHour { get; }

    /// <summary>Where the corner of the works goes, once the ground height is known.</summary>
    /// <param name="groundHeight">The surface height at the origin.</param>
    /// <returns>The block the drill stands in.</returns>
    public static int3 CornerAt(int groundHeight) => new(-2, groundHeight + 1, -2);

    /// <summary>Where to stand to see the whole of it.</summary>
    /// <param name="corner">Where the works was built.</param>
    /// <returns>The eye position and the angles to look along.</returns>
    /// <remarks>
    /// The angles are worked out from the two positions rather than written down, so that moving
    /// the camera cannot leave it pointing at where the works used to be. High enough to be above
    /// the hillside the works was flattened out of, which is the mistake the first version made:
    /// an eye at head height beside it is an eye inside a hill.
    /// </remarks>
    public virtual Viewpoint ViewFrom(int3 corner)
    {
        var eye = new double3(corner.X - 3.5, corner.Y + 7.0, corner.Z + 6.0);
        var middle = new double3(corner.X + 2.5, corner.Y + 0.5, corner.Z + 1.0);

        return Aimed(eye, middle);
    }

    /// <summary>A viewpoint at one place looking at another.</summary>
    /// <param name="eye">Where the camera is.</param>
    /// <param name="at">What it should be looking at.</param>
    /// <returns>The viewpoint.</returns>
    protected static Viewpoint Aimed(double3 eye, double3 at)
    {
        var along = at - eye;
        var flat = double.Sqrt((along.X * along.X) + (along.Z * along.Z));

        return new Viewpoint(
            eye,
            (float)double.Atan2(along.Z, along.X),
            (float)double.Atan2(along.Y, flat)
        );
    }

    /// <summary>Builds the works into the world.</summary>
    /// <param name="blocks">The game's block types.</param>
    /// <param name="world">The world to build it in.</param>
    /// <param name="machines">Where the things standing in those blocks are registered.</param>
    /// <param name="suggested">Roughly where it should go.</param>
    /// <returns>Where it actually went, which is what the camera should be aimed at.</returns>
    /// <remarks>
    /// The chunks it touches are loaded first, because writing a block into a chunk that has not
    /// streamed in is a write that silently does nothing. The height is then taken from the
    /// ground rather than from the suggestion: this coast slopes, and a works laid at the height
    /// of one corner of it is a works with a hillside through the middle.
    /// </remarks>
    public virtual int3 Build(GameBlocks blocks, VoxelWorld world, Machines machines, int3 suggested)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(machines);

        LoadAround(world, suggested);
        var corner = OnTopOfTheGround(world, suggested);
        LoadAround(world, corner);
        Clear(world, corner);
        Floor(blocks, world, corner);

        // The seam the drill works. It is never consumed — a drill reads what is under it and
        // leaves it there — so one block of ore is a works that runs for as long as it is fed.
        world.SetBlock(Under(corner), blocks.IronOre);

        Stand(blocks, world, machines, corner, blocks.Miner, new Miner());
        Stand(blocks, world, machines, corner + new int3(1, 0, 0), blocks.Conveyor, new Conveyor(Facing.East));
        Stand(blocks, world, machines, corner + new int3(2, 0, 0), blocks.Conveyor, new Conveyor(Facing.East));
        Stand(blocks, world, machines, corner + new int3(3, 0, 0), blocks.Smelter, new Smelter());

        // One fire, run to the rest on a wire: the thing the whole middle of the game is about.
        var generator = new Generator();
        generator.Fuel.Add(blocks.CoalOre, 32);
        Stand(blocks, world, machines, corner + new int3(0, 0, 2), blocks.Generator, generator);

        for (var along = 0; along <= 3; along++)
            Stand(blocks, world, machines, corner + new int3(along, 0, 1), blocks.Cable, new Cable());

        return corner;
    }

    /// <summary>Lifts the works to stand on the highest ground under its footprint.</summary>
    /// <param name="world">The world, whose surface this reads.</param>
    /// <param name="suggested">Roughly where the works should go.</param>
    /// <returns>The same place, at a height nothing in the footprint pokes through.</returns>
    private static int3 OnTopOfTheGround(VoxelWorld world, int3 suggested)
    {
        var highest = suggested.Y;

        for (var x = -1; x < Reach; x++)
        for (var z = -1; z < 5; z++)
            highest = int.Max(highest, world.HighestBlock(suggested.X + x, suggested.Z + z) + 1);

        return new int3(suggested.X, highest, suggested.Z);
    }

    /// <summary>What the works did, in one line.</summary>
    /// <param name="blocks">The game's block types, for naming what came out.</param>
    /// <param name="machines">The works.</param>
    /// <param name="stalkers">What came out of the dark for it.</param>
    /// <returns>A line for the console and for the run's report.</returns>
    public virtual string Report(GameBlocks blocks, Machines machines, Stalkers stalkers)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(machines);

        var dug = 0;
        var made = 0;
        var waiting = 0;

        foreach (var (_, machine) in machines.All)
        {
            switch (machine)
            {
                case Miner miner:
                    dug += miner.Output.CountOf(blocks.IronOre);
                    break;

                case Smelter smelter:
                    made += smelter.Output.CountOf(blocks.IronIngot);
                    waiting += smelter.Input.CountOf(blocks.IronOre);
                    break;
            }
        }

        return $"{Name}: {made} ingots made, {waiting} ore waiting at the fire, "
            + $"{machines.CarriedCount} riding the belts, {dug} stacked at the drill, "
            + $"{machines.Power.DeliveredWatts:F0} watts delivered of {machines.Power.DemandedWatts:F0} asked for.";
    }

    /// <summary>Puts a machine in the world and in the register, the way a click does.</summary>
    /// <param name="blocks">The game's block types.</param>
    /// <param name="world">The world the block goes in.</param>
    /// <param name="machines">Where the machine is registered.</param>
    /// <param name="position">Where it stands.</param>
    /// <param name="block">The block that is drawn there.</param>
    /// <param name="machine">The thing standing in it.</param>
    protected static void Stand(
        GameBlocks blocks,
        VoxelWorld world,
        Machines machines,
        int3 position,
        BlockId block,
        IMachine machine
    )
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(machines);

        world.SetBlock(position, block);
        machines.Place(position, machine);
    }

    /// <summary>The block a drill reads, which is the one under it.</summary>
    /// <param name="corner">Where the drill stands.</param>
    /// <returns>The block beneath.</returns>
    protected static int3 Under(int3 corner) => new(corner.X, corner.Y - 1, corner.Z);

    private static void LoadAround(VoxelWorld world, int3 corner)
    {
        var lowest = WorldCoordinates.ToChunk(new int3(corner.X - Reach, corner.Y - Reach, corner.Z - Reach));
        var highest = WorldCoordinates.ToChunk(new int3(corner.X + Reach, corner.Y + Reach, corner.Z + Reach));

        for (var x = lowest.X; x <= highest.X; x++)
        for (var y = lowest.Y; y <= highest.Y; y++)
        for (var z = lowest.Z; z <= highest.Z; z++)
            world.Load(new int3(x, y, z));
    }

    /// <summary>Empties the air above the works, so terrain cannot stand in the middle of it.</summary>
    private static void Clear(VoxelWorld world, int3 corner)
    {
        for (var x = -1; x < Reach; x++)
        for (var y = 0; y < 4; y++)
        for (var z = -1; z < 5; z++)
            world.SetBlock(corner + new int3(x, y, z), BlockId.Air);
    }

    /// <summary>Lays something flat to build on, because a hillside is not a works.</summary>
    private static void Floor(GameBlocks blocks, VoxelWorld world, int3 corner)
    {
        for (var x = -1; x < Reach; x++)
        for (var z = -1; z < 5; z++)
            world.SetBlock(corner + new int3(x, -1, z), blocks.StoneBrick);
    }
}

/// <summary>A drill, two belts and a fire, all run off one generator, in the middle of the day.</summary>
/// <remarks>
/// The shortest arrangement that is the whole production loop: something that digs, something that
/// carries, something that transforms, and electricity holding all three up. If this runs, the
/// game's middle runs.
/// </remarks>
public sealed class Works : Scenario
{
    /// <inheritdoc />
    public override string Name => "works";

    /// <inheritdoc />
    public override double StartHour => 11.0;
}

/// <summary>The same works at night, with something standing watch over it.</summary>
/// <remarks>
/// The other half of the loop, and the one that makes the first half a decision rather than a
/// chore: the more of the works is running, the more comes out of the dark for it. A turret on the
/// same wire as the drill is the game's answer, and whether that answer holds is a thing that can
/// only be found out by leaving it running in the dark.
/// </remarks>
public sealed class Night : Scenario
{
    /// <inheritdoc />
    public override string Name => "night";

    /// <summary>Late enough that the dark is already out, and hours before it lifts.</summary>
    public override double StartHour => 22.5;

    /// <inheritdoc />
    public override int3 Build(GameBlocks blocks, VoxelWorld world, Machines machines, int3 suggested)
    {
        var corner = base.Build(blocks, world, machines, suggested);

        ArgumentNullException.ThrowIfNull(blocks);

        // On the same wire as everything else, because a turret that needed its own fire would be
        // a turret nobody builds.
        Stand(blocks, world, machines, corner + new int3(4, 0, 1), blocks.Cable, new Cable());
        Stand(blocks, world, machines, corner + new int3(5, 0, 1), blocks.Turret, new Turret());

        return corner;
    }

    /// <summary>Watched from above the works, high enough up that the dark cannot reach.</summary>
    /// <remarks>
    /// <para>
    /// The first version stood beside the works at head height and was eaten, which is the game
    /// behaving correctly and a useless screenshot. The second stood ten blocks off to one side,
    /// which was worse than useless: the horde hunts the player rather than the machines, so it
    /// walked past the works to the watcher and the turret had nothing to shoot at. The works was
    /// defended by being boring.
    /// </para>
    /// <para>
    /// So: over the works, out of reach but not out of the way. What comes for the watcher
    /// arrives inside the turret's reach, which is the arrangement a player standing in their own
    /// base is actually in.
    /// </para>
    /// </remarks>
    public override Viewpoint ViewFrom(int3 corner) =>
        Aimed(
            new double3(corner.X + 1.0, corner.Y + 9.0, corner.Z + 6.0),
            new double3(corner.X + 3.0, corner.Y + 0.5, corner.Z + 1.0)
        );

    /// <inheritdoc />
    public override string Report(GameBlocks blocks, Machines machines, Stalkers stalkers)
    {
        ArgumentNullException.ThrowIfNull(machines);
        ArgumentNullException.ThrowIfNull(stalkers);

        var shots = 0;
        foreach (var (_, machine) in machines.All)
        {
            if (machine is Turret turret)
                shots += turret.Shots;
        }

        return base.Report(blocks, machines, stalkers)
            + $" The turret fired {shots} times; {stalkers.Count} still about.";
    }
}
