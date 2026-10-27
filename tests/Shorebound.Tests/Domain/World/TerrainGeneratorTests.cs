using Shorebound.Domain.World;

namespace Shorebound.Tests.Domain.World;

public class TerrainGeneratorTests
{
    private readonly GameBlocks _blocks;
    private readonly VoxelWorld _world;

    public TerrainGeneratorTests()
    {
        var registry = new BlockRegistry();
        _blocks = new GameBlocks(registry);
        _world = new VoxelWorld(registry, new TerrainGenerator(_blocks, seed: 7));
    }

    [Fact]
    public void the_bottom_of_the_world_is_bedrock_and_the_top_is_air()
    {
        _world.Load(new int3(0, 0, 0));
        _world.Load(new int3(0, VoxelWorld.ChunksTall - 1, 0));

        Assert.Equal(_blocks.Bedrock, _world.BlockAt(1, 0, 1));
        Assert.True(_world.BlockAt(1, VoxelWorld.HeightBlocks - 1, 1).IsAir);
    }

    [Fact]
    public void every_column_has_a_surface_with_something_solid_at_it()
    {
        // The contract the whole generator is built to keep: a column has one surface, and a player
        // dropped at that altitude stands on something.
        var generator = new TerrainGenerator(_blocks, seed: 7);

        for (var x = -40; x <= 40; x += 7)
        {
            var surface = generator.SurfaceHeight(x, x * 3);
            Assert.InRange(surface, 2, VoxelWorld.HeightBlocks - 2);

            _world.Load(
                new int3(
                    WorldCoordinates.ToChunk(x),
                    WorldCoordinates.ToChunk(surface),
                    WorldCoordinates.ToChunk(x * 3)
                )
            );

            Assert.True(_world.BlockAt(x, surface, x * 3).IsSolid, $"nothing solid at the surface of column {x}");
        }
    }

    [Fact]
    public void the_same_seed_gives_the_same_terrain_from_a_fresh_generator()
    {
        // The promise that makes the world storable as a single number. A generator that consulted
        // anything but its coordinate and its seed would break it, and nothing else would notice.
        var registry = new BlockRegistry();
        var other = new VoxelWorld(registry, new TerrainGenerator(new GameBlocks(registry), seed: 7));

        var coordinate = new int3(5, 2, -3);

        Assert.Equal(_world.Load(coordinate)!.Blocks.ToArray(), other.Load(coordinate)!.Blocks.ToArray());
    }

    [Fact]
    public void a_different_seed_gives_a_different_surface()
    {
        var first = new TerrainGenerator(_blocks, seed: 7);
        var second = new TerrainGenerator(_blocks, seed: 8);

        var differences = 0;
        for (var x = 0; x < 50; x++)
        {
            if (first.SurfaceHeight(x * 13, x * 7) != second.SurfaceHeight(x * 13, x * 7))
                differences++;
        }

        // Not all fifty: two height fields can agree at a point by coincidence, and on flat ground
        // rounding to a whole block makes that likelier.
        Assert.True(differences > 40, $"only {differences} of 50 columns differed between seeds");
    }

    [Fact]
    public void the_sea_is_filled_to_the_water_line_and_no_higher()
    {
        var generator = new TerrainGenerator(_blocks, seed: 7);

        // A column whose ground is below the water line must be water from the ground up to that
        // line, and air immediately above it. Searched over a wide square rather than along one
        // diagonal: a continent is fifteen hundred blocks across, so a few hundred blocks in one
        // direction can be entirely inland without anything being wrong.
        for (var x = -3000; x <= 3000; x += 211)
        for (var z = -3000; z <= 3000; z += 211)
        {
            if (generator.SurfaceHeight(x, z) >= TerrainGenerator.SeaLevel)
                continue;

            _world.Load(
                new int3(
                    WorldCoordinates.ToChunk(x),
                    WorldCoordinates.ToChunk(TerrainGenerator.SeaLevel),
                    WorldCoordinates.ToChunk(z)
                )
            );

            Assert.Equal(_blocks.Water, _world.BlockAt(x, TerrainGenerator.SeaLevel, z));
            Assert.True(_world.BlockAt(x, TerrainGenerator.SeaLevel + 1, z).IsAir);
            return;
        }

        Assert.Fail("no column below the water line was found to test");
    }

