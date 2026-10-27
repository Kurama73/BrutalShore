using BrutalShore.Render;

namespace Shorebound;

/// <summary>What an animal is made of: a body, a head and four legs, as boxes.</summary>
/// <remarks>
/// <para>
/// Six boxes and a stride. There is no model to load and no skeleton to animate, because at the
/// scale a player sees one of these — a few metres away, a metre tall, made of the same blocks
/// the world is — a body on four legs that swing is already the whole of what reads as an animal.
/// </para>
/// <para>
/// The legs swing by moving rather than by turning, which is a simplification with a visible
/// consequence and the right one: a box turned about its top would need the renderer to know
/// where a thing's joints are, and a box slid forward and back does not. At this size nobody can
/// tell, and the difference in the engine is a whole skeleton.
/// </para>
/// </remarks>
public static class GrazerShape
{
    /// <summary>How many boxes one animal is drawn as.</summary>
    public const int PartCount = 6;

    private static readonly float3 BodySize = new(0.52f, 0.5f, 0.92f);
    private static readonly float3 HeadSize = new(0.4f, 0.4f, 0.38f);
    private static readonly float3 LegSize = new(0.15f, 0.55f, 0.15f);

    /// <summary>How far a leg swings forward and back, in metres.</summary>
    private const double StrideMeters = 0.17;

    /// <summary>The colour of the body, which is most of what is seen.</summary>
    private static readonly float3 Wool = new(0.86f, 0.84f, 0.78f);

    /// <summary>The colour of the head and legs, darker so the shape reads at a distance.</summary>
    private static readonly float3 Hide = new(0.42f, 0.36f, 0.3f);

    /// <summary>Where one of an animal's boxes goes and what it looks like.</summary>
    /// <param name="MiddleMeters">The box's middle, in world metres.</param>
    /// <param name="SizeMeters">Its width, height and depth, in metres.</param>
    /// <param name="Color">Its colour, in sRGB, for when there is no sheet to draw it from.</param>
    /// <param name="TextureLayer">Which layer of the texture array its sheet is on, or zero for none.</param>
    /// <param name="Skin">Which patch of that sheet each of its faces takes.</param>
    public readonly record struct Part(
        double3 MiddleMeters,
        float3 SizeMeters,
        float3 Color,
        byte TextureLayer = 0,
        CubeSkin? Skin = null
    );

    /// <summary>Works out where every box of one animal goes.</summary>
    /// <param name="grazer">The animal.</param>
    /// <param name="into">Where to write the parts; must hold <see cref="PartCount" /> of them.</param>
    /// <exception cref="ArgumentException">The span is too short.</exception>
    /// <remarks>
    /// Written into a span the caller owns rather than returned as a list, because this runs once
    /// per animal per frame and the parts are read immediately and thrown away.
    /// </remarks>
    public static void Lay(in Grazer grazer, Span<Part> into)
    {
        if (into.Length < PartCount)
            throw new ArgumentException($"An animal is {PartCount} boxes, not {into.Length}.", nameof(into));

        var forward = grazer.Forward;
        var right = new double3(forward.Z, 0.0, -forward.X);
        var feet = grazer.FeetMeters;

        var swing = double.Sin(grazer.StrideRadians) * StrideMeters;

        // Diagonal pairs move together, which is how a four-legged animal actually walks and is
        // also the only arrangement that does not look like hopping.
        into[0] = Leg(feet, forward, right, +0.28, +0.3, +swing);
        into[1] = Leg(feet, forward, right, -0.28, +0.3, -swing);
        into[2] = Leg(feet, forward, right, +0.28, -0.3, -swing);
        into[3] = Leg(feet, forward, right, -0.28, -0.3, +swing);

        var bodyMiddle = feet + new double3(0.0, LegSize.Y + (BodySize.Y / 2.0), 0.0);
        into[4] = new Part(bodyMiddle, BodySize, Wool, MobSkins.Grazer.Layer, MobSkins.Grazer.Body);

        // The head rides a little higher than the body and bobs with the stride, which is most of
        // what makes a walk look like an effort rather than a slide.
        var bob = double.Sin(grazer.StrideRadians * 2.0) * 0.03;
        var headMiddle =
            bodyMiddle
            + (forward * ((BodySize.Z / 2.0) + (HeadSize.Z / 2.0) - 0.05))
            + new double3(0.0, (BodySize.Y / 2.0) + bob, 0.0);

        into[5] = new Part(headMiddle, HeadSize, Hide, MobSkins.Grazer.Layer, MobSkins.Grazer.Head);
    }

    private static Part Leg(double3 feet, double3 forward, double3 right, double along, double across, double swing)
    {
        var middle =
            feet
            + (forward * (along + swing))
            + (right * across)
            + new double3(0.0, LegSize.Y / 2.0, 0.0);

        return new Part(middle, LegSize, Hide, MobSkins.Grazer.Layer, MobSkins.Grazer.Leg);
    }
}
