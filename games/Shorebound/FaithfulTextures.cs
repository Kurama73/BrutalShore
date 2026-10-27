using Brutal.StbApi;

namespace Shorebound;

/// <summary>Loads the small Faithful subset Shorebound draws its blocks and creatures from.</summary>
/// <remarks>
/// <para>
/// Texture indices are stable and stored in meshed vertices, so this list and the mappings in
/// <see cref="GameBlocks" /> must change together. Animated source images use their first frame;
/// water reflections and block lighting remain animated independently.
/// </para>
/// <para>
/// Every layer is the same size, because that is what a texture array is. The size is the one
/// the creature sheets come at rather than the one the blocks do: a Faithful block is 64 pixels
/// square and a Faithful creature sheet is 256 across, and a sheet shrunk to fit a block's layer
/// would lose three quarters of a cow. The blocks go the other way instead — each pixel repeated
/// four times — which costs memory and changes nothing on screen, since the sampler is a nearest
/// one and a pixel repeated four times is the same pixel.
/// </para>
/// </remarks>
internal static class FaithfulTextures
{
    public const byte Stone = 1;
    public const byte Dirt = 2;
    public const byte GrassSide = 3;
    public const byte GrassTop = 4;
    public const byte Sand = 5;
    public const byte Snow = 6;
    public const byte Water = 7;
    public const byte Bedrock = 8;
    public const byte LogSide = 9;
    public const byte LogEnd = 10;
    public const byte Leaves = 11;
    public const byte Lantern = 12;
    public const byte Planks = 13;
    public const byte StoneBricks = 14;
    public const byte CopperBlock = 15;
    public const byte IronBlock = 16;
    public const byte Glass = 17;
    public const byte BlastFurnace = 18;
    public const byte DispenserFront = 19;
    public const byte Scaffolding = 20;
    public const byte StonecutterSide = 21;
    public const byte CoalOre = 22;
    public const byte CopperOre = 23;
    public const byte IronOre = 24;
    public const byte Magma = 25;
    public const byte StonecutterTop = 26;

    /// <summary>The sheet a grazing animal is drawn from.</summary>
    public const byte Cow = 27;

    /// <summary>The sheet a stalker is drawn from.</summary>
    public const byte Wolf = 28;

    /// <summary>The sheet the player's own limbs are drawn from.</summary>
    public const byte Player = 29;

    /// <summary>How large every layer of the array is, in pixels.</summary>
    public const int LayerSize = 256;

    private const int BlockSize = 64;

    /// <summary>How many times a block's pixel is repeated to fill a layer.</summary>
    private const int BlockMagnification = LayerSize / BlockSize;

    private static readonly string[] BlockFiles =
    [
        "stone",
        "dirt",
        "grass_block_side",
        "grass_block_top",
        "sand",
        "snow",
        "water_still",
        "bedrock",
        "oak_log",
        "oak_log_top",
        "oak_leaves",
        "lantern",
        "oak_planks",
        "stone_bricks",
        "copper_block",
        "iron_block",
        "glass",
        "blast_furnace_side",
        "dispenser_front",
        "scaffolding_side",
        "stonecutter_side",
        "coal_ore",
        "copper_ore",
        "iron_ore",
        "magma",
        "stonecutter_top",
    ];

    // In the order their layer numbers are declared above, and placed at the top left of their
    // layer: a sheet that is 256 by 128 is a creature laid out in 64 by 32, and leaving it where
    // it falls is what makes one rule — a pixel of the layout is four pixels of the sheet — hold
    // for every creature whatever shape its sheet is.
    private static readonly string[] EntityFiles =
    [
        "entity/cow/cow_temperate",
        "entity/wolf/wolf_angry",
        "entity/player/wide/steve",
    ];

    /// <summary>The output-directory path where the locally installed pack is copied.</summary>
    public static string DirectoryPath => Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Faithful",
        "assets",
        "minecraft",
        "textures"
    );

    /// <summary>Whether the ignored local pack directory is present in the application output.</summary>
    public static bool IsInstalled => Directory.Exists(DirectoryPath);

    /// <summary>The number of entries, including the reserved untextured layer zero.</summary>
    public static int LayerCount => BlockFiles.Length + EntityFiles.Length + 1;

    /// <summary>Decodes every mapped texture into a contiguous RGBA8 layer buffer.</summary>
    /// <param name="textureDirectory">The pack's <c>textures</c> directory.</param>
    /// <returns>Layer-major pixel data for an array of <see cref="LayerSize" />-square layers.</returns>
    /// <exception cref="InvalidDataException">A texture is missing, invalid, or too small.</exception>
    public static byte[] Load(string textureDirectory)
    {
        var layerBytes = LayerSize * LayerSize * 4;
        var pixels = new byte[LayerCount * layerBytes];
        if (!Directory.Exists(textureDirectory))
            return pixels;

        for (var index = 0; index < BlockFiles.Length; index++)
        {
            var path = Path.Combine(textureDirectory, "block", BlockFiles[index] + ".png");
            Magnify(Decode(path, BlockSize, BlockSize), pixels.AsSpan((index + 1) * layerBytes, layerBytes));
        }

        for (var index = 0; index < EntityFiles.Length; index++)
        {
            var path = Path.Combine(textureDirectory, EntityFiles[index].Replace('/', Path.DirectorySeparatorChar) + ".png");
            Place(Decode(path, LayerSize, 0), pixels.AsSpan((BlockFiles.Length + index + 1) * layerBytes, layerBytes));
        }

        return pixels;
    }

    /// <summary>Reads one PNG and checks it is the shape the caller is going to treat it as.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="width">How wide it has to be.</param>
    /// <param name="height">How tall it has to be at least, or zero for any height.</param>
    /// <returns>Its rows, top to bottom, as RGBA8.</returns>
    private static unsafe (byte[] Pixels, int Height) Decode(string path, int width, int height)
    {
        using var image = Stb.Load(path, 4);
        if (image.IsNull())
        {
            var reason = Stb.TryGetFailureReason(out var failure) ? failure : "unknown image error";
            throw new InvalidDataException($"Could not load Faithful texture '{path}': {reason}");
        }

        if (image.Width != width || image.Height < height)
            throw new InvalidDataException(
                $"Faithful texture '{path}' is {image.Width}x{image.Height}; expected {width} wide and at least {height} tall."
            );

        // Capped at a layer, because an animated block's PNG is a column of frames and the first
        // of them is the one this game draws.
        var rows = int.Min(image.Height, LayerSize);
        return (image.AsSpan()[..(width * rows * 4)].ToArray(), rows);
    }

    /// <summary>Copies a sheet into the top left of a layer, leaving the rest transparent.</summary>
    private static void Place((byte[] Pixels, int Height) sheet, Span<byte> layer) =>
        sheet.Pixels.AsSpan(0, LayerSize * sheet.Height * 4).CopyTo(layer);

    /// <summary>Repeats each pixel of a block's texture until it fills a layer.</summary>
    private static void Magnify((byte[] Pixels, int Height) block, Span<byte> layer)
    {
        var source = block.Pixels.AsSpan();

        for (var y = 0; y < LayerSize; y++)
        {
            var sourceRow = (y / BlockMagnification) * BlockSize * 4;
            for (var x = 0; x < LayerSize; x++)
            {
                var from = sourceRow + ((x / BlockMagnification) * 4);
                source.Slice(from, 4).CopyTo(layer[(((y * LayerSize) + x) * 4)..]);
            }
        }
    }
}
