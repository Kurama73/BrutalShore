using BrutalShore.Voxel;

namespace BrutalShore.Voxel.Tests;

/// <summary>
/// That a chunk always knows how high each of its columns reaches, however the blocks got there.
/// </summary>
/// <remarks>
/// Four ways in — a single write, a fill, a bulk write, and a removal — and the answer has to
/// survive all of them, because lighting asks this question and a stale answer is a cave lit from
/// nowhere or a hillside in shadow at noon.
/// </remarks>
public sealed class ChunkColumnTests
{
    private readonly BlockRegistry _registry = new();
    private readonly BlockId _rock;

    /// <summary>Registers the one block these claims need.</summary>
    public ChunkColumnTests() =>
        _rock = _registry.Register(new BlockType { Name = "rock", Color = new byte3(120, 120, 120) });

    [Fact]
    public void a_column_of_nothing_but_air_reaches_nowhere()
    {
        var chunk = new Chunk();

        Assert.Equal(-1, chunk.HighestBlock(0, 0));
        Assert.Equal(-1, chunk.HighestBlock(31, 31));
    }

    [Fact]
    public void a_written_block_raises_its_column_and_leaves_the_others_alone()
    {
        var chunk = new Chunk();
        chunk[4, 7, 9] = _rock;

        Assert.Equal(7, chunk.HighestBlock(4, 9));
        Assert.Equal(-1, chunk.HighestBlock(9, 4));
    }

    [Fact]
    public void a_block_written_below_the_top_of_its_column_does_not_lower_it()
    {
        var chunk = new Chunk();
        chunk[4, 7, 9] = _rock;
        chunk[4, 2, 9] = _rock;

        Assert.Equal(7, chunk.HighestBlock(4, 9));
    }

    [Fact]
    public void removing_the_top_of_a_column_falls_back_to_the_next_block_down()
    {
        var chunk = new Chunk();
        chunk[4, 7, 9] = _rock;
        chunk[4, 2, 9] = _rock;

        chunk[4, 7, 9] = BlockId.Air;

        Assert.Equal(2, chunk.HighestBlock(4, 9));
    }

    [Fact]
    public void removing_the_only_block_in_a_column_empties_it()
    {
        var chunk = new Chunk();
        chunk[4, 7, 9] = _rock;

        chunk[4, 7, 9] = BlockId.Air;

        Assert.Equal(-1, chunk.HighestBlock(4, 9));
    }

    [Fact]
    public void a_filled_chunk_reaches_its_own_ceiling_everywhere()
    {
        var chunk = new Chunk();
        chunk.Fill(_rock);

        Assert.Equal(Chunk.Size - 1, chunk.HighestBlock(0, 0));
        Assert.Equal(Chunk.Size - 1, chunk.HighestBlock(31, 31));

        chunk.Fill(BlockId.Air);

        Assert.Equal(-1, chunk.HighestBlock(0, 0));
    }

    [Fact]
    public void a_bulk_write_is_counted_the_same_as_the_writes_it_replaces()
    {
        var bulk = new Chunk();
        var blocks = bulk.BeginBulkWrite();
        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            // A ramp, so every column has a different answer and a single wrong stride shows up.
            var height = (x + z) % Chunk.Size;
            for (var y = 0; y <= height; y++)
                blocks[Chunk.IndexOf(x, y, z)] = _rock;
        }

        bulk.EndBulkWrite();

        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
            Assert.Equal((x + z) % Chunk.Size, bulk.HighestBlock(x, z));
    }
}
