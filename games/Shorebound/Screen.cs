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
}
