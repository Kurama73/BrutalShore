namespace Shorebound;

/// <summary>Which of the game's three states it is in.</summary>
/// <remarks>
/// The state decides three things at once, which is why it is one value rather than a handful of
/// flags: whether the world is simulated, whether the mouse is captured, and what the interface
/// draws. Any pair of those disagreeing is a bug a player feels immediately.
/// </remarks>
public enum Screen
{
    /// <summary>The title screen, before a world has been entered.</summary>
    Title,

    /// <summary>In the world, mouse captured, time passing.</summary>
    Playing,

    /// <summary>In the world, mouse free, everything stopped.</summary>
    Paused,

    /// <summary>The settings, which the title screen and the pause menu both open.</summary>
    /// <remarks>
    /// A state of its own rather than a panel inside the other two, because the world behind it
    /// has to keep being drawn and must not keep being simulated — which is exactly what the
    /// other states already decide.
    /// </remarks>
    Settings,

    /// <summary>In the world, mouse free, looking at what is being carried.</summary>
    Inventory,

    /// <summary>In the world, mouse free, standing at a machine and looking inside it.</summary>
    /// <remarks>
    /// A state of its own rather than a mode of the inventory, because the panels differ and the
    /// escape key has somewhere different to go from each.
    /// </remarks>
    Machine,
}

/// <summary>Which screens are a menu and which are a panel over a running game.</summary>
/// <remarks>
/// <para>
/// The distinction is worth a name because it was got wrong once. A smelter is built so that it
/// works while the player is doing something else; a player standing at one, watching it, is
/// doing something else. Stopping the world to show them the inside of a machine meant the
/// machine stopped too, and the screen reported IDLE for as long as it was looked at.
/// </para>
/// <para>
/// So: a menu stops the game and a panel does not. Opening a panel takes the player's input away
/// and nothing else, which also means a player who opens one in mid-air keeps falling. That is
/// the right answer — the alternative is a pause button disguised as an inventory key.
/// </para>
/// </remarks>
public static class Screens
{
    /// <summary>Whether time passes and the world steps while this screen is up.</summary>
    /// <param name="screen">The screen showing.</param>
    /// <returns><see langword="true" /> for the ones drawn over a running game.</returns>
    public static bool RunsTheWorld(this Screen screen) =>
        screen is Screen.Playing or Screen.Inventory or Screen.Machine;

    /// <summary>Whether the player's own keys and mouse reach the world.</summary>
    /// <param name="screen">The screen showing.</param>
    /// <returns><see langword="true" /> only while actually playing.</returns>
    public static bool TakesInput(this Screen screen) => screen is Screen.Playing;
}
