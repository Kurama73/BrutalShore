using Shorebound.Domain.Player;
using Shorebound.Domain.World;

namespace Shorebound.Tests.Domain.Player;

/// <summary>
/// The slots the player carries blocks in: what goes in, what comes out, what moves between
/// them, and what happens at the edges of all three.
/// </summary>
/// <remarks>
/// The stacking rules are the ones worth pinning down. An inventory that filled an empty slot
/// before topping up a part-full one would run out of slots in a minute of digging, and one that
/// let a placement take a block it did not have would quietly put blocks into the world from
/// nowhere. The same goes for moving a stack by hand: every path through it has to conserve
/// blocks, because the one that does not is the one that is found by a player and not by a test.
/// </remarks>
public sealed class InventoryTests
{
    private readonly GameBlocks _blocks = new(new BlockRegistry());

    [Fact]
    public void a_fresh_row_is_empty_and_has_nothing_to_place()
    {
        var inventory = new Inventory(_blocks);

        Assert.False(inventory.HasSelection);
        Assert.False(inventory.TakeOne());

        for (var slot = 0; slot < inventory.Count; slot++)
            Assert.Equal(0, inventory.At(slot).Count);
    }

    [Fact]
    public void picking_a_slot_puts_what_is_in_it_in_hand()
    {
        var inventory = new Inventory(_blocks);
        inventory.Add(_blocks.Sand, 1);

        inventory.Select(0);

        Assert.Equal(0, inventory.SelectedIndex);
        Assert.Equal(_blocks.Sand, inventory.Selected);
        Assert.Equal("sand", inventory.SelectedName);
    }

    [Fact]
    public void a_slot_that_is_not_there_is_ignored_rather_than_an_error()
    {
        var inventory = new Inventory(_blocks);
        inventory.Select(2);

        // The keys 1 to 9 are read in a loop, and a row with fewer slots has to survive the rest.
        inventory.Select(99);
        inventory.Select(-1);

        Assert.Equal(2, inventory.SelectedIndex);
    }

    [Fact]
    public void moving_past_either_end_of_the_row_wraps_round()
    {
        var inventory = new Inventory(_blocks);

        inventory.Select(0);
        inventory.Cycle(-1);
        Assert.Equal(Inventory.RowCount - 1, inventory.SelectedIndex);

        inventory.Cycle(1);
        Assert.Equal(0, inventory.SelectedIndex);
    }

    [Fact]
    public void more_of_the_same_block_goes_onto_the_stack_that_is_already_there()
    {
        var inventory = new Inventory(_blocks);

        inventory.Add(_blocks.Stone, 1);
        inventory.Select(4);
        inventory.Add(_blocks.Stone, 3);

        Assert.Equal((_blocks.Stone, 4), inventory.At(0));
        Assert.Equal(0, inventory.At(1).Count);
    }

    [Fact]
    public void a_different_block_takes_the_next_empty_slot()
    {
        var inventory = new Inventory(_blocks);

        inventory.Add(_blocks.Stone, 2);
        inventory.Add(_blocks.Sand, 5);

        Assert.Equal((_blocks.Stone, 2), inventory.At(0));
        Assert.Equal((_blocks.Sand, 5), inventory.At(1));
    }

    [Fact]
    public void a_stack_that_is_full_spills_into_the_next_slot()
    {
        var inventory = new Inventory(_blocks);

        var taken = inventory.Add(_blocks.Dirt, Inventory.StackSize + 3);

        Assert.Equal(Inventory.StackSize + 3, taken);
        Assert.Equal(Inventory.StackSize, inventory.At(0).Count);
        Assert.Equal(3, inventory.At(1).Count);
    }

