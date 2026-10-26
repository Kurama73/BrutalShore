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

    /// <summary>In the world, mouse free, reading what the shore still asks for.</summary>
    Milestones,
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
        screen is Screen.Playing or Screen.Inventory or Screen.Machine or Screen.Milestones;

    /// <summary>Whether the player's own keys and mouse reach the world.</summary>
    /// <param name="screen">The screen showing.</param>
    /// <returns><see langword="true" /> only while actually playing.</returns>
    public static bool TakesInput(this Screen screen) => screen is Screen.Playing;

    /// <summary>Where the keys that open and close a panel take the screen.</summary>
    /// <param name="screen">The screen showing.</param>
    /// <param name="carrying">Whether the key for what is being carried was pressed.</param>
    /// <param name="milestones">Whether the key for what the shore asks was pressed.</param>
    /// <returns>The screen to show, which is the same one when nothing happens.</returns>
    /// <remarks>
    /// <para>
    /// Opening and closing are one function on purpose. They used to be two — the panel key was
    /// read where a screen was open, and read again where the player was playing — and because
    /// closing a panel puts the player back to playing within the same frame, the second read saw
    /// the same key press and opened it straight back up. The panel key did nothing, for months,
    /// and only escape ever closed anything.
    /// </para>
    /// <para>
    /// Written as one step from one screen to another, that cannot happen: there is one answer
    /// per frame and it is this one. The pause menu and the settings are not here because where
    /// they go back to is not a function of the screen showing — it depends on how they were
    /// opened, which is something only the game knows.
    /// </para>
    /// </remarks>
    public static Screen Panel(this Screen screen, bool carrying, bool milestones) =>
        screen switch
        {
            Screen.Inventory => carrying ? Screen.Playing : screen,
            Screen.Milestones => milestones ? Screen.Playing : screen,
            Screen.Playing when carrying => Screen.Inventory,
            Screen.Playing when milestones => Screen.Milestones,
            _ => screen,
        };
}
