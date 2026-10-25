namespace RustSpace.Orbital;

/// <summary>
/// The six classical elements describing a two-body Keplerian orbit.
/// </summary>
/// <remarks>
/// All angles are in radians. The frame convention is the usual astrodynamics one: a right-handed
/// reference frame with a reference plane (the parent body's equator or the ecliptic) and a
/// reference direction within it. The inclination tips the orbital plane away from the reference
/// plane, the longitude of the ascending node locates where the orbit crosses back north through
/// it, and the argument of periapsis locates periapsis within the tipped plane.
/// </remarks>
public readonly record struct OrbitalElements
{
    /// <summary>Semi-major axis, in metres: half the long axis of the ellipse.</summary>
    public required double SemiMajorAxisMeters { get; init; }

    /// <summary>
    /// Eccentricity, dimensionless. Zero is circular. <see cref="TwoBody"/> only handles closed
    /// orbits, so this must stay in <c>[0, 1)</c>.
    /// </summary>
    public required double Eccentricity { get; init; }

    /// <summary>Inclination of the orbital plane to the reference plane, in radians.</summary>
    public double InclinationRadians { get; init; }

    /// <summary>
    /// Longitude of the ascending node, in radians: the angle within the reference plane from the
    /// reference direction to the point where the orbit crosses it moving north.
    /// </summary>
    public double LongitudeOfAscendingNodeRadians { get; init; }

    /// <summary>
    /// Argument of periapsis, in radians: the angle from the ascending node to periapsis, measured
    /// within the orbital plane in the direction of motion.
    /// </summary>
    public double ArgumentOfPeriapsisRadians { get; init; }

    /// <summary>
    /// Mean anomaly at the elements' epoch, in radians.
    /// </summary>
    /// <remarks>
    /// Mean anomaly has no direct geometric meaning, but it advances exactly linearly with time
    /// (<c>MeanAnomalyAtEpoch + meanMotion * time</c>), which is what turns propagation into a
    /// closed-form evaluation at any time instead of a tick-by-tick integration.
    /// </remarks>
    public double MeanAnomalyAtEpochRadians { get; init; }

    /// <summary>Distance from the parent at periapsis, in metres.</summary>
    public double PeriapsisMeters => SemiMajorAxisMeters * (1.0 - Eccentricity);

    /// <summary>Distance from the parent at apoapsis, in metres.</summary>
    public double ApoapsisMeters => SemiMajorAxisMeters * (1.0 + Eccentricity);

    /// <summary>
    /// Mean motion in radians per second, for a parent with standard gravitational parameter
    /// <paramref name="mu"/> in m^3/s^2.
    /// </summary>
    public double MeanMotion(double mu) => double.Sqrt(mu / (SemiMajorAxisMeters * SemiMajorAxisMeters * SemiMajorAxisMeters));

    /// <summary>
    /// Orbital period in seconds, for a parent with standard gravitational parameter
    /// <paramref name="mu"/> in m^3/s^2.
    /// </summary>
    public double PeriodSeconds(double mu) => double.Tau / MeanMotion(mu);
}
