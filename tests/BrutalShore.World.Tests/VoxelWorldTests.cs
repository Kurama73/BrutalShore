namespace BrutalShore.World.Tests;

public class VoxelWorldTests
{
    private readonly BlockId _stone;
    private readonly VoxelWorld _world;

    public VoxelWorldTests()
    {
        // Flat ground and one nameless block: a test about residency or dirty marking must not be
        // able to fail because a real generator happened to put stone where the test wanted to put
        // stone, and the engine has no business knowing which blocks a game defines.
        var registry = new BlockRegistry();
        _stone = registry.Register(new BlockType { Name = "stone", Color = byte3.HexColor(0x7F7F84) });
        _world = new VoxelWorld(registry, new FlatGenerator(_stone, surfaceHeight: 40));
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
        // Chunk (0, 1, 0) spans blocks 32 to 63, which straddles the flat surface at 40, so the
        // column read back has both stone and air in it.
        var chunk = _world.Load(new int3(0, 1, 0));

        Assert.NotNull(chunk);
        for (var local = 0; local < Chunk.Size; local++)
        {
            var world = Chunk.Size + local;
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
    public void a_chunk_unloaded_and_loaded_again_comes_back_identical()
    {
        var coordinate = new int3(5, 2, -3);

        var first = _world.Load(coordinate)!.Blocks.ToArray();
        _world.Unload(coordinate);
        var second = _world.Load(coordinate)!.Blocks.ToArray();

        Assert.Equal(first, second);
    }

    [Fact]
    public void streaming_loads_a_column_of_chunks_around_the_observer_and_drops_the_far_ones()
    {
        _world.UpdateStreaming(int3.Zero, radiusChunks: 2, maxLoadsPerCall: 10_000);

        // A 5 by 5 footprint, the full height of the world.
        Assert.Equal(5 * 5 * VoxelWorld.ChunksTall, _world.LoadedChunkCount);

        _world.UpdateStreaming(new int3(40, 0, 0), radiusChunks: 2, maxLoadsPerCall: 10_000);

        Assert.DoesNotContain(new int3(0, 0, 0), _world.LoadedCoordinates);
        Assert.Contains(new int3(40, 0, 0), _world.LoadedCoordinates);
    }

    [Fact]
    public void streaming_never_loads_more_than_its_budget_in_one_call()
    {
        // The budget is what stops a frame freezing when the observer turns a corner and brings
        // hundreds of chunks into range at once.
        var loaded = _world.UpdateStreaming(int3.Zero, radiusChunks: 8, maxLoadsPerCall: 12);

        Assert.Equal(12, loaded);
        Assert.Equal(12, _world.LoadedChunkCount);
    }

    [Fact]
    public void a_chunk_is_only_meshable_once_every_neighbour_it_reads_is_resident()
    {
        var centre = new int3(0, 3, 0);
        _world.Load(centre);

        Assert.False(_world.IsMeshable(centre));

        _world.UpdateStreaming(int3.Zero, radiusChunks: 1, maxLoadsPerCall: 10_000);

        Assert.True(_world.IsMeshable(centre));
    }

    [Fact]
    public void a_chunk_at_the_top_of_the_world_is_meshable_without_a_chunk_above_it()
    {
        // There is genuinely nothing above the world, so a missing neighbour there is not a
        // neighbour that has yet to load. Without this distinction the topmost chunks never mesh.
        _world.UpdateStreaming(int3.Zero, radiusChunks: 1, maxLoadsPerCall: 10_000);

        Assert.True(_world.IsMeshable(new int3(0, VoxelWorld.ChunksTall - 1, 0)));
        Assert.True(_world.IsMeshable(new int3(0, 0, 0)));
    }

    [Fact]
    public void editing_a_block_on_a_boundary_marks_the_chunk_on_the_other_side_too()
    {
        // Otherwise the neighbour keeps drawing the face that has just been opened, and the hole
        // the player dug is covered by a wall from the next chunk along.
        _world.UpdateStreaming(int3.Zero, radiusChunks: 1, maxLoadsPerCall: 10_000);
        foreach (var coordinate in _world.LoadedCoordinates.ToArray())
            _world.ClearDirty(coordinate);

        // Block (0, 48, 0) is air over flat ground, and sits on the minimum corner of chunk
        // (0, 1, 0), so it touches the two neighbours across those faces.
        Assert.True(_world.SetBlock(new int3(0, 48, 0), _stone));

        Assert.Contains(new int3(0, 1, 0), _world.Dirty);
        Assert.Contains(new int3(-1, 1, 0), _world.Dirty);
        Assert.Contains(new int3(0, 1, -1), _world.Dirty);
    }

    [Fact]
    public void editing_a_block_in_the_middle_of_a_chunk_marks_only_that_chunk()
    {
        _world.UpdateStreaming(int3.Zero, radiusChunks: 1, maxLoadsPerCall: 10_000);
        foreach (var coordinate in _world.LoadedCoordinates.ToArray())
            _world.ClearDirty(coordinate);

        Assert.True(_world.SetBlock(new int3(16, 48, 16), _stone));

        Assert.Single(_world.Dirty);
        Assert.Contains(new int3(0, 1, 0), _world.Dirty);
    }

    [Fact]
    public void an_unloaded_column_has_no_highest_block()
    {
        var registry = new BlockRegistry();
        var ground = registry.Register(new BlockType { Name = "ground", Color = byte3.HexColor(0x808080) });
        var world = new VoxelWorld(registry, new FlatGenerator(ground, surfaceHeight: 20));

        Assert.Equal(int.MinValue, world.HighestBlock(3, 5));
    }

    [Fact]
    public void the_highest_block_of_a_flat_world_is_its_surface()
    {
        var registry = new BlockRegistry();
        var ground = registry.Register(new BlockType { Name = "ground", Color = byte3.HexColor(0x808080) });
        var world = new VoxelWorld(registry, new FlatGenerator(ground, surfaceHeight: 20));

        for (var chunkY = 0; chunkY < VoxelWorld.ChunksTall; chunkY++)
            world.Load(new int3(0, chunkY, 0));

        // The generator fills up to and including the surface height.
        Assert.Equal(20, world.HighestBlock(3, 5));
    }

    [Fact]
    public void a_block_placed_above_the_surface_becomes_the_highest()
    {
        var registry = new BlockRegistry();
        var ground = registry.Register(new BlockType { Name = "ground", Color = byte3.HexColor(0x808080) });
        var world = new VoxelWorld(registry, new FlatGenerator(ground, surfaceHeight: 20));

        for (var chunkY = 0; chunkY < VoxelWorld.ChunksTall; chunkY++)
            world.Load(new int3(0, chunkY, 0));

        world.SetBlock(new int3(3, 40, 5), ground);

        // Across chunks, which is the case the naive answer gets wrong: the block is two chunks
        // above the one holding the surface.
        Assert.Equal(40, world.HighestBlock(3, 5));
    }

    [Fact]
    public void breaking_the_top_block_lowers_the_highest()
    {
        var registry = new BlockRegistry();
        var ground = registry.Register(new BlockType { Name = "ground", Color = byte3.HexColor(0x808080) });
        var world = new VoxelWorld(registry, new FlatGenerator(ground, surfaceHeight: 20));

        for (var chunkY = 0; chunkY < VoxelWorld.ChunksTall; chunkY++)
            world.Load(new int3(0, chunkY, 0));

        world.SetBlock(new int3(3, 20, 5), BlockId.Air);

        Assert.Equal(19, world.HighestBlock(3, 5));
    }

    [Fact]
    public void resetting_a_world_discards_its_chunks_and_edits_before_using_the_new_generator()
    {
        var registry = new BlockRegistry();
        var oldGround = registry.Register(new BlockType { Name = "old ground", Color = byte3.HexColor(0x808080) });
        var newGround = registry.Register(new BlockType { Name = "new ground", Color = byte3.HexColor(0xA08060) });
        using var world = new VoxelWorld(registry, new FlatGenerator(oldGround, surfaceHeight: 40));
        world.Load(new int3(0, 1, 0));
        world.SetBlock(new int3(3, 41, 5), BlockId.Air);

        world.Reset(new FlatGenerator(newGround, surfaceHeight: 10), streamer: null);
        world.Load(new int3(0, 0, 0));

        Assert.Equal(0, world.Edits.Count);
        Assert.Equal(1, world.LoadedChunkCount);
        Assert.Equal(newGround, world.BlockAt(3, 10, 5));
        Assert.True(world.BlockAt(3, 40, 5).IsAir);
    }
}
