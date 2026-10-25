using System.Diagnostics;
using BrutalShore.Core;

namespace BrutalShore.Runtime;

/// <summary>Where a frame's time went, kept for the last few seconds of them.</summary>
/// <remarks>
/// <para>
/// The phases are the host's own division of a frame, and they are deliberately coarse: what a
/// number like this is for is deciding which half of the engine to go and look at, not telling
/// anyone what to do when they get there. Anything finer belongs to whatever is being
/// investigated, and should go away again when it is done.
/// </para>
/// <para>
/// Measuring costs two timestamp reads per phase per frame — about twenty nanoseconds — so it is
/// always on. A profiler that has to be switched on is one that is off on the machine where the
/// problem happens.
/// </para>
/// </remarks>
public sealed class FrameProfile
{
    /// <summary>How many frames are kept: four seconds of them at sixty a second.</summary>
    public const int WindowFrames = 240;

    /// <summary>The whole frame, wall clock to wall clock.</summary>
    public FrameTimeline Frame { get; } = new(WindowFrames);

    /// <summary>The fixed simulation steps the frame owed.</summary>
    public FrameTimeline Simulation { get; } = new(WindowFrames);

    /// <summary>The game's once-a-frame work: streaming, meshing, uploads.</summary>
    public FrameTimeline Streaming { get; } = new(WindowFrames);

    /// <summary>Building the frame's draw commands, including the interface.</summary>
    public FrameTimeline Recording { get; } = new(WindowFrames);

    /// <summary>Submitting the frame and waiting for a slot to present it in.</summary>
    /// <remarks>
    /// The one phase a game cannot make shorter by doing less. When it dominates, the frame is
    /// waiting on the GPU or on the display's refresh, and the work to cut is in a shader.
    /// </remarks>
    public FrameTimeline Presenting { get; } = new(WindowFrames);

    /// <summary>Times a block of work into one of the timelines.</summary>
    /// <param name="into">Where to record how long it took.</param>
    /// <returns>A scope that records the elapsed time when it is disposed.</returns>
    /// <example>
    /// <code>
    /// using (profile.Measure(profile.Streaming))
    ///     game.Update(context);
    /// </code>
    /// </example>
    public static Scope Measure(FrameTimeline into) => new(into);

    /// <summary>A block of work being timed.</summary>
    /// <remarks>
    /// A <c>ref struct</c> so that it cannot be put in a field or a closure and so cannot outlive
    /// the <c>using</c> that opened it — and so that timing a phase allocates nothing at all.
    /// </remarks>
    public readonly ref struct Scope
    {
        private readonly FrameTimeline _into;
        private readonly long _start;

        internal Scope(FrameTimeline into)
        {
            _into = into;
            _start = Stopwatch.GetTimestamp();
        }

        /// <summary>Records how long the block took, in milliseconds.</summary>
        public void Dispose() => _into.Add((float)Stopwatch.GetElapsedTime(_start).TotalMilliseconds);
    }
}
