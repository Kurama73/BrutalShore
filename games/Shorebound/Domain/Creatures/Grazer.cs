using Shorebound.Domain.Movement;

namespace Shorebound.Domain.Creatures;

/// <summary>One animal: where it is, which way it faces, and what it is in the middle of doing.</summary>
/// <remarks>
/// <para>
/// Everything about it is a few numbers, deliberately. An animal in this game is something that
/// makes a field look inhabited rather than something to fight, trade with or follow — so it has
/// a position, a heading, a timer and nothing else. The day one of those needs a memory, it can
/// have one.
/// </para>
/// <para>
/// A record struct rather than an object, because there are a few dozen of them at most and they
/// are stepped as a block: an array of values with no references in it is the shape that costs
/// nothing to walk every frame.
/// </para>
/// </remarks>
public record struct Grazer
{
    /// <summary>Where its body is: position, speed, footing and stride.</summary>
    /// <remarks>
    /// The half of an animal that something else also has. Everything below is what makes it a
    /// grazer rather than a creature; this is what makes it a thing standing on the ground.
    /// </remarks>
    public Footing Footing { get; set; }

    /// <summary>The middle of its feet, in world metres.</summary>
    public double3 FeetMeters => Footing.FeetMeters;

    /// <summary>Whether it is resting on something solid.</summary>
    public bool IsOnGround => Footing.IsOnGround;

    /// <summary>How far through its stride it is, in radians, which is what swings its legs.</summary>
    public float StrideRadians => Footing.StrideRadians;

    /// <summary>Which way it faces, in radians.</summary>
    public float Yaw { get; set; }

    /// <summary>How long it keeps doing whatever it is doing, in seconds.</summary>
    public double MindLeftSeconds { get; set; }

    /// <summary>Whether it is walking rather than standing.</summary>
    public bool IsWalking { get; set; }

    /// <summary>Which way it is facing, as a direction on the ground.</summary>
    public double3 Forward
    {
        get
        {
            var (sine, cosine) = double.SinCos(Yaw);
            return new double3(sine, 0.0, cosine);
        }
    }
}
