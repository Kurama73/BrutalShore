namespace Shorebound.Tests;

/// <summary>Which screens stop the game and which only cover it.</summary>
/// <remarks>
/// A regression test with a story. The smelter screen was added as a state that stopped the
/// world, the way the inventory did, and the result was that a smelter stopped smelting for
/// exactly as long as anyone watched it — a machine built to run unattended, reporting IDLE
/// whenever it was looked at. The rule is one line of code and was worth naming so that the next
/// panel over the world gets it right by default.
/// </remarks>
public sealed class ScreenTests
{
    [Theory]
    [InlineData(Screen.Playing)]
    [InlineData(Screen.Inventory)]
    [InlineData(Screen.Machine)]
    public void a_panel_over_the_world_lets_the_world_keep_running(Screen screen)
    {
        Assert.True(screen.RunsTheWorld());
    }

    [Theory]
    [InlineData(Screen.Title)]
    [InlineData(Screen.Saves)]
    [InlineData(Screen.CreateWorld)]
    [InlineData(Screen.Paused)]
    [InlineData(Screen.Terminal)]
    [InlineData(Screen.Settings)]
    public void a_menu_stops_the_world(Screen screen)
    {
        Assert.False(screen.RunsTheWorld());
    }

    [Theory]
    [InlineData(Screen.Title)]
    [InlineData(Screen.Saves)]
    [InlineData(Screen.CreateWorld)]
    [InlineData(Screen.Paused)]
    [InlineData(Screen.Terminal)]
    [InlineData(Screen.Settings)]
    [InlineData(Screen.Inventory)]
    [InlineData(Screen.Machine)]
    public void only_playing_takes_the_players_keys(Screen screen)
    {
        // The other half of the same rule: the world running is not the same as the player
        // steering it. A panel takes the input away and leaves the clock alone.
        Assert.False(screen.TakesInput());
    }

    [Fact]
    public void playing_both_runs_the_world_and_takes_input()
    {
        Assert.True(Screen.Playing.RunsTheWorld());
        Assert.True(Screen.Playing.TakesInput());
    }

    [Fact]
    public void every_screen_says_one_or_the_other()
    {
        // So that a screen added later cannot quietly default to stopping the world by being
        // left out of a list somewhere.
        foreach (var screen in Enum.GetValues<Screen>())
            Assert.True(screen.RunsTheWorld() || !screen.TakesInput());
    }

    [Fact]
    public void the_key_that_opened_a_panel_closes_it()
    {
        // The regression. Opening and closing used to be read in two places, and because closing
        // puts the player back to playing in the same frame, the second place saw the same press
        // and opened it again — so the key appeared to do nothing at all.
        Assert.Equal(Screen.Inventory, Screen.Playing.Panel(carrying: true, milestones: false));
        Assert.Equal(Screen.Playing, Screen.Inventory.Panel(carrying: true, milestones: false));

        Assert.Equal(Screen.Milestones, Screen.Playing.Panel(carrying: false, milestones: true));
        Assert.Equal(Screen.Playing, Screen.Milestones.Panel(carrying: false, milestones: true));
    }

    [Fact]
    public void a_panel_is_left_alone_by_the_other_panel_s_key()
    {
        // Otherwise a player with the inventory open presses the milestones key and lands
        // somewhere neither key asked for.
        Assert.Equal(Screen.Inventory, Screen.Inventory.Panel(carrying: false, milestones: true));
        Assert.Equal(Screen.Milestones, Screen.Milestones.Panel(carrying: true, milestones: false));
    }

    [Fact]
    public void a_panel_key_does_nothing_in_a_menu()
    {
        foreach (var screen in new[]
                 {
                     Screen.Title, Screen.Saves, Screen.CreateWorld, Screen.Paused, Screen.Terminal, Screen.Settings,
                     Screen.Machine
                 })
            Assert.Equal(screen, screen.Panel(carrying: true, milestones: true));
    }

    [Fact]
    public void nothing_pressed_changes_nothing()
    {
        foreach (var screen in Enum.GetValues<Screen>())
            Assert.Equal(screen, screen.Panel(carrying: false, milestones: false));
    }

    [Fact]
    public void every_panel_the_keys_open_can_be_closed_again_by_them()
    {
        // The claim that keeps a panel from being added that only escape gets out of.
        foreach (var screen in Enum.GetValues<Screen>())
        {
            var opened = screen.Panel(carrying: true, milestones: false);
            if (opened != screen)
                Assert.Equal(screen, opened.Panel(carrying: true, milestones: false));

            opened = screen.Panel(carrying: false, milestones: true);
            if (opened != screen)
                Assert.Equal(screen, opened.Panel(carrying: false, milestones: true));
        }
    }
}