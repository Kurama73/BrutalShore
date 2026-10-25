namespace Shorebound.Tests;

/// <summary>Pins down the row of blocks: picking one, and moving along it.</summary>
public sealed class HotbarTests
{
    [Fact]
    public void picking_a_slot_puts_that_block_in_hand()
    {
        var blocks = new GameBlocks(new BlockRegistry());
        var hotbar = new Hotbar(blocks);

        hotbar.Select(3);

        Assert.Equal(3, hotbar.SelectedIndex);
        Assert.Equal(blocks.Sand, hotbar.Selected);
        Assert.Equal("Sand", hotbar.SelectedName);
    }

    [Fact]
    public void a_slot_that_is_not_there_is_ignored_rather_than_an_error()
    {
        var hotbar = new Hotbar(new GameBlocks(new BlockRegistry()));
        hotbar.Select(2);

        // The keys 1 to 9 are read in a loop, and a row with six slots has to survive the rest.
        hotbar.Select(99);
        hotbar.Select(-1);

        Assert.Equal(2, hotbar.SelectedIndex);
    }

    [Fact]
    public void moving_past_either_end_of_the_row_wraps_round()
    {
        var hotbar = new Hotbar(new GameBlocks(new BlockRegistry()));

        hotbar.Select(0);
        hotbar.Cycle(-1);
        Assert.Equal(hotbar.Count - 1, hotbar.SelectedIndex);

        hotbar.Cycle(1);
        Assert.Equal(0, hotbar.SelectedIndex);
    }

    [Fact]
    public void every_slot_holds_a_block_that_can_actually_be_placed()
    {
        var hotbar = new Hotbar(new GameBlocks(new BlockRegistry()));

        for (var slot = 0; slot < hotbar.Count; slot++)
        {
            hotbar.Select(slot);

            Assert.True(hotbar.Selected.IsSolid, $"slot {slot} holds air");
            Assert.False(string.IsNullOrWhiteSpace(hotbar.NameOf(slot)));
        }
    }
}
