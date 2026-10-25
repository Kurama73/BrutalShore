using RustSpace.Gpu;
using RustSpace.Render;

namespace RustSpace.App;

/// <summary>Turns keyboard and mouse state into camera movement.</summary>
/// <remarks>
/// Separate from <see cref="Camera" /> so the camera stays a piece of maths with no idea what a
/// key is: the mapping from a physical control to a movement is the part that changes often, and
/// the part that will need to grow a configuration file.
/// </remarks>
public sealed class PlayerController
{
    private readonly GameWindow _window;
    private readonly Camera _camera;

    private bool _escapeWasDown;

    /// <summary>Wires a window's input to a camera.</summary>
    public PlayerController(GameWindow window, Camera camera)
    {
        _window = window;
        _camera = camera;
    }

    /// <summary>Metres per second while walking.</summary>
    public double Speed { get; set; } = 14.0;

    /// <summary>How much faster the sprint key is.</summary>
    public double SprintFactor { get; set; } = 5.0;

    /// <summary>Radians of rotation per pixel of mouse movement.</summary>
    public float Sensitivity { get; set; } = 0.0022f;

    /// <summary>Applies one step's worth of input to the camera.</summary>
    /// <param name="seconds">The step's length.</param>
    public void Step(double seconds)
    {
        UpdateMouseCapture();

        // Only turn while the cursor is captured. Looking around as the mouse travels to a menu
        // would be maddening, and the delta while uncaptured belongs to whatever is on screen.
        if (_window.IsMouseCaptured)
            _camera.Look(_window.CursorDelta.X, _window.CursorDelta.Y, Sensitivity);

        var distance = seconds * (_window.IsKeyDown(GlfwKey.LeftShift) ? Speed * SprintFactor : Speed);

        var forward = Axis(GlfwKey.W, GlfwKey.S);
        var right = Axis(GlfwKey.D, GlfwKey.A);
        var up = Axis(GlfwKey.Space, GlfwKey.LeftControl);

        if (forward == 0 && right == 0 && up == 0)
            return;

        // Normalising the horizontal pair keeps a diagonal from being faster than a straight line,
        // which is the oldest bug in first-person movement.
        var horizontal = forward != 0 && right != 0 ? 0.70710678118654752 : 1.0;

        _camera.Move(forward * distance * horizontal, right * distance * horizontal, up * distance);
    }

    /// <summary>
    /// Captures the cursor on a click in the window, and releases it on escape.
    /// </summary>
    /// <remarks>
    /// Escape is read as an edge rather than a level, so holding it does not fight with anything
    /// that would like to capture the cursor again on the same frame.
    /// </remarks>
    private void UpdateMouseCapture()
    {
        var escapeIsDown = _window.IsKeyDown(GlfwKey.Escape);
        if (escapeIsDown && !_escapeWasDown && _window.IsMouseCaptured)
            _window.SetMouseCaptured(false);

        _escapeWasDown = escapeIsDown;

        if (!_window.IsMouseCaptured && _window.IsMouseButtonDown(GlfwMouseButton.Left))
            _window.SetMouseCaptured(true);
    }

    private int Axis(GlfwKey positive, GlfwKey negative) =>
        (_window.IsKeyDown(positive) ? 1 : 0) - (_window.IsKeyDown(negative) ? 1 : 0);
}
