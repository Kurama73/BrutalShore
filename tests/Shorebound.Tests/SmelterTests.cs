namespace Shorebound.Tests;

/// <summary>
/// A smelter as a machine left alone: what it burns, when it refuses to, and what it does with
/// work that was interrupted.
/// </summary>
/// <remarks>
/// The whole value of a machine is that it runs while the player is somewhere else, so every
/// claim here is about what they find when they come back. The ones that matter are the refusals:
/// a smelter that burns fuel with nothing to melt, or finishes a job with nowhere to put it, is
/// one that quietly costs a player resources in their absence.
/// </remarks>
public sealed class SmelterTests
{
    private readonly GameBlocks _blocks = new(new BlockRegistry());
    private readonly Smelting _smelting;

    public SmelterTests() => _smelting = new Smelting(_blocks);

    [Fact]
    public void an_empty_smelter_burns_nothing()
    {
        var smelter = new Smelter();
        smelter.Fuel.Add(_blocks.CoalOre, 4);

        Run(smelter, 30.0);

        Assert.Equal(4, smelter.Fuel.At(0).Count);
        Assert.False(smelter.IsLit);
    }

    [Fact]
    public void a_smelter_with_no_fuel_does_nothing()
    {
        var smelter = new Smelter();
        smelter.Input.Add(_blocks.CopperOre, 1);

        Run(smelter, 30.0);

        Assert.True(smelter.Output.At(0).IsNothing);
        Assert.Equal(1, smelter.Input.At(0).Count);
    }

    [Fact]
    public void fed_and_fuelled_it_makes_an_ingot()
    {
        var smelter = new Smelter();
        smelter.Input.Add(_blocks.CopperOre, 1);
        smelter.Fuel.Add(_blocks.CoalOre, 1);

        Run(smelter, 10.0);

        Assert.Equal(_blocks.CopperIngot, smelter.Output.At(0).Block);
        Assert.Equal(1, smelter.Output.At(0).Count);
        Assert.True(smelter.Input.At(0).IsNothing);
    }

    [Fact]
    public void one_lump_of_coal_smelts_several_blocks()
    {
        // The reason fuel is a clock rather than a cost per job: a smelter that spent a whole
        // lump on each block would make coal the bottleneck instead of ore, which is the wrong
        // thing for the game to be about.
        var smelter = new Smelter();
        smelter.Input.Add(_blocks.CopperOre, 5);
        smelter.Fuel.Add(_blocks.CoalOre, 1);

        Run(smelter, 40.0);

        Assert.True(smelter.Output.At(0).Count >= 4);
    }

    [Fact]
    public void it_will_not_light_when_there_is_nowhere_to_put_the_result()
    {
        var smelter = new Smelter();
        smelter.Input.Add(_blocks.CopperOre, 4);
        smelter.Fuel.Add(_blocks.CoalOre, 2);
        smelter.Output.Add(_blocks.IronIngot, 1);

        Run(smelter, 40.0);

        Assert.Equal(2, smelter.Fuel.At(0).Count);
        Assert.Equal(4, smelter.Input.At(0).Count);
    }

    [Fact]
    public void it_stops_when_the_output_slot_fills_up()
    {
        var smelter = new Smelter();
        smelter.Input.Add(_blocks.Sand, Slots.StackSize);
        smelter.Fuel.Add(_blocks.CoalOre, Slots.StackSize);

        Run(smelter, 4f * Slots.StackSize * 2f);

        Assert.Equal(Slots.StackSize, smelter.Output.At(0).Count);
    }

    [Fact]
    public void work_survives_the_fire_going_out()
    {
        // A smelter that ran out of coal halfway through a block and started again from nothing
        // when fed would punish a player for not standing next to it, which is the opposite of
        // what a machine is for.
        var smelter = new Smelter();
        smelter.Input.Add(_blocks.IronOre, 1);
        smelter.Fuel.Add(_blocks.Planks, 1);

        Run(smelter, 20.0);

        Assert.False(smelter.IsLit);
        Assert.True(smelter.Output.At(0).IsNothing);
        Assert.True(smelter.Progress(_smelting) > 0.5f);

        smelter.Fuel.Add(_blocks.CoalOre, 1);
        Run(smelter, 6.0);

        Assert.Equal(_blocks.IronIngot, smelter.Output.At(0).Block);
    }

    [Fact]
    public void taking_the_input_out_throws_the_part_finished_work_away()
    {
        var smelter = new Smelter();
        smelter.Input.Add(_blocks.IronOre, 1);
        smelter.Fuel.Add(_blocks.CoalOre, 1);

        Run(smelter, 4.0);
        Assert.True(smelter.Progress(_smelting) > 0f);

        smelter.Input.Take(0);
        Run(smelter, 0.1);

        Assert.Equal(0f, smelter.Progress(_smelting));
    }

    [Fact]
    public void something_that_does_not_burn_is_not_fuel()
    {
        var smelter = new Smelter();
        smelter.Input.Add(_blocks.CopperOre, 1);
        smelter.Fuel.Add(_blocks.Stone, 8);

        Run(smelter, 30.0);

        Assert.Equal(8, smelter.Fuel.At(0).Count);
        Assert.True(smelter.Output.At(0).IsNothing);
    }

    [Fact]
    public void something_that_does_not_melt_is_not_work()
    {
        var smelter = new Smelter();
        smelter.Input.Add(_blocks.Dirt, 3);
        smelter.Fuel.Add(_blocks.CoalOre, 1);

        Run(smelter, 30.0);

        Assert.Equal(3, smelter.Input.At(0).Count);
        Assert.Equal(1, smelter.Fuel.At(0).Count);
    }

    [Fact]
    public void breaking_it_gives_back_everything_that_was_in_it()
    {
        var smelter = new Smelter();
        smelter.Input.Add(_blocks.IronOre, 2);
        smelter.Fuel.Add(_blocks.CoalOre, 3);
        smelter.Output.Add(_blocks.Glass, 4);

        var contents = smelter.Contents().ToList();

        Assert.Equal(3, contents.Count);
        Assert.Equal(9, contents.Sum(stack => stack.Count));
    }

    [Fact]
    public void the_result_is_the_same_however_the_time_is_cut_up()
    {
        // The property that makes a frame rate not a game rule. A machine whose output depended
        // on how often it was stepped would run faster on a better computer.
        var coarse = new Smelter();
        var fine = new Smelter();

        foreach (var smelter in (ReadOnlySpan<Smelter>)[coarse, fine])
        {
            smelter.Input.Add(_blocks.Sand, 6);
            smelter.Fuel.Add(_blocks.CoalOre, 1);
        }

        for (var step = 0; step < 30; step++)
            coarse.Step(_smelting, 1.0);

        for (var step = 0; step < 300; step++)
            fine.Step(_smelting, 0.1);

        Assert.Equal(coarse.Output.At(0).Count, fine.Output.At(0).Count);
    }

    private void Run(Smelter smelter, double seconds)
    {
        // A sixtieth of a second a step, which is the step the game runs at.
        const double Step = 1.0 / 60.0;

        for (var elapsed = 0.0; elapsed < seconds; elapsed += Step)
            smelter.Step(_smelting, Step);
    }
}
