namespace BrutalShore.Core;

/// <summary>Every measurement a run ever made, kept in buckets rather than one by one.</summary>
/// <remarks>
/// <para>
/// A <see cref="FrameTimeline" /> answers questions about the last few seconds, which is what an
/// overlay needs and what a report cannot use: a run that played for ninety seconds and reports
/// the last four of them has reported nothing. Keeping all ninety seconds of samples instead
/// would be a growing array, and the one question worth asking of them — what the worst one in a
/// hundred cost — would mean sorting it.
/// </para>
/// <para>
/// So samples are counted into buckets of increasing width, twenty to a decade, and a percentile
/// is one walk over a hundred and twenty counters. The cost is resolution: a bucket spans a
/// factor of 10^(1/20), so a percentile read back is within about twelve per cent of the sample
/// it names. That is the right trade for a report, where the question is whether something costs
/// a tenth of a millisecond or five, and never whether it costs 2.00 or 2.05.
/// </para>
/// <para>
/// Zero is counted on its own rather than in the first bucket, so that a counter which does
/// nothing most frames reports nothing rather than the bottom of the range. The worst sample is
/// kept exactly, because it is the number a hitch is diagnosed from and it must not be a bucket
/// edge rounded up.
/// </para>
/// </remarks>
public sealed class Histogram
{
    /// <summary>The smallest measurement told apart from zero, in milliseconds.</summary>
    /// <remarks>
    /// A microsecond. Below it there is nothing a frame-timing measurement can honestly claim,
    /// since reading the clock twice already costs about twenty nanoseconds.
    /// </remarks>
    public const double SmallestMilliseconds = 0.001;

    /// <summary>How many buckets each factor of ten is divided into.</summary>
    public const int BucketsPerDecade = 20;

    /// <summary>How many factors of ten the buckets span, from <see cref="SmallestMilliseconds" />.</summary>
    /// <remarks>Six: a microsecond to a second, which covers everything from a hashed lookup to a stall.</remarks>
    public const int Decades = 6;

    /// <summary>How many buckets there are, counting the one that holds zero.</summary>
    public const int BucketCount = Decades * BucketsPerDecade + 2;

    private readonly long[] _counts = new long[BucketCount];

    /// <summary>How many samples have been added.</summary>
    public long Count { get; private set; }

    /// <summary>Everything added together, in milliseconds.</summary>
    /// <remarks>
    /// Summed from the samples themselves rather than from the buckets, so a total is exact even
    /// though a percentile is not. It is what answers "how much of the run went here".
    /// </remarks>
    public double TotalMilliseconds { get; private set; }

    /// <summary>The largest sample, exactly as it was added.</summary>
    public double WorstMilliseconds { get; private set; }

    /// <summary>The mean sample, or zero when nothing has been added.</summary>
    public double MeanMilliseconds => Count == 0 ? 0.0 : TotalMilliseconds / Count;

    /// <summary>Counts a measurement.</summary>
    /// <param name="milliseconds">How long something took. Negative counts as zero.</param>
    /// <remarks>
    /// One logarithm per sample. Called once a frame per counter, which is nowhere near often
    /// enough for that to matter, and the alternative — picking the bucket apart from the
    /// exponent of the float — would trade a readable line for nanoseconds nobody is short of.
    /// </remarks>
    public void Add(double milliseconds)
    {
        var value = double.IsNaN(milliseconds) ? 0.0 : double.Max(milliseconds, 0.0);

        _counts[BucketOf(value)]++;
        Count++;
        TotalMilliseconds += value;
        WorstMilliseconds = double.Max(WorstMilliseconds, value);
    }

    /// <summary>The sample a given fraction of the run came in at or under.</summary>
    /// <param name="fraction">Where to look, from 0 for the best to 1 for the worst.</param>
    /// <returns>An upper bound on that sample, or zero when nothing has been added.</returns>
    /// <remarks>
    /// The bucket's top edge rather than its middle, so the answer is never optimistic: whatever
    /// comes back, that fraction of the run really did come in under it. It is capped at
    /// <see cref="WorstMilliseconds" />, so the hundredth percentile is the worst sample exactly
    /// rather than the edge above it.
    /// </remarks>
    public double Percentile(double fraction)
    {
        if (Count == 0)
            return 0.0;

        var rank = (long)double.Ceiling(double.Clamp(fraction, 0.0, 1.0) * Count);
        var seen = 0L;

        for (var bucket = 0; bucket < BucketCount; bucket++)
        {
            seen += _counts[bucket];
            if (seen < long.Max(rank, 1L))
                continue;

            // The last bucket has no top edge — it is where everything past the range went — so
            // the only honest answer for it is the worst sample, which is kept exactly.
            return bucket == BucketCount - 1
                ? WorstMilliseconds
                : double.Min(UpperBoundOf(bucket), WorstMilliseconds);
        }

        return WorstMilliseconds;
    }

    /// <summary>How many samples landed in one bucket.</summary>
    /// <param name="bucket">Which bucket, from 0 to <see cref="BucketCount" /> less one.</param>
    /// <returns>The number of samples it holds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">There is no such bucket.</exception>
    public long CountIn(int bucket) =>
        bucket >= 0 && bucket < BucketCount
            ? _counts[bucket]
            : throw new ArgumentOutOfRangeException(nameof(bucket), bucket, "There is no such bucket.");

    /// <summary>Forgets every sample.</summary>
    public void Clear()
    {
        Array.Clear(_counts);
        Count = 0;
        TotalMilliseconds = 0.0;
        WorstMilliseconds = 0.0;
    }

    /// <summary>Which bucket a measurement is counted in.</summary>
    /// <param name="milliseconds">The measurement, zero or more.</param>
    /// <returns>Its bucket.</returns>
    /// <remarks>
    /// Rounded up, so that a bucket holds the samples at or below its top edge and nothing above
    /// it. Anything past the top of the range lands in the last bucket; the total and the worst
    /// are kept apart from the buckets so that nothing is lost when it does.
    /// </remarks>
    public static int BucketOf(double milliseconds)
    {
        if (milliseconds <= 0.0)
            return 0;

        var decades = double.Log10(milliseconds / SmallestMilliseconds);

        // Rounding up puts a sample sitting exactly on an edge into the next bucket whenever the
        // logarithm of that edge comes back a hair over the whole number it should be, which it
        // does for most of them. The slack is a billionth of a decade: far below the resolution
        // the buckets claim, and far above the error that reintroduces the problem.
        var bucket = 1 + (int)double.Ceiling(decades * BucketsPerDecade - 1e-9);

        return int.Clamp(bucket, 1, BucketCount - 1);
    }

    /// <summary>The largest measurement a bucket holds.</summary>
    /// <param name="bucket">Which bucket, from 0 to <see cref="BucketCount" /> less one.</param>
    /// <returns>Its top edge, in milliseconds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">There is no such bucket.</exception>
    public static double UpperBoundOf(int bucket)
    {
        if (bucket < 0 || bucket >= BucketCount)
            throw new ArgumentOutOfRangeException(nameof(bucket), bucket, "There is no such bucket.");

        return bucket == 0 ? 0.0 : SmallestMilliseconds * double.Pow(10.0, (bucket - 1.0) / BucketsPerDecade);
    }
}
