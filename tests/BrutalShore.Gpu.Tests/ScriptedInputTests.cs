namespace BrutalShore.Gpu.Tests;

/// <summary>
/// Driving a game from a script: that a tap is seen by one frame, that a hold is held for as long
/// as it says, and that the meaning of a script does not depend on the frame rate.
/// </summary>
/// <remarks>
/// The last of those is the whole value. A scripted session is for reproducing something, and the
/// machine a problem happens on is rarely the machine it was recorded on — so a script that meant
/// one thing at two hundred frames a second and another at four would reproduce nothing.
/// </remarks>
public sealed class ScriptedInputTests
{
    [Fact]
    public void a_fresh_script_does_nothing()
    {
        var input = Driving(new SessionScript());

        Assert.True(input.IsFinished);
        Assert.False(input.IsKeyDown(GlfwKey.W));
    }

    [Fact]
    public void a_tap_is_seen_by_exactly_one_frame()
    {
        // The contract a window keeps, and the one a script has to keep too: a press is an edge.
        var input = Driving(new SessionScript().Tap(GlfwKey.Space));

        input.Advance(0.1);
        Assert.True(input.WasKeyPressed(GlfwKey.Space));

        input.Advance(0.1);
        Assert.False(input.WasKeyPressed(GlfwKey.Space));
    }

    [Fact]
    public void a_tap_is_never_held()
    {
        var input = Driving(new SessionScript().Tap(GlfwKey.Space));

        input.Advance(0.1);

        Assert.False(input.IsKeyDown(GlfwKey.Space));
    }

    [Fact]
    public void a_hold_stays_down_for_as_long_as_it_says()
    {
        var script = new SessionScript().Hold(GlfwKey.W, 1.0);
        var input = Driving(script);

        input.Advance(0.5);
        Assert.True(input.IsKeyDown(GlfwKey.W));

        input.Advance(0.6);
        Assert.False(input.IsKeyDown(GlfwKey.W));
    }

    [Fact]
    public void a_hold_also_reads_as_pressed_on_its_first_frame()
    {
        // So that a game which acts on the edge rather than the level sees a held key at all.
        var input = Driving(new SessionScript().Hold(GlfwKey.W, 1.0));

        input.Advance(0.1);

        Assert.True(input.WasKeyPressed(GlfwKey.W));
    }

    [Fact]
    public void a_click_is_an_edge_and_a_held_button_is_a_level()
    {
        var click = Driving(new SessionScript().Click(GlfwMouseButton.Left));
        click.Advance(0.1);
        Assert.True(click.WasMouseButtonPressed(GlfwMouseButton.Left));
        Assert.False(click.IsMouseButtonDown(GlfwMouseButton.Left));

        var held = Driving(new SessionScript().HoldButton(GlfwMouseButton.Left, 1.0));
        held.Advance(0.1);
        Assert.True(held.IsMouseButtonDown(GlfwMouseButton.Left));
    }

    [Fact]
    public void looking_turns_by_what_it_was_told_and_then_stops()
    {
        var input = Driving(new SessionScript().Look(new double2(40.0, -10.0)));

        input.Advance(0.1);
        Assert.Equal(40.0, input.CursorDelta.X);
        Assert.Equal(-10.0, input.CursorDelta.Y);

        input.Advance(0.1);
        Assert.Equal(0.0, input.CursorDelta.X);
    }

    [Fact]
    public void the_wheel_turns_once_per_step()
    {
        var input = Driving(new SessionScript().Scroll(1.0));

        input.Advance(0.1);
        Assert.Equal(1.0, input.ScrollDelta);

        input.Advance(0.1);
        Assert.Equal(0.0, input.ScrollDelta);
    }

    [Fact]
    public void a_mark_is_written_where_the_script_says()
    {
        var written = new List<string>();
        var script = new SessionScript().Wait(1.0).Mark("halfway");
        var input = new ScriptedInput(new NothingPressed(), script, written.Add);

        input.Advance(0.5);
        Assert.Empty(written);

        input.Advance(0.6);
        Assert.Single(written);
        Assert.Contains("halfway", written[0]);
    }

    [Fact]
    public void a_script_means_the_same_thing_at_any_frame_rate()
    {
        // Driven at four frames a second and at two hundred, the same script has to leave the
        // same key held and have delivered the same number of presses.
        Assert.Equal(Played(1.0 / 4.0), Played(1.0 / 200.0));
    }

    [Fact]
    public void everything_between_two_frames_happens_even_on_a_long_one()
    {
        // A frame that took a whole second must not swallow the three taps inside it.
        var script = new SessionScript()
            .Tap(GlfwKey.Number1)
            .Wait(0.1)
            .Tap(GlfwKey.Number2)
            .Wait(0.1)
            .Tap(GlfwKey.Number3);

        var input = Driving(script);
        input.Advance(1.0);

        Assert.True(input.WasKeyPressed(GlfwKey.Number1));
        Assert.True(input.WasKeyPressed(GlfwKey.Number2));
        Assert.True(input.WasKeyPressed(GlfwKey.Number3));
        Assert.True(input.IsFinished);
    }

    [Fact]
    public void a_person_at_the_keyboard_is_still_heard()
    {
        // A scripted session is still a session: a script that silenced the window would be one
        // nobody could take over from, and taking over is most of why anyone watches a replay.
        var real = new NothingPressed { Held = GlfwKey.LeftShift };
        var input = new ScriptedInput(real, new SessionScript().Hold(GlfwKey.W, 1.0), _ => { });

        input.Advance(0.1);

        Assert.True(input.IsKeyDown(GlfwKey.W));
        Assert.True(input.IsKeyDown(GlfwKey.LeftShift));
    }

    [Fact]
    public void a_script_refuses_to_be_built_on_nothing()
    {
        Assert.Throws<ArgumentNullException>(() => new ScriptedInput(new NothingPressed(), null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new ScriptedInput(null!, new SessionScript(), _ => { }));
    }

    /// <summary>How many presses a script delivers when driven at a given frame length.</summary>
    private static (int Presses, bool Held) Played(double frameSeconds)
    {
        var script = new SessionScript()
            .Tap(GlfwKey.Number1)
            .Wait(0.5)
            .Tap(GlfwKey.Number2)
            .Wait(0.5)
            .Hold(GlfwKey.W, 1.0)
            .Wait(0.2);

        var input = Driving(script);
        var presses = 0;

        for (var elapsed = 0.0; elapsed < 1.6; elapsed += frameSeconds)
        {
            input.Advance(frameSeconds);

            if (input.WasKeyPressed(GlfwKey.Number1) || input.WasKeyPressed(GlfwKey.Number2))
                presses++;
        }

        return (presses, input.IsKeyDown(GlfwKey.W));
    }

    private static ScriptedInput Driving(SessionScript script) =>
        new(new NothingPressed(), script, _ => { });

    /// <summary>A window with nobody at it, optionally holding one key.</summary>
    private sealed class NothingPressed : IInputSource
    {
        public GlfwKey? Held { get; init; }

        public double2 CursorDelta => double2.Zero;

        public double ScrollDelta => 0.0;

        public bool IsMouseCaptured => true;

        public bool IsKeyDown(GlfwKey key) => Held == key;

        public bool WasKeyPressed(GlfwKey key) => false;

        public bool IsMouseButtonDown(GlfwMouseButton button) => false;

        public bool WasMouseButtonPressed(GlfwMouseButton button) => false;

        public void SetMouseCaptured(bool captured)
        {
            // Nothing to capture.
        }
    }
}
