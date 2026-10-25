namespace Shorebound.Tests;

/// <summary>
/// The row of blocks the player carries: what goes in, what comes out, and what happens at the
/// edges of both.
/// </summary>
/// <remarks>
/// The stacking rules are the ones worth pinning down. A row that filled an empty slot before
/// topping up a part-full one would run out of slots in a minute of digging, and a row that let
/// a placement take a block it did not have would quietly put blocks into the world from nowhere.
/// </remarks>
public sealed class HotbarTests
{
    private readonly GameBlocks _blocks = new(new BlockRegistry());

    [Fact]
    public void a_fresh_row_is_empty_and_has_nothing_to_place()
    {
        var hotbar = new Hotbar(_blocks);

        Assert.False(hotbar.HasSelection);
        Assert.False(hotbar.TakeOne());

        for (var slot = 0; slot < hotbar.Count; slot++)
            Assert.Equal(0, hotbar.At(slot).Count);
    }

    [Fact]
    public void picking_a_slot_puts_what_is_in_it_in_hand()
    {
        var hotbar = new Hotbar(_blocks);
        hotbar.Add(_blocks.Sand, 1);

        hotbar.Select(0);

        Assert.Equal(0, hotbar.SelectedIndex);
        Assert.Equal(_blocks.Sand, hotbar.Selected);
        Assert.Equal("sand", hotbar.SelectedName);
    }

    [Fact]
    public void a_slot_that_is_not_there_is_ignored_rather_than_an_error()
    {
        var hotbar = new Hotbar(_blocks);
        hotbar.Select(2);

        // The keys 1 to 9 are read in a loop, and a row with fewer slots has to survive the rest.
        hotbar.Select(99);
        hotbar.Select(-1);

        Assert.Equal(2, hotbar.SelectedIndex);
    }

    [Fact]
    public void moving_past_either_end_of_the_row_wraps_round()
    {
        var hotbar = new Hotbar(_blocks);

        hotbar.Select(0);
        hotbar.Cycle(-1);
        Assert.Equal(hotbar.Count - 1, hotbar.SelectedIndex);

        hotbar.Cycle(1);
        Assert.Equal(0, hotbar.SelectedIndex);
    }

    [Fact]
    public void more_of_the_same_block_goes_onto_the_stack_that_is_already_there()
    {
        var hotbar = new Hotbar(_blocks);

        hotbar.Add(_blocks.Stone, 1);
        hotbar.Select(4);
        hotbar.Add(_blocks.Stone, 3);

        Assert.Equal((_blocks.Stone, 4), hotbar.At(0));
        Assert.Equal(0, hotbar.At(1).Count);
    }

    [Fact]
    public void a_different_block_takes_the_next_empty_slot()
    {
        var hotbar = new Hotbar(_blocks);

        hotbar.Add(_blocks.Stone, 2);
        hotbar.Add(_blocks.Sand, 5);

        Assert.Equal((_blocks.Stone, 2), hotbar.At(0));
        Assert.Equal((_blocks.Sand, 5), hotbar.At(1));
    }

    [Fact]
    public void a_stack_that_is_full_spills_into_the_next_slot()
    {
        var hotbar = new Hotbar(_blocks);

        var taken = hotbar.Add(_blocks.Dirt, Hotbar.StackSize + 3);

        Assert.Equal(Hotbar.StackSize + 3, taken);
        Assert.Equal(Hotbar.StackSize, hotbar.At(0).Count);
        Assert.Equal(3, hotbar.At(1).Count);
    }

    [Fact]
    public void a_full_row_refuses_what_will_not_fit_rather_than_losing_it()
    {
        // The count that comes back is what the caller keeps: a drop the row would not take has
        // to stay lying on the ground, not vanish into a row that was already full.
        var hotbar = new Hotbar(_blocks);
        for (var slot = 0; slot < Hotbar.SlotCount; slot++)
            hotbar.Add(_blocks.Stone, Hotbar.StackSize);

        Assert.Equal(0, hotbar.Add(_blocks.Sand, 1));
        Assert.Equal(0, hotbar.Add(_blocks.Stone, 1));
    }

    [Fact]
    public void placing_a_block_spends_one_and_empties_the_slot_at_the_last_one()
    {
        var hotbar = new Hotbar(_blocks);
        hotbar.Add(_blocks.Snow, 2);

        Assert.True(hotbar.TakeOne());
        Assert.Equal(1, hotbar.At(0).Count);

        Assert.True(hotbar.TakeOne());
        Assert.Equal(0, hotbar.At(0).Count);
        Assert.False(hotbar.HasSelection);
        Assert.False(hotbar.TakeOne());
    }

    [Fact]
    public void air_is_never_carried_however_it_is_offered()
    {
        var hotbar = new Hotbar(_blocks);

        Assert.Equal(0, hotbar.Add(BlockId.Air, 4));
        Assert.Equal(0, hotbar.Add(_blocks.Stone, 0));
        Assert.False(hotbar.HasSelection);
    }

    [Fact]
    public void a_row_comes_back_from_a_save_the_way_it_went_in()
    {
        var hotbar = new Hotbar(_blocks);
        hotbar.Add(_blocks.Stone, 7);
        hotbar.Add(_blocks.Leaves, 2);

        var restored = new Hotbar(_blocks);
        restored.Restore(hotbar.Carried(_blocks.Registry), _blocks.Registry);

        Assert.Equal((_blocks.Stone, 7), restored.At(0));
        Assert.Equal((_blocks.Leaves, 2), restored.At(1));
        Assert.Equal(0, restored.At(2).Count);
    }

    [Fact]
    public void a_saved_block_this_build_no_longer_has_is_dropped_rather_than_refused()
    {
        // The same rule the edits follow. A row that would not open because of a block someone
        // took out between two versions is worse than a row that opens a slot short.
        var hotbar = new Hotbar(_blocks);

        hotbar.Restore([("moonrock", 4), ("stone", 3)], _blocks.Registry);

        Assert.Equal(0, hotbar.At(0).Count);
        Assert.Equal((_blocks.Stone, 3), hotbar.At(1));
    }
}
