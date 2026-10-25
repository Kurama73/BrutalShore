namespace BrutalShore.World.Tests;

/// <summary>
/// Pins down the frozen view a mesh worker reads: it sees its neighbours, it sees air past them,
/// and it keeps working after the world has moved on.
/// </summary>
/// <remarks>
/// The last of those is the whole safety argument for meshing off the frame, and it is the one
/// that would otherwise only be found as a crash under load on someone else's machine.
/// </remarks>
public sealed class ChunkNeighbourhoodTests
{
    [Fact]
    public void the_middle_is_the_chunk_that_was_asked_for()
    {
        var (world, _) = Flat();
        world.UpdateStreaming(int3.Zero, radiusChunks: 1, maxLoadsPerCall: 10_000);

        var captured = world.CaptureNeighbourhood(new int3(0, 1, 0));

        Assert.NotNull(captured);
        Assert.Equal(new int3(0, 1, 0), captured.Centre);
        Assert.Same(world.Find(new int3(0, 1, 0)), captured.Middle);
    }

    [Fact]
    public void a_block_in_a_neighbouring_chunk_reads_through()
    {
        var (world, ground) = Flat();
        world.UpdateStreaming(int3.Zero, radiusChunks: 1, maxLoadsPerCall: 10_000);

        var captured = world.CaptureNeighbourhood(new int3(0, 0, 0))!;

        // One block past the chunk's own edge on each axis, which is exactly what the mesher
        // reads to decide whether a boundary face is worth building.
        Assert.Equal(ground, captured.BlockAt(-1, 5, 5));
        Assert.Equal(ground, captured.BlockAt(Chunk.Size, 5, 5));
        Assert.Equal(ground, captured.BlockAt(5, 5, -1));
    }

    [Fact]
    public void anything_past_the_neighbours_reads_as_air()
    {
        var (world, _) = Flat();
        world.UpdateStreaming(int3.Zero, radiusChunks: 3, maxLoadsPerCall: 10_000);

        var captured = world.CaptureNeighbourhood(new int3(0, 0, 0))!;

        // Two chunks out there is solid ground, but this view does not reach it. Air is the right
        // answer for a mesher: a face with nothing known beyond it is a face worth drawing.
        Assert.Equal(BlockId.Air, captured.BlockAt(Chunk.Size * 2, 5, 5));
    }

    [Fact]
    public void a_neighbour_that_is_not_loaded_reads_as_air()
    {
        var (world, _) = Flat();
        world.Load(new int3(0, 0, 0));

        var captured = world.CaptureNeighbourhood(new int3(0, 0, 0))!;

        Assert.Equal(BlockId.Air, captured.BlockAt(-1, 5, 5));
    }

    [Fact]
    public void a_chunk_that_is_not_loaded_cannot_be_captured()
    {
        var (world, _) = Flat();

        Assert.Null(world.CaptureNeighbourhood(new int3(9, 9, 9)));
    }

    [Fact]
    public void a_capture_still_reads_after_the_world_has_unloaded_it()
    {
        var (world, ground) = Flat();
        world.UpdateStreaming(int3.Zero, radiusChunks: 1, maxLoadsPerCall: 10_000);

        var captured = world.CaptureNeighbourhood(new int3(0, 0, 0))!;

        foreach (var coordinate in world.LoadedCoordinates.ToArray())
            world.Unload(coordinate);

        // This is why a worker may hold one across frames: the references keep the chunks alive,
        // so a capture can only ever go out of date, never become a read of freed memory.
        Assert.Equal(0, world.LoadedChunkCount);
        Assert.Equal(ground, captured.BlockAt(5, 5, 5));
        Assert.Equal(ground, captured.BlockAt(-1, 5, 5));
    }

    [Fact]
    public void a_capture_sees_an_edit_made_after_it_was_taken()
    {
        var (world, _) = Flat();
        world.UpdateStreaming(int3.Zero, radiusChunks: 1, maxLoadsPerCall: 10_000);

        var captured = world.CaptureNeighbourhood(new int3(0, 0, 0))!;
        world.SetBlock(new int3(5, 5, 5), BlockId.Air);

        // Deliberate: the view holds references, not copies. A worker can therefore mesh a chunk
        // part way through an edit — which is why an edit also marks the chunk dirty, so whatever
        // that worker produces is replaced on the next pass.
        Assert.Equal(BlockId.Air, captured.BlockAt(5, 5, 5));
    }

    private static (VoxelWorld World, BlockId Ground) Flat()
    {
        var registry = new BlockRegistry();
        var ground = registry.Register(new BlockType { Name = "ground", Color = new byte3(128, 128, 128) });
        return (new VoxelWorld(registry, new FlatGenerator(ground, surfaceHeight: 200)), ground);
    }
}
