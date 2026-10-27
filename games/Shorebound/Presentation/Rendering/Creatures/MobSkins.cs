using BrutalShore.Render;
using Shorebound.Domain.World;

namespace Shorebound.Presentation.Rendering.Creatures;

/// <summary>Which patch of a creature's sheet each of its boxes is drawn from.</summary>
/// <remarks>
/// <para>
/// A creature sheet is laid out the way Minecraft lays one out, because that is the pack these
/// textures come from: every box of the model has a corner on a sixty-four unit grid, and the
/// six faces of that box are unwrapped around it in a fixed cross — top and bottom along the
/// row above, the four sides along the row below. <see cref="Box" /> is that cross, written once.
/// </para>
/// <para>
/// The game's creatures are not the Minecraft models and are not going to become them: a grazer
/// is six boxes with its own proportions, chosen for what reads at fifty metres. So a patch is
/// stretched onto a box that is not quite the shape the patch was drawn for, and one patch — the
/// flank of a body, whose sheet entry is drawn lying down because the model it came from lies its
/// body down — arrives turned a quarter. Neither is visible on a thing a metre tall seen from
/// three metres away, and the alternative is a skeleton and a rotation per box.
/// </para>
/// </remarks>
internal static class MobSkins
{
    /// <summary>How many units across the grid a sheet's layout is.</summary>
    /// <remarks>
    /// Sixty-four, whatever the resolution of the pack. A Faithful sheet is four pixels to the
    /// unit and a vanilla one is one, and a patch named in units is the same patch in both.
    /// </remarks>
    private const float Grid = 64f;

    /// <summary>The faces of one box of a model, unwrapped the way the sheet lays them out.</summary>
    /// <param name="u">The box's corner on the sheet, across.</param>
    /// <param name="v">The box's corner on the sheet, down.</param>
    /// <param name="width">How wide the box is, in grid units.</param>
    /// <param name="height">How tall it is.</param>
    /// <param name="depth">How deep it is.</param>
    /// <returns>The patch each face is cut from.</returns>
    public static CubeSkin Box(int u, int v, int width, int height, int depth) =>
        new(
            Side: Rect(u, v + depth, depth, height),
            Top: Rect(u + depth, v, width, depth),
            Front: Rect(u + depth, v + depth, width, height),
            Back: Rect(u + depth + width + depth, v + depth, width, height)
        );

    /// <summary>
    /// The same, for a box whose model lies it down — a four-legged creature's body — so that
    /// what the sheet calls the front is the belly and what it calls the back is the spine.
    /// </summary>
    /// <param name="u">The box's corner on the sheet, across.</param>
    /// <param name="v">The box's corner on the sheet, down.</param>
    /// <param name="width">How wide the box is, in grid units.</param>
    /// <param name="length">How long it is from chest to rump.</param>
    /// <param name="depth">How deep it is from spine to belly.</param>
    /// <returns>The patch each face is cut from, with the spine on top and the chest in front.</returns>
    public static CubeSkin LyingBox(int u, int v, int width, int length, int depth) =>
        new(
            Side: Rect(u, v + depth, depth, length),
            Top: Rect(u + depth + width + depth, v + depth, width, length),
            Front: Rect(u + depth, v, width, depth),
            Back: Rect(u + depth + width, v, width, depth)
        );

    /// <summary>A grazing animal: the sheet of a cow, and the patches its six boxes take.</summary>
    public static class Grazer
    {
        /// <summary>Which layer of the texture array the sheet is on.</summary>
        public const byte Layer = GameTextureLayers.Cow;

        /// <summary>The head, whose face is on the end pointing the way it walks.</summary>
        public static CubeSkin Head { get; } = Box(0, 0, 8, 8, 6);

        /// <summary>The body, lying down on the sheet the way a cow's does.</summary>
        public static CubeSkin Body { get; } = LyingBox(18, 4, 12, 18, 10);

        /// <summary>One leg; all four take the same patch, which is what the sheet has.</summary>
        public static CubeSkin Leg { get; } = Box(0, 16, 4, 12, 4);
    }

    /// <summary>A stalker: an angry wolf's sheet, which is the one that comes with red eyes.</summary>
    public static class Stalker
    {
        /// <summary>Which layer of the texture array the sheet is on.</summary>
        public const byte Layer = GameTextureLayers.Wolf;

        /// <summary>The head, which is the part a player in the dark is looking for.</summary>
        public static CubeSkin Head { get; } = Box(0, 0, 6, 6, 4);

        /// <summary>The body.</summary>
        public static CubeSkin Body { get; } = LyingBox(18, 14, 6, 9, 6);

        /// <summary>One leg.</summary>
        public static CubeSkin Leg { get; } = Box(0, 18, 2, 8, 2);
    }

    /// <summary>The player, of whom only an arm is ever on screen.</summary>
    /// <remarks>
    /// The arm is two boxes rather than one. A box takes one patch per face and stretches it
    /// along the whole of that face, so a single box from shoulder to hand would run the sleeve
    /// down the length of the arm instead of around the top of it. Cutting the sheet's arm into
    /// a sleeve band and a bare band, and drawing a short box from each, puts the shirt where
    /// the shirt goes.
    /// </remarks>
    public static class Player
    {
        /// <summary>Which layer of the texture array the sheet is on.</summary>
        public const byte Layer = GameTextureLayers.Player;

        /// <summary>How much of the arm, from end to end, the sleeve covers.</summary>
        public const float SleeveShare = 5f / 12f;

        /// <summary>The shirt end of the right arm, nearest the shoulder.</summary>
        public static CubeSkin Sleeve { get; } = Band(40, 16, 4, 12, 4, from: 0, rows: 5);

        /// <summary>The bare end of the right arm, which is what holds whatever is in hand.</summary>
        public static CubeSkin Forearm { get; } = Band(40, 16, 4, 12, 4, from: 5, rows: 7) with
        {
            // The hand end is the lower-arm cap on the skin sheet, not the shoulder's sleeve cap.
            Top = Rect(44, 28, 4, 4),
        };
    }

    /// <summary>
    /// The faces of a part of a box: the same unwrap, cut to a band of rows down the sides.
    /// </summary>
    /// <param name="u">The whole box's corner on the sheet, across.</param>
    /// <param name="v">The whole box's corner on the sheet, down.</param>
    /// <param name="width">How wide the whole box is, in grid units.</param>
    /// <param name="height">How tall it is.</param>
    /// <param name="depth">How deep it is.</param>
    /// <param name="from">How far down the box's sides the band starts.</param>
    /// <param name="rows">How many rows of the sides it covers.</param>
    /// <returns>The patch each face of the band is cut from.</returns>
    private static CubeSkin Band(int u, int v, int width, int height, int depth, int from, int rows) =>
        new(
            Side: Rect(u, v + depth + from, depth, rows),
            Top: Rect(u + depth, v, width, depth),
            Front: Rect(u + depth, v + depth + from, width, rows),
            Back: Rect(u + depth + width + depth, v + depth + from, width, rows)
        );

    private static float4 Rect(int u, int v, int width, int height) =>
        new(u / Grid, v / Grid, width / Grid, height / Grid);
}