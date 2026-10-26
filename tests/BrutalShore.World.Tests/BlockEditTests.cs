namespace BrutalShore.World.Tests;

/// <summary>
/// Pins down the one thing that makes a generated world editable: a change survives the chunk
/// being forgotten, and survives being written to a file and read back.
/// </summary>
public sealed class BlockEditTests
{
    [Fact]
    public void an_edit_outlives_the_chunk_being_unloaded_and_generated_again()
    {
        var (world, ground) = Flat();
        var at = new int3(5, 4, 5);
        world.Load(new int3(0, 0, 0));

        Assert.True(world.SetBlock(at, BlockId.Air));
        Assert.True(world.Unload(new int3(0, 0, 0)));
        world.Load(new int3(0, 0, 0));

        // Walking far enough to unload a chunk and coming back is the ordinary case, not an edge
        // one: without the edit log the generator simply makes the block again.
        Assert.Equal(BlockId.Air, world.BlockAt(at.X, at.Y, at.Z));
        Assert.Equal(ground, world.BlockAt(at.X, at.Y - 1, at.Z));
    }

    [Fact]
    public void editing_the_same_block_twice_records_what_it_is_and_not_how_it_got_there()
    {
        var (world, ground) = Flat();
        var at = new int3(5, 4, 5);
        world.Load(new int3(0, 0, 0));

        world.SetBlock(at, BlockId.Air);
        world.SetBlock(at, ground);

        Assert.Equal(1, world.Edits.Count);
    }

    [Fact]
    public void a_block_set_back_to_what_was_generated_is_still_an_edit()
    {
        var (world, ground) = Flat();
        var at = new int3(5, 4, 5);
        world.Load(new int3(0, 0, 0));

        world.SetBlock(at, BlockId.Air);
        world.SetBlock(at, ground);
        world.Unload(new int3(0, 0, 0));
        world.Load(new int3(0, 0, 0));

        // Keeping it costs one entry and means nothing has to decide what the generator would
        // have produced here, which is a question only the generator can answer.
        Assert.Equal(ground, world.BlockAt(at.X, at.Y, at.Z));
    }

    [Fact]
    public void setting_a_block_to_what_it_already_is_records_nothing()
    {
        var (world, ground) = Flat();
        world.Load(new int3(0, 0, 0));

        // Nothing changed, so nothing is owed to a save and nothing needs remeshing. A log that
        // grew every time a player pointed at the ground would be a log of where they walked.
        Assert.False(world.SetBlock(new int3(5, 4, 5), ground));
        Assert.Equal(0, world.Edits.Count);
    }

    [Fact]
    public void edits_survive_a_round_trip_through_a_file()
    {
        var (world, ground) = Flat();
        world.Load(new int3(0, 0, 0));
        world.Load(new int3(1, 0, 0));

        world.SetBlock(new int3(5, 4, 5), BlockId.Air);
        world.SetBlock(new int3(6, 4, 5), BlockId.Air);
        world.SetBlock(new int3(40, 25, 3), ground);

        using var stream = new MemoryStream();
        BlockEditFile.Write(stream, world.Edits, world.Registry);
        stream.Position = 0;

        var reloaded = new BlockEdits();
        var report = BlockEditFile.Read(stream, reloaded, world.Registry);

        Assert.Equal(3, report.Applied);
        Assert.Equal(0, report.Dropped);
        Assert.Equal(3, reloaded.Count);

        var chunk = new Chunk();
        chunk.Fill(ground);
        reloaded.ApplyTo(new int3(0, 0, 0), chunk);
        Assert.Equal(BlockId.Air, chunk[5, 4, 5]);
        Assert.Equal(BlockId.Air, chunk[6, 4, 5]);
        Assert.Equal(ground, chunk[7, 4, 5]);
    }

    [Fact]
    public void a_block_the_game_no_longer_has_is_dropped_rather_than_mistaken_for_another()
    {
        var written = new BlockRegistry();
        var gone = written.Register(new BlockType { Name = "mystery", Color = new byte3(1, 2, 3) });
        var edits = new BlockEdits();
        edits.Record(new int3(1, 2, 3), gone);

        using var stream = new MemoryStream();
        BlockEditFile.Write(stream, edits, written);
        stream.Position = 0;

        // A later build of the game that dropped the block type. Ids are registration order, so
        // reading this by id would silently turn every "mystery" into whatever is now first.
        var reading = new BlockRegistry();
        reading.Register(new BlockType { Name = "stone", Color = new byte3(4, 5, 6) });

        var report = BlockEditFile.Read(stream, new BlockEdits(), reading);

        Assert.Equal(0, report.Applied);
        Assert.Equal(1, report.Dropped);
    }

    [Fact]
    public void a_block_that_moved_in_the_registry_still_reads_back_as_itself()
    {
        var written = new BlockRegistry();
        written.Register(new BlockType { Name = "dirt", Color = new byte3(1, 1, 1) });
        var stone = written.Register(new BlockType { Name = "stone", Color = new byte3(2, 2, 2) });

        var edits = new BlockEdits();
        edits.Record(new int3(1, 2, 3), stone);

        using var stream = new MemoryStream();
        BlockEditFile.Write(stream, edits, written);
        stream.Position = 0;

        // The same two blocks, registered the other way round. By id this would come back dirt.
        var reading = new BlockRegistry();
        reading.Register(new BlockType { Name = "stone", Color = new byte3(2, 2, 2) });
        reading.Register(new BlockType { Name = "dirt", Color = new byte3(1, 1, 1) });

        var reloaded = new BlockEdits();
        BlockEditFile.Read(stream, reloaded, reading);

        var chunk = new Chunk();
        reloaded.ApplyTo(int3.Zero, chunk);
        Assert.Equal("stone", reading[chunk[1, 2, 3]].Name);
    }

    [Fact]
    public void something_that_is_not_an_edit_file_is_refused()
    {
        using var stream = new MemoryStream("not a save at all, just some bytes"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => BlockEditFile.Read(stream, new BlockEdits(), new BlockRegistry()));
    }

    [Fact]
    public void an_empty_log_writes_a_file_that_reads_back_empty()
    {
        using var stream = new MemoryStream();
        BlockEditFile.Write(stream, new BlockEdits(), new BlockRegistry());
        stream.Position = 0;

        var report = BlockEditFile.Read(stream, new BlockEdits(), new BlockRegistry());

        Assert.Equal(0, report.Applied);
        Assert.Equal(0, report.Dropped);
    }

    private static (VoxelWorld World, BlockId Ground) Flat()
    {
        var registry = new BlockRegistry();
        var ground = registry.Register(new BlockType { Name = "ground", Color = new byte3(128, 128, 128) });
        return (new VoxelWorld(registry, new FlatGenerator(ground, surfaceHeight: 20)), ground);
    }
}
