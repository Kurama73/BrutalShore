using System.Globalization;
using System.Text;
using BrutalShore.Core;

namespace BrutalShore.Runtime;

/// <summary>What a run cost, as a table meant to be read after the window has closed.</summary>
/// <remarks>
/// <para>
/// The overlay answers "what is slow right now", which needs a person watching it. This answers
/// "what was slow", which does not — and that is the difference between a measurement anyone can
/// take and one that only happens when someone remembers to look. A scripted session with a
/// report is a performance test: the same inputs, the same world, and numbers at the end that can
/// be put beside yesterday's.
/// </para>
/// <para>
/// Every column comes from <see cref="FrameTimeline.WholeRun" /> rather than the window, so a
/// ninety-second run is reported over ninety seconds. The host's phases come first, in the order
/// a frame does them, and then whatever counters the game opened — which is the only part of the
/// table the engine cannot predict, and the reason the shape is a list of rows rather than a
/// fixed set of fields.
/// </para>
/// <para>
/// Numbers are formatted invariantly on purpose. A report is compared against another report,
/// often one produced on a different machine, and a decimal comma on one of them would make the
/// diff unreadable.
/// </para>
/// </remarks>
public static class RunReport
{
    /// <summary>Builds the report.</summary>
    /// <param name="title">What the run was, as its first line.</param>
    /// <param name="profile">The phases and counters the run measured.</param>
    /// <param name="summary">A closing line from the game, or nothing.</param>
    /// <returns>The whole report, ending in a newline.</returns>
    public static string Of(string title, FrameProfile profile, string? summary = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var frames = profile.Frame.WholeRun;
        var seconds = frames.TotalMilliseconds / 1000.0;
        var report = new StringBuilder();

        report.Append(
            Line(
                "{0}: {1} frames in {2:F1} s, {3:F1} fps on average",
                title,
                frames.Count,
                seconds,
                frames.Count == 0 ? 0.0 : frames.Count / double.Max(seconds, double.Epsilon)
            )
        );

        report.AppendLine();
        report.Append(Line("{0,-18}{1,7}{2,9}{3,9}{4,9}{5,9}", "", "share", "mean", "p50", "p99", "worst"));

        foreach (var (name, timeline) in Rows(profile))
            report.Append(Row(name, timeline.WholeRun, frames.TotalMilliseconds));

        if (summary is not null)
        {
            report.AppendLine();
            report.Append(Line("{0}", summary));
        }

        return report.ToString();
    }

    /// <summary>Writes the report to a file.</summary>
    /// <param name="path">Where to write it.</param>
    /// <param name="title">What the run was.</param>
    /// <param name="profile">The phases and counters the run measured.</param>
    /// <param name="summary">A closing line from the game, or nothing.</param>
    public static void WriteTo(string path, string title, FrameProfile profile, string? summary = null) =>
        File.WriteAllText(path, Of(title, profile, summary));

    /// <summary>The rows of the table, in the order they are printed.</summary>
    /// <param name="profile">The phases and counters the run measured.</param>
    /// <returns>Each row's name and the timeline behind it.</returns>
    /// <remarks>
    /// The host's phases are indented under the whole frame they divide, which is what makes the
    /// share column add up to something a reader can check: the five of them should come to a
    /// hundred per cent, and a game's counters are inside them rather than beside them.
    /// </remarks>
    private static IEnumerable<(string Name, FrameTimeline Timeline)> Rows(FrameProfile profile)
    {
        yield return ("frame", profile.Frame);
        yield return ("  simulation", profile.Simulation);
        yield return ("  streaming", profile.Streaming);
        yield return ("  recording", profile.Recording);
        yield return ("  presenting", profile.Presenting);

        foreach (var (name, timeline) in profile.Counters.All())
            yield return ("  " + name, timeline);
    }

    private static string Row(string name, Histogram run, double frameTotalMilliseconds) =>
        Line(
            "{0,-18}{1,6:F1}%{2,9:F3}{3,9:F3}{4,9:F3}{5,9:F3}",
            name,
            frameTotalMilliseconds <= 0.0 ? 0.0 : 100.0 * run.TotalMilliseconds / frameTotalMilliseconds,
            run.MeanMilliseconds,
            run.Percentile(0.5),
            run.Percentile(0.99),
            run.WorstMilliseconds
        );

    private static string Line(string format, params object?[] values) =>
        string.Format(CultureInfo.InvariantCulture, format, values) + Environment.NewLine;
}