    [Fact]
    public void a_world_has_both_a_sea_floor_and_a_skyline()
    {
        // The claim the whole height field exists for. A world that is all shelf has nowhere to
        // dig down from and nothing to walk towards, which is what this one was: ten blocks of
        // relief across a kilometre, and every view of it a green plain.
        var generator = new TerrainGenerator(_blocks, seed: 1337);
        var drowned = 0;
        var snowy = 0;
        var columns = 0;

        for (var x = -3000; x <= 3000; x += 97)
        for (var z = -3000; z <= 3000; z += 97)
        {
            var height = generator.SurfaceHeight(x, z);
            columns++;
            drowned += height < TerrainGenerator.SeaLevel ? 1 : 0;
            snowy += height >= TerrainGenerator.SnowLine ? 1 : 0;
        }

        Assert.True(drowned > columns / 10, $"only {drowned} of {columns} columns were under water");
        Assert.True(snowy > columns / 100, $"only {snowy} of {columns} columns reached the snow line");
    }

    [Fact]
    public void most_of_the_world_is_still_somewhere_a_player_can_build()
    {
        // The other half of it. Relief is only worth having if it is the exception: a world that
        // is mountainous everywhere is a world with nowhere to put a works, and this game asks
        // the player to put down belts in straight lines.
        var generator = new TerrainGenerator(_blocks, seed: 1337);
        var gentle = 0;
        var places = 0;

        for (var x = -2000; x <= 2000; x += 211)
        for (var z = -2000; z <= 2000; z += 211)
        {
            var lowest = int.MaxValue;
            var highest = int.MinValue;

            for (var alongX = 0; alongX < 32; alongX += 8)
            for (var alongZ = 0; alongZ < 32; alongZ += 8)
            {
                var height = generator.SurfaceHeight(x + alongX, z + alongZ);
                lowest = int.Min(lowest, height);
                highest = int.Max(highest, height);
            }

            places++;
            gentle += highest - lowest <= 8 ? 1 : 0;
        }

        Assert.True(gentle > places / 2, $"only {gentle} of {places} places were flat enough to build on");
    }

    [Fact]
    public void no_surface_reaches_above_the_height_the_sky_is_skipped_at()
    {
        // Load-bearing rather than cosmetic. A chunk whose bottom is above this is never
        // generated at all, so a peak that could exceed it would not be a tall mountain — it
        // would be a mountain with its top sliced off and the sky showing through.
        var generator = new TerrainGenerator(_blocks, seed: 4);

        for (var x = -4000; x <= 4000; x += 67)
        for (var z = -4000; z <= 4000; z += 67)
            Assert.InRange(generator.SurfaceHeight(x, z), 1, TerrainGenerator.HighestSurface);
    }

    [Fact]
    public void a_steep_face_is_bare_rock()
    {
        // What tells a player at a distance where the stone they want is already exposed, and
        // what stops a cliff face being a wall of grass standing on nothing.
        var generator = new TerrainGenerator(_blocks, seed: 1337);
        var world = new VoxelWorld(_blocks.Registry, generator);
        var looked = 0;

        for (var x = -600; x <= 600 && looked < 4; x += 13)
        for (var z = -600; z <= 600 && looked < 4; z += 13)
        {
            var here = generator.SurfaceHeight(x, z);
            if (here < TerrainGenerator.SeaLevel + 2
                || here >= TerrainGenerator.SnowLine
                || int.Abs(here - generator.SurfaceHeight(x + 1, z)) < 3)
                continue;

            world.Load(
                new int3(WorldCoordinates.ToChunk(x), WorldCoordinates.ToChunk(here), WorldCoordinates.ToChunk(z))
            );

            Assert.Equal(_blocks.Stone, world.BlockAt(x, here, z));
            looked++;
        }

        Assert.True(looked > 0, "no cliff was found within six hundred blocks of the origin to look at");
    }
}
