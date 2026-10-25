using System.Diagnostics;

namespace BrutalShore.Core;

/// <summary>Where a frame's time went, in whatever divisions the program cares about.</summary>
/// <remarks>
/// <para>
/// The engine knows what a frame is and nothing about what is in one. A voxel game wants to see
/// meshing, streaming and lighting; something else would want pathfinding or physics islands.
/// So the divisions are not stated here — a program opens a counter by name, keeps the handle,
/// and times whatever it likes into it. What the engine provides is the measuring, the rolling
/// window behind each counter and the guarantee that it costs nothing to leave switched on.
/// </para>
/// <para>
/// Three rules make it usable in a hot loop. A counter is a handle rather than a string, so
/// timing something is an array index and never a hash. A scope is a <c>ref struct</c>, so it
/// allocates nothing and cannot outlive its <c>using</c>. And a counter opened several times in
/// one frame <em>adds</em> — timing a method called eight times a frame gives the total it cost
/// that frame, which is the number anyone actually wants.
/// </para>
/// <para>
/// It is always on. Two timestamp reads is about twenty nanoseconds, and a profiler that has to
/// be switched on is a profiler that is off on the machine where the problem is happening.
/// </para>
/// </remarks>
public sealed class Profiler
{
    /// <summary>How many frames each counter keeps: four seconds of them at sixty a second.</summary>
    public const int WindowFrames = 240;

    private readonly List<string> _names = [];
    private readonly List<FrameTimeline> _timelines = [];
    private readonly Dictionary<string, Counter> _byName = [];
    private double[] _thisFrame = [];

    /// <summary>How many counters have been opened.</summary>
    public int Count => _names.Count;

    /// <summary>Opens a counter, or gives back the one already opened under that name.</summary>
    /// <param name="name">What it measures, as the overlay will show it.</param>
    /// <returns>A handle to time work into.</returns>
    /// <exception cref="ArgumentException">The name is empty.</exception>
    /// <remarks>
    /// Meant to be called once, at startup, and the handle kept. Calling it every frame works and
    /// costs a dictionary lookup, which is the thing the handle exists to avoid.
    /// </remarks>
    public Counter Open(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_byName.TryGetValue(name, out var existing))
            return existing;

        var counter = new Counter(_names.Count);
        _names.Add(name);
        _timelines.Add(new FrameTimeline(WindowFrames));
        _byName[name] = counter;

        if (_thisFrame.Length < _names.Count)
            Array.Resize(ref _thisFrame, int.Max(8, _names.Count * 2));

        return counter;
    }

    /// <summary>What a counter is called.</summary>
    /// <param name="counter">The counter to name.</param>
    /// <returns>The name it was opened under.</returns>
    public string NameOf(Counter counter) => _names[counter.Index];

    /// <summary>The window of frames behind a counter.</summary>
    /// <param name="counter">The counter to read.</param>
    /// <returns>Its timeline, in milliseconds a frame.</returns>
    public FrameTimeline TimelineOf(Counter counter) => _timelines[counter.Index];

    /// <summary>Every counter, in the order they were opened.</summary>
    /// <returns>Each counter's name and its timeline.</returns>
    /// <remarks>
    /// In the order they were opened rather than sorted, so that a program which opens them in
    /// the order the work happens gets an overlay that reads down the frame.
    /// </remarks>
    public IEnumerable<(string Name, FrameTimeline Timeline)> All()
    {
        for (var index = 0; index < _names.Count; index++)
            yield return (_names[index], _timelines[index]);
    }

    /// <summary>Times a block of work into a counter.</summary>
    /// <param name="counter">Where to add the time.</param>
    /// <returns>A scope that adds the elapsed time when it is disposed.</returns>
    /// <example>
    /// <code>
    /// using (profiler.Measure(_meshing))
    ///     MeshOneChunk();
    /// </code>
    /// </example>
    public Scope Measure(Counter counter) => new(this, counter.Index);

    /// <summary>Adds time to a counter directly, for work that was timed some other way.</summary>
    /// <param name="counter">Where to add it.</param>
    /// <param name="milliseconds">How long it took.</param>
    public void Add(Counter counter, double milliseconds) => _thisFrame[counter.Index] += milliseconds;

    /// <summary>Rolls this frame's totals into the windows and starts the next frame.</summary>
    /// <remarks>
    /// Called once a frame by whatever owns the frame. Until it is called, a counter opened
    /// several times is still accumulating, which is the whole reason the two are separate.
    /// </remarks>
    public void EndFrame()
    {
        for (var index = 0; index < _names.Count; index++)
        {
            _timelines[index].Add((float)_thisFrame[index]);
            _thisFrame[index] = 0.0;
        }
    }

    /// <summary>A handle to one counter.</summary>
    /// <remarks>
    /// An index rather than a name, so that timing something in a loop is an array write. It is
    /// only meaningful to the profiler that opened it.
    /// </remarks>
    public readonly record struct Counter
    {
        internal Counter(int index) => Index = index;

        /// <summary>Which counter this is, within its profiler.</summary>
        internal int Index { get; }
    }

    /// <summary>A block of work being timed.</summary>
    /// <remarks>
    /// A <c>ref struct</c> so that it cannot be put in a field or captured by a closure, and so
    /// cannot outlive the <c>using</c> that opened it. Timing therefore allocates nothing.
    /// </remarks>
    public readonly ref struct Scope
    {
        private readonly Profiler _profiler;
        private readonly int _index;
        private readonly long _start;

        internal Scope(Profiler profiler, int index)
        {
            _profiler = profiler;
            _index = index;
            _start = Stopwatch.GetTimestamp();
        }

        /// <summary>Adds how long the block took to this frame's total for the counter.</summary>
        public void Dispose() =>
            _profiler._thisFrame[_index] += Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
    }
}
