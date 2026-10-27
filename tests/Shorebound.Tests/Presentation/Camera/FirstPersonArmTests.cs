using Shorebound.Presentation.Camera;
using Shorebound.Presentation.Rendering.Creatures;

namespace Shorebound.Tests.Presentation.Camera;

/// <summary>Checks that the first-person arm remains one connected, camera-facing shape.</summary>
/// <remarks>
/// These tests cover the world-space geometry independently of the renderer so a perspective or
/// tilt change cannot silently pull the sleeve, hand or held block apart.
/// </remarks>
public sealed class FirstPersonArmTests
{
    [Fact]
    public void the_sleeve_forearm_and_hand_share_one_upward_axis()
    {
        var pose = FirstPersonArm.Lay(double3.Zero, cameraYaw: 0f, cameraPitch: 0f, swingMeters: 0.0);
        var sleeveEnd = pose.ShoulderMeters + (pose.Axis * pose.SleeveLengthMeters);
        var forearmStart = pose.ForearmMiddleMeters - (pose.Axis * (pose.ForearmLengthMeters / 2.0));
        var forearmEnd = pose.ForearmMiddleMeters + (pose.Axis * (pose.ForearmLengthMeters / 2.0));
        var armLength = double.Sqrt(double3.Dot(pose.Axis, pose.Axis));

        AssertVectorNear(sleeveEnd, forearmStart);
        AssertVectorNear(pose.HandMeters, forearmEnd);
        Assert.Equal(0.75, pose.SleeveLengthMeters + pose.ForearmLengthMeters, 1e-12);
        Assert.Equal(0.25f, pose.CrossSectionMeters);
        Assert.Equal(1.0, armLength, 1e-6);
        Assert.True(pose.HandMeters.Y > pose.ShoulderMeters.Y);
    }

    [Fact]
    public void the_arm_follows_the_camera_when_it_turns()
    {
        var pose = FirstPersonArm.Lay(double3.Zero, cameraYaw: float.Pi / 2f, cameraPitch: 0f, swingMeters: 0.0);

        Assert.Equal(0.0, pose.Axis.X, 1e-6);
        Assert.True(pose.Axis.Z > 0.0);
        Assert.Equal(0.0f, pose.TurnRadians, 1e-6f);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1.2f, 0.3f)]
    [InlineData(-2.1f, -0.5f)]
    public void the_arm_skin_rows_run_from_shoulder_to_hand(float yaw, float pitch)
    {
        var pose = FirstPersonArm.Lay(double3.Zero, yaw, pitch, swingMeters: 0.0);
        var (sineTurn, cosineTurn) = float.SinCos(pose.TurnRadians);
        var (sineTilt, cosineTilt) = float.SinCos(pose.TiltRadians);
        var localUp = new double3(sineTurn * sineTilt, cosineTilt, cosineTurn * sineTilt);

        // The shader performs this rotation in f32; one part per million bounds its angular rounding.
        AssertVectorNear(-pose.Axis, localUp, tolerance: 1e-6);
    }

    [Fact]
    public void the_forearm_hand_end_uses_the_skin_sheet_hand_patch()
    {
        var handEnd = MobSkins.Player.Forearm.Top;

        Assert.Equal(44f / 64f, handEnd.X);
        Assert.Equal(28f / 64f, handEnd.Y);
        Assert.Equal(4f / 64f, handEnd.Z);
        Assert.Equal(4f / 64f, handEnd.W);
    }

    private static void AssertVectorNear(double3 expected, double3 actual, double tolerance = 1e-9)
    {
        Assert.Equal(expected.X, actual.X, tolerance);
        Assert.Equal(expected.Y, actual.Y, tolerance);
        Assert.Equal(expected.Z, actual.Z, tolerance);
    }
}
