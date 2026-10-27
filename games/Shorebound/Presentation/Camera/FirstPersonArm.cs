using Shorebound.Presentation.Rendering.Creatures;

namespace Shorebound.Presentation.Camera;

/// <summary>Positions the arm and hand held in front of the player's camera.</summary>
/// <remarks>
/// The arm comes in from outside the lower-right of the view. Its boxes share one tilted axis so
/// the sleeve, forearm and held block meet instead of appearing as separate pieces. The arm runs
/// along the box's local vertical axis because its skin unwrap places the arm's length on that axis.
/// </remarks>
internal static class FirstPersonArm
{
    private const double LengthMeters = 0.75;
    private const float CrossSectionMeters = 0.25f;

    internal readonly record struct Pose(
        double3 ShoulderMeters,
        double3 Axis,
        double3 SleeveMiddleMeters,
        double3 ForearmMiddleMeters,
        double3 HandMeters,
        float TurnRadians,
        float TiltRadians,
        float CrossSectionMeters,
        double SleeveLengthMeters,
        double ForearmLengthMeters
    );

    internal static Pose Lay(double3 cameraPosition, float cameraYaw, float cameraPitch, double swingMeters)
    {
        var (sine, cosine) = double.SinCos(cameraYaw);
        var forward = new double3(cosine, 0.0, sine);
        var right = new double3(sine, 0.0, -cosine);
        var turn = (float.Pi / 2f) - cameraYaw;
        var elevation = 0.62f - (cameraPitch * 0.35f);
        // cube.vert's local-up axis turns toward negative forward for a positive tilt.
        var tilt = -((float.Pi / 2f) + elevation);

        var axis = (forward * double.Cos(elevation)) + new double3(0.0, double.Sin(elevation), 0.0);
        var shoulder =
            cameraPosition
            + (forward * (0.42 + swingMeters))
            - (right * 0.42)
            + new double3(0.0, -0.55 + (cameraPitch * 0.18), 0.0);

        var sleeveLength = LengthMeters * MobSkins.Player.SleeveShare;
        var forearmLength = LengthMeters - sleeveLength;

        return new Pose(
            shoulder,
            axis,
            shoulder + (axis * (sleeveLength * 0.5)),
            shoulder + (axis * (sleeveLength + (forearmLength * 0.5))),
            shoulder + (axis * LengthMeters),
            turn,
            tilt,
            CrossSectionMeters,
            sleeveLength,
            forearmLength
        );
    }
}
