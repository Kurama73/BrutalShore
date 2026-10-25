namespace Shorebound.Tests;

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
        // line, and air immediately above it.
        for (var x = -200; x <= 200; x += 11)
        {
            if (generator.SurfaceHeight(x, x) >= TerrainGenerator.SeaLevel)
                continue;

            _world.Load(new int3(WorldCoordinates.ToChunk(x), WorldCoordinates.ToChunk(TerrainGenerator.SeaLevel), WorldCoordinates.ToChunk(x)));

            Assert.Equal(_blocks.Water, _world.BlockAt(x, TerrainGenerator.SeaLevel, x));
            Assert.True(_world.BlockAt(x, TerrainGenerator.SeaLevel + 1, x).IsAir);
            return;
        }

        Assert.Fail("no column below the water line was found to test");
    }
}
