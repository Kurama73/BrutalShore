namespace BrutalShore.Core.Tests;

/// <summary>
/// Pins down the window of measurements: what it keeps, what it forgets, and what it answers.
/// </summary>
/// <remarks>
/// The percentile is the one worth the care. It is the number a frame budget is actually judged
/// against, and an off-by-one in picking it reads as a plausible figure rather than a wrong one.
/// </remarks>
public sealed class FrameTimelineTests
{
    [Fact]
    public void a_new_timeline_answers_everything_with_nothing()
    {
        var timeline = new FrameTimeline(16);

        Assert.Equal(0, timeline.Count);
        Assert.True(timeline.Window.IsEmpty);
        Assert.Equal(0f, timeline.Latest);
        Assert.Equal(0f, timeline.Average());
        Assert.Equal(0f, timeline.Worst());
        Assert.Equal(0f, timeline.Percentile(0.99));
    }

    [Fact]
    public void the_window_runs_oldest_to_newest()
    {
        var timeline = new FrameTimeline(4);

        foreach (var value in new[] { 1f, 2f, 3f })
            timeline.Add(value);

        Assert.Equal(new[] { 1f, 2f, 3f }, timeline.Window.ToArray());
        Assert.Equal(3f, timeline.Latest);
    }

    [Fact]
    public void the_oldest_sample_falls_off_once_the_window_is_full()
    {
        var timeline = new FrameTimeline(4);

        foreach (var value in new[] { 1f, 2f, 3f, 4f, 5f, 6f })
            timeline.Add(value);

        // Still oldest to newest after wrapping, which is the whole reason the buffer is doubled.
        Assert.Equal(new[] { 3f, 4f, 5f, 6f }, timeline.Window.ToArray());
        Assert.Equal(6, timeline.Count);
        Assert.Equal(4, timeline.Window.Length);
    }

    [Fact]
    public void the_average_is_over_the_window_and_not_over_all_time()
    {
        var timeline = new FrameTimeline(4);

        // A hundred, then four tens. A timeline that averaged everything ever seen would still be
        // reporting the spike long after it stopped mattering.
        timeline.Add(100f);
        for (var tick = 0; tick < 4; tick++)
            timeline.Add(10f);

        Assert.Equal(10f, timeline.Average(), 1e-5f);
        Assert.Equal(10f, timeline.Worst());
    }

    [Fact]
    public void the_worst_is_the_worst_still_in_the_window()
    {
        var timeline = new FrameTimeline(100);

        for (var tick = 0; tick < 50; tick++)
            timeline.Add(8f);

        timeline.Add(41f);

        Assert.Equal(41f, timeline.Worst());
        Assert.True(timeline.Average() < 9f, "one spike should barely move the average");
    }

    [Fact]
    public void the_percentile_finds_the_spike_the_average_hides()
    {
        var timeline = new FrameTimeline(100);

        // Ninety-nine frames at eight milliseconds and one at forty. The average calls this a
        // hundred and twenty frames a second; the player sees a hitch once a second.
        for (var tick = 0; tick < 99; tick++)
            timeline.Add(8f);

        timeline.Add(40f);

        Assert.True(timeline.Average() < 9f);
        Assert.Equal(8f, timeline.Percentile(0.5));
        Assert.Equal(40f, timeline.Percentile(1.0));
        Assert.Equal(40f, timeline.Percentile(0.995));
    }

    [Fact]
    public void the_percentile_is_clamped_rather_than_allowed_off_the_end()
    {
        var timeline = new FrameTimeline(8);

        for (var tick = 0; tick < 8; tick++)
            timeline.Add(tick);

        Assert.Equal(0f, timeline.Percentile(-3.0));
        Assert.Equal(7f, timeline.Percentile(12.0));
    }

    [Fact]
    public void clearing_puts_it_back_to_new()
    {
        var timeline = new FrameTimeline(4);
        timeline.Add(5f);
        timeline.Add(6f);

        timeline.Clear();

        Assert.Equal(0, timeline.Count);
        Assert.True(timeline.Window.IsEmpty);
    }

    [Fact]
    public void a_timeline_with_no_room_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameTimeline(0));
    }
}
