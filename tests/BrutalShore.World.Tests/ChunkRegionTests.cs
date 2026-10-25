namespace BrutalShore.World.Tests;

/// <summary>
/// That a region reads the same blocks the world does, and air everywhere it was not given one.
/// </summary>
/// <remarks>
/// A region is a snapshot handed to a worker, so the claims that matter are about what it answers
/// rather than about what it holds: the blocks inside it, air outside it, and air where a chunk
/// was simply not loaded. The third is the one with a consequence — a ray marching through an
/// unloaded part of the world should find nothing in the way, not a wall.
/// </remarks>
public sealed class ChunkRegionTests
{
    private readonly BlockRegistry _registry = new();
    private readonly BlockId _stone;

    /// <summary>Registers the one block these claims need.</summary>
    public ChunkRegionTests() =>
        _stone = _registry.Register(new BlockType { Name = "stone", Color = new byte3(128, 128, 128) });

    [Fact]
    public void a_region_reads_back_the_blocks_the_chunks_hold()
    {
        var world = Chunks((0, 0, 0), (1, 0, 0));
        world[new int3(0, 0, 0)]![5, 6, 7] = _stone;
        world[new int3(1, 0, 0)]![0, 1, 2] = _stone;

        var region = new ChunkRegion(int3.Zero, new int3(64, 32, 32), coordinate => world.GetValueOrDefault(coordinate));

        Assert.Equal(_stone, region.BlockAt(5, 6, 7));
        Assert.Equal(_stone, region.BlockAt(Chunk.Size, 1, 2));
        Assert.Equal(BlockId.Air, region.BlockAt(6, 6, 7));
    }

    [Fact]
    public void a_chunk_that_was_not_loaded_reads_as_air_rather_than_as_a_wall()
    {
        var region = new ChunkRegion(int3.Zero, new int3(64, 64, 64), _ => null);

        Assert.Equal(BlockId.Air, region.BlockAt(10, 10, 10));
        Assert.Equal(BlockId.Air, region.BlockAt(40, 40, 40));
    }

    [Fact]
    public void anything_outside_the_box_reads_as_air()
    {
        var world = Chunks((0, 0, 0));
        world[new int3(0, 0, 0)]!.Fill(_stone);

        var region = new ChunkRegion(int3.Zero, new int3(32, 32, 32), coordinate => world.GetValueOrDefault(coordinate));

        Assert.Equal(_stone, region.BlockAt(0, 0, 0));
        Assert.Equal(BlockId.Air, region.BlockAt(-1, 0, 0));
        Assert.Equal(BlockId.Air, region.BlockAt(0, 0, Chunk.Size));
    }

    [Fact]
    public void a_box_that_straddles_a_chunk_boundary_captures_both_sides_of_it()
    {
        // Negative coordinates are the case that catches an arithmetic shortcut: block -1 is in
        // chunk -1 at offset 31, and a division that rounds towards zero puts it in chunk 0.
        var world = Chunks((-1, -1, -1), (0, -1, -1), (-1, 0, -1), (0, 0, -1), (-1, -1, 0), (0, -1, 0), (-1, 0, 0), (0, 0, 0));
        world[new int3(-1, -1, -1)]![31, 31, 31] = _stone;
        world[new int3(0, 0, 0)]![0, 0, 0] = _stone;

        var region = new ChunkRegion(
            new int3(-4, -4, -4),
            new int3(8, 8, 8),
            coordinate => world.GetValueOrDefault(coordinate)
        );

        Assert.Equal(_stone, region.BlockAt(-1, -1, -1));
        Assert.Equal(_stone, region.BlockAt(0, 0, 0));
        Assert.Equal(BlockId.Air, region.BlockAt(-2, -1, -1));
    }

    [Fact]
    public void a_region_with_no_size_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChunkRegion(int3.Zero, new int3(0, 32, 32), _ => null)
        );
    }

    private static Dictionary<int3, Chunk> Chunks(params (int X, int Y, int Z)[] coordinates)
    {
        var world = new Dictionary<int3, Chunk>();
        foreach (var (x, y, z) in coordinates)
            world[new int3(x, y, z)] = new Chunk();

        return world;
    }
}
