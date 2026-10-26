namespace BrutalShore.Core.Tests;

/// <summary>What a run's measurements must still be able to say once they are in buckets.</summary>
/// <remarks>
/// The claims worth pinning down are the ones a report rests on: nothing is lost, a percentile is
/// never optimistic, and the resolution is the one the type says it is. How the buckets are laid
/// out is not asserted beyond that — it is free to change as long as these hold.
/// </remarks>
public sealed class HistogramTests
{
    [Fact]
    public void an_empty_histogram_answers_zero_to_everything()
    {
        var histogram = new Histogram();

        Assert.Equal(0, histogram.Count);
        Assert.Equal(0.0, histogram.TotalMilliseconds);
        Assert.Equal(0.0, histogram.WorstMilliseconds);
        Assert.Equal(0.0, histogram.MeanMilliseconds);
        Assert.Equal(0.0, histogram.Percentile(0.99));
    }

    [Fact]
    public void every_sample_is_counted_whatever_its_size()
    {
        var histogram = new Histogram();

        foreach (var sample in new[] { 0.0, 0.0001, 0.5, 17.0, 4000.0 })
            histogram.Add(sample);

        Assert.Equal(5, histogram.Count);
    }

    [Fact]
    public void the_total_and_the_worst_are_exact_though_the_buckets_are_not()
    {
        var histogram = new Histogram();

        histogram.Add(1.25);
        histogram.Add(3.75);

        // Both are kept beside the buckets precisely so that bucketing cannot blunt them.
        Assert.Equal(5.0, histogram.TotalMilliseconds, 1e-12);
        Assert.Equal(3.75, histogram.WorstMilliseconds, 1e-12);
        Assert.Equal(2.5, histogram.MeanMilliseconds, 1e-12);
    }

    [Fact]
    public void the_hundredth_percentile_is_the_worst_sample_exactly()
    {
        var histogram = new Histogram();

        foreach (var sample in new[] { 0.3, 11.1, 2.2 })
            histogram.Add(sample);

        Assert.Equal(11.1, histogram.Percentile(1.0), 1e-12);
    }

    [Fact]
    public void a_percentile_is_never_optimistic()
    {
        var histogram = new Histogram();
        var samples = Enumerable.Range(1, 1000).Select(step => step * 0.037).ToArray();

        foreach (var sample in samples)
            histogram.Add(sample);

        // Whatever comes back, that fraction of the run really did come in at or under it.
        foreach (var fraction in new[] { 0.5, 0.9, 0.99 })
        {
            var answer = histogram.Percentile(fraction);
            var under = samples.Count(sample => sample <= answer);

            Assert.True(
                under >= fraction * samples.Length,
                $"the {fraction:P0} said {answer} ms, which only {under} of {samples.Length} samples met"
            );
        }
    }

    [Fact]
    public void a_percentile_is_within_the_resolution_the_buckets_promise()
    {
        var histogram = new Histogram();

        // A thousand identical samples: the median is that sample, to within one bucket.
        for (var sample = 0; sample < 1000; sample++)
            histogram.Add(2.37);

        // Twenty buckets to a decade is a ratio of 10^(1/20), so 12.2 per cent at the worst.
        Assert.InRange(histogram.Percentile(0.5), 2.37, 2.37 * 1.123);
    }

    [Fact]
    public void a_sample_past_the_top_of_the_range_is_still_reported_in_full()
    {
        var histogram = new Histogram();

        histogram.Add(0.5);
        histogram.Add(8_000.0);

        // The buckets stop at a second; a stall longer than that must not come back shortened.
        Assert.Equal(8_000.0, histogram.WorstMilliseconds, 1e-9);
        Assert.Equal(8_000.0, histogram.Percentile(1.0), 1e-9);
    }

    [Fact]
    public void a_counter_that_does_nothing_reports_nothing_rather_than_the_bottom_of_the_range()
    {
        var histogram = new Histogram();

        for (var frame = 0; frame < 100; frame++)
            histogram.Add(0.0);

        Assert.Equal(0.0, histogram.Percentile(0.5));
    }

    [Fact]
    public void zero_is_told_apart_from_the_smallest_measurement()
    {
        Assert.Equal(0, Histogram.BucketOf(0.0));
        Assert.NotEqual(0, Histogram.BucketOf(Histogram.SmallestMilliseconds));
    }

    [Fact]
    public void a_bucket_holds_the_samples_at_or_below_its_top_edge()
    {
        for (var bucket = 1; bucket < Histogram.BucketCount - 1; bucket++)
        {
            var edge = Histogram.UpperBoundOf(bucket);

            Assert.Equal(bucket, Histogram.BucketOf(edge));
            Assert.True(Histogram.BucketOf(edge * 1.001) > bucket, $"bucket {bucket} swallowed a larger sample");
        }
    }

    [Fact]
    public void the_buckets_only_ever_grow_wider()
    {
        for (var bucket = 2; bucket < Histogram.BucketCount; bucket++)
            Assert.True(
                Histogram.UpperBoundOf(bucket) > Histogram.UpperBoundOf(bucket - 1),
                $"bucket {bucket} does not come after the one before it"
            );
    }

    [Fact]
    public void a_negative_measurement_counts_as_no_time_at_all()
    {
        var histogram = new Histogram();

        histogram.Add(-5.0);

        // A clock that goes backwards is a measurement error, not a negative frame.
        Assert.Equal(1, histogram.Count);
        Assert.Equal(0.0, histogram.TotalMilliseconds);
    }

    [Fact]
    public void clearing_forgets_the_run()
    {
        var histogram = new Histogram();
        histogram.Add(4.0);

        histogram.Clear();

        Assert.Equal(0, histogram.Count);
        Assert.Equal(0.0, histogram.WorstMilliseconds);
    }

    [Fact]
    public void there_is_no_bucket_outside_the_range()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Histogram.UpperBoundOf(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Histogram.UpperBoundOf(Histogram.BucketCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Histogram().CountIn(Histogram.BucketCount));
    }

    [Fact]
    public void a_timeline_keeps_the_whole_run_as_well_as_the_window()
    {
        var timeline = new FrameTimeline(4);

        for (var frame = 0; frame < 40; frame++)
            timeline.Add(frame == 7 ? 50f : 1f);

        // The window is long past the hitch; the run still remembers it.
        Assert.Equal(4, timeline.Window.Length);
        Assert.Equal(1f, timeline.Worst());
        Assert.Equal(40, timeline.WholeRun.Count);
        Assert.Equal(50.0, timeline.WholeRun.WorstMilliseconds, 1e-6);
    }
}
