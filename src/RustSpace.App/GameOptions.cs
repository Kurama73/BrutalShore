namespace RustSpace.App;

/// <summary>Everything the game can be started differently with.</summary>
/// <param name="Seed">The world seed; the same seed always generates the same world.</param>
/// <param name="EnableValidation">
/// Whether to turn on the Vulkan validation layers, which report API misuse at a large cost in
/// frame time. Off by default so the default run is representative.
/// </param>
/// <param name="ViewRadiusChunks">
/// How far chunks are kept loaded, in chunks. Memory grows with the square of this: a chunk is
/// 64 KiB of block ids, and a radius of r keeps <c>(2r+1)² × 8</c> of them resident.
/// </param>
/// <param name="FrameLimit">How many frames to run before closing, or zero to run until closed.</param>
/// <param name="Viewpoint">
/// Where to put the camera and which way to point it, or <see langword="null" /> to spawn on the
/// ground at the origin. A screenshot is only worth comparing against another one if the camera
/// was in the same place, which is what this is for.
/// </param>
/// <param name="ScreenshotPath">
/// Where to save a PNG of the last frame, or <see langword="null" /> for no screenshot. Together
/// with <paramref name="FrameLimit" /> this is what makes a run self-verifying: it renders a fixed
/// number of frames of a fixed seed and leaves an image behind.
/// </param>
public sealed record GameOptions(
    int Seed = 1337,
    bool EnableValidation = false,
    int ViewRadiusChunks = 8,
    int FrameLimit = 0,
    Viewpoint? Viewpoint = null,
    string? ScreenshotPath = null
)
{
    /// <summary>Reads the options from a command line.</summary>
    /// <remarks>
    /// Unknown arguments are an error rather than a warning: a mistyped flag that silently did
    /// nothing would be found out by the run behaving unexpectedly, which is a much worse way to
    /// learn about it.
    /// </remarks>
    /// <exception cref="ArgumentException">On an unknown or malformed argument.</exception>
    public static GameOptions Parse(string[] arguments)
    {
        var options = new GameOptions();

        for (var index = 0; index < arguments.Length; index++)
        {
            switch (arguments[index])
            {
                case "--validation":
                    options = options with { EnableValidation = true };
                    break;

                case "--seed":
                    options = options with { Seed = ReadInteger(arguments, ++index, "--seed") };
                    break;

                case "--view-radius":
                    options = options with { ViewRadiusChunks = ReadInteger(arguments, ++index, "--view-radius") };
                    break;

                case "--frames":
                    options = options with { FrameLimit = ReadInteger(arguments, ++index, "--frames") };
                    break;

                case "--camera":
                    options = options with { Viewpoint = ReadViewpoint(arguments, ref index) };
                    break;

                case "--screenshot":
                    options = options with { ScreenshotPath = ReadText(arguments, ++index, "--screenshot") };
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
        index < arguments.Length ? arguments[index] : throw new ArgumentException($"'{flag}' needs a value.", nameof(arguments));

    private static int ReadInteger(string[] arguments, int index, string flag) =>
        index < arguments.Length && int.TryParse(arguments[index], out var value)
            ? value
            : throw new ArgumentException($"'{flag}' needs a whole number.", nameof(arguments));
}
