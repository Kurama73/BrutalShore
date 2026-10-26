using System.Globalization;

namespace BrutalShore.Runtime.Tests;

/// <summary>What a run's report has to say, whoever reads it and wherever it was produced.</summary>
/// <remarks>
/// The layout is not asserted — column widths are free to change. What is asserted is that the
/// numbers are the whole run rather than the window, that a game's own counters appear beside the
/// host's phases, and that the text is the same on a machine whose decimal separator is a comma.
/// </remarks>
public sealed class RunReportTests
{
    [Fact]
    public void a_report_counts_every_frame_the_run_had_rather_than_the_window()
    {
        var profile = new FrameProfile();
        var frames = FrameProfile.WindowFrames * 3;

        for (var frame = 0; frame < frames; frame++)
            profile.Frame.Add(10f);

        Assert.Contains($"{frames} frames", RunReport.Of("run", profile), StringComparison.Ordinal);
    }

    [Fact]
    public void a_report_remembers_a_hitch_the_window_has_forgotten()
    {
        var profile = new FrameProfile();

        profile.Frame.Add(900f);
        for (var frame = 0; frame < FrameProfile.WindowFrames * 2; frame++)
            profile.Frame.Add(10f);

        // The point of the whole exercise: the one frame worth knowing about is the one a
        // four-second window has thrown away by the time anybody reads the report.
        Assert.Equal(10f, profile.Frame.Worst());
        Assert.Contains("900.000", RunReport.Of("run", profile), StringComparison.Ordinal);
    }

    [Fact]
    public void every_phase_of_a_frame_is_named_in_the_report()
    {
        var profile = new FrameProfile();
        profile.Frame.Add(10f);

        var report = RunReport.Of("run", profile);

        foreach (var phase in new[] { "frame", "simulation", "streaming", "recording", "presenting" })
            Assert.Contains(phase, report, StringComparison.Ordinal);
    }

    [Fact]
    public void a_game_s_own_counters_are_reported_beside_the_host_s_phases()
    {
        var profile = new FrameProfile();
        var machines = profile.Counters.Open("machines");

        profile.Counters.Add(machines, 0.4);
        profile.Counters.EndFrame();
        profile.Frame.Add(10f);

        // The engine cannot know what a game measures, so this is the proof that it does not
        // have to: whatever was opened turns up in the table.
        Assert.Contains("machines", RunReport.Of("run", profile), StringComparison.Ordinal);
    }

    [Fact]
    public void a_phase_s_share_of_the_frame_is_what_it_took_out_of_it()
    {
        var profile = new FrameProfile();

        for (var frame = 0; frame < 10; frame++)
        {
            profile.Frame.Add(20f);
            profile.Presenting.Add(5f);
        }

        Assert.Contains("25.0%", RunReport.Of("run", profile), StringComparison.Ordinal);
    }

    [Fact]
    public void a_report_reads_the_same_wherever_it_was_produced()
    {
        var profile = new FrameProfile();
        profile.Frame.Add(12.5f);

        var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";

        // Two reports are compared against each other far more often than either is read alone,
        // and a decimal comma in one of them would make that diff useless. The repository builds
        // with InvariantGlobalization, so the separator is changed by hand rather than by asking
        // for a locale: this asserts that the formatting says which culture it wants, instead of
        // relying on a build setting that a localised game would have to turn off.
        Assert.Equal(Produce(profile, CultureInfo.InvariantCulture), Produce(profile, comma));
    }

    [Fact]
    public void a_report_of_a_run_that_never_drew_anything_still_reads_as_a_report()
    {
        var report = RunReport.Of("run", new FrameProfile());

        Assert.Contains("0 frames", report, StringComparison.Ordinal);
        Assert.DoesNotContain("NaN", report, StringComparison.Ordinal);
        Assert.DoesNotContain("∞", report, StringComparison.Ordinal);
    }

    [Fact]
    public void the_game_has_the_last_word()
    {
        var profile = new FrameProfile();
        profile.Frame.Add(10f);

        Assert.EndsWith(
            "968 chunks loaded." + Environment.NewLine,
            RunReport.Of("run", profile, "968 chunks loaded."),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void a_report_can_be_left_behind_in_a_file()
    {
        var profile = new FrameProfile();
        profile.Frame.Add(10f);
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            RunReport.WriteTo(path, "run", profile);

            Assert.Equal(RunReport.Of("run", profile), File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Produce(FrameProfile profile, CultureInfo culture)
    {
        var was = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            return RunReport.Of("run", profile);
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }
}
