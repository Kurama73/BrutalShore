using Brutal.GlfwApi;
using BrutalShore.Gpu;
using BrutalShore.Render;

namespace Shorebound.Tests;

/// <summary>What the keyboard asks for, before anything decides whether it is allowed.</summary>
/// <remarks>
/// The controller is the one place a key becomes an intention, which makes it the one place a
/// binding can quietly not exist. That is worth a test on its own: every other test in here starts
/// from a <see cref="PlayerIntent" /> that was built by hand, so a key that reaches nothing passes
/// all of them.
/// </remarks>
public sealed class ControllerTests
{
    private readonly Keyboard _keyboard = new();
    private readonly PlayerController _controller;

    public ControllerTests() => _controller = new PlayerController(_keyboard, new Camera());

    [Fact]
    public void holding_shift_asks_to_go_down()
    {
        // The regression: sinking was on control alone, so flying had no way down on a keyboard
        // where every other voxel game descends with shift.
        _keyboard.Hold(GlfwKey.LeftShift);

        Assert.True(_controller.ReadIntent().Sink);
    }

    [Fact]
    public void holding_control_still_asks_to_go_down()
    {
        _keyboard.Hold(GlfwKey.LeftControl);

        Assert.True(_controller.ReadIntent().Sink);
    }

    [Fact]
    public void shift_still_means_sprint_as_well()
    {
        // Sinking is only ever read while flying, so the same key can mean both: on the ground it
        // is a sprint, in the air it is a fast way down.
        _keyboard.Hold(GlfwKey.LeftShift);

        Assert.True(_controller.ReadIntent().Sprint);
    }

    [Fact]
    public void nothing_held_asks_for_nothing()
    {
        var intent = _controller.ReadIntent();

        Assert.False(intent.Sink);
        Assert.False(intent.Jump);
        Assert.Equal(0, intent.Forward);
        Assert.Equal(0, intent.Right);
    }

    [Fact]
    public void the_keys_that_move_the_player_all_reach_something()
    {
        _keyboard.Hold(GlfwKey.W);
        Assert.Equal(1, _controller.ReadIntent().Forward);

        _keyboard.Release(GlfwKey.W);
        _keyboard.Hold(GlfwKey.S);
        Assert.Equal(-1, _controller.ReadIntent().Forward);

        _keyboard.Release(GlfwKey.S);
        _keyboard.Hold(GlfwKey.D);
        Assert.Equal(1, _controller.ReadIntent().Right);

        _keyboard.Release(GlfwKey.D);
        _keyboard.Hold(GlfwKey.A);
        Assert.Equal(-1, _controller.ReadIntent().Right);

        _keyboard.Release(GlfwKey.A);
        _keyboard.Hold(GlfwKey.Space);
        Assert.True(_controller.ReadIntent().Jump);
    }

    [Fact]
    public void nothing_is_read_while_the_mouse_is_free()
    {
        // Otherwise typing in a menu walks the player off whatever is behind it.
        _keyboard.Hold(GlfwKey.W);
        _keyboard.Hold(GlfwKey.LeftShift);
        _keyboard.SetMouseCaptured(false);

        Assert.Equal(PlayerIntent.Still, _controller.ReadIntent());
    }

    /// <summary>A keyboard with whatever keys a test says are held down.</summary>
    private sealed class Keyboard : IInputSource
    {
        private readonly HashSet<GlfwKey> _held = [];

        public double2 CursorDelta => double2.Zero;

        public double ScrollDelta => 0.0;

        public bool IsMouseCaptured { get; private set; } = true;

        public void Hold(GlfwKey key) => _held.Add(key);

        public void Release(GlfwKey key) => _held.Remove(key);

        public bool IsKeyDown(GlfwKey key) => _held.Contains(key);

        public bool WasKeyPressed(GlfwKey key) => false;

        public bool IsMouseButtonDown(GlfwMouseButton button) => false;

        public bool WasMouseButtonPressed(GlfwMouseButton button) => false;

        public void SetMouseCaptured(bool captured) => IsMouseCaptured = captured;
    }
}
