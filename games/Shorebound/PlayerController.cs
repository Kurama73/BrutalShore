using BrutalShore.Gpu;
using BrutalShore.Render;

namespace Shorebound;

/// <summary>Turns the keyboard and mouse into where the player is looking and what they want.</summary>
/// <remarks>
/// It reads input and nothing else: what the player can do with the result — how fast they move,
/// whether they can fly through a wall — is <see cref="Player" />'s, and what a click does to the
/// world is the game's. Keeping the three apart is what makes the movement testable at all, since
/// a window cannot be opened in a test.
/// </remarks>
public sealed class PlayerController
{
    private readonly IInputSource _input;
    private readonly Camera _camera;

    /// <summary>Wires the controller to an input source and the camera it aims.</summary>
    /// <param name="input">Where the keyboard and the mouse come from; a window, or a script.</param>
    /// <param name="camera">The camera the mouse turns.</param>
    public PlayerController(IInputSource input, Camera camera)
    {
        _input = input;
        _camera = camera;
    }

    /// <summary>How far the camera turns per pixel of mouse movement, in radians.</summary>
    public float Sensitivity { get; set; } = 0.0022f;

    /// <summary>Reads what the player is asking for this step.</summary>
    /// <returns>The intent, or <see cref="PlayerIntent.Still" /> while the mouse is free.</returns>
    /// <remarks>
    /// Nothing is read while the mouse is not captured. Otherwise typing in a menu would walk the
    /// player off a cliff behind it.
    /// </remarks>
    public PlayerIntent ReadIntent()
    {
        if (!_input.IsMouseCaptured)
            return PlayerIntent.Still;

        return new PlayerIntent
        {
            Forward = Axis(GlfwKey.W, GlfwKey.S),
            Right = Axis(GlfwKey.D, GlfwKey.A),
            Jump = _input.IsKeyDown(GlfwKey.Space),
            Sink = _input.IsKeyDown(GlfwKey.LeftControl),
            Sprint = _input.IsKeyDown(GlfwKey.LeftShift),
            Crouch = _input.IsKeyDown(GlfwKey.LeftControl),
        };
    }

    /// <summary>Turns the camera by however far the mouse moved.</summary>
    public void Look()
    {
        if (_input.IsMouseCaptured)
            _camera.Look(_input.CursorDelta.X, _input.CursorDelta.Y, Sensitivity);
    }

    /// <summary>Aims the camera somewhere, for a loaded save or a fixed viewpoint.</summary>
    /// <param name="yaw">Which way to face, in radians.</param>
    /// <param name="pitch">How far up or down to look, in radians.</param>
    public void AimAt(float yaw, float pitch)
    {
        // The camera only turns by deltas, so aiming it absolutely means undoing where it is.
        _camera.Look((yaw - _camera.Yaw) / Sensitivity, -(pitch - _camera.Pitch) / Sensitivity, Sensitivity);
    }

    private int Axis(GlfwKey positive, GlfwKey negative) =>
        (_input.IsKeyDown(positive) ? 1 : 0) - (_input.IsKeyDown(negative) ? 1 : 0);
}
