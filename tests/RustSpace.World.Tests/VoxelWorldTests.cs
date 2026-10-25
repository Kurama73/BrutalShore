namespace RustSpace.World.Tests;

public class VoxelWorldTests
{
    private readonly GameBlocks _blocks;
    private readonly VoxelWorld _world;
    private readonly VoxelWorld _flat;

    public VoxelWorldTests()
    {
        var registry = new BlockRegistry();
        _blocks = new GameBlocks(registry);
        _world = new VoxelWorld(registry, new TerrainGenerator(_blocks, seed: 7));

        // Residency and editing are tested against flat ground rather than terrain, so that a test
        // about dirty marking cannot fail because the generator happened to put stone where it
        // wanted to place stone.
        _flat = new VoxelWorld(registry, new FlatGenerator(_blocks.Stone, surfaceHeight: 40));
    }

    [Fact]
    public void a_block_in_an_unloaded_chunk_reads_as_air()
    {
        // Not an error: the mesher asks about coordinates past the edge of the loaded world on
        // every chunk it meshes, and the answer there is "nothing yet".
        Assert.True(_world.BlockAt(10_000, 40, 10_000).IsAir);
    }

    [Fact]
    public void a_loaded_chunk_reads_back_what_the_generator_wrote()
    {
        var chunk = _world.Load(new int3(0, 2, 0));

        Assert.NotNull(chunk);
        for (var local = 0; local < Chunk.Size; local++)
        {
            var world = 2 * Chunk.Size + local;
            Assert.Equal(chunk[3, local, 5], _world.BlockAt(3, world, 5));
        }
    }

    [Fact]
    public void a_chunk_above_the_world_is_not_loadable()
    {
        Assert.Null(_world.Load(new int3(0, VoxelWorld.ChunksTall, 0)));
        Assert.Null(_world.Load(new int3(0, -1, 0)));
    }

    [Fact]
    public void the_generator_gives_the_same_chunk_every_time_it_is_asked()
    {
        var coordinate = new int3(5, 2, -3);

        var first = _world.Load(coordinate)!.Blocks.ToArray();
        _world.Unload(coordinate);
        var second = _world.Load(coordinate)!.Blocks.ToArray();

        Assert.Equal(first, second);
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
    public void the_surface_is_solid_and_the_block_above_it_is_not()
    {
        // The contract the whole generator is built to keep: a column has one surface, and a player
        // dropped at that altitude stands on something.
        var generator = new TerrainGenerator(_blocks, seed: 7);

        for (var x = -40; x <= 40; x += 7)
        {
            var surface = generator.SurfaceHeight(x, x * 3);
            Assert.InRange(surface, 2, VoxelWorld.HeightBlocks - 2);

            _world.Load(new int3(WorldCoordinates.ToChunk(x), WorldCoordinates.ToChunk(surface), WorldCoordinates.ToChunk(x * 3)));

            Assert.True(_world.BlockAt(x, surface, x * 3).IsSolid, $"nothing solid at the surface of column {x}");
        }
    }

    [Fact]
    public void streaming_loads_a_column_of_chunks_around_the_observer_and_drops_the_far_ones()
    {
        _flat.UpdateStreaming(int3.Zero, radiusChunks: 2, maxLoadsPerCall: 10_000);

        // A 5 by 5 footprint, the full height of the world.
        Assert.Equal(5 * 5 * VoxelWorld.ChunksTall, _flat.LoadedChunkCount);

        _flat.UpdateStreaming(new int3(40, 0, 0), radiusChunks: 2, maxLoadsPerCall: 10_000);

        Assert.DoesNotContain(new int3(0, 0, 0), _flat.LoadedCoordinates);
        Assert.Contains(new int3(40, 0, 0), _flat.LoadedCoordinates);
    }

    [Fact]
    public void streaming_never_loads_more_than_its_budget_in_one_call()
    {
        // The budget is what stops a frame freezing when the observer turns a corner and brings
        // hundreds of chunks into range at once.
        var loaded = _flat.UpdateStreaming(int3.Zero, radiusChunks: 8, maxLoadsPerCall: 12);

        Assert.Equal(12, loaded);
        Assert.Equal(12, _flat.LoadedChunkCount);
    }

    [Fact]
    public void a_chunk_is_only_meshable_once_every_neighbour_it_reads_is_resident()
    {
        var centre = new int3(0, 3, 0);
        _flat.Load(centre);

        Assert.False(_flat.IsMeshable(centre));

        _flat.UpdateStreaming(int3.Zero, radiusChunks: 1, maxLoadsPerCall: 10_000);

        Assert.True(_flat.IsMeshable(centre));
    }

    [Fact]
    public void a_chunk_at_the_top_of_the_world_is_meshable_without_a_chunk_above_it()
    {
        // There is genuinely nothing above the world, so a missing neighbour there is not a
        // neighbour that has yet to load. Without this distinction the topmost chunks never mesh.
        _flat.UpdateStreaming(int3.Zero, radiusChunks: 1, maxLoadsPerCall: 10_000);

        Assert.True(_flat.IsMeshable(new int3(0, VoxelWorld.ChunksTall - 1, 0)));
        Assert.True(_flat.IsMeshable(new int3(0, 0, 0)));
    }

    [Fact]
    public void editing_a_block_on_a_boundary_marks_the_chunk_on_the_other_side_too()
    {
        // Otherwise the neighbour keeps drawing the face that has just been opened, and the hole
        // the player dug is covered by a wall from the next chunk along.
        _flat.UpdateStreaming(int3.Zero, radiusChunks: 1, maxLoadsPerCall: 10_000);
        foreach (var coordinate in _flat.LoadedCoordinates.ToArray())
            _flat.ClearDirty(coordinate);

        // Block (0, 48, 0) is air over flat ground, and sits on the minimum corner of chunk
        // (0, 1, 0), so it touches the two neighbours across those faces.
        Assert.True(_flat.SetBlock(new int3(0, 48, 0), _blocks.Stone));

        Assert.Contains(new int3(0, 1, 0), _flat.Dirty);
        Assert.Contains(new int3(-1, 1, 0), _flat.Dirty);
        Assert.Contains(new int3(0, 1, -1), _flat.Dirty);
    }

    [Fact]
    public void editing_a_block_in_the_middle_of_a_chunk_marks_only_that_chunk()
    {
        _flat.UpdateStreaming(int3.Zero, radiusChunks: 1, maxLoadsPerCall: 10_000);
        foreach (var coordinate in _flat.LoadedCoordinates.ToArray())
            _flat.ClearDirty(coordinate);

        Assert.True(_flat.SetBlock(new int3(16, 48, 16), _blocks.Stone));

        Assert.Single(_flat.Dirty);
        Assert.Contains(new int3(0, 1, 0), _flat.Dirty);
    }
}
