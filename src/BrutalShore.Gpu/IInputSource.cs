namespace BrutalShore.Gpu;

/// <summary>Everything a game asks about the keyboard and the mouse this frame.</summary>
/// <remarks>
/// <para>
/// A seam rather than the window itself, and it exists for one reason: a game that reads its
/// input through an interface can be <em>driven</em>. The window is one implementation and a
/// script is another, which is what makes it possible to play a real build of a real game from
/// top to bottom with nobody at the keyboard — and therefore to find the things that only break
/// when the whole thing runs together, which no unit test reaches.
/// </para>
/// <para>
/// It is deliberately the smallest surface that covers what a game actually needs, and it is
/// stated in terms of a frame: what is held now, what became held since the last frame, how far
/// the mouse moved, how far the wheel turned. Anything about <em>windows</em> — the framebuffer,
/// whether it is minimised, closing it — stays on the window, because a script has no opinion
/// about any of that.
/// </para>
/// </remarks>
public interface IInputSource
{
    /// <summary>How far the mouse moved since the last frame, in pixels.</summary>
    double2 CursorDelta { get; }

    /// <summary>How far the wheel turned since the last frame.</summary>
    double ScrollDelta { get; }

    /// <summary>Whether the mouse is held inside the window and hidden.</summary>
    bool IsMouseCaptured { get; }

    /// <summary>Whether a key is held right now.</summary>
    /// <param name="key">The key to ask about.</param>
    /// <returns><see langword="true" /> while it is down.</returns>
    bool IsKeyDown(GlfwKey key);

    /// <summary>Whether a key went down since the last frame.</summary>
    /// <param name="key">The key to ask about.</param>
    /// <returns><see langword="true" /> on the frame it was pressed.</returns>
    bool WasKeyPressed(GlfwKey key);

    /// <summary>Whether a mouse button is held right now.</summary>
    /// <param name="button">The button to ask about.</param>
    /// <returns><see langword="true" /> while it is down.</returns>
    bool IsMouseButtonDown(GlfwMouseButton button);

    /// <summary>Whether a mouse button went down since the last frame.</summary>
    /// <param name="button">The button to ask about.</param>
    /// <returns><see langword="true" /> on the frame it was pressed.</returns>
    bool WasMouseButtonPressed(GlfwMouseButton button);

    /// <summary>Takes the mouse, or gives it back.</summary>
    /// <param name="captured">Whether to hold it inside the window and hide it.</param>
    void SetMouseCaptured(bool captured);
}
