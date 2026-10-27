using System.Globalization;
using Shorebound.Application.Configuration;

namespace Shorebound.Infrastructure.Persistence;

/// <summary>Reading and writing the player's settings as a text file beside the game.</summary>
/// <remarks>
/// <para>
/// One <c>name value</c> per line, and nothing else. A settings file is the one file a player
/// might open in an editor to fix something they cannot reach — a window scale that made the
/// interface too large to click — so it is plain text, and every name in it is the name the
/// interface shows.
/// </para>
/// <para>
/// Unknown names are skipped and malformed values fall back to the default for that one setting.
/// A file from a newer version, or one with a typo in it, has to keep the game startable: losing
/// one line is a far better failure than refusing to open.
/// </para>
/// </remarks>
public static class SettingsFile
{
    /// <summary>Where settings live unless the command line says otherwise.</summary>
    public const string DefaultPath = "shorebound.settings";

    /// <summary>Reads the settings, falling back to the defaults for anything missing.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The settings, clamped to the ranges the interface offers.</returns>
    public static Settings Read(string path)
    {
        if (!File.Exists(path))
            return Settings.Default;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.AsSpan().Trim();
            if (trimmed.IsEmpty || trimmed[0] == '#')
                continue;

            var gap = trimmed.IndexOf(' ');
            if (gap <= 0)
                continue;

            values[trimmed[..gap].ToString()] = trimmed[(gap + 1)..].Trim().ToString();
        }

        var settings = Settings.Default;

        var read = settings with
        {
            ViewRadiusChunks = Integer(values, "view-radius", settings.ViewRadiusChunks),
            FieldOfViewDegrees = Real(values, "field-of-view", settings.FieldOfViewDegrees),
            LookSensitivity = Real(values, "sensitivity", settings.LookSensitivity),
            Shadows = Flag(values, "shadows", settings.Shadows),
            ShadowStrength = Real(values, "shadow-strength", settings.ShadowStrength),
            Fog = Flag(values, "fog", settings.Fog),
            Reflections = Flag(values, "reflections", settings.Reflections),
            Brightness = Real(values, "brightness", settings.Brightness),
            Volume = Real(values, "volume", settings.Volume),
            UiScale = Real(values, "interface-scale", settings.UiScale),
            ShowOverlay = Flag(values, "overlay", settings.ShowOverlay),
            DayLengthSeconds = Real(values, "day-length", (float)settings.DayLengthSeconds),
        };

        return read.Clamped();
    }

    /// <summary>Writes the settings, replacing whatever was there.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="settings">What to write.</param>
    public static void Write(string path, Settings settings)
    {
        var lines = new[]
        {
            "# Shorebound settings. One name and one value a line; delete a line for its default.",
            $"view-radius {settings.ViewRadiusChunks}",
            $"field-of-view {Text(settings.FieldOfViewDegrees)}",
            $"sensitivity {Text(settings.LookSensitivity)}",
            $"shadows {Text(settings.Shadows)}",
            $"shadow-strength {Text(settings.ShadowStrength)}",
            $"fog {Text(settings.Fog)}",
            $"reflections {Text(settings.Reflections)}",
            $"brightness {Text(settings.Brightness)}",
            $"volume {Text(settings.Volume)}",
            $"interface-scale {Text(settings.UiScale)}",
            $"overlay {Text(settings.ShowOverlay)}",
            $"day-length {Text((float)settings.DayLengthSeconds)}",
        };

        File.WriteAllLines(path, lines);
    }

    // Invariant culture throughout, so a settings file written on a machine that uses a comma
    // for a decimal point still reads on one that does not.
    private static string Text(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Text(bool value) => value ? "on" : "off";

    private static int Integer(Dictionary<string, string> values, string name, int fallback) =>
        values.TryGetValue(name, out var text)
        && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static float Real(Dictionary<string, string> values, string name, float fallback) =>
        values.TryGetValue(name, out var text)
        && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static bool Flag(Dictionary<string, string> values, string name, bool fallback) =>
        values.TryGetValue(name, out var text)
            ? text is "on" or "true" or "yes" or "1"
            : fallback;
}
