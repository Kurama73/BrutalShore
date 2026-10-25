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
    /// How far chunks are kept loaded, in chunks.
    /// </summary>
    /// <remarks>
    /// Memory grows with the square of this: a chunk is 64 KiB of block ids, and a radius of r
    /// keeps <c>(2r+1)² × 8</c> of them resident.
    /// </remarks>
    public int ViewRadiusChunks { get; init; } = 8;

    /// <summary>Where the camera starts, or <see langword="null" /> to stand on the ground at the origin.</summary>
    public Viewpoint? Viewpoint { get; init; }

    /// <summary>What the host is told.</summary>
    public HostOptions Host { get; init; } = new() { Title = "Shorebound" };

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

                case "--camera":
                    options = options with { Viewpoint = ReadViewpoint(arguments, ref index) };
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
    private static Viewpoint ReadViewpoint(string[] arguments, ref int index)
    {
        index++;
        var x = ReadDouble(arguments, index++, "--camera");
        var y = ReadDouble(arguments, index++, "--camera");
        var z = ReadDouble(arguments, index++, "--camera");
        var yaw = ReadDouble(arguments, index++, "--camera");
        var pitch = ReadDouble(arguments, index, "--camera");

        return new Viewpoint(
            new double3(x, y, z),
            float.DegreesToRadians((float)yaw),
            float.DegreesToRadians((float)pitch)
        );
    }

    private static double ReadDouble(string[] arguments, int index, string flag) =>
        index < arguments.Length
        && double.TryParse(arguments[index], System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ArgumentException($"'{flag}' needs five numbers: x y z yaw pitch.", nameof(arguments));

    private static string ReadText(string[] arguments, int index, string flag) =>
        index < arguments.Length
            ? arguments[index]
            : throw new ArgumentException($"'{flag}' needs a value.", nameof(arguments));

    private static int ReadInteger(string[] arguments, int index, string flag) =>
        index < arguments.Length && int.TryParse(arguments[index], out var value)
            ? value
            : throw new ArgumentException($"'{flag}' needs a whole number.", nameof(arguments));
}
