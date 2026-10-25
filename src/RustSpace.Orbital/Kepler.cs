namespace RustSpace.Orbital;

/// <summary>
/// Kepler's equation, and the anomaly conversion built on top of it.
/// </summary>
public static class Kepler
{
    /// <summary>
    /// Maximum Newton-Raphson iterations in <see cref="SolveEquation"/>.
    /// </summary>
    /// <remarks>
    /// Newton-Raphson on Kepler's equation converges quadratically for any eccentricity strictly
    /// below 1, so this bound is reached only by orbits with eccentricity approaching the parabolic
    /// limit; it exists to guarantee termination for those near-degenerate cases, not because
    /// convergence is otherwise in doubt.
    /// </remarks>
    private const int MaxIterations = 50;

    /// <summary>
    /// Convergence tolerance for <see cref="SolveEquation"/>, in radians.
    /// </summary>
    /// <remarks>
    /// At an orbital radius of 1e7 m, an error of 1e-12 rad in the eccentric anomaly corresponds to
    /// a position error on the order of <c>radius * tolerance</c> = 1e-5 m — a hundredth of a
    /// millimetre, far below anything a game or a human observer resolves.
    /// </remarks>
    private const double Tolerance = 1e-12;

    /// <summary>
    /// Solves Kepler's equation <c>M = E - e * sin(E)</c> for the eccentric anomaly, via
    /// Newton-Raphson.
    /// </summary>
    /// <remarks>
    /// Valid for closed elliptical orbits only: <c>0 &lt;= eccentricity &lt; 1</c>. Passing an
    /// eccentricity outside that range is a caller bug. The mean anomaly is not required to lie
    /// within a single revolution, since it is wrapped internally.
    /// </remarks>
    /// <param name="meanAnomalyRadians">Mean anomaly, in radians, over any number of revolutions.</param>
    /// <param name="eccentricity">Eccentricity in <c>[0, 1)</c>.</param>
    /// <returns>The eccentric anomaly in radians, in <c>(-pi, pi]</c>.</returns>
    public static double SolveEquation(double meanAnomalyRadians, double eccentricity)
    {
        // Wrapping first keeps every quantity in the iteration well-scaled, which matters once the
        // mean anomaly has accumulated over many orbits of simulated time at high time warp;
        // solving the unwrapped equation would still converge, but on ever less precise inputs.
        var meanAnomaly = WrapToPi(meanAnomalyRadians);

        // Standard starting guess: exact for a circular orbit, and close for moderate eccentricity
        // since it already accounts for the leading-order term of the equation of centre.
        var eccentricAnomaly = meanAnomaly + eccentricity * double.Sin(meanAnomaly);

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var residual = eccentricAnomaly - eccentricity * double.Sin(eccentricAnomaly) - meanAnomaly;
            var derivative = 1.0 - eccentricity * double.Cos(eccentricAnomaly);
            var step = residual / derivative;
            eccentricAnomaly -= step;
            if (double.Abs(step) < Tolerance)
                break;
        }

        return eccentricAnomaly;
    }

    /// <summary>
    /// Recovers the true anomaly from the eccentric anomaly, for a closed elliptical orbit.
    /// </summary>
    /// <remarks>
    /// Uses the <c>atan2</c> form of the half-angle identity rather than a bare
    /// <c>tan(E / 2)</c> conversion, so it stays accurate and well-defined across the full angle
    /// range instead of blowing up near +-pi.
    /// </remarks>
    public static double TrueAnomalyFromEccentric(double eccentricAnomalyRadians, double eccentricity)
    {
        var half = eccentricAnomalyRadians / 2.0;
        var y = double.Sqrt(1.0 + eccentricity) * double.Sin(half);
        var x = double.Sqrt(1.0 - eccentricity) * double.Cos(half);
        return 2.0 * double.Atan2(y, x);
    }

    /// <summary>
    /// Recovers the eccentric anomaly from the true anomaly: the inverse of
    /// <see cref="TrueAnomalyFromEccentric"/>.
    /// </summary>
    public static double EccentricAnomalyFromTrue(double trueAnomalyRadians, double eccentricity)
    {
        var half = trueAnomalyRadians / 2.0;
        var y = double.Sqrt(1.0 - eccentricity) * double.Sin(half);
        var x = double.Sqrt(1.0 + eccentricity) * double.Cos(half);
        return 2.0 * double.Atan2(y, x);
    }

    /// <summary>Wraps an angle in radians into <c>[-pi, pi]</c>.</summary>
    /// <remarks>
    /// The IEEE remainder is the right primitive here rather than a modulo: it subtracts the
    /// nearest whole multiple of a full turn, so the result stays centred on zero and keeps its
    /// precision even for an angle that has accumulated over millions of revolutions.
    /// </remarks>
    public static double WrapToPi(double angleRadians) => double.Ieee754Remainder(angleRadians, double.Tau);
}
