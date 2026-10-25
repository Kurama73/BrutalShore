namespace Shorebound.Tests;

/// <summary>Pins down that a world comes back the way it was left.</summary>
/// <remarks>
/// A save holds the seed and the differences from it, never the terrain. These check that both
/// halves survive, and that the player's own state — which is the half nothing else can rebuild —
/// comes back exactly rather than approximately.
/// </remarks>
public sealed class SaveFileTests
{
    [Fact]
    public void a_world_comes_back_the_way_it_was_left()
    {
        var blocks = new GameBlocks(new BlockRegistry());
        var written = new BlockEdits();
        written.Record(new int3(12, 70, -5), BlockId.Air);
        written.Record(new int3(12, 71, -5), blocks.Stone);

        var save = new SaveFile
        {
            Seed = 4242,
            FeetMeters = new double3(12.25, 71.0, -5.75),
            Yaw = 1.25f,
            Pitch = -0.4f,
            IsFlying = false,
            DayFraction = 3.6,
            HotbarIndex = 2,
        };

        var path = Path.Combine(Path.GetTempPath(), $"shorebound-{Guid.NewGuid():N}.world");
        try
        {
            save.Write(path, written, blocks.Registry);

            var reloaded = new BlockEdits();
            var (read, report) = SaveFile.Read(path, reloaded, blocks.Registry);

            Assert.Equal(save, read);
            Assert.Equal(2, report.Applied);
            Assert.Equal(0, report.Dropped);
            Assert.Equal(2, reloaded.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void an_untouched_world_still_saves_and_loads()
    {
        var blocks = new GameBlocks(new BlockRegistry());
        var save = new SaveFile
        {
            Seed = 1,
            FeetMeters = double3.Zero,
            Yaw = 0f,
            Pitch = 0f,
            IsFlying = true,
            DayFraction = 0.0,
            HotbarIndex = 0,
        };

        var path = Path.Combine(Path.GetTempPath(), $"shorebound-{Guid.NewGuid():N}.world");
        try
        {
            save.Write(path, new BlockEdits(), blocks.Registry);

            // A world nobody has changed is still a world: it has a seed, a time and a player in
            // it, and that is the whole file.
            var (read, report) = SaveFile.Read(path, new BlockEdits(), blocks.Registry);

            Assert.Equal(save, read);
            Assert.Equal(0, report.Applied);
            Assert.True(new FileInfo(path).Length < 128, "an empty world should not need a hundred bytes");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void something_that_is_not_a_world_is_refused()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shorebound-{Guid.NewGuid():N}.world");
        try
        {
            File.WriteAllText(path, "this is a letter, not a world");

            Assert.Throws<InvalidDataException>(
                () => SaveFile.Read(path, new BlockEdits(), new BlockRegistry())
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void a_failed_write_leaves_the_previous_world_alone()
    {
        var blocks = new GameBlocks(new BlockRegistry());
        var path = Path.Combine(Path.GetTempPath(), $"shorebound-{Guid.NewGuid():N}.world");

        var first = new SaveFile
        {
            Seed = 7,
            FeetMeters = new double3(1.0, 2.0, 3.0),
            Yaw = 0f,
            Pitch = 0f,
            IsFlying = false,
            DayFraction = 0.5,
            HotbarIndex = 1,
        };

        try
        {
            first.Write(path, new BlockEdits(), blocks.Registry);
            first.Write(path, new BlockEdits(), blocks.Registry);

            // Saving twice has to overwrite rather than fail on the file it left behind, which is
            // the thing writing through a temporary file most easily gets wrong.
            var (read, _) = SaveFile.Read(path, new BlockEdits(), blocks.Registry);
            Assert.Equal(first, read);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
