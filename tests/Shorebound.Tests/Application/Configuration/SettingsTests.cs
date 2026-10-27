using Shorebound.Application.Configuration;
using Shorebound.Infrastructure.Persistence;

namespace Shorebound.Tests.Application.Configuration;

/// <summary>
/// What a settings file promises: it survives a round trip, it survives being edited by hand, and
/// the command line beats it.
/// </summary>
/// <remarks>
/// The hand-editing claims matter more than the round trip. A settings file is the one file a
/// player opens in an editor, usually to undo something that made the game unusable, and a parser
/// that threw on a stray line would be a parser that locked them out.
/// </remarks>
public sealed class SettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        $"shorebound-settings-{Guid.NewGuid():N}.txt"
    );

    /// <summary>Removes the file this test wrote.</summary>
    public void Dispose() => File.Delete(_path);

    [Fact]
    public void settings_that_were_written_come_back_as_they_went_in()
    {
        var written = new Settings
        {
            ViewRadiusChunks = 24,
            FieldOfViewDegrees = 92f,
            LookSensitivity = 85f,
            Shadows = false,
            ShadowStrength = 0.4f,
            Fog = false,
            Brightness = 1.25f,
            UiScale = 1.5f,
            ShowOverlay = false,
            DayLengthSeconds = 300.0,
        };

        SettingsFile.Write(_path, written);

        Assert.Equal(written, SettingsFile.Read(_path));
    }

    [Fact]
    public void a_settings_file_that_is_not_there_gives_the_defaults()
    {
        Assert.Equal(Settings.Default, SettingsFile.Read(_path));
    }

    [Fact]
    public void the_debug_overlay_starts_hidden_by_default()
    {
        Assert.False(Settings.Default.ShowOverlay);
    }

    [Fact]
    public void view_distance_defaults_to_24_chunks_and_uses_the_supported_range()
    {
        Assert.Equal(24, Settings.Default.ViewRadiusChunks);
        Assert.Equal(10, Settings.MinimumViewRadiusChunks);
        Assert.Equal(256, Settings.MaximumViewRadiusChunks);
    }

    [Fact]
    public void an_unknown_line_is_skipped_and_the_rest_of_the_file_still_reads()
    {
        File.WriteAllLines(
            _path,
            ["# a comment", "brightness 1.4", "wobble 7", string.Empty, "fog off", "nonsense"]
        );

        var read = SettingsFile.Read(_path);

        Assert.Equal(1.4f, read.Brightness, 0.001f);
        Assert.False(read.Fog);
        Assert.Equal(Settings.Default.ViewRadiusChunks, read.ViewRadiusChunks);
    }

    [Fact]
    public void a_value_that_is_not_a_number_falls_back_to_that_settings_default()
    {
        File.WriteAllLines(_path, ["view-radius twelve", "brightness 1.1"]);

        var read = SettingsFile.Read(_path);

        Assert.Equal(Settings.Default.ViewRadiusChunks, read.ViewRadiusChunks);
        Assert.Equal(1.1f, read.Brightness, 0.001f);
    }

    [Fact]
    public void a_value_outside_what_the_interface_offers_is_brought_back_into_range()
    {
        File.WriteAllLines(_path, ["view-radius 4000", "field-of-view 1", "interface-scale 99"]);

        var read = SettingsFile.Read(_path);

        // An interface scale of ninety-nine would be a single letter filling the window, which is
        // the one setting a player cannot undo from inside the game.
        Assert.Equal(Settings.MaximumViewRadiusChunks, read.ViewRadiusChunks);
        Assert.InRange(read.FieldOfViewDegrees, 50f, 110f);
        Assert.InRange(read.UiScale, 0.75f, 2f);
    }

    [Fact]
    public void a_view_distance_below_the_minimum_is_clamped_to_10_chunks()
    {
        File.WriteAllLines(_path, ["view-radius 2"]);

        Assert.Equal(Settings.MinimumViewRadiusChunks, SettingsFile.Read(_path).ViewRadiusChunks);
    }

    [Fact]
    public void the_command_line_beats_the_settings_file_for_what_it_mentions()
    {
        var saved = Settings.Default with { ViewRadiusChunks = 16, DayLengthSeconds = 900.0 };
        var options = Options.Parse(["--view-radius", "14"]);

        var used = options.Override(saved);

        Assert.Equal(14, used.ViewRadiusChunks);
        Assert.Equal(900.0, used.DayLengthSeconds);
    }

    [Fact]
    public void a_command_line_that_mentions_nothing_leaves_every_setting_alone()
    {
        var saved = Settings.Default with { ViewRadiusChunks = 16, Brightness = 1.3f, ShowOverlay = false };

        var used = Options.Parse([]).Override(saved);

        Assert.Equal(saved, used);
    }

    [Fact]
    public void hiding_the_overlay_on_the_command_line_beats_a_setting_that_shows_it()
    {
        var saved = Settings.Default with { ShowOverlay = true };

        Assert.False(Options.Parse(["--no-overlay"]).Override(saved).ShowOverlay);
    }

    [Fact]
    public void sensitivity_is_the_default_turn_rate_at_the_middle_of_its_range()
    {
        // 50 is the number the slider sits at out of the box, and 0.0022 radians a pixel is what
        // the controller used before there was a slider at all.
        Assert.Equal(0.0022f, (Settings.Default with { LookSensitivity = 50f }).RadiansPerPixel, 1e-6f);
        Assert.Equal(0.0044f, (Settings.Default with { LookSensitivity = 100f }).RadiansPerPixel, 1e-6f);
    }
}