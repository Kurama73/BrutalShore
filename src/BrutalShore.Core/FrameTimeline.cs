namespace BrutalShore.Core;

/// <summary>The last few seconds of some per-frame measurement, kept for reading back.</summary>
/// <remarks>
/// <para>
/// A frame rate is the wrong number to tune against. It is an average, and the thing a player
/// notices is not the average but the handful of frames that took four times as long — so what
/// this keeps is every sample in a window, and what it answers are the questions that find those:
/// the worst one, and the one a given fraction of frames came in under.
/// </para>
/// <para>
/// The samples are held twice, in a buffer of double the length, with every write going to both
/// copies. That is what lets the window be handed out as one contiguous span in the right order
/// at any moment, which is what a plot wants, without shuffling an array every frame.
/// </para>
/// <para>
/// Measurements are <c>float</c> rather than <c>double</c> because this is display data and
/// nothing is integrated over it — and because a plotting library wants floats.
/// </para>
/// </remarks>
public sealed class FrameTimeline
{
    private readonly float[] _samples;
    private readonly float[] _sorted;
    private int _next;

    /// <summary>Creates an empty timeline.</summary>
    /// <param name="capacity">How many samples the window holds.</param>
    /// <exception cref="ArgumentOutOfRangeException">The capacity is not positive.</exception>
    public FrameTimeline(int capacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "A timeline needs room for a sample.");

        Capacity = capacity;
        _samples = new float[capacity * 2];
        _sorted = new float[capacity];
    }

    /// <summary>How many samples the window holds.</summary>
    public int Capacity { get; }

    /// <summary>How many samples have ever been added.</summary>
    public long Count { get; private set; }

    /// <summary>The most recent sample, or zero if there has not been one.</summary>
    public float Latest => Count == 0 ? 0f : _samples[(_next + Capacity - 1) % Capacity];

    /// <summary>
    /// The window, oldest first. Shorter than <see cref="Capacity" /> until that many samples
    /// have been added.
    /// </summary>
    public ReadOnlySpan<float> Window =>
        Count >= Capacity
            ? _samples.AsSpan(_next, Capacity)
            : _samples.AsSpan(0, (int)Count);

    /// <summary>Adds a sample, pushing out the oldest if the window is full.</summary>
    /// <param name="value">The measurement.</param>
    public void Add(float value)
    {
        _samples[_next] = value;
        _samples[_next + Capacity] = value;
        _next = (_next + 1) % Capacity;
        Count++;
    }

    /// <summary>The mean of the window.</summary>
    /// <returns>Zero when nothing has been added.</returns>
    public float Average()
    {
        var window = Window;
        if (window.IsEmpty)
            return 0f;

        var total = 0.0;
        foreach (var sample in window)
            total += sample;

        return (float)(total / window.Length);
    }

    /// <summary>The largest sample in the window.</summary>
    /// <returns>Zero when nothing has been added.</returns>
    public float Worst()
    {
        var worst = 0f;
        foreach (var sample in Window)
            worst = float.Max(worst, sample);

        return worst;
    }

    /// <summary>The sample a given fraction of the window comes in at or under.</summary>
    /// <param name="fraction">Where to look, from 0 for the best to 1 for the worst.</param>
    /// <returns>Zero when nothing has been added.</returns>
    /// <remarks>
    /// <para>
    /// The number to watch is the high end. A ninety-ninth percentile frame time of forty
    /// milliseconds against an average of eight means one frame in a hundred is a visible hitch,
    /// and the average alone would have called that a hundred and twenty frames a second.
    /// </para>
    /// <para>
    /// Sorting happens into a buffer kept for the purpose, so asking every frame costs no
    /// allocation. It does cost a sort of the whole window, which is why it is a method rather
    /// than a property: it is not free and the name should not suggest it is.
    /// </para>
    /// </remarks>
    public float Percentile(double fraction)
    {
        var window = Window;
        if (window.IsEmpty)
            return 0f;

        var sorted = _sorted.AsSpan(0, window.Length);
        window.CopyTo(sorted);
        sorted.Sort();

        var index = (int)double.Round(double.Clamp(fraction, 0.0, 1.0) * (sorted.Length - 1));
        return sorted[index];
    }

    /// <summary>Forgets every sample.</summary>
    public void Clear()
    {
        Array.Clear(_samples);
        _next = 0;
        Count = 0;
    }
}
