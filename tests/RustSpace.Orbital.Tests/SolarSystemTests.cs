using RustSpace.Orbital;

namespace RustSpace.Orbital.Tests;

public class SolarSystemTests
{
    private const double SecondsPerDay = 86_400.0;

    [Theory]
    // Sidereal periods in days, from the NASA/JPL planetary fact sheets. The tolerance is one part
    // in a thousand: tighter than that would be testing the fact sheet's rounding rather than the
    // elements, and looser would not catch a mistyped semi-major axis.
    [InlineData("Mercury", 87.969, 1e-3)]
    [InlineData("Venus", 224.701, 1e-3)]
    [InlineData("Earth", 365.256, 1e-3)]
    [InlineData("Mars", 686.980, 1e-3)]
    [InlineData("Jupiter", 4332.589, 1e-3)]
    [InlineData("Saturn", 10759.22, 1e-3)]
    [InlineData("Uranus", 30685.4, 1e-3)]
    [InlineData("Neptune", 60189.0, 1e-3)]
    // The Moon gets two parts in a thousand, and the reason is physics rather than slack. Its
    // published mean distance and its published sidereal period are not a consistent Keplerian
    // pair: the Sun perturbs the orbit strongly enough that the two numbers come out of different
    // reductions, and a two-body model cannot satisfy both at once. Propagating 3.84399e8 m gives
    // 27.284 days where the observed figure is 27.3217. Closing that last 0.14% needs solar
    // perturbation, which is a different model, not a better constant.
    [InlineData("Moon", 27.3217, 2e-3)]
    [InlineData("Io", 1.769, 1e-3)]
    [InlineData("Europa", 3.551, 1e-3)]
    [InlineData("Ganymede", 7.155, 1e-3)]
    [InlineData("Callisto", 16.689, 1e-3)]
    [InlineData("Titan", 15.945, 1e-3)]
    public void each_body_orbits_with_its_published_period(string name, double expectedDays, double relativeTolerance)
    {
        var body = Find(name);

        var periodDays = body.PeriodSeconds / SecondsPerDay;

        Assert.True(
            double.Abs(periodDays - expectedDays) / expectedDays < relativeTolerance,
            $"{name} orbits in {periodDays:F3} days, published {expectedDays:F3}"
        );
    }

    [Fact]
    public void the_sun_is_the_root_and_everything_else_hangs_off_it()
    {
        var bodies = SolarSystem.Create();

        var roots = bodies.Where(body => body.Parent is null).ToArray();

        Assert.Single(roots);
        Assert.Equal("Sun", roots[0].Name);
        Assert.All(bodies.Where(body => body.Parent is not null), body => Assert.NotNull(body.Orbit));
    }

    [Fact]
    public void a_moon_sits_within_its_planets_neighbourhood_rather_than_at_the_suns_origin()
    {
        // The point of the parent chain: the Moon's absolute position is Earth's plus its own
        // orbit. If the chain were dropped, the Moon would be propagated around the Sun's origin
        // and would sit 1 AU from Earth — a mistake that reads as "the moons are all in the middle
        // of the screen" and is worth a test rather than an eyeball.
        var bodies = SolarSystem.Create();
        var earth = bodies.Single(body => body.Name == "Earth");
        var moon = bodies.Single(body => body.Name == "Moon");

        foreach (var day in new[] { 0.0, 3.0, 11.0, 200.0, 4000.0 })
        {
            var separation = (moon.AbsolutePositionAt(day * SecondsPerDay) - earth.AbsolutePositionAt(day * SecondsPerDay))
                .Length();

            Assert.InRange(separation, 3.5e8, 4.1e8);
        }
    }

    [Fact]
    public void the_system_spans_the_seven_orders_of_magnitude_that_rule_out_f32()
    {
        // Stated as a test because it is the constraint the whole engine is built around: if this
        // ever stopped being true, f64 world positions would be over-engineering.
        var bodies = SolarSystem.Create();

        var farthest = bodies.Max(body => body.AbsolutePositionAt(0.0).Length());
        var smallest = bodies.Min(body => body.RadiusMeters);

        Assert.True(farthest / smallest > 1e6, $"span is only {farthest / smallest:E1}");
    }

    [Fact]
    public void depth_counts_the_parents_between_a_body_and_the_root()
    {
        Assert.Equal(0, Find("Sun").Depth);
        Assert.Equal(1, Find("Jupiter").Depth);
        Assert.Equal(2, Find("Europa").Depth);
    }

    private static CelestialBody Find(string name) => SolarSystem.Create().Single(body => body.Name == name);
}
