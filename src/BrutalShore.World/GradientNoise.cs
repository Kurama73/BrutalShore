namespace BrutalShore.World;

/// <summary>
/// Deterministic gradient noise, in two and three dimensions, with fractal summation on top.
/// </summary>
/// <remarks>
/// <para>
/// Classic Perlin construction: the lattice points around a sample each get a pseudo-random unit
/// gradient, the sample's offset from each is projected onto that gradient, and the results are
/// blended with a curve whose first and second derivatives vanish at the lattice points — which is
/// what makes the field smooth rather than visibly creased along the grid.
/// </para>
/// <para>
/// Gradients come from hashing the lattice coordinate with the seed rather than from a shuffled
/// permutation table. The table is the textbook implementation and is marginally faster, but it is
/// 512 bytes of state per seed and it repeats every 256 lattice points. A hash has no state, so the
/// same generator can be asked for a chunk at the far edge of the world, in any order, from any
/// thread, and give the same answer — which is the property a world that regenerates rather than
/// stores terrain is built on.
/// </para>
/// </remarks>
public sealed class GradientNoise(int seed)
{
    /// <summary>The seed every gradient in this field is derived from.</summary>
    public int Seed { get; } = seed;

    /// <summary>
    /// Samples the field at a point, in <c>[-1, 1]</c>.
    /// </summary>
    /// <remarks>
    /// Perlin noise with unit gradients is usually quoted as bounded by <c>sqrt(n)/2</c> in n
    /// dimensions — about 0.707 in 2D, 0.866 in 3D — and the scale factors here are the reciprocals
    /// of those. That bound is not tight for every gradient set, and this one overshoots it by a
    /// few percent in the worst configurations, so the result is clamped. Flattening a few
    /// thousandths of the extremes costs nothing a terrain height field can see, and it means
    /// callers that multiply this by a range get a value inside that range rather than nearly
    /// inside it.
    /// </remarks>
    public float Sample(float x, float y)
    {
        var cellX = (int)float.Floor(x);
        var cellY = (int)float.Floor(y);
        var fractionX = x - cellX;
        var fractionY = y - cellY;

        var smoothX = Smoothstep(fractionX);
        var smoothY = Smoothstep(fractionY);

        var bottom = Lerp(
            DotGradient(cellX, cellY, fractionX, fractionY),
            DotGradient(cellX + 1, cellY, fractionX - 1.0f, fractionY),
            smoothX
        );
        var top = Lerp(
            DotGradient(cellX, cellY + 1, fractionX, fractionY - 1.0f),
            DotGradient(cellX + 1, cellY + 1, fractionX - 1.0f, fractionY - 1.0f),
            smoothX
        );

        return float.Clamp(Lerp(bottom, top, smoothY) * 1.4142f, -1.0f, 1.0f);
    }

    /// <summary>Samples the field at a point in space, in <c>[-1, 1]</c>.</summary>
    /// <remarks>See <see cref="Sample(float, float)"/> for why the result is clamped.</remarks>
    public float Sample(float x, float y, float z)
    {
        var cellX = (int)float.Floor(x);
        var cellY = (int)float.Floor(y);
        var cellZ = (int)float.Floor(z);
        var fractionX = x - cellX;
        var fractionY = y - cellY;
        var fractionZ = z - cellZ;

        var smoothX = Smoothstep(fractionX);
        var smoothY = Smoothstep(fractionY);
        var smoothZ = Smoothstep(fractionZ);

        var near = Lerp(
            Lerp(
                DotGradient(cellX, cellY, cellZ, fractionX, fractionY, fractionZ),
                DotGradient(cellX + 1, cellY, cellZ, fractionX - 1.0f, fractionY, fractionZ),
                smoothX
            ),
            Lerp(
                DotGradient(cellX, cellY + 1, cellZ, fractionX, fractionY - 1.0f, fractionZ),
                DotGradient(cellX + 1, cellY + 1, cellZ, fractionX - 1.0f, fractionY - 1.0f, fractionZ),
                smoothX
            ),
            smoothY
        );

        var far = Lerp(
            Lerp(
                DotGradient(cellX, cellY, cellZ + 1, fractionX, fractionY, fractionZ - 1.0f),
                DotGradient(cellX + 1, cellY, cellZ + 1, fractionX - 1.0f, fractionY, fractionZ - 1.0f),
                smoothX
            ),
            Lerp(
                DotGradient(cellX, cellY + 1, cellZ + 1, fractionX, fractionY - 1.0f, fractionZ - 1.0f),
                DotGradient(cellX + 1, cellY + 1, cellZ + 1, fractionX - 1.0f, fractionY - 1.0f, fractionZ - 1.0f),
                smoothX
            ),
            smoothY
        );

        return float.Clamp(Lerp(near, far, smoothZ) * 1.1547f, -1.0f, 1.0f);
    }

    /// <summary>
    /// Sums several octaves of <see cref="Sample(float, float)"/>, each at twice the frequency and
    /// half the amplitude of the last, normalised back to <c>[-1, 1]</c>.
    /// </summary>
    /// <remarks>
    /// This is what turns one smooth hill into terrain: the first octave is the shape of the
    /// continent, the last is the shape of a boulder, and the halving is what makes the detail read
    /// as detail rather than as competing hills.
    /// </remarks>
    public float Fractal(float x, float y, int octaves, float lacunarity = 2.0f, float gain = 0.5f)
    {
        var total = 0.0f;
        var amplitude = 1.0f;
        var normalisation = 0.0f;
        var frequency = 1.0f;

        for (var octave = 0; octave < octaves; octave++)
        {
            total += Sample(x * frequency, y * frequency) * amplitude;
            normalisation += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }

        return normalisation > 0.0f ? total / normalisation : 0.0f;
    }

