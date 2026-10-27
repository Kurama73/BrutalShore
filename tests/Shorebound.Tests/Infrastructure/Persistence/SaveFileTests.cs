using Shorebound.Domain.Movement;
using Shorebound.Domain.Progression;
using Shorebound.Domain.World;
using Shorebound.Infrastructure.Persistence;

namespace Shorebound.Tests.Infrastructure.Persistence;

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
            RowIndex = 2,
            Machines = [],
            MilestonesDone = [],
            Carried = [("stone", 9), (string.Empty, 0)],
        };

        var path = Path.Combine(Path.GetTempPath(), $"shorebound-{Guid.NewGuid():N}.world");
        try
        {
            save.Write(path, written, blocks.Registry);

            var reloaded = new BlockEdits();
            var (read, report) = SaveFile.Read(path, reloaded, blocks.Registry);

            // Compared member by member rather than as a whole, because what is carried is a
            // list and a record compares a list by which list it is, not by what is in it.
            Assert.Equal(save with { Carried = [], Machines = [], MilestonesDone = [] }, read with { Carried = [], Machines = [], MilestonesDone = [] });
            Assert.Equal(save.Carried, read.Carried);
            Assert.Equal(save.MilestonesDone, read.MilestonesDone);

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
            RowIndex = 0,
            Machines = [],
            MilestonesDone = [],
            Carried = [],
        };

        var path = Path.Combine(Path.GetTempPath(), $"shorebound-{Guid.NewGuid():N}.world");
        try
        {
            save.Write(path, new BlockEdits(), blocks.Registry);

            // A world nobody has changed is still a world: it has a seed, a time and a player in
            // it, and that is the whole file.
            var (read, report) = SaveFile.Read(path, new BlockEdits(), blocks.Registry);

            Assert.Equal(save with { Carried = [], Machines = [], MilestonesDone = [] }, read with { Carried = [], Machines = [], MilestonesDone = [] });
            Assert.Equal(save.Carried, read.Carried);
            Assert.Equal(save.MilestonesDone, read.MilestonesDone);
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
    public void a_half_finished_tree_comes_back_half_finished()
    {
        var blocks = new GameBlocks(new BlockRegistry());
        var path = Path.Combine(Path.GetTempPath(), $"shorebound-{Guid.NewGuid():N}.world");
        var progression = new Progression(blocks);
        progression.Restore(["Make landfall", "First smoke", "The line"]);

        var save = new SaveFile
        {
            Seed = 11,
            FeetMeters = new double3(0, 80, 0),
            Yaw = 0f,
            Pitch = 0f,
            IsFlying = false,
            DayFraction = 0.25,
            RowIndex = 0,
            Machines = [],
            Carried = [],
            MilestonesDone = progression.DoneNames(),
        };

        try
        {
            save.Write(path, new BlockEdits(), blocks.Registry);
            var (read, _) = SaveFile.Read(path, new BlockEdits(), blocks.Registry);

            // Which branch the player took, not how many steps they took: two players three
            // steps in have not necessarily taken the same three.
            var opened = new Progression(blocks);
            opened.Restore(read.MilestonesDone);

            Assert.True(opened.IsDone("The line"));
            Assert.False(opened.IsDone("Into the dark"));
            Assert.True(opened.IsUnlocked("conveyor"));
            Assert.False(opened.IsUnlocked("lantern"));
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
            RowIndex = 1,
            Machines = [],
            MilestonesDone = [],
            Carried = [],
        };

        try
        {
            first.Write(path, new BlockEdits(), blocks.Registry);
            first.Write(path, new BlockEdits(), blocks.Registry);

            // Saving twice has to overwrite rather than fail on the file it left behind, which is
            // the thing writing through a temporary file most easily gets wrong.
            var (read, _) = SaveFile.Read(path, new BlockEdits(), blocks.Registry);
            Assert.Equal(first with { Carried = [], Machines = [], MilestonesDone = [] }, read with { Carried = [], Machines = [], MilestonesDone = [] });
            Assert.Equal(first.Carried, read.Carried);
            Assert.Equal(first.MilestonesDone, read.MilestonesDone);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void a_machine_comes_back_where_it_was_with_what_was_in_it()
    {
        // The claim that makes a machine worth building: it has to survive closing the game. A
        // smelter that came back empty would cost a player everything they had queued in it.
        var registry = new BlockRegistry();
        _ = new GameBlocks(registry);
        var path = Path.Combine(Path.GetTempPath(), $"shorebound-{Guid.NewGuid():N}.world");

        var save = new SaveFile
        {
            Seed = 5,
            FeetMeters = new double3(0, 80, 0),
            Yaw = 0f,
            Pitch = 0f,
            IsFlying = false,
            DayFraction = 0.25,
            RowIndex = 0,
            MilestonesDone = ["Make landfall", "First smoke"],
            Machines =
            [
                new SavedMachine
                {
                    Kind = SavedMachine.Smelter,
                    Carrying = [],
                    Position = new int3(3, 70, -4),
                    BurnLeftSeconds = 12.5f,
                    BurnTotalSeconds = 32f,
                    WorkedSeconds = 2.25f,
                    Input = ("iron ore", 7),
                    Fuel = ("coal ore", 3),
                    Output = ("iron ingot", 11),
                },
            ],
            Carried = [],
        };

        save.Write(path, new BlockEdits(), registry);
        var (read, _) = SaveFile.Read(path, new BlockEdits(), registry);

        var machine = Assert.Single(read.Machines);
        Assert.Equal(SavedMachine.Smelter, machine.Kind);
        Assert.Equal(new int3(3, 70, -4), machine.Position);
        Assert.Equal(12.5f, machine.BurnLeftSeconds);
        Assert.Equal(32f, machine.BurnTotalSeconds);
        Assert.Equal(2.25f, machine.WorkedSeconds);
        Assert.Equal(("iron ore", 7), machine.Input);
        Assert.Equal(("coal ore", 3), machine.Fuel);
        Assert.Equal(("iron ingot", 11), machine.Output);
    }

    [Fact]
    public void a_belt_comes_back_with_what_was_on_it_where_it_was()
    {
        // A line of belts is most of what a built-up world is, and an item halfway along one is
        // state that cannot be derived from anything. Losing it on every reload would make a
        // factory stutter every time the game was opened.
        var registry = new BlockRegistry();
        _ = new GameBlocks(registry);
        var path = Path.Combine(Path.GetTempPath(), $"shorebound-{Guid.NewGuid():N}.world");

        var save = new SaveFile
        {
            Seed = 6,
            FeetMeters = new double3(0, 80, 0),
            Yaw = 0f,
            Pitch = 0f,
            IsFlying = false,
            DayFraction = 0.5,
            RowIndex = 0,
            MilestonesDone = ["Make landfall", "First smoke", "The line", "Into the dark"],
            Machines =
            [
                new SavedMachine
                {
                    Kind = SavedMachine.Conveyor,
                    Position = new int3(-7, 40, 2),
                    Facing = Facing.West,
                    BurnLeftSeconds = 0f,
                    BurnTotalSeconds = 0f,
                    WorkedSeconds = 0f,
                    Input = (string.Empty, 0),
                    Fuel = (string.Empty, 0),
                    Output = (string.Empty, 0),
                    Carrying = [("iron ore", 0.75), ("coal ore", 0.25)],
                },
            ],
            Carried = [],
        };

        save.Write(path, new BlockEdits(), registry);
        var (read, _) = SaveFile.Read(path, new BlockEdits(), registry);

        var belt = Assert.Single(read.Machines);
        Assert.Equal(SavedMachine.Conveyor, belt.Kind);
        Assert.Equal(Facing.West, belt.Facing);
        Assert.Equal(2, belt.Carrying.Count);
        Assert.Equal(("iron ore", 0.75), belt.Carrying[0]);
        Assert.Equal(("coal ore", 0.25), belt.Carrying[1]);
    }
}
