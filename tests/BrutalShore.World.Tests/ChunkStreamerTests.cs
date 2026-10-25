namespace BrutalShore.World.Tests;

/// <summary>
/// Pins down the handover between the worker threads and the thread that owns the world.
/// </summary>
/// <remarks>
/// Threaded code cannot be tested by asserting what has happened at a given moment, so these
/// assert what is true once the work has drained: every chunk asked for arrives exactly once, a
/// chunk asked for twice is made once, and the queue refuses to grow past its cap.
/// </remarks>
public sealed class ChunkStreamerTests
{
    /// <summary>How long a test waits for workers before calling it a hang rather than slow.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    [Fact]
    public void every_chunk_asked_for_arrives_exactly_once()
    {
        var generator = new CountingGenerator();
        using var streamer = new ChunkStreamer(generator, workerCount: 4, queueCapacity: 256);

        var wanted = new List<int3>();
        for (var x = 0; x < 8; x++)
        for (var z = 0; z < 8; z++)
            wanted.Add(new int3(x, 0, z));

        foreach (var coordinate in wanted)
            Assert.True(streamer.Request(coordinate));

        var arrived = Drain(streamer, wanted.Count);

        Assert.Equal(wanted.Count, arrived.Count);
        Assert.Equal(Sorted(wanted), Sorted(arrived.Keys));
        Assert.Equal(0, streamer.OutstandingCount);
    }

    [Fact]
    public void a_chunk_asked_for_twice_is_only_made_once()
    {
        var generator = new CountingGenerator();
        using var streamer = new ChunkStreamer(generator, workerCount: 2, queueCapacity: 16);

        Assert.True(streamer.Request(new int3(1, 2, 3)));
        Assert.False(streamer.Request(new int3(1, 2, 3)));

        Drain(streamer, 1);

        Assert.Equal(1, generator.Calls);
    }

    [Fact]
    public void the_queue_refuses_to_grow_past_its_capacity()
    {
        // A player sprinting in a straight line asks faster than the workers can answer, and the
        // refusal is what stops the queue filling with ground already behind them.
        var generator = new BlockingGenerator();
        using var streamer = new ChunkStreamer(generator, workerCount: 1, queueCapacity: 4);

        var accepted = 0;
        for (var x = 0; x < 50; x++)
        {
            if (streamer.Request(new int3(x, 0, 0)))
                accepted++;
        }

        Assert.Equal(4, accepted);
        Assert.Equal(4, streamer.OutstandingCount);

        generator.Release();
    }

    [Fact]
    public void collecting_takes_no_more_than_it_was_asked_for()
    {
        using var streamer = new ChunkStreamer(new CountingGenerator(), workerCount: 2, queueCapacity: 64);

        for (var x = 0; x < 10; x++)
            streamer.Request(new int3(x, 0, 0));

        WaitUntil(() => streamer.ReadyCount == 10);

        // The budget is what keeps a frame from paying for a whole queue at once, so a collection
        // that quietly took everything available would be the bound not holding.
        Assert.Equal(3, streamer.Collect(3, (_, _) => { }));
        Assert.Equal(7, streamer.OutstandingCount);
        Assert.Equal(7, streamer.ReadyCount);
    }

    [Fact]
    public void the_chunk_that_arrives_is_the_one_the_generator_made()
    {
        var generator = new CountingGenerator();
        using var streamer = new ChunkStreamer(generator, workerCount: 2, queueCapacity: 16);

        streamer.Request(new int3(4, 1, -2));
        var arrived = Drain(streamer, 1);

        // The generator writes the coordinate it was given into the chunk's first block, so a
        // worker that mixed two jobs up would show here rather than as a wrong-looking world.
        Assert.Equal(CountingGenerator.Mark(new int3(4, 1, -2)), arrived[new int3(4, 1, -2)][0, 0, 0]);
    }

    [Fact]
    public void a_disposed_streamer_accepts_nothing_more()
    {
        var streamer = new ChunkStreamer(new CountingGenerator(), workerCount: 1, queueCapacity: 4);
        streamer.Dispose();

        Assert.False(streamer.Request(new int3(0, 0, 0)));

        // Disposing twice has to be safe: the world disposes its streamer, and a test or a crash
        // path may well have done so already.
        streamer.Dispose();
    }

    /// <summary>Puts coordinates in a fixed order so two sets can be compared.</summary>
    /// <remarks>
    /// Written out because a vector has no total order in this framework — its comparisons are
    /// component by component — so sorting one needs a key chosen on purpose.
    /// </remarks>
    private static (int X, int Y, int Z)[] Sorted(IEnumerable<int3> coordinates) =>
        coordinates.Select(c => (c.X, c.Y, c.Z)).OrderBy(c => c).ToArray();

    private static Dictionary<int3, Chunk> Drain(ChunkStreamer streamer, int expected)
    {
        var arrived = new Dictionary<int3, Chunk>();
        WaitUntil(() =>
        {
            streamer.Collect(expected, (coordinate, chunk) => arrived[coordinate] = chunk);
            return arrived.Count >= expected;
        });

        return arrived;
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

    /// <summary>Writes its coordinate into the chunk, and counts how often it was called.</summary>
    private sealed class CountingGenerator : IChunkGenerator
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public static BlockId Mark(int3 coordinate) =>
            new((ushort)(1 + (uint)((coordinate.X * 73856093) ^ (coordinate.Y * 19349663) ^ (coordinate.Z * 83492791)) % 60000));

        public void Generate(int3 chunkCoordinate, Chunk into)
        {
            Interlocked.Increment(ref _calls);
            into[0, 0, 0] = Mark(chunkCoordinate);
        }
    }

    /// <summary>Never finishes until released, so the queue can be seen filling up.</summary>
    private sealed class BlockingGenerator : IChunkGenerator
    {
        private readonly ManualResetEventSlim _gate = new(false);

        public void Release() => _gate.Set();

        public void Generate(int3 chunkCoordinate, Chunk into) => _gate.Wait(TimeSpan.FromSeconds(30));
    }
}
