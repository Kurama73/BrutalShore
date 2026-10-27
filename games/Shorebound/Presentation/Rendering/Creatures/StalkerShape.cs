using Shorebound.Domain.Creatures;

namespace Shorebound.Presentation.Rendering.Creatures;

/// <summary>What a stalker is made of: a long body, a low head and four legs, as boxes.</summary>
/// <remarks>
/// Built out of the same six boxes as an animal and deliberately not the same silhouette. The one
/// thing a player has to be able to do at fifty metres, in the dark, is tell which of the two is
/// walking towards them — so this one is lower, longer, darker and has its head out in front
/// rather than up, which reads as something hunting even before it is close enough to see.
/// </remarks>
public static class StalkerShape
{
    /// <summary>How many boxes one is drawn as.</summary>
    public const int PartCount = 6;

    private static readonly float3 BodySize = new(0.46f, 0.42f, 1.08f);
    private static readonly float3 HeadSize = new(0.36f, 0.32f, 0.44f);
    private static readonly float3 LegSize = new(0.13f, 0.46f, 0.13f);
    private const double HeadForwardMeters = 0.48;

    /// <summary>How far a leg swings forward and back, in metres.</summary>
    private const double StrideMeters = 0.2;

    /// <summary>The colour of the body: dark enough to be a shape rather than a thing.</summary>
    private static readonly float3 Hide = new(0.16f, 0.15f, 0.19f);

    /// <summary>The colour of the head, which is the part a player wants to find.</summary>
    private static readonly float3 Maw = new(0.52f, 0.14f, 0.11f);

    /// <summary>Lays one out as boxes in world space.</summary>
    /// <param name="stalker">The creature to lay out.</param>
    /// <param name="into">Where to write the parts; must hold <see cref="PartCount" />.</param>
    /// <exception cref="ArgumentException">If the span is too short.</exception>
    public static void Lay(in Stalker stalker, Span<GrazerShape.Part> into)
    {
        if (into.Length < PartCount)
            throw new ArgumentException($"a stalker is {PartCount} boxes", nameof(into));

        var feet = stalker.FeetMeters;
        var (sine, cosine) = double.SinCos(stalker.Yaw);
        var forward = new double3(sine, 0.0, cosine);
        var right = new double3(cosine, 0.0, -sine);

        var swing = double.Sin(stalker.StrideRadians) * StrideMeters;
        var legMiddle = LegSize.Y / 2.0;
        var bodyMiddle = LegSize.Y + (BodySize.Y / 2.0);

        into[0] = new GrazerShape.Part(
            feet + new double3(0.0, bodyMiddle, 0.0),
            BodySize,
            Hide,
            MobSkins.Stalker.Layer,
            MobSkins.Stalker.Body
        );

        // The head out in front and barely above the body, which is the whole of the silhouette.
        into[1] = new GrazerShape.Part(
            feet + (forward * HeadForwardMeters) + new double3(0.0, bodyMiddle - 0.04, 0.0),
            HeadSize,
            Maw,
            MobSkins.Stalker.Layer,
            MobSkins.Stalker.Head
        );

        // Diagonal pairs swing together, the way a four-legged thing actually walks.
        into[2] = Leg(feet, forward, right, +0.34, +0.17, +swing, legMiddle);
        into[3] = Leg(feet, forward, right, +0.34, -0.17, -swing, legMiddle);
        into[4] = Leg(feet, forward, right, -0.34, +0.17, -swing, legMiddle);
        into[5] = Leg(feet, forward, right, -0.34, -0.17, +swing, legMiddle);
    }

    private static GrazerShape.Part Leg(
        double3 feet,
        double3 forward,
        double3 right,
        double along,
        double across,
        double swing,
        double middle
    ) =>
        new(
            feet + (forward * (along + swing)) + (right * across) + new double3(0.0, middle, 0.0),
            LegSize,
            Hide,
            MobSkins.Stalker.Layer,
            MobSkins.Stalker.Leg
        );
}
