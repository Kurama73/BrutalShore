using BrutalShore.Runtime;

namespace Shorebound;

/// <summary>How this run of Shorebound differs from the default one.</summary>
/// <remarks>
/// The game's own settings and the host's are kept apart on purpose: <see cref="Host" /> holds what
/// would mean something to any game, and everything beside it is particular to this one. The
/// command line is the only place the two meet.
/// </remarks>
public sealed record Options
{
    /// <summary>The world seed. The same seed gives the same world, forever.</summary>
    public int Seed { get; init; } = 1337;

    /// <summary>
    /// How far chunks are kept loaded, in chunks, or nothing to leave the player's setting alone.
    /// </summary>
    /// <remarks>
    /// Nullable because this is one of the values that is now a setting as well. A command line
    /// that does not mention it must not quietly put it back to a default — a player who chose a
    /// view distance chose it — and a command line that does mention it has to win, because that
    /// is the only way to render a comparable screenshot on a machine with a settings file on it.
    /// </remarks>
    public int? ViewRadiusChunks { get; init; }

    /// <summary>The hour of the first morning, from 0 to 24.</summary>
    public double StartHour { get; init; } = 8.0;

    /// <summary>
    /// How long a whole day takes in real seconds, zero stopping the sun, or nothing to leave the
    /// player's setting alone.
    /// </summary>
    public double? DayLengthSeconds { get; init; }

    /// <summary>Where the world is saved to and loaded from.</summary>
    public string SavePath { get; init; } = "shorebound.world";

    /// <summary>Where the player's settings are read from and written back to.</summary>
    /// <remarks>
    /// An option rather than a constant so that a fixed-frame render can be given an empty path
    /// and get the defaults: a screenshot that came out different because of whatever was last
    /// left in a settings file is a screenshot that compares against nothing.
    /// </remarks>
    public string SettingsPath { get; init; } = SettingsFile.DefaultPath;

    /// <summary>Where the camera starts, or <see langword="null" /> to stand on the ground at the origin.</summary>
    public Viewpoint? Viewpoint { get; init; }

    /// <summary>What the host is told, before the player's settings are folded into it.</summary>
    public HostOptions Host { get; init; } = new() { Title = "Shorebound" };

    /// <summary>Whether <c>--no-overlay</c> was given, which beats the player's setting.</summary>
    public bool HideOverlay { get; init; }

    /// <summary>
    /// The player's settings with whatever this command line said about them written over the top.
    /// </summary>
    /// <param name="settings">What was read from the settings file.</param>
    /// <returns>The settings this run should use.</returns>
    public Settings Override(Settings settings) =>
        (
            settings with
            {
                ViewRadiusChunks = ViewRadiusChunks ?? settings.ViewRadiusChunks,
                DayLengthSeconds = DayLengthSeconds ?? settings.DayLengthSeconds,
                ShowOverlay = settings.ShowOverlay && !HideOverlay,
            }
        ).Clamped();

    /// <summary>What the host is told, once the player's settings are known.</summary>
    /// <param name="settings">The settings this run is using.</param>
    /// <returns>The host options, with the interface scale and the overlay taken from them.</returns>
    public HostOptions HostWith(Settings settings) =>
        Host with
        {
            ShowOverlay = settings.ShowOverlay,
            UiScale = settings.UiScale,
        };

    /// <summary>Reads the options from a command line.</summary>
    /// <remarks>
    /// Unknown arguments are an error rather than a warning: a mistyped flag that silently did
    /// nothing would be found out by the run behaving unexpectedly, which is a much worse way to
    /// learn about it.
    /// </remarks>
    /// <exception cref="ArgumentException">On an unknown or malformed argument.</exception>
    public static Options Parse(string[] arguments)
    {
        var options = new Options();

        for (var index = 0; index < arguments.Length; index++)
        {
            switch (arguments[index])
            {
                case "--seed":
                    options = options with { Seed = ReadInteger(arguments, ++index, "--seed") };
                    break;

                case "--view-radius":
                    options = options with { ViewRadiusChunks = ReadInteger(arguments, ++index, "--view-radius") };
                    break;

                case "--time":
                    options = options with { StartHour = ReadDouble(arguments, ++index, "--time") };
                    break;
                case "--day-length":
                    options = options with
                    {
                        DayLengthSeconds = ReadDouble(arguments, ++index, "--day-length"),
                    };
                    break;
                case "--save":
                    options = options with { SavePath = ReadText(arguments, ++index, "--save") };
                    break;
                case "--settings":
                    options = options with { SettingsPath = ReadText(arguments, ++index, "--settings") };
                    break;
                case "--camera":
                    options = options with { Viewpoint = ReadViewpoint(arguments, ref index) };
                    break;

                case "--no-overlay":
                    options = options with { HideOverlay = true };
                    break;
                case "--validation":
                    options = options with { Host = options.Host with { EnableValidation = true } };
                    break;

                case "--frames":
                    options = options with
                    {
                        Host = options.Host with { FrameLimit = ReadInteger(arguments, ++index, "--frames") },
                    };
                    break;

                case "--screenshot":
                    options = options with
                    {
                        Host = options.Host with { ScreenshotPath = ReadText(arguments, ++index, "--screenshot") },
                    };
                    break;

                default:
                    throw new ArgumentException($"Unknown argument '{arguments[index]}'.", nameof(arguments));
            }
        }

        return options;
    }

    /// <summary>
    /// Reads <c>--camera x y z yaw pitch</c>, with the two angles in degrees.
    /// </summary>
    /// <remarks>
    /// Degrees on the command line and radians everywhere inside: nobody types a heading in
    /// radians, and the conversion belongs at the edge where the human input arrives.
    /// </remarks>
    private const string CameraShape = "five numbers: x y z yaw pitch";

    private static Viewpoint ReadViewpoint(string[] arguments, ref int index)
    {
        index++;
        var x = ReadDouble(arguments, index++, "--camera", CameraShape);
        var y = ReadDouble(arguments, index++, "--camera", CameraShape);
        var z = ReadDouble(arguments, index++, "--camera", CameraShape);
        var yaw = ReadDouble(arguments, index++, "--camera", CameraShape);
        var pitch = ReadDouble(arguments, index, "--camera", CameraShape);

        return new Viewpoint(
            new double3(x, y, z),
            float.DegreesToRadians((float)yaw),
            float.DegreesToRadians((float)pitch)
        );
    }

    private static double ReadDouble(string[] arguments, int index, string flag, string shape = "a number") =>
        index < arguments.Length
        && double.TryParse(arguments[index], System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ArgumentException($"'{flag}' needs {shape}.", nameof(arguments));

    private static string ReadText(string[] arguments, int index, string flag) =>
        index < arguments.Length
            ? arguments[index]
            : throw new ArgumentException($"'{flag}' needs a value.", nameof(arguments));

    private static int ReadInteger(string[] arguments, int index, string flag) =>
        index < arguments.Length && int.TryParse(arguments[index], out var value)
            ? value
            : throw new ArgumentException($"'{flag}' needs a whole number.", nameof(arguments));
}
