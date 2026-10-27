namespace Shorebound.Presentation.Camera;

/// <summary>The slow circle the camera flies while the title screen is up.</summary>
/// <remarks>
/// <para>
/// A title screen whose background is the game has one problem a painted one does not: the game
/// has to be looking at something. Standing where the player will spawn means looking at whatever
/// happens to be a metre in front of their face, which on a wooded coast is a tree trunk.
/// </para>
/// <para>
/// So the camera goes up and circles. High enough to see the coastline, tilted down enough that
/// the horizon sits in the upper third, and slowly enough that a full turn takes longer than
/// anyone spends on a menu — the movement is there to show the world is live, not to be watched.
/// </para>
/// </remarks>
public sealed class TitleCamera
{
    /// <summary>How far the camera orbits from the middle, in metres.</summary>
    private const double RadiusMeters = 46.0;

    /// <summary>How far above the ground it flies, in metres.</summary>
    private const double HeightMeters = 34.0;

    /// <summary>How long one full turn takes, in seconds.</summary>
    private const double TurnSeconds = 180.0;

    /// <summary>How far down it looks, in degrees.</summary>
    private const float PitchDegrees = -14f;

    private readonly double3 _middleMeters;
    private double _seconds;

    /// <summary>Starts the orbit around a point.</summary>
    /// <param name="middleMeters">What to circle, which is where the player would stand.</param>
    public TitleCamera(double3 middleMeters) => _middleMeters = middleMeters;

    /// <summary>Where the eye is, in world metres.</summary>
    public double3 EyeMeters =>
        new(
            _middleMeters.X + (RadiusMeters * double.Cos(Angle)),
            _middleMeters.Y + HeightMeters,
            _middleMeters.Z + (RadiusMeters * double.Sin(Angle))
        );

    /// <summary>Which way it faces, in radians, which is always inwards.</summary>
    /// <remarks>
    /// Yaw here is measured the way <see cref="BrutalShore.Render.Camera" /> measures it, so the
    /// direction that takes the eye back to the middle is half a turn from the angle it stands at.
    /// </remarks>
    public float Yaw => (float)(Angle + double.Pi * 0.5);

    /// <summary>How far down it looks, in radians.</summary>
    public float Pitch => float.DegreesToRadians(PitchDegrees);

    private double Angle => _seconds / TurnSeconds * double.Tau;

    /// <summary>Moves the camera along by one step.</summary>
    /// <param name="seconds">How much real time has passed.</param>
    public void Step(double seconds) => _seconds += seconds;
}
