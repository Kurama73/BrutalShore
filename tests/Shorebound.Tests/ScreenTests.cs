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
    [InlineData(Screen.Paused)]
    [InlineData(Screen.Settings)]
    public void a_menu_stops_the_world(Screen screen)
    {
        Assert.False(screen.RunsTheWorld());
    }

    [Theory]
    [InlineData(Screen.Title)]
    [InlineData(Screen.Paused)]
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
}
