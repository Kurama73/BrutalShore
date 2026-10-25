namespace RustSpace.Orbital;

/// <summary>
/// Analytic two-body propagation: orbital elements to a Cartesian state, and back.
/// </summary>
/// <remarks>
/// Because the two-body problem has a closed-form solution, time warp is nothing more than
/// evaluating <see cref="StateAt"/> at a different time — there is no integrator to step forward,
/// and so no accumulated stepping error however large the jump.
/// </remarks>
public static class TwoBody
{
    /// <summary>
    /// A position and velocity in the parent's frame, in metres and metres per second.
    /// </summary>
    /// <param name="PositionMeters">Position relative to the parent body, in metres.</param>
    /// <param name="VelocityMetersPerSecond">Velocity relative to the parent body, in m/s.</param>
    public readonly record struct State(double3 PositionMeters, double3 VelocityMetersPerSecond);

    /// <summary>
    /// Computes position and velocity at <paramref name="timeSeconds"/> seconds since the elements'
    /// epoch, for a two-body Keplerian orbit around a parent with standard gravitational parameter
    /// <paramref name="mu"/> in m^3/s^2.
    /// </summary>
    /// <remarks>
    /// Valid for closed elliptical orbits: <c>0 &lt;= eccentricity &lt; 1</c>.
    /// </remarks>
    public static State StateAt(in OrbitalElements elements, double mu, double timeSeconds)
    {
        // Mean motion is constant for a two-body orbit, which is exactly what lets the mean anomaly
        // extrapolate linearly with time.
        var meanAnomaly = elements.MeanAnomalyAtEpochRadians + elements.MeanMotion(mu) * timeSeconds;

        var eccentricAnomaly = Kepler.SolveEquation(meanAnomaly, elements.Eccentricity);
        var trueAnomaly = Kepler.TrueAnomalyFromEccentric(eccentricAnomaly, elements.Eccentricity);

        return StateAtTrueAnomaly(elements, mu, trueAnomaly);
    }

    /// <summary>
    /// Computes position and velocity at a given true anomaly, which is what an orbit's drawn shape
    /// is sampled over — a fixed sweep of true anomaly traces the whole ellipse exactly once.
    /// </summary>
    public static State StateAtTrueAnomaly(in OrbitalElements elements, double mu, double trueAnomalyRadians)
    {
        var eccentricity = elements.Eccentricity;
        var eccentricAnomaly = Kepler.EccentricAnomalyFromTrue(trueAnomalyRadians, eccentricity);
        var distance = elements.SemiMajorAxisMeters * (1.0 - eccentricity * double.Cos(eccentricAnomaly));

        // Perifocal frame: x toward periapsis, y ninety degrees ahead of it in the direction of
        // motion, z along the orbit's angular momentum vector.
        var (sinTrue, cosTrue) = double.SinCos(trueAnomalyRadians);
        var positionPerifocal = new double3(distance * cosTrue, distance * sinTrue, 0.0);

        var semiLatusRectum = elements.SemiMajorAxisMeters * (1.0 - eccentricity * eccentricity);
        var angularMomentum = double.Sqrt(mu * semiLatusRectum);
        var velocityPerifocal = new double3(
            -mu / angularMomentum * sinTrue,
            mu / angularMomentum * (eccentricity + cosTrue),
            0.0
        );

        return new State(
            RotateToParentFrame(positionPerifocal, elements),
            RotateToParentFrame(velocityPerifocal, elements)
        );
    }

