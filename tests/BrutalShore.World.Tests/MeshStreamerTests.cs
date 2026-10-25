namespace BrutalShore.World.Tests;

/// <summary>Pins down the handover of finished meshes, and that they are reused rather than piled up.</summary>
public sealed class MeshStreamerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    [Fact]
    public void every_chunk_asked_for_comes_back_with_a_surface()
    {
        var (world, _) = Flat();
        world.UpdateStreaming(int3.Zero, radiusChunks: 1, maxLoadsPerCall: 10_000);

        using var streamer = new MeshStreamer(world.Registry, workerCount: 2, queueCapacity: 32);

        // The chunk the ground's surface runs through, so it has faces rather than nothing.
        var coordinate = new int3(0, 0, 0);
        Assert.True(streamer.Request(world.CaptureNeighbourhood(coordinate)!));

        var triangles = 0;
        WaitUntil(() => streamer.Collect(1, (_, mesh) => triangles = mesh.TriangleCount) == 1);

        Assert.True(triangles > 0, "a chunk with a surface in it meshed to nothing");
        Assert.Equal(0, streamer.OutstandingCount);
    }

    [Fact]
    public void a_chunk_already_being_meshed_is_not_asked_for_twice()
    {
        var (world, _) = Flat();
        world.UpdateStreaming(int3.Zero, radiusChunks: 1, maxLoadsPerCall: 10_000);

        using var streamer = new MeshStreamer(world.Registry, workerCount: 1, queueCapacity: 8);
        var captured = world.CaptureNeighbourhood(new int3(0, 0, 0))!;

        Assert.True(streamer.Request(captured));
        Assert.False(streamer.Request(world.CaptureNeighbourhood(new int3(0, 0, 0))!));
    }

    [Fact]
    public void finished_meshes_are_reused_rather_than_allocated_one_per_chunk()
    {
        var (world, _) = Flat();
        world.UpdateStreaming(int3.Zero, radiusChunks: 2, maxLoadsPerCall: 10_000);

        using var streamer = new MeshStreamer(world.Registry, workerCount: 2, queueCapacity: 8);

        var seen = new HashSet<VoxelMesh>(ReferenceEqualityComparer.Instance as IEqualityComparer<VoxelMesh>);
        var done = 0;

        foreach (var coordinate in world.LoadedCoordinates.ToArray())
        {
            while (!streamer.Request(world.CaptureNeighbourhood(coordinate)!))
            {
                WaitUntil(() => streamer.ReadyCount > 0);
                done += streamer.Collect(8, (_, mesh) => seen.Add(mesh));
            }
        }

        WaitUntil(() =>
        {
            done += streamer.Collect(64, (_, mesh) => seen.Add(mesh));
            return streamer.OutstandingCount == 0;
        });

        // A mesh is tens of kilobytes and a world being flown across produces hundreds a second,
        // so the pool is what keeps the large object heap out of it. The bound is generous: what
        // matters is that it does not grow with the number of chunks meshed.
        Assert.True(done > 100, $"only {done} chunks were meshed, which is too few to prove anything");
        Assert.True(seen.Count <= 16, $"{seen.Count} meshes were allocated for {done} chunks");
    }

    [Fact]
    public void the_queue_refuses_to_grow_past_its_capacity()
    {
        var (world, _) = Flat();
        world.UpdateStreaming(int3.Zero, radiusChunks: 2, maxLoadsPerCall: 10_000);

        using var streamer = new MeshStreamer(world.Registry, workerCount: 1, queueCapacity: 3);

        var accepted = 0;
        foreach (var coordinate in world.LoadedCoordinates.ToArray())
        {
            if (streamer.Request(world.CaptureNeighbourhood(coordinate)!))
                accepted++;
        }

        Assert.True(accepted <= 3 + 1, $"{accepted} requests were taken against a capacity of 3");
    }

    [Fact]
    public void a_disposed_streamer_accepts_nothing_more()
    {
        var (world, _) = Flat();
        world.Load(int3.Zero);

        var streamer = new MeshStreamer(world.Registry, workerCount: 1, queueCapacity: 4);
        streamer.Dispose();

        Assert.False(streamer.Request(world.CaptureNeighbourhood(int3.Zero)!));
        streamer.Dispose();
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            Thread.Sleep(1);
        }

        Assert.Fail("the workers never finished");
    }

    private static (VoxelWorld World, BlockId Ground) Flat()
    {
        var registry = new BlockRegistry();
        var ground = registry.Register(new BlockType { Name = "ground", Color = new byte3(128, 128, 128) });

        // A surface inside the first chunk, so the chunk at the origin has faces to build.
        return (new VoxelWorld(registry, new FlatGenerator(ground, surfaceHeight: 20)), ground);
    }
}
