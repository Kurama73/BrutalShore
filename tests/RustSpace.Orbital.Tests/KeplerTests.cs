using RustSpace.Orbital;

namespace RustSpace.Orbital.Tests;

public class KeplerTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(0.6)]
    [InlineData(0.9)]
    [InlineData(0.99)]
    public void a_zero_mean_anomaly_solves_to_a_zero_eccentric_anomaly_at_any_eccentricity(double eccentricity)
    {
        // M = 0 is periapsis-aligned regardless of e: E - e*sin(E) = 0 has the exact root E = 0.
        var eccentricAnomaly = Kepler.SolveEquation(0.0, eccentricity);

        Assert.True(double.Abs(eccentricAnomaly) < 1e-13, $"e={eccentricity} gave E={eccentricAnomaly}");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(0.8)]
    [InlineData(0.95)]
    public void the_solved_eccentric_anomaly_satisfies_keplers_equation(double eccentricity)
    {
        // The property that actually matters: plugging the result back into E - e*sin(E)
        // reproduces the mean anomaly that was asked for, to within the documented tolerance.
        foreach (var meanAnomaly in new[] { -3.0, -0.5, 0.2, 1.7, 3.0, 7.5 })
        {
            var eccentricAnomaly = Kepler.SolveEquation(meanAnomaly, eccentricity);
            var residual = eccentricAnomaly
                - eccentricity * double.Sin(eccentricAnomaly)
                - Kepler.WrapToPi(meanAnomaly);

            Assert.True(double.Abs(residual) < 1e-10, $"e={eccentricity} M={meanAnomaly}: residual {residual}");
        }
    }

    [Theory]
    [InlineData(-2.5)]
    [InlineData(-0.1)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(2.9)]
    public void a_circular_orbit_has_a_true_anomaly_equal_to_its_eccentric_anomaly(double eccentricAnomaly)
    {
        // For e = 0 the ellipse is a circle and the two anomalies coincide by definition.
        var trueAnomaly = Kepler.TrueAnomalyFromEccentric(eccentricAnomaly, 0.0);

        Assert.True(double.Abs(trueAnomaly - eccentricAnomaly) < 1e-12);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.2)]
    [InlineData(0.7)]
    [InlineData(0.93)]
    public void converting_an_eccentric_anomaly_to_a_true_anomaly_and_back_returns_it(double eccentricity)
    {
        // The two conversions are each other's inverse, which is what lets a drawn orbit be
        // sampled in true anomaly and a propagated body be solved in mean anomaly without the two
        // disagreeing about where periapsis is.
        foreach (var eccentricAnomaly in new[] { -3.0, -1.2, 0.0, 0.4, 2.8 })
        {
            var trueAnomaly = Kepler.TrueAnomalyFromEccentric(eccentricAnomaly, eccentricity);
            var roundTripped = Kepler.EccentricAnomalyFromTrue(trueAnomaly, eccentricity);

            Assert.True(
                double.Abs(roundTripped - eccentricAnomaly) < 1e-12,
                $"e={eccentricity} E={eccentricAnomaly} came back as {roundTripped}"
            );
        }
    }

    [Fact]
    public void wrapping_an_angle_accumulated_over_a_million_turns_keeps_full_precision()
    {
        // This is the case time warp creates: a mean anomaly that has run up over millions of
        // revolutions. A modulo would have lost the fractional turn in the subtraction; the IEEE
        // remainder keeps it, and that fraction is the body's actual position.
        const double fraction = 0.25;
        var accumulated = 1_000_000.0 * double.Tau + fraction;

        var wrapped = Kepler.WrapToPi(accumulated);

        // 1e6 turns is about 6.3e6 rad, where an f64 resolves roughly 1e-9 rad; the tolerance is
        // that representation limit, not the algorithm's.
        Assert.True(double.Abs(wrapped - fraction) < 1e-8, $"wrapped to {wrapped}");
    }
}
