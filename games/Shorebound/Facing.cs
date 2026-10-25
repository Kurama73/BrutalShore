using BrutalShore.World;

namespace Shorebound;

/// <summary>Which of the four horizontal directions something is turned towards.</summary>
/// <remarks>
/// Four rather than six. Everything that has a direction in this game moves things along the
/// ground, and a belt pointing at the sky is a problem without a shape — what would be on it.
/// When something needs to go up, it will be a different machine with a different reason.
/// </remarks>
public enum Facing
{
    /// <summary>Towards negative z.</summary>
    North,

    /// <summary>Towards positive x.</summary>
    East,

    /// <summary>Towards positive z.</summary>
    South,

    /// <summary>Towards negative x.</summary>
    West,
}

/// <summary>Turning a facing into the things a machine actually needs from it.</summary>
public static class Facings
{
    /// <summary>The block one step along a facing.</summary>
    /// <param name="from">Where to start.</param>
    /// <param name="facing">Which way to go.</param>
    /// <returns>The next block along.</returns>
    public static int3 Ahead(int3 from, Facing facing) =>
        facing switch
        {
            Facing.North => new int3(from.X, from.Y, from.Z - 1),
            Facing.East => new int3(from.X + 1, from.Y, from.Z),
            Facing.South => new int3(from.X, from.Y, from.Z + 1),
            _ => new int3(from.X - 1, from.Y, from.Z),
        };

    /// <summary>The block one step against a facing.</summary>
    /// <param name="from">Where to start.</param>
    /// <param name="facing">Which way it points.</param>
    /// <returns>The block behind it.</returns>
    public static int3 Behind(int3 from, Facing facing) => Ahead(from, Opposite(facing));

    /// <summary>The facing pointing the other way.</summary>
    /// <param name="facing">The facing to turn around.</param>
    /// <returns>Its opposite.</returns>
    public static Facing Opposite(Facing facing) => (Facing)(((int)facing + 2) % 4);

    /// <summary>Which way something is turned, as a direction on the ground.</summary>
    /// <param name="facing">The facing.</param>
    /// <returns>A unit vector along it.</returns>
    public static double3 Along(Facing facing) =>
        facing switch
        {
            Facing.North => new double3(0.0, 0.0, -1.0),
            Facing.East => new double3(1.0, 0.0, 0.0),
            Facing.South => new double3(0.0, 0.0, 1.0),
            _ => new double3(-1.0, 0.0, 0.0),
        };

    /// <summary>Which way a player facing a heading would place something.</summary>
    /// <param name="yawRadians">Which way they are looking.</param>
    /// <returns>The nearest of the four.</returns>
    /// <remarks>
    /// Snapped from the camera rather than chosen from a menu. A belt placed in the direction the
    /// player was walking is a belt placed by walking and clicking, which is the only way anyone
    /// wants to lay a hundred of them.
    /// </remarks>
    public static Facing FromYaw(float yawRadians)
    {
        // The camera's yaw is measured so that zero looks along positive z, which is south here.
        var turns = yawRadians / double.Tau;
        var quarter = (int)double.Round(turns * 4.0) & 3;

        return quarter switch
        {
            0 => Facing.South,
            1 => Facing.West,
            2 => Facing.North,
            _ => Facing.East,
        };
    }
}
