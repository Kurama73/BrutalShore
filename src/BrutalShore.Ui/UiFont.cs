using Brutal.ImGuiApi;

namespace BrutalShore.Ui;

/// <summary>The typefaces the interface is drawn in, loaded once from the machine's own fonts.</summary>
/// <remarks>
/// <para>
/// Dear ImGui's built-in font is a bitmap face designed for tool panels at one size. It is the
/// right default for a library that must work with no files at all, and it is the wrong face for
/// a title screen: it has no real weight, it does not hint, and it falls apart the moment the
/// interface scale is anything but one.
/// </para>
/// <para>
/// So the interface asks the operating system for a face instead of shipping one. That keeps the
/// repository free of a font licence — the one asset exception here is a texture pack, and
/// widening it for a typeface nobody asked for would be a bad trade — and it gets a hinted,
/// outline face that scales. Every platform has had a clean grotesque and a monospace in a known
/// place for twenty years; the lists below are those places, best first.
/// </para>
/// <para>
/// Nothing here is load bearing. If no candidate exists the built-in face is used and every
/// screen still draws, which is why this reports what it found rather than throwing.
/// </para>
/// </remarks>
public static class UiFont
{
    /// <summary>The size the faces are baked at, before the interface scale.</summary>
    /// <remarks>
    /// Dear ImGui bakes glyphs on demand at whatever size is asked for, so this is the size the
    /// layout constants in <see cref="UiControls" /> were chosen against rather than a limit.
    /// Seventeen pixels is the smallest size at which a lower-case grotesque stays readable over
    /// a sunlit field, which is the hardest thing this interface is drawn over.
    /// </remarks>
    public const float BaseSizePixels = 17f;

    // Plain .ttf only, and regular weights only. A .ttc collection needs an index chosen through
    // ImFontConfig.FontNo, and guessing which face of a collection is the regular one is how a
    // menu ends up in an italic.
    private static readonly string[] InterfaceCandidates =
    [
        // macOS. SF Compact before SF Pro deliberately: it is the squarer of the two, which is
        // the right note for a screen of machined plates.
        "/System/Library/Fonts/SFCompact.ttf",
        "/System/Library/Fonts/SFNS.ttf",
        "/System/Library/Fonts/Supplemental/Tahoma.ttf",
        "/System/Library/Fonts/Supplemental/Verdana.ttf",
        "/System/Library/Fonts/Supplemental/Arial.ttf",

        // Windows.
        @"C:\Windows\Fonts\segoeui.ttf",
        @"C:\Windows\Fonts\tahoma.ttf",
        @"C:\Windows\Fonts\arial.ttf",

        // Linux, in the order the usual distributions install them.
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf",
        "/usr/share/fonts/TTF/DejaVuSans.ttf",
    ];

    private static readonly string[] MonospaceCandidates =
    [
        "/System/Library/Fonts/SFNSMono.ttf",
        "/System/Library/Fonts/Monaco.ttf",
        "/System/Library/Fonts/Supplemental/Andale Mono.ttf",
        @"C:\Windows\Fonts\consola.ttf",
        @"C:\Windows\Fonts\cour.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationMono-Regular.ttf",
        "/usr/share/fonts/TTF/DejaVuSansMono.ttf",
    ];

    /// <summary>The face every panel and menu is drawn in.</summary>
    /// <remarks>
    /// Null until <see cref="Load" /> has run, and null afterwards on a machine where no
    /// candidate was found; every call site treats that as "use whatever ImGui is using".
    /// </remarks>
    public static ImFontPtr? Interface { get; private set; }

    /// <summary>The face the terminal and anything column-aligned is drawn in.</summary>
    /// <remarks>
    /// A console whose characters are not all one width cannot line up a table, and half of what
    /// the in-game terminal prints is a table.
    /// </remarks>
    public static ImFontPtr? Monospace { get; private set; }

    /// <summary>Finds the two faces and hands them to the current ImGui context.</summary>
    /// <remarks>
    /// Must run on the one ImGui context, after it is created and before the drawing backend
    /// builds its atlas. Calling it twice would add the same faces again, so it answers the
    /// second call by doing nothing.
    /// </remarks>
    public static void Load()
    {
        if (Interface is not null || Monospace is not null)
            return;

        var fonts = ImGui.GetIO().Fonts;

        if (FirstExisting(InterfaceCandidates) is { } interfacePath)
        {
            Interface = fonts.AddFontFromFileTTF(interfacePath, BaseSizePixels);
            ImGui.GetIO().FontDefault = Interface.Value;
        }

        if (FirstExisting(MonospaceCandidates) is { } monospacePath)
            Monospace = fonts.AddFontFromFileTTF(monospacePath, BaseSizePixels);
    }

    /// <summary>Pushes the monospace face, or the current one where there is none.</summary>
    /// <param name="sizePixels">How tall to draw it, or zero for the panel's own size.</param>
    /// <remarks>
    /// Always pushes something, so that every call pairs with one <see cref="Pop" /> whether or
    /// not a monospace face was found. An interface whose push and pop counts depend on what is
    /// installed is an interface that asserts on one machine in five.
    /// </remarks>
    public static void PushMonospace(float sizePixels = 0f) =>
        ImGui.PushFont(Monospace ?? ImGui.GetFont(), sizePixels);

    /// <summary>Undoes one <see cref="PushMonospace" />.</summary>
    public static void Pop() => ImGui.PopFont();

    private static string? FirstExisting(string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
