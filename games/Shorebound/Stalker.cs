namespace Shorebound;

/// <summary>One of the things that comes out of the dark: where it is and what it is doing.</summary>
/// <remarks>
/// <para>
/// A stalker has one idea at a time and the idea is always the player. What varies is whether it
/// can currently see a way towards them: a creature walking straight at a wall is a creature that
/// has stopped being frightening, so when one is blocked it picks a direction around and commits
/// to it for a second or two rather than re-deciding every frame.
/// </para>
/// <para>
/// Like the animals it is never saved. A horde belongs to a night, not to a world, and one that
/// persisted across a reload would mean a player could put a siege on hold by quitting.
/// </para>
/// </remarks>
public record struct Stalker
{
    /// <summary>Where its body is: position, speed, footing and stride.</summary>
    public Footing Footing { get; set; }

    /// <summary>The middle of its feet, in world metres.</summary>
    public double3 FeetMeters => Footing.FeetMeters;

    /// <summary>Whether it is resting on something solid.</summary>
    public bool IsOnGround => Footing.IsOnGround;

    /// <summary>How far through its stride it is, in radians.</summary>
    public float StrideRadians => Footing.StrideRadians;

    /// <summary>Which way it faces, in radians.</summary>
    public float Yaw { get; set; }

    /// <summary>How much it can take before it comes apart.</summary>
    public float Health { get; set; }

    /// <summary>
    /// How long it keeps going around an obstacle rather than straight at the player, in seconds.
    /// </summary>
    public double SidleLeftSeconds { get; set; }

    /// <summary>Which way it sidles when it is blocked: 1 for right, -1 for left.</summary>
    public double SidleSide { get; set; }

    /// <summary>How long until it can bite again, in seconds.</summary>
    public double BiteLeftSeconds { get; set; }

    /// <summary>How long it has been alight in daylight, which is what finishes it.</summary>
    public double BurningSeconds { get; set; }

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
