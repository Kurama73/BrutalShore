using Shorebound.Domain.Movement;

namespace Shorebound.Tests.Domain.Movement;

/// <summary>Checks the body bounds used to collide creatures against the voxel world.</summary>
/// <remarks>
/// Rendering turns a creature around its upright axis; collision uses an upright box, so the
/// horizontal dimensions have to be projected into that box before movement.
/// </remarks>
public sealed class WalkingTests
{
    [Fact]
    public void an_unturned_body_keeps_its_local_dimensions()
    {
        var size = new double3(0.62, 0.9, 1.42);

        Assert.Equal(size, Walking.OrientedSize(size, yawRadians: 0f));
    }

    [Fact]
    public void a_quarter_turn_swaps_the_horizontal_collision_dimensions()
    {
        var size = Walking.OrientedSize(new double3(0.62, 0.9, 1.42), yawRadians: float.Pi / 2f);

        Assert.Equal(1.42, size.X, 1e-6);
        Assert.Equal(0.9, size.Y, 1e-9);
        Assert.Equal(0.62, size.Z, 1e-6);
    }

    [Fact]
    public void a_diagonal_body_uses_the_projected_world_bounds()
    {
        var yaw = float.Pi / 4f;
        var size = Walking.OrientedSize(new double3(0.62, 0.9, 1.42), yaw);
        var expected = (0.62 + 1.42) * double.Sqrt(0.5);

        // Float yaw is the input, so 1e-6 covers its rounding while still catching a wrong axis.
        Assert.Equal(expected, size.X, 1e-6);
        Assert.Equal(expected, size.Z, 1e-6);
    }
}
