namespace RustSpace.Gpu;

/// <summary>
/// The window, and the input read through it.
/// </summary>
/// <remarks>
/// Input is polled rather than delivered by callback. For a game loop that asks "is forward held?"
/// once per step, polling is the shape that fits: a callback would have to write into a state table
/// that the loop then reads, which is the same thing with an extra indirection and an ordering
/// question. The one quantity that genuinely is an event — how far the mouse moved since last frame
/// — is accumulated here, because there is no way to ask for it.
/// </remarks>
public sealed class GameWindow : IDisposable
{
    private double2 _lastCursorPosition;
    private bool _hasCursorPosition;

    /// <summary>Opens a window with no graphics API bound, ready for a Vulkan surface.</summary>
    public GameWindow(string title, int2 size)
    {
        // No client API: the window must not create an OpenGL context, or it cannot carry a Vulkan
        // surface. On macOS this is also what makes GLFW attach the CAMetalLayer MoltenVK needs.
        Glfw.WindowHint(GlfwWindowHint.ClientApi, 0);
        Handle = Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title = title, Size = size });
    }

    /// <summary>The GLFW window, for the surface and for the ImGui platform backend.</summary>
    public GlfwWindow Handle { get; }

    /// <summary>The drawable size in pixels, which on a scaled display is not the window size.</summary>
    public int2 FramebufferSize => Handle.FramebufferSize;

    /// <summary>Whether the user has asked to close the window.</summary>
    public bool ShouldClose => Handle.ShouldClose;

    /// <summary>
    /// Whether the window currently has no drawable area, which is what a minimised window looks
    /// like and the one state a swapchain cannot be created for.
    /// </summary>
    public bool IsMinimised => FramebufferSize.X <= 0 || FramebufferSize.Y <= 0;

    /// <summary>How far the cursor moved during the last <see cref="PollEvents"/>, in pixels.</summary>
    public double2 CursorDelta { get; private set; }

    /// <summary>Whether the cursor is hidden and locked to the window, as it is while flying.</summary>
    public bool IsMouseCaptured { get; private set; }

    /// <summary>Pumps the event queue and refreshes the cursor delta.</summary>
    public void PollEvents()
    {
        Glfw.PollEvents();

        var position = Handle.CursorPos;

        // The first frame after capture has no previous position to subtract, and using the
        // window's centre or zero would throw the camera across the sky on the first mouse move.
        CursorDelta = _hasCursorPosition ? position - _lastCursorPosition : double2.Zero;
        _lastCursorPosition = position;
        _hasCursorPosition = true;
    }

    /// <summary>Whether a key is held down this frame.</summary>
    public bool IsKeyDown(GlfwKey key) => Glfw.PInvoke.GetKey(Handle, (int)key) == (int)GlfwKeyAction.Press;

    /// <summary>Whether a mouse button is held down this frame.</summary>
    public bool IsMouseButtonDown(GlfwMouseButton button) =>
        Glfw.PInvoke.GetMouseButton(Handle, (int)button) == (int)GlfwButtonAction.Press;

    /// <summary>Hides and locks the cursor, or releases it.</summary>
    public void SetMouseCaptured(bool captured)
    {
        if (captured == IsMouseCaptured)
            return;

        Glfw.PInvoke.SetInputMode(
            Handle,
            Glfw.Constants.GLFW_CURSOR,
            captured ? Glfw.Constants.GLFW_CURSOR_DISABLED : Glfw.Constants.GLFW_CURSOR_NORMAL
        );

        IsMouseCaptured = captured;

        // Releasing and re-capturing moves the cursor, so the next delta is measured from where it
        // ends up rather than from where it was before.
        _hasCursorPosition = false;
    }

    /// <summary>Asks for the window to close, which ends the game loop on the next iteration.</summary>
    public void Close() => Handle.ShouldClose = true;

    /// <inheritdoc />
    public void Dispose() => Handle.Dispose();
}