    [Fact]
    public void a_full_row_refuses_what_will_not_fit_rather_than_losing_it()
    {
        // The count that comes back is what the caller keeps: a drop the row would not take has
        // to stay lying on the ground, not vanish into a row that was already full.
        var inventory = new Inventory(_blocks);
        for (var slot = 0; slot < Inventory.SlotCount; slot++)
            inventory.Add(_blocks.Stone, Inventory.StackSize);

        Assert.Equal(0, inventory.Add(_blocks.Sand, 1));
        Assert.Equal(0, inventory.Add(_blocks.Stone, 1));
    }

    [Fact]
    public void placing_a_block_spends_one_and_empties_the_slot_at_the_last_one()
    {
        var inventory = new Inventory(_blocks);
        inventory.Add(_blocks.Snow, 2);

        Assert.True(inventory.TakeOne());
        Assert.Equal(1, inventory.At(0).Count);

        Assert.True(inventory.TakeOne());
        Assert.Equal(0, inventory.At(0).Count);
        Assert.False(inventory.HasSelection);
        Assert.False(inventory.TakeOne());
    }

    [Fact]
    public void air_is_never_carried_however_it_is_offered()
    {
        var inventory = new Inventory(_blocks);

        Assert.Equal(0, inventory.Add(BlockId.Air, 4));
        Assert.Equal(0, inventory.Add(_blocks.Stone, 0));
        Assert.False(inventory.HasSelection);
    }

    [Fact]
    public void a_row_comes_back_from_a_save_the_way_it_went_in()
    {
        var inventory = new Inventory(_blocks);
        inventory.Add(_blocks.Stone, 7);
        inventory.Add(_blocks.Leaves, 2);

        var restored = new Inventory(_blocks);
        restored.Restore(inventory.Carried(_blocks.Registry), _blocks.Registry);

        Assert.Equal((_blocks.Stone, 7), restored.At(0));
        Assert.Equal((_blocks.Leaves, 2), restored.At(1));
        Assert.Equal(0, restored.At(2).Count);
    }

    [Fact]
    public void a_saved_block_this_build_no_longer_has_is_dropped_rather_than_refused()
    {
        // The same rule the edits follow. A row that would not open because of a block someone
        // took out between two versions is worse than a row that opens a slot short.
        var inventory = new Inventory(_blocks);

        inventory.Restore([("moonrock", 4), ("stone", 3)], _blocks.Registry);

        Assert.Equal(0, inventory.At(0).Count);
        Assert.Equal((_blocks.Stone, 3), inventory.At(1));
    }

    [Fact]
    public void what_is_picked_up_goes_into_the_row_before_the_pack()
    {
        var inventory = new Inventory(_blocks);

        inventory.Add(_blocks.Stone, Inventory.StackSize * Inventory.RowCount);

        for (var slot = 0; slot < Inventory.RowCount; slot++)
            Assert.Equal(Inventory.StackSize, inventory.At(slot).Count);

        Assert.Equal(0, inventory.At(Inventory.RowCount).Count);
    }

    [Fact]
    public void the_pack_takes_what_the_row_has_no_room_for()
    {
        var inventory = new Inventory(_blocks);

        var taken = inventory.Add(_blocks.Stone, Inventory.StackSize * Inventory.SlotCount);

        Assert.Equal(Inventory.StackSize * Inventory.SlotCount, taken);
        Assert.Equal(Inventory.StackSize, inventory.At(Inventory.SlotCount - 1).Count);
    }

    [Fact]
    public void only_a_slot_of_the_row_can_be_put_in_hand()
    {
        var inventory = new Inventory(_blocks);

        inventory.Select(Inventory.RowCount);

        Assert.Equal(0, inventory.SelectedIndex);
    }

    [Fact]
    public void taking_a_slot_empties_it_and_hands_over_everything_in_it()
    {
        var inventory = new Inventory(_blocks);
        inventory.Add(_blocks.Dirt, 7);

        var taken = inventory.Take(0);

        Assert.Equal(_blocks.Dirt, taken.Block);
        Assert.Equal(7, taken.Count);
        Assert.Equal(0, inventory.At(0).Count);
    }

