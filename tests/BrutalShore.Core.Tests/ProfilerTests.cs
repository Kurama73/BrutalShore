namespace BrutalShore.Core.Tests;

/// <summary>
/// The profiler as a thing a program extends: that a counter is the same counter twice, that
/// timing the same thing repeatedly in a frame adds up, and that a frame's totals are a frame's.
/// </summary>
/// <remarks>
/// The adding is the claim that matters and it is the one every naive profiler gets wrong. Work
/// that happens eight times a frame — meshing a chunk, stepping a machine — is work whose cost
/// anyone asking about it wants as one number per frame, not eight overlapping samples of which
/// the window keeps the last.
/// </remarks>
public sealed class ProfilerTests
{
    [Fact]
    public void a_fresh_profiler_has_no_counters()
    {
        Assert.Equal(0, new Profiler().Count);
    }

    [Fact]
    public void opening_the_same_name_twice_gives_the_same_counter()
    {
        // Because a program that opens one in two places must not end up with two rows in the
        // overlay measuring halves of the same thing.
        var profiler = new Profiler();

        var first = profiler.Open("meshing");
        var second = profiler.Open("meshing");

        Assert.Equal(first, second);
        Assert.Equal(1, profiler.Count);
    }

    [Fact]
    public void a_counter_with_no_name_is_refused()
    {
        var profiler = new Profiler();

        Assert.Throws<ArgumentException>(() => profiler.Open(string.Empty));
        Assert.Throws<ArgumentException>(() => profiler.Open("   "));
    }

    [Fact]
    public void a_counter_remembers_what_it_was_called()
    {
        var profiler = new Profiler();
        var counter = profiler.Open("streaming");

        Assert.Equal("streaming", profiler.NameOf(counter));
    }

    [Fact]
    public void counters_come_back_in_the_order_they_were_opened()
    {
        // So a program that opens them in the order the work happens gets an overlay that reads
        // down the frame rather than alphabetically.
        var profiler = new Profiler();
        profiler.Open("first");
        profiler.Open("second");
        profiler.Open("third");

        Assert.Equal(["first", "second", "third"], profiler.All().Select(counter => counter.Name));
    }

    [Fact]
    public void nothing_is_recorded_until_the_frame_ends()
    {
        var profiler = new Profiler();
        var counter = profiler.Open("work");

        profiler.Add(counter, 5.0);

        Assert.Equal(0, profiler.TimelineOf(counter).Count);
    }

    [Fact]
    public void several_measurements_in_one_frame_add_up()
    {
        // The claim the whole shape of this type exists for.
        var profiler = new Profiler();
        var counter = profiler.Open("meshing");

        for (var chunk = 0; chunk < 8; chunk++)
            profiler.Add(counter, 1.5);

        profiler.EndFrame();

        Assert.Equal(12f, profiler.TimelineOf(counter).Average(), 1e-3f);
    }

    [Fact]
    public void each_frame_starts_again_from_nothing()
    {
        var profiler = new Profiler();
        var counter = profiler.Open("work");

        profiler.Add(counter, 10.0);
        profiler.EndFrame();
        profiler.EndFrame();

        Assert.Equal(2, profiler.TimelineOf(counter).Count);
        Assert.Equal(5f, profiler.TimelineOf(counter).Average(), 1e-3f);
    }

    [Fact]
    public void a_counter_opened_late_does_not_disturb_the_others()
    {
        // A program may open a counter the first time the work it measures happens, which can be
        // minutes in. The ones already running must not lose their windows to the resize.
        var profiler = new Profiler();
        var early = profiler.Open("early");

        profiler.Add(early, 4.0);
        profiler.EndFrame();

        for (var extra = 0; extra < 20; extra++)
            profiler.Open($"later {extra}");

        profiler.Add(early, 2.0);
        profiler.EndFrame();

        Assert.Equal(2, profiler.TimelineOf(early).Count);
        Assert.Equal(3f, profiler.TimelineOf(early).Average(), 1e-3f);
    }

    [Fact]
    public void a_scope_records_roughly_how_long_it_took()
    {
        var profiler = new Profiler();
        var counter = profiler.Open("work");

        using (profiler.Measure(counter))
        {
            // Busy rather than asleep, because a sleep is a claim about the scheduler and this
            // is a claim about the clock.
            var until = System.Diagnostics.Stopwatch.GetTimestamp()
                + (System.Diagnostics.Stopwatch.Frequency / 200);

            while (System.Diagnostics.Stopwatch.GetTimestamp() < until)
            {
                // Spin.
            }
        }

        profiler.EndFrame();

        // Five milliseconds asked for; anything in the same order is the clock working.
        Assert.InRange(profiler.TimelineOf(counter).Average(), 1f, 100f);
    }

    [Fact]
    public void scopes_can_be_nested_without_either_losing_its_time()
    {
        // An outer counter measuring a whole phase and an inner one measuring part of it is the
        // normal way to use this, and the inner must not be subtracted from the outer.
        var profiler = new Profiler();
        var outer = profiler.Open("outer");
        var inner = profiler.Open("inner");

        using (profiler.Measure(outer))
        {
            profiler.Add(inner, 3.0);
            profiler.Add(outer, 0.0);
        }

        profiler.EndFrame();

        Assert.Equal(3f, profiler.TimelineOf(inner).Average(), 1e-3f);
        Assert.True(profiler.TimelineOf(outer).Count == 1);
    }

    [Fact]
    public void timing_something_allocates_nothing()
    {
        // The property that lets this be left switched on. A profiler that allocates is one that
        // changes the thing it is measuring.
        var profiler = new Profiler();
        var counter = profiler.Open("work");

        // Warm up, so that what is measured is the steady state rather than the first call.
        for (var warm = 0; warm < 100; warm++)
        {
            using (profiler.Measure(counter))
            {
                // Nothing.
            }
        }

        profiler.EndFrame();
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var round = 0; round < 10_000; round++)
        {
            using (profiler.Measure(counter))
            {
                // Nothing.
            }
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
