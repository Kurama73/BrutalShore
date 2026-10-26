namespace BrutalShore.Gpu;

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
/// <remarks>
/// Every key and button is read once per <see cref="PollEvents" /> into a pair of snapshots, so
/// that asking twice in a frame gives the same answer and so that "was it just pressed" is a
/// question that can be asked at all. Without the snapshot every caller that needs an edge keeps
/// its own copy of the previous state, which is three copies of the same bug waiting to differ.
/// </remarks>
public sealed class GameWindow : IInputSource, IDisposable
{
    /// <summary>One past the highest key GLFW defines, which is the escape-to-menu hatch's size.</summary>
    private const int KeyCount = 349;

    /// <summary>One past the highest mouse button GLFW defines.</summary>
    private const int ButtonCount = 8;

    private readonly bool[] _keysNow = new bool[KeyCount];
    private readonly bool[] _keysBefore = new bool[KeyCount];
    private readonly bool[] _buttonsNow = new bool[ButtonCount];
    private readonly bool[] _buttonsBefore = new bool[ButtonCount];

    private double2 _lastCursorPosition;
    private bool _hasCursorPosition;

    // Added to by the callback as events arrive and taken by the frame that reads it. A wheel
    // has no state to ask for the way a key does — it only ever reports that it turned — so the
    // notches have to be collected between one poll and the next or they are lost.
    private double _scrollSinceRead;
    private double _scrolledThisFrame;

    /// <summary>Opens a window with no graphics API bound, ready for a Vulkan surface.</summary>
    public GameWindow(string title, int2 size)
    {
        // No client API: the window must not create an OpenGL context, or it cannot carry a Vulkan
        // surface. On macOS this is also what makes GLFW attach the CAMetalLayer MoltenVK needs.
        Glfw.WindowHint(GlfwWindowHint.ClientApi, 0);
        Handle = Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title = title, Size = size });

        // Sticky input latches a press until it has been read once, so a tap shorter than a frame
        // is still seen. Without it the shortest press a player can land is a whole frame long,
        // which on a machine struggling at twenty frames a second is fifty milliseconds — and the
        // keys that suffer are exactly the tapped ones: jump, the hotbar, the menu.
        Glfw.PInvoke.SetInputMode(Handle, Glfw.Constants.GLFW_STICKY_KEYS, 1);
        Glfw.PInvoke.SetInputMode(Handle, Glfw.Constants.GLFW_STICKY_MOUSE_BUTTONS, 1);

        Handle.OnScroll += OnScrolled;
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

    /// <summary>
    /// How far the wheel turned since the last <see cref="PollEvents" />, in notches; positive
    /// is away from the hand.
    /// </summary>
    /// <remarks>
    /// Summed over the poll rather than reported per event, because a frame wants to know how far
    /// the wheel went, and a fast flick is several events that all belong to the same gesture.
    /// </remarks>
    public double ScrollDelta => _scrolledThisFrame;

    /// <summary>Pumps the event queue and refreshes the cursor delta.</summary>
    public void PollEvents()
    {
        Glfw.PollEvents();

        Array.Copy(_keysNow, _keysBefore, KeyCount);
        for (var key = 0; key < KeyCount; key++)
            _keysNow[key] = Glfw.PInvoke.GetKey(Handle, key) == (int)GlfwKeyAction.Press;

        Array.Copy(_buttonsNow, _buttonsBefore, ButtonCount);
        for (var button = 0; button < ButtonCount; button++)
            _buttonsNow[button] = Glfw.PInvoke.GetMouseButton(Handle, button) == (int)GlfwButtonAction.Press;

        _scrolledThisFrame = _scrollSinceRead;
        _scrollSinceRead = 0.0;

        var position = Handle.CursorPos;

        // The first frame after capture has no previous position to subtract, and using the
        // window's centre or zero would throw the camera across the sky on the first mouse move.
        CursorDelta = _hasCursorPosition ? position - _lastCursorPosition : double2.Zero;
        _lastCursorPosition = position;
        _hasCursorPosition = true;
    }

    private void OnScrolled(GlfwWindow window, double2 offset) => _scrollSinceRead += offset.Y;

    /// <summary>Whether a key is held down this frame.</summary>
    public bool IsKeyDown(GlfwKey key) => InRange((int)key, KeyCount) && _keysNow[(int)key];

    /// <summary>Whether a key went down between the last two polls.</summary>
    /// <param name="key">The key to ask about.</param>
    /// <returns><see langword="true" /> on the one frame the key was pressed.</returns>
    public bool WasKeyPressed(GlfwKey key) =>
        InRange((int)key, KeyCount) && _keysNow[(int)key] && !_keysBefore[(int)key];

    /// <summary>Whether a mouse button is held down this frame.</summary>
    public bool IsMouseButtonDown(GlfwMouseButton button) =>
        InRange((int)button, ButtonCount) && _buttonsNow[(int)button];

    /// <summary>Whether a mouse button went down between the last two polls.</summary>
    /// <param name="button">The button to ask about.</param>
    /// <returns><see langword="true" /> on the one frame the button was pressed.</returns>
    public bool WasMouseButtonPressed(GlfwMouseButton button) =>
        InRange((int)button, ButtonCount) && _buttonsNow[(int)button] && !_buttonsBefore[(int)button];

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

        // Put the pointer in the middle of the window on the way out. Two reasons, and both of
        // them are things a player notices: a menu opened with the pointer already in the middle
        // of it is a menu whose first button is where the hand already is, and moving the cursor
        // is what tells everything downstream — the interface's own input layer included — where
        // it now is. Released without being moved, it can be left at whatever it was before the
        // capture, which is wherever the player last clicked a title screen.
        if (!captured)
        {
            var size = Handle.FramebufferSize;
            Handle.CursorPos = new double2(size.X / 2.0, size.Y / 2.0);
        }

        // Releasing and re-capturing moves the cursor, so the next delta is measured from where it
        // ends up rather than from where it was before.
        _hasCursorPosition = false;
    }

    /// <summary>Asks for the window to close, which ends the game loop on the next iteration.</summary>
    public void Close() => Handle.ShouldClose = true;

    private static bool InRange(int index, int count) => (uint)index < count;

    /// <inheritdoc />
    public void Dispose() => Handle.Dispose();
}