    [Fact]
    public void taking_half_rounds_up_so_that_half_of_one_is_one()
    {
        var inventory = new Inventory(_blocks);
        inventory.Add(_blocks.Dirt, 1);

        var taken = inventory.TakeHalf(0);

        Assert.Equal(1, taken.Count);
        Assert.Equal(0, inventory.At(0).Count);
    }

    [Fact]
    public void taking_half_of_an_odd_stack_leaves_the_smaller_half_behind()
    {
        var inventory = new Inventory(_blocks);
        inventory.Add(_blocks.Dirt, 7);

        var taken = inventory.TakeHalf(0);

        Assert.Equal(4, taken.Count);
        Assert.Equal(3, inventory.At(0).Count);
    }

    [Fact]
    public void putting_a_stack_onto_the_same_block_merges_the_two()
    {
        var inventory = new Inventory(_blocks);
        inventory.Add(_blocks.Dirt, 10);

        var left = inventory.Put(0, new Stack(_blocks.Dirt, 5));

        Assert.True(left.IsNothing);
        Assert.Equal(15, inventory.At(0).Count);
    }

    [Fact]
    public void a_merge_that_does_not_fit_leaves_the_remainder_in_hand()
    {
        var inventory = new Inventory(_blocks);
        inventory.Add(_blocks.Dirt, Inventory.StackSize - 2);

        var left = inventory.Put(0, new Stack(_blocks.Dirt, 9));

        Assert.Equal(_blocks.Dirt, left.Block);
        Assert.Equal(7, left.Count);
        Assert.Equal(Inventory.StackSize, inventory.At(0).Count);
    }

    [Fact]
    public void putting_a_stack_onto_a_different_block_swaps_the_two()
    {
        var inventory = new Inventory(_blocks);
        inventory.Add(_blocks.Dirt, 4);

        var left = inventory.Put(0, new Stack(_blocks.Stone, 9));

        Assert.Equal(_blocks.Dirt, left.Block);
        Assert.Equal(4, left.Count);
        Assert.Equal(_blocks.Stone, inventory.At(0).Block);
        Assert.Equal(9, inventory.At(0).Count);
    }

    [Fact]
    public void putting_one_down_moves_exactly_one()
    {
        var inventory = new Inventory(_blocks);

        var left = inventory.PutOne(5, new Stack(_blocks.Sand, 3));

        Assert.Equal(2, left.Count);
        Assert.Equal(1, inventory.At(5).Count);
        Assert.Equal(_blocks.Sand, inventory.At(5).Block);
    }

    [Fact]
    public void putting_one_onto_a_different_block_does_nothing_rather_than_swapping()
    {
        // The gesture means "one of these here", and a swap is not a smaller version of that: a
        // player tapping right click along a row would otherwise scatter what they were holding.
        var inventory = new Inventory(_blocks);
        inventory.Add(_blocks.Dirt, 4);

        var left = inventory.PutOne(0, new Stack(_blocks.Stone, 3));

        Assert.Equal(3, left.Count);
        Assert.Equal(_blocks.Dirt, inventory.At(0).Block);
        Assert.Equal(4, inventory.At(0).Count);
    }

    [Fact]
    public void moving_a_stack_between_two_slots_conserves_every_block()
    {
        var inventory = new Inventory(_blocks);
        inventory.Add(_blocks.Dirt, 30);
        inventory.Add(_blocks.Stone, 40);

        var held = inventory.Take(0);
        held = inventory.Put(Inventory.SlotCount - 1, held);
        held = inventory.Put(0, held);

        Assert.True(held.IsNothing);
        Assert.Equal(30, Total(inventory, _blocks.Dirt));
        Assert.Equal(40, Total(inventory, _blocks.Stone));
    }

    private static int Total(Inventory inventory, BlockId block)
    {
        var total = 0;
        for (var slot = 0; slot < Inventory.SlotCount; slot++)
        {
            var (held, count) = inventory.At(slot);
            if (count > 0 && held == block)
                total += count;
        }

        return total;
    }
}
