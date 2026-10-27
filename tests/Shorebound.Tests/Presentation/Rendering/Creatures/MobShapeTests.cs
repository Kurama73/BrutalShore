using Shorebound.Domain.Creatures;
using Shorebound.Domain.Movement;
using Shorebound.Presentation.Rendering.Creatures;

namespace Shorebound.Tests.Presentation.Rendering.Creatures;

/// <summary>Checks that the Faithful-textured creatures fit the boxes that stop them in the world.</summary>
/// <remarks>
/// Each rendered part is rotated by the creature's yaw, so these checks project its corners into
/// the same upright collision bounds used by movement and spawning.
/// </remarks>
public sealed class MobShapeTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(0.7f)]
    [InlineData(1.5707964f)]
    public void a_grazers_rendered_parts_fit_its_collision_box(float yaw)
    {
        var feet = new double3(2.5, 41.0, -3.5);
        var grazer = new Grazer
        {
            Footing = new Footing { FeetMeters = feet, StrideRadians = 1f },
            Yaw = yaw,
        };
        Span<GrazerShape.Part> parts = stackalloc GrazerShape.Part[GrazerShape.PartCount];

        GrazerShape.Lay(grazer, parts);

        AssertFits(parts, feet, Grazers.Size, yaw);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.7f)]
    [InlineData(1.5707964f)]
    public void a_stalkers_rendered_parts_fit_its_collision_box(float yaw)
    {
        var feet = new double3(-2.5, 21.0, 3.5);
        var stalker = new Stalker
        {
            Footing = new Footing { FeetMeters = feet, StrideRadians = 1f },
            Yaw = yaw,
        };
        Span<GrazerShape.Part> parts = stackalloc GrazerShape.Part[StalkerShape.PartCount];

        StalkerShape.Lay(stalker, parts);

        AssertFits(parts, feet, Stalkers.Size, yaw);
    }

    private static void AssertFits(
        ReadOnlySpan<GrazerShape.Part> parts,
        double3 feet,
        double3 localSize,
        float yaw
    )
    {
        var collisionSize = Walking.OrientedSize(localSize, yaw);
        var (sine, cosine) = double.SinCos(yaw);
        var sin = double.Abs(sine);
        var cos = double.Abs(cosine);
        const double tolerance = 0.002;

        foreach (var part in parts)
        {
            var halfX = ((cos * part.SizeMeters.X) + (sin * part.SizeMeters.Z)) / 2.0;
            var halfZ = ((sin * part.SizeMeters.X) + (cos * part.SizeMeters.Z)) / 2.0;

            Assert.True(part.MiddleMeters.X - halfX >= feet.X - (collisionSize.X / 2.0) - tolerance);
            Assert.True(part.MiddleMeters.X + halfX <= feet.X + (collisionSize.X / 2.0) + tolerance);
            Assert.True(part.MiddleMeters.Y - (part.SizeMeters.Y / 2.0) >= feet.Y - tolerance);
            Assert.True(part.MiddleMeters.Y + (part.SizeMeters.Y / 2.0) <= feet.Y + collisionSize.Y + tolerance);
            Assert.True(part.MiddleMeters.Z - halfZ >= feet.Z - (collisionSize.Z / 2.0) - tolerance);
            Assert.True(part.MiddleMeters.Z + halfZ <= feet.Z + (collisionSize.Z / 2.0) + tolerance);
        }
    }
}