    /// <summary>
    /// Recovers orbital elements from a Cartesian state vector: the inverse of
    /// <see cref="StateAt"/>.
    /// </summary>
    /// <remarks>
    /// The returned <see cref="OrbitalElements.MeanAnomalyAtEpochRadians"/> describes the mean
    /// anomaly of this state itself, so the elements' epoch is the moment the state was sampled.
    /// <para>
    /// Only meaningful for a bound (elliptical) orbit. As with the classical element set in
    /// general, the longitude of the ascending node is undefined for an exactly equatorial orbit
    /// and the argument of periapsis is undefined for an exactly circular one, both dividing by a
    /// zero-length vector; neither case is special-cased here.
    /// </para>
    /// </remarks>
    public static OrbitalElements ElementsFromState(double3 positionMeters, double3 velocityMetersPerSecond, double mu)
    {
        var radius = positionMeters.Length();
        var speedSquared = velocityMetersPerSecond.LengthSquared();

        var angularMomentumVector = double3.Cross(positionMeters, velocityMetersPerSecond);
        var angularMomentum = angularMomentumVector.Length();

        // Node vector: lies along the intersection of the orbital and reference planes, pointing
        // toward the ascending node.
        var nodeVector = double3.Cross(double3.UnitZ, angularMomentumVector);
        var nodeMagnitude = nodeVector.Length();

        // Eccentricity vector: points toward periapsis, with magnitude e. Derived from the vector
        // form of the two-body equation of motion, not from energy and momentum scalars alone, so
        // it hands us both the eccentricity and the periapsis direction in one shot.
        var eccentricityVector =
            double3.Cross(velocityMetersPerSecond, angularMomentumVector) / mu - positionMeters / radius;
        var eccentricity = eccentricityVector.Length();

        // Vis-viva, solved for the semi-major axis.
        var semiMajorAxis = 1.0 / (2.0 / radius - speedSquared / mu);

        var inclination = AcosClamped(angularMomentumVector.Z / angularMomentum);

        var longitudeOfAscendingNode = AcosClamped(nodeVector.X / nodeMagnitude);
        if (nodeVector.Y < 0.0)
            longitudeOfAscendingNode = double.Tau - longitudeOfAscendingNode;

        var argumentOfPeriapsis = AcosClamped(
            double3.Dot(nodeVector, eccentricityVector) / (nodeMagnitude * eccentricity)
        );
        if (eccentricityVector.Z < 0.0)
            argumentOfPeriapsis = double.Tau - argumentOfPeriapsis;

        var trueAnomaly = AcosClamped(double3.Dot(eccentricityVector, positionMeters) / (eccentricity * radius));
        if (double3.Dot(positionMeters, velocityMetersPerSecond) < 0.0)
            trueAnomaly = double.Tau - trueAnomaly;

        // True anomaly to eccentric anomaly to mean anomaly: the inverse of the chain StateAt
        // walks forward.
        var eccentricAnomaly = Kepler.EccentricAnomalyFromTrue(trueAnomaly, eccentricity);
        var meanAnomalyAtEpoch = eccentricAnomaly - eccentricity * double.Sin(eccentricAnomaly);

        return new OrbitalElements
        {
            SemiMajorAxisMeters = semiMajorAxis,
            Eccentricity = eccentricity,
            InclinationRadians = inclination,
            LongitudeOfAscendingNodeRadians = longitudeOfAscendingNode,
            ArgumentOfPeriapsisRadians = argumentOfPeriapsis,
            MeanAnomalyAtEpochRadians = meanAnomalyAtEpoch,
        };
    }

    /// <summary>
    /// Classical 3-1-3 rotation from the perifocal frame into the parent frame: the argument of
    /// periapsis spins the orbit within its own plane, the inclination tips that plane, and the
    /// longitude of the ascending node swings the tipped plane around the parent's reference axis.
    /// </summary>
    /// <remarks>
    /// Written out as three composed plane rotations rather than built as a matrix: with the
    /// perifocal z component always zero, two thirds of a full matrix multiply would be multiplying
    /// by it.
    /// </remarks>
    private static double3 RotateToParentFrame(double3 perifocal, in OrbitalElements elements)
    {
        var (sinArgument, cosArgument) = double.SinCos(elements.ArgumentOfPeriapsisRadians);
        var (sinInclination, cosInclination) = double.SinCos(elements.InclinationRadians);
        var (sinNode, cosNode) = double.SinCos(elements.LongitudeOfAscendingNodeRadians);

        // Spin within the orbital plane by the argument of periapsis.
        var x = perifocal.X * cosArgument - perifocal.Y * sinArgument;
        var y = perifocal.X * sinArgument + perifocal.Y * cosArgument;

        // Tip the plane by the inclination, then swing it by the longitude of the ascending node.
        var tippedY = y * cosInclination;
        var tippedZ = y * sinInclination;

        return new double3(
            x * cosNode - tippedY * sinNode,
            x * sinNode + tippedY * cosNode,
            tippedZ
        );
    }

    /// <summary>
    /// <c>Acos</c>, clamped to <c>[-1, 1]</c> first.
    /// </summary>
    /// <remarks>
    /// The arguments passed to it here are ratios that are mathematically within that range, but
    /// floating-point round-off can push them fractionally past +-1, which would otherwise turn a
    /// valid orbit into a NaN.
    /// </remarks>
    private static double AcosClamped(double x) => double.Acos(double.Clamp(x, -1.0, 1.0));
}
