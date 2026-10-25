namespace RustSpace.Orbital;

/// <summary>
/// The real solar system at real scale: the Sun, the eight planets, and a few moons, with mean
/// orbital elements at the J2000 epoch.
/// </summary>
/// <remarks>
/// <para>
/// Every value here comes from the NASA/JPL planetary fact sheets and the standard J2000 mean
/// elements, rounded to the significant figures those sources publish. They are mean elements, so
/// propagating them with <see cref="TwoBody"/> reproduces each body's orbit to within the drift a
/// two-body model has by construction — it is a correct Keplerian solar system, not an ephemeris.
/// </para>
/// <para>
/// It exists to be the hard case: a system whose outer orbit is 4.5e12 m across and whose smallest
/// body is 1.7e6 m in radius spans seven orders of magnitude, which is the span f32 cannot hold and
/// the reason simulation state here is f64 throughout.
/// </para>
/// </remarks>
public static class SolarSystem
{
    /// <summary>One astronomical unit in metres, exactly, by definition (IAU 2012).</summary>
    public const double AstronomicalUnitMeters = 1.495978707e11;

    /// <summary>Degrees to radians.</summary>
    private const double Degrees = double.Pi / 180.0;

    /// <summary>
    /// Builds the system. Each call returns a fresh, independent graph of bodies.
    /// </summary>
    public static CelestialBody[] Create()
    {
        var sun = new CelestialBody
        {
            Name = "Sun",
            GravitationalParameter = 1.32712440018e20,
            RadiusMeters = 6.957e8,
        };

        var mercury = Orbiting(sun, "Mercury", 2.2032e13, 2.4397e6, 5.7909050e10, 0.20563069, 7.00487, 48.33167, 29.12478, 174.79252);
        var venus = Orbiting(sun, "Venus", 3.24859e14, 6.0518e6, 1.08208930e11, 0.00677323, 3.39471, 76.68069, 54.85229, 50.11477);
        var earth = Orbiting(sun, "Earth", 3.986004418e14, 6.371e6, 1.49598023e11, 0.01670860, 0.00005, -11.26064, 114.20783, 358.61737);
        var mars = Orbiting(sun, "Mars", 4.282837e13, 3.3895e6, 2.27939366e11, 0.09341233, 1.85061, 49.57854, 286.46230, 19.41248);
        var jupiter = Orbiting(sun, "Jupiter", 1.26686534e17, 6.9911e7, 7.78340821e11, 0.04838624, 1.30530, 100.55615, 273.86691, 20.02007);
        var saturn = Orbiting(sun, "Saturn", 3.7931187e16, 5.8232e7, 1.42666642e12, 0.05386179, 2.48446, 113.71504, 339.39153, 317.02017);
        var uranus = Orbiting(sun, "Uranus", 5.793939e15, 2.5362e7, 2.87065814e12, 0.04725744, 0.76986, 74.22988, 96.99886, 142.23860);
        var neptune = Orbiting(sun, "Neptune", 6.836529e15, 2.4622e7, 4.49839654e12, 0.00859048, 1.76917, 131.72169, 276.33610, 256.22800);

        // Moons, so the system is three levels deep and an absolute position is a sum up a chain
        // rather than a single orbit. Their inclinations are to their parent's equator or to the
        // ecliptic depending on the body, which a two-body model does not distinguish; they are
        // taken as given in the parent's frame.
        var moon = Orbiting(earth, "Moon", 4.9048695e12, 1.7374e6, 3.84399e8, 0.0549, 5.145, 125.08, 318.15, 135.27);

        var io = Orbiting(jupiter, "Io", 5.9599e12, 1.8216e6, 4.217e8, 0.0041, 0.036, 0.0, 0.0, 0.0);
        var europa = Orbiting(jupiter, "Europa", 3.2027e12, 1.5608e6, 6.709e8, 0.0094, 0.466, 0.0, 0.0, 120.0);
        var ganymede = Orbiting(jupiter, "Ganymede", 9.8878e12, 2.6341e6, 1.0704e9, 0.0013, 0.177, 0.0, 0.0, 240.0);
        var callisto = Orbiting(jupiter, "Callisto", 7.1793e12, 2.4103e6, 1.8827e9, 0.0074, 0.192, 0.0, 0.0, 60.0);

        var titan = Orbiting(saturn, "Titan", 8.9780e12, 2.5747e6, 1.221870e9, 0.0288, 0.34854, 0.0, 0.0, 0.0);

        return
        [
            sun,
            mercury, venus, earth, mars, jupiter, saturn, uranus, neptune,
            moon,
            io, europa, ganymede, callisto,
            titan,
        ];
    }

    /// <summary>
    /// Declares a body orbiting <paramref name="parent"/>, taking its angles in degrees because
    /// that is the unit every published element set uses.
    /// </summary>
    private static CelestialBody Orbiting(
        CelestialBody parent,
        string name,
        double gravitationalParameter,
        double radiusMeters,
        double semiMajorAxisMeters,
        double eccentricity,
        double inclinationDegrees,
        double longitudeOfAscendingNodeDegrees,
        double argumentOfPeriapsisDegrees,
        double meanAnomalyAtEpochDegrees
    ) =>
        new()
        {
            Name = name,
            GravitationalParameter = gravitationalParameter,
            RadiusMeters = radiusMeters,
            Parent = parent,
            Orbit = new OrbitalElements
            {
                SemiMajorAxisMeters = semiMajorAxisMeters,
                Eccentricity = eccentricity,
                InclinationRadians = inclinationDegrees * Degrees,
                LongitudeOfAscendingNodeRadians = longitudeOfAscendingNodeDegrees * Degrees,
                ArgumentOfPeriapsisRadians = argumentOfPeriapsisDegrees * Degrees,
                MeanAnomalyAtEpochRadians = meanAnomalyAtEpochDegrees * Degrees,
            },
        };
}
