namespace BrutalShore.World.Tests;

/// <summary>
/// What ridged noise has to be that plain fractal noise is not: bounded, repeatable, and creased.
/// </summary>
/// <remarks>
/// The claim that matters is the last one, and it is the only one worth the trouble of asserting:
/// the whole reason this exists beside <see cref="GradientNoise.Fractal(float, float, int, float,
/// float)" /> is that summed hills have no skyline. A test that only checked the range would pass
/// against an implementation that returned the fractal field shifted into <c>[0, 1]</c>.
/// </remarks>
public sealed class RidgedNoiseTests
{
    private readonly GradientNoise _noise = new(20_260);

    [Fact]
    public void a_ridged_field_stays_between_a_valley_floor_and_a_crest()
    {
        for (var step = 0; step < 4000; step++)
        {
            var value = _noise.Ridged(step * 0.037f, step * -0.021f, octaves: 5);

            Assert.InRange(value, 0.0f, 1.0f);
        }
    }

    [Fact]
    public void the_same_seed_gives_the_same_mountains_forever()
    {
        var again = new GradientNoise(20_260);

        Assert.Equal(_noise.Ridged(12.5f, -7.25f, octaves: 5), again.Ridged(12.5f, -7.25f, octaves: 5));
    }

    [Fact]
    public void a_different_seed_gives_different_mountains()
    {
        var elsewhere = new GradientNoise(20_261);

        Assert.NotEqual(_noise.Ridged(12.5f, -7.25f, octaves: 5), elsewhere.Ridged(12.5f, -7.25f, octaves: 5));
    }

    [Fact]
    public void a_ridged_field_is_continuous()
    {
        // No cliffs out of nowhere: terrain built on this has to be walkable, and a field that
        // could jump between neighbouring blocks would be a wall one block thick.
        var worst = 0.0f;

        for (var step = 0; step < 2000; step++)
        {
            var at = step * 0.01f;
            worst = float.Max(
                worst,
                float.Abs(_noise.Ridged(at, 3.5f, octaves: 5) - _noise.Ridged(at + 0.001f, 3.5f, octaves: 5))
            );
        }

        // A thousandth of a cell moves a five-octave field by at most a few thousandths: the
        // finest octave is sixteen times the frequency, and the gradient of one cell is bounded.
        Assert.True(worst < 0.02f, $"the field jumped by {worst} over a thousandth of a cell");
    }

    [Fact]
    public void a_ridge_is_a_crease_and_a_hill_is_not()
    {
        // The property the whole type exists for. A crease is curvature: along a line crossing
        // one, the field turns far more sharply than a sum of hills ever does. Measured as the
        // largest second difference along the same line through the same field.
        Assert.True(
            Sharpness(ridged: true) > Sharpness(ridged: false) * 3.0f,
            $"ridges bend at {Sharpness(ridged: true)}, hills at {Sharpness(ridged: false)}"
        );
    }

    [Fact]
    public void more_octaves_put_their_detail_on_the_ridges()
    {
        // The weighting is what separates this from folding a fractal field. Adding fine octaves
        // has to roughen the crests and leave the valley floors alone; without the weighting both
        // are roughened equally and the result is noise with a skyline drawn on it.
        var floors = 0;
        var floorsMoved = 0;
        var crests = 0;
        var crestsMoved = 0;

        for (var step = 0; step < 4000; step++)
        {
            var x = step * 0.013f;
            var few = _noise.Ridged(x, 1.5f, octaves: 2);
            var many = _noise.Ridged(x, 1.5f, octaves: 6);
            var moved = float.Abs(many - few) > 0.1f;

            if (few <= 0.15f)
            {
                floors++;
                floorsMoved += moved ? 1 : 0;
            }
            else if (few >= 0.8f)
            {
                crests++;
                crestsMoved += moved ? 1 : 0;
            }
        }

        Assert.True(floors > 100 && crests > 100, $"only {floors} floors and {crests} crests to judge by");

        var floorRate = (float)floorsMoved / floors;
        var crestRate = (float)crestsMoved / crests;

        Assert.True(
            crestRate > floorRate * 3.0f,
            $"the fine octaves moved {crestRate:P0} of crests and {floorRate:P0} of valley floors"
        );
    }

    private float Sharpness(bool ridged)
    {
        var worst = 0.0f;
        const float Step = 0.004f;

        for (var index = 1; index < 3000; index++)
        {
            var at = index * Step;
            var before = Sample(at - Step, ridged);
            var here = Sample(at, ridged);
            var after = Sample(at + Step, ridged);

            worst = float.Max(worst, float.Abs(after - (2.0f * here) + before));
        }

        return worst;
    }

    private float Sample(float x, bool ridged) =>
        ridged ? _noise.Ridged(x, 0.75f, octaves: 5) : _noise.Fractal(x, 0.75f, octaves: 5);
}
