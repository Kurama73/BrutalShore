namespace BrutalShore.Core.Tests;

/// <summary>
/// Pins down how real time is turned into simulation steps, and what happens when a frame is late.
/// </summary>
/// <remarks>
/// The catch-up cap is the part worth testing. It only shows itself on a frame that took longer
/// than the steps it owes — a stall, a breakpoint, a chunk upload that blocked — and the failure
/// it prevents is a spiral in which every frame owes more than the last and none ever catches up.
/// Waiting for one of those to happen is not a test.
/// </remarks>
public sealed class FrameClockTests
{
    /// <summary>The length of one step, which everything here is measured in.</summary>
    private const double Step = FrameClock.StepSeconds;

    [Fact]
    public void a_frame_shorter_than_a_step_runs_nothing_yet()
    {
        var clock = new FrameClock();

        Assert.Equal(0, clock.Advance(Step * 0.4));
        Assert.Equal(0, clock.StepCount);
        Assert.Equal(1, clock.FrameCount);
    }

    [Fact]
    public void what_is_left_over_carries_into_the_next_frame()
    {
        var clock = new FrameClock();

        clock.Advance(Step * 0.6);

        // Six tenths and six tenths is one step and a fifth, not one step and nothing: dropping
        // the remainder every frame would run the simulation slow by however much was dropped.
        Assert.Equal(1, clock.Advance(Step * 0.6));
        Assert.Equal(0.2f, clock.StepFraction, 1e-4f);
    }

    [Fact]
    public void a_long_frame_runs_the_steps_it_owes()
    {
        var clock = new FrameClock();

        Assert.Equal(4, clock.Advance(Step * 4.0));
        Assert.Equal(4, clock.StepCount);
    }

    [Fact]
    public void a_stall_cannot_owe_more_steps_than_one_frame_may_run()
    {
        var clock = new FrameClock();

        // Two whole seconds, which at sixty steps a second is a hundred and twenty of them.
        var steps = clock.Advance(2.0);

        Assert.True(steps <= 8, $"a single frame ran {steps} steps");
        Assert.True(steps > 0);
    }

    [Fact]
    public void after_a_capped_frame_the_next_one_starts_clean()
    {
        var clock = new FrameClock();
        clock.Advance(2.0);

        // The backlog is dropped rather than carried. Carrying it is what turns one stall into a
        // run of late frames, each owing more than the last.
        Assert.Equal(0, clock.Advance(Step * 0.5));
        Assert.Equal(0.5f, clock.StepFraction, 1e-4f);
    }

    [Fact]
    public void simulated_time_falls_behind_rather_than_the_frame_rate_collapsing()
    {
        var clock = new FrameClock();

        clock.Advance(2.0);
        var ranAfterTheStall = clock.StepCount;

        // Two seconds of real time produced far fewer than two seconds of simulation, which is
        // the trade: the world is briefly in slow motion instead of the game being unresponsive.
        Assert.True(ranAfterTheStall * Step < 2.0);
    }

    [Fact]
    public void the_step_fraction_says_where_between_two_steps_the_frame_sits()
    {
        var clock = new FrameClock();

        clock.Advance(Step * 2.25);

        // What an interpolating renderer needs in order to draw between two simulated states.
        Assert.Equal(0.25f, clock.StepFraction, 1e-4f);
    }

    [Fact]
    public void the_frame_rate_settles_on_a_steady_frame_length()
    {
        var clock = new FrameClock();

        for (var frame = 0; frame < 200; frame++)
            clock.Advance(1.0 / 144.0);

        // An exponential average, so it approaches rather than arrives; a per cent is close
        // enough to read off an overlay and far enough from 60 to prove it followed the input.
        Assert.Equal(144.0, clock.FramesPerSecond, 1.5);
    }
}
