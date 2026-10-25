using RustSpace.Orbital;

namespace RustSpace.Orbital.Tests;

public class TwoBodyTests
{
    /// <summary>
    /// Earth's standard gravitational parameter, m^3/s^2 (NASA/JPL). Used here as a parent for
    /// orbits at a scale that can be checked against published numbers by hand.
    /// </summary>
    private const double EarthMu = 3.986004418e14;

    private static OrbitalElements CircularLowOrbit => new()
    {
        SemiMajorAxisMeters = 6.771e6, // about 400 km above the mean radius
        Eccentricity = 0.0,
    };

    [Fact]
    public void a_circular_orbit_keeps_a_constant_radius_all_the_way_round()
    {
        var mu = EarthMu;
        var period = CircularLowOrbit.PeriodSeconds(mu);

        for (var step = 0; step < 32; step++)
        {
            var state = TwoBody.StateAt(CircularLowOrbit, mu, period * step / 32.0);
            var radius = state.PositionMeters.Length();

            // A circle's radius is its semi-major axis exactly; the tolerance is the solver's
            // 1e-12 rad converted to metres at this radius, rounded up generously.
            Assert.True(
                double.Abs(radius - CircularLowOrbit.SemiMajorAxisMeters) < 1e-3,
                $"step {step} had radius {radius}"
            );
        }
    }

    [Fact]
    public void a_body_is_back_where_it_started_after_exactly_one_period()
    {
        var elements = new OrbitalElements
        {
            SemiMajorAxisMeters = 2.6e7,
            Eccentricity = 0.72,
            InclinationRadians = 0.5,
            LongitudeOfAscendingNodeRadians = 1.1,
            ArgumentOfPeriapsisRadians = 2.3,
            MeanAnomalyAtEpochRadians = 0.4,
        };
        var period = elements.PeriodSeconds(EarthMu);

        var start = TwoBody.StateAt(elements, EarthMu, 0.0);
        var afterOnePeriod = TwoBody.StateAt(elements, EarthMu, period);

        // Closure of the orbit, to within a millimetre at a 2.6e7 m scale: that ratio is about
        // 4e-11, which is the f64 round-off of the trigonometry, not drift in the propagator.
        Assert.True(
            (afterOnePeriod.PositionMeters - start.PositionMeters).Length() < 1e-3,
            $"moved {(afterOnePeriod.PositionMeters - start.PositionMeters).Length()} m"
        );
    }

    [Fact]
    public void a_thousand_periods_of_warp_cost_no_more_accuracy_than_one()
    {
        // The reason time warp is free: the propagator is a closed-form evaluation, so a jump of a
        // thousand orbits is one evaluation and accumulates nothing. An integrator would have
        // needed millions of steps here and would be visibly off by the end.
        var elements = new OrbitalElements { SemiMajorAxisMeters = 2.6e7, Eccentricity = 0.3 };
        var period = elements.PeriodSeconds(EarthMu);

        var start = TwoBody.StateAt(elements, EarthMu, 0.0);
        var afterOne = TwoBody.StateAt(elements, EarthMu, period);
        var afterAThousand = TwoBody.StateAt(elements, EarthMu, period * 1000.0);

        var errorAfterOne = (afterOne.PositionMeters - start.PositionMeters).Length();
        var errorAfterAThousand = (afterAThousand.PositionMeters - start.PositionMeters).Length();

        // Both are round-off, so the thousandth revolution is allowed to be no worse than ten
        // times the first — whereas a stepped integrator would be worse by orders of magnitude.
        Assert.True(
            errorAfterAThousand < double.Max(errorAfterOne * 10.0, 1e-3),
            $"one period: {errorAfterOne} m, a thousand: {errorAfterAThousand} m"
        );
    }

    [Fact]
    public void elements_recovered_from_a_state_describe_the_same_orbit()
    {
        var elements = new OrbitalElements
        {
            SemiMajorAxisMeters = 2.6e7,
            Eccentricity = 0.45,
            InclinationRadians = 0.7,
            LongitudeOfAscendingNodeRadians = 2.0,
            ArgumentOfPeriapsisRadians = 1.3,
            MeanAnomalyAtEpochRadians = 0.9,
        };

        var state = TwoBody.StateAt(elements, EarthMu, 0.0);
        var recovered = TwoBody.ElementsFromState(state.PositionMeters, state.VelocityMetersPerSecond, EarthMu);

        // Relative tolerance on the axis, absolute on the angles: both at 1e-9, which is where the
        // chain of acos and atan2 in the inverse bottoms out.
        Assert.True(
            double.Abs(recovered.SemiMajorAxisMeters - elements.SemiMajorAxisMeters) / elements.SemiMajorAxisMeters < 1e-9
        );
        Assert.True(double.Abs(recovered.Eccentricity - elements.Eccentricity) < 1e-9);
        Assert.True(double.Abs(recovered.InclinationRadians - elements.InclinationRadians) < 1e-9);
        Assert.True(
            double.Abs(recovered.LongitudeOfAscendingNodeRadians - elements.LongitudeOfAscendingNodeRadians) < 1e-9
        );
        Assert.True(double.Abs(recovered.ArgumentOfPeriapsisRadians - elements.ArgumentOfPeriapsisRadians) < 1e-9);
        Assert.True(double.Abs(recovered.MeanAnomalyAtEpochRadians - elements.MeanAnomalyAtEpochRadians) < 1e-9);
    }

    [Fact]
    public void sampling_an_orbit_by_true_anomaly_traces_the_same_curve_the_propagator_walks()
    {
        // A drawn orbit is sampled in true anomaly and a propagated body is solved in mean anomaly.
        // If those two disagreed, every body would sit slightly off the ellipse drawn under it —
        // which is the kind of error that looks like a rendering bug for a week.
        var elements = new OrbitalElements
        {
            SemiMajorAxisMeters = 2.6e7,
            Eccentricity = 0.6,
            InclinationRadians = 0.4,
            LongitudeOfAscendingNodeRadians = 1.0,
            ArgumentOfPeriapsisRadians = 2.0,
        };

        for (var step = 0; step < 16; step++)
        {
            var time = elements.PeriodSeconds(EarthMu) * step / 16.0;
            var propagated = TwoBody.StateAt(elements, EarthMu, time);

            var meanAnomaly = elements.MeanAnomalyAtEpochRadians + elements.MeanMotion(EarthMu) * time;
            var trueAnomaly = Kepler.TrueAnomalyFromEccentric(
                Kepler.SolveEquation(meanAnomaly, elements.Eccentricity),
                elements.Eccentricity
            );
            var sampled = TwoBody.StateAtTrueAnomaly(elements, EarthMu, trueAnomaly);

            Assert.True(
                (propagated.PositionMeters - sampled.PositionMeters).Length() < 1e-6,
                $"step {step} differed by {(propagated.PositionMeters - sampled.PositionMeters).Length()} m"
            );
        }
    }
}