    /// <summary>
    /// Sums several octaves of creases rather than of hills, giving a field whose high ground is
    /// a network of sharp ridgelines.
    /// </summary>
    /// <param name="x">Where to sample, along the first axis.</param>
    /// <param name="y">Where to sample, along the second.</param>
    /// <param name="octaves">How many to sum.</param>
    /// <param name="lacunarity">How much finer each octave is than the one before it.</param>
    /// <param name="gain">How much quieter each octave is than the one before it.</param>
    /// <returns>A value in <c>[0, 1]</c>, 1 being the top of a ridge.</returns>
    /// <remarks>
    /// <para>
    /// Three changes to <see cref="Fractal(float, float, int, float, float)" />, each doing one
    /// job. Folding the field about zero — <c>1 - |sample|</c> — turns the smooth crossing where
    /// it changes sign into a crease, which is a ridgeline. Squaring the result sharpens that
    /// crease and flattens everything away from it, so the low ground between ridges becomes
    /// valley floor rather than more rolling hills. And weighting each octave by the one above it
    /// keeps the detail on the ridges: without it, the fine octaves put the same crumple into a
    /// valley floor as onto a summit, and the result reads as noise rather than as mountains.
    /// </para>
    /// <para>
    /// This is Musgrave's ridged multifractal, and the reason it is worth having beside plain
    /// fractal noise is that the two fail differently. Summed hills have no skyline: every peak
    /// is the same rounded shape as every valley, which is why terrain built from them alone
    /// looks like a crumpled sheet however many octaves are added.
    /// </para>
    /// </remarks>
    public float Ridged(float x, float y, int octaves, float lacunarity = 2.0f, float gain = 0.5f)
    {
        var total = 0.0f;
        var amplitude = 1.0f;
        var normalisation = 0.0f;
        var frequency = 1.0f;
        var weight = 1.0f;

        for (var octave = 0; octave < octaves; octave++)
        {
            var ridge = 1.0f - float.Abs(Sample(x * frequency, y * frequency));
            ridge *= ridge;
            ridge *= weight;

            // The next octave is only as loud as the ridge it is sitting on. Doubling before the
            // clamp means a crest carries its detail at full strength and a valley carries none.
            weight = float.Clamp(ridge * 2.0f, 0.0f, 1.0f);

            total += ridge * amplitude;
            normalisation += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }

        return normalisation > 0.0f ? float.Clamp(total / normalisation, 0.0f, 1.0f) : 0.0f;
    }

    /// <summary>Sums several octaves of the three-dimensional field.</summary>
    public float Fractal(float x, float y, float z, int octaves, float lacunarity = 2.0f, float gain = 0.5f)
    {
        var total = 0.0f;
        var amplitude = 1.0f;
        var normalisation = 0.0f;
        var frequency = 1.0f;

        for (var octave = 0; octave < octaves; octave++)
        {
            total += Sample(x * frequency, y * frequency, z * frequency) * amplitude;
            normalisation += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }

        return normalisation > 0.0f ? total / normalisation : 0.0f;
    }

    /// <summary>
    /// Perlin's fade curve, <c>6t^5 - 15t^4 + 10t^3</c>.
    /// </summary>
    /// <remarks>
    /// Its first and second derivatives are zero at 0 and 1. The first being zero is what removes
    /// the crease at a lattice point; the second being zero is what stops the shading of a surface
    /// derived from the field showing the grid, which a plain cubic smoothstep does.
    /// </remarks>
    private static float Smoothstep(float t) => t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>The 2D lattice gradient at a point, dotted with the offset to the sample.</summary>
    private float DotGradient(int cellX, int cellY, float offsetX, float offsetY)
    {
        // Eight directions around the circle, which is enough that the axis-aligned bias of four
        // is gone and cheap enough to select with three bits.
        var hash = Hash(cellX, cellY, 0);
        return (hash & 7) switch
        {
            0 => offsetX,
            1 => -offsetX,
            2 => offsetY,
            3 => -offsetY,
            4 => (offsetX + offsetY) * 0.7071f,
            5 => (offsetX - offsetY) * 0.7071f,
            6 => (-offsetX + offsetY) * 0.7071f,
            _ => (-offsetX - offsetY) * 0.7071f,
        };
    }

    /// <summary>The 3D lattice gradient at a point, dotted with the offset to the sample.</summary>
    private float DotGradient(int cellX, int cellY, int cellZ, float offsetX, float offsetY, float offsetZ)
    {
        // The twelve midpoints of a cube's edges: Perlin's own choice, and the one that avoids the
        // clumping that uniformly random directions in 3D produce.
        var hash = Hash(cellX, cellY, cellZ) & 15;
        var u = hash < 8 ? offsetX : offsetY;
        var v = hash < 4 ? offsetY : hash is 12 or 14 ? offsetX : offsetZ;
        return ((hash & 1) == 0 ? u : -u) + ((hash & 2) == 0 ? v : -v);
    }

    /// <summary>
    /// A hash of a lattice coordinate and the seed, well mixed in its low bits.
    /// </summary>
    /// <remarks>
    /// The large odd multipliers are the standard trick for folding several integers into one: each
    /// is coprime with 2^32, so none of them loses information, and the final avalanche is
    /// splitmix32's, whose low bits are what the gradient selection reads.
    /// </remarks>
    private uint Hash(int cellX, int cellY, int cellZ)
    {
        var hash = (uint)(cellX * 0x27D4EB2D) ^ (uint)(cellY * 0x165667B1) ^ (uint)(cellZ * 0x9E3779B1) ^ (uint)Seed;
        hash ^= hash >> 15;
        hash *= 0x2C1B3C6D;
        hash ^= hash >> 12;
        hash *= 0x297A2D39;
        hash ^= hash >> 15;
        return hash;
    }
}
