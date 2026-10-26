using BrutalShore.Voxel;
using BrutalShore.World;

namespace Shorebound.Tests;

/// <summary>
/// What breaking a block promises: it takes the block's own time, looking away gives it up, and
/// some blocks never break at all.
/// </summary>
/// <remarks>
/// All of it is about the rule rather than the number. A player cannot tell 1.5 seconds from 1.6,
/// and every one of these would still matter if every time in the game were retuned tomorrow:
/// that harder is slower, that letting go costs the progress, and that the floor of the world
/// stays there.
/// </remarks>
public sealed class MiningTests
{
    private const double Step = 1.0 / 60.0;

    private readonly BlockRegistry _registry = new();
    private readonly VoxelWorld _world;
    private readonly BlockId _soft;
    private readonly BlockId _hard;
    private readonly BlockId _floor;

    /// <summary>Builds a world of one chunk with three kinds of block in it.</summary>
    public MiningTests()
    {
        _soft = _registry.Register(
            new BlockType { Name = "soft", Color = new byte3(120, 90, 60), BreakSeconds = 0.25f }
        );
        _hard = _registry.Register(
            new BlockType { Name = "hard", Color = new byte3(128, 128, 128), BreakSeconds = 1.5f }
        );
        _floor = _registry.Register(
            new BlockType
            {
                Name = "floor",
                Color = new byte3(40, 40, 40),
                BreakSeconds = float.PositiveInfinity,
            }
        );

        _world = new VoxelWorld(_registry, new FlatGenerator(_soft));
        _world.Load(int3.Zero);
    }

    [Fact]
    public void a_block_does_not_break_on_the_first_step()
    {
        var mining = new Mining();

        Assert.Null(mining.Step(_world, At(1, 1, 1, _soft), isBreaking: true, Step));
        Assert.True(mining.Progress > 0f);
    }

    [Fact]
    public void a_block_breaks_once_its_own_time_has_passed()
    {
        var mining = new Mining();
        var aimed = At(1, 1, 1, _soft);
        var steps = 0;

        while (mining.Step(_world, aimed, isBreaking: true, Step) is null)
        {
            steps++;
            Assert.True(steps < 600, "a block that should take a quarter of a second never broke");
        }

        // A quarter of a second at sixty steps is fifteen of them, give or take the one the last
        // fraction lands in.
        Assert.InRange(steps, 13, 17);
    }

    [Fact]
    public void a_harder_block_takes_longer_than_a_softer_one()
    {
        Assert.True(StepsToBreak(_hard, 2, 1, 1) > StepsToBreak(_soft, 1, 1, 1) * 4);
    }

    [Fact]
    public void letting_go_of_the_button_gives_up_whatever_was_done()
    {
        var mining = new Mining();
        var aimed = At(1, 1, 1, _hard);

        for (var tick = 0; tick < 30; tick++)
            mining.Step(_world, aimed, isBreaking: true, Step);

        var before = mining.Progress;
        mining.Step(_world, aimed, isBreaking: false, Step);

        Assert.True(before > 0.2f, $"half a second of breaking only reached {before:F2}");
        Assert.Equal(0f, mining.Progress);
        Assert.Null(mining.Target);
    }

    [Fact]
    public void looking_at_a_different_block_starts_again()
    {
        // The rule that stops a player chipping at six blocks at once and finishing them all
        // together by sweeping the crosshair back and forth.
        var mining = new Mining();

        for (var tick = 0; tick < 30; tick++)
            mining.Step(_world, At(1, 1, 1, _hard), isBreaking: true, Step);

        mining.Step(_world, At(2, 1, 1, _hard), isBreaking: true, Step);

        Assert.Equal(new int3(2, 1, 1), mining.Target);
        Assert.True(mining.Progress < 0.05f, $"moving the aim kept {mining.Progress:F2} of the progress");
    }

    [Fact]
    public void aiming_at_nothing_gives_up_as_letting_go_does()
    {
        var mining = new Mining();

        for (var tick = 0; tick < 30; tick++)
            mining.Step(_world, At(1, 1, 1, _hard), isBreaking: true, Step);

        mining.Step(_world, aimed: null, isBreaking: true, Step);

        Assert.Equal(0f, mining.Progress);
    }

    [Fact]
    public void a_block_that_never_breaks_never_does_however_long_it_is_held()
    {
        var mining = new Mining();
        _world.SetBlock(new int3(3, 1, 1), _floor);
        var aimed = At(3, 1, 1, _floor);

        for (var tick = 0; tick < 600; tick++)
            Assert.Null(mining.Step(_world, aimed, isBreaking: true, Step));

        Assert.Equal(0f, mining.Progress);
    }

    private int StepsToBreak(BlockId block, int x, int y, int z)
    {
        _world.SetBlock(new int3(x, y, z), block);

        var mining = new Mining();
        var aimed = At(x, y, z, block);
        var steps = 0;

        while (mining.Step(_world, aimed, isBreaking: true, Step) is null)
        {
            steps++;
            Assert.True(steps < 1200, "the block never broke");
        }

        return steps;
    }

    private VoxelHit At(int x, int y, int z, BlockId block)
    {
        _world.SetBlock(new int3(x, y, z), block);
        return new VoxelHit
        {
            Block = new int3(x, y, z),
            Before = new int3(x, y + 1, z),
            Face = VoxelFace.PositiveY,
            DistanceMeters = 2.0,
        };
    }

    /// <summary>A world of one solid layer, so there is always something to aim at.</summary>
    private sealed class FlatGenerator(BlockId block) : IChunkGenerator
    {
        public void Generate(int3 chunkCoordinate, Chunk into)
        {
            if (chunkCoordinate.Y != 0)
                return;

            var blocks = into.BeginBulkWrite();
            for (var z = 0; z < Chunk.Size; z++)
            for (var x = 0; x < Chunk.Size; x++)
                blocks[Chunk.IndexOf(x, 1, z)] = block;

            into.EndBulkWrite();
        }
    }
}
