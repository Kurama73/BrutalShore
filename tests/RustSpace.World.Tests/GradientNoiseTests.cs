namespace RustSpace.World.Tests;

public class GradientNoiseTests
{
    private readonly GradientNoise _noise = new(1234);

    [Fact]
    public void the_same_coordinate_always_gives_the_same_value()
    {
        // The property the whole regenerate-rather-than-store design rests on: a chunk asked for
        // again, in a later session, in a different order, has to come back identical.
        var first = new GradientNoise(99);
        var second = new GradientNoise(99);

        for (var step = 0; step < 50; step++)
        {
            var x = step * 3.7f - 40.0f;
            var y = step * -1.3f + 11.0f;

            Assert.Equal(first.Sample(x, y), second.Sample(x, y));
            Assert.Equal(first.Sample(x, y, x * 0.5f), second.Sample(x, y, x * 0.5f));
        }
    }

    [Fact]
    public void two_seeds_give_different_fields()
    {
        // Stated because the hash folds the seed in with the coordinate, and a hash that dropped it
        // would silently make every world the same world.
        var differences = 0;
        var other = new GradientNoise(4321);

        for (var step = 0; step < 50; step++)
            if (_noise.Sample(step * 1.7f, step * 0.3f) != other.Sample(step * 1.7f, step * 0.3f))
                differences++;

        // Not all fifty: two seeds can happen to select the same gradient at a given lattice cell,
        // and the field is exactly zero at a lattice point whatever the seed. A seed that was
        // dropped entirely would give zero differences, which is what this is guarding.
        Assert.True(differences > 40, $"only {differences} of 50 samples differed between seeds");
    }

    [Fact]
    public void samples_stay_inside_the_documented_range()
    {
        // The generator turns these into altitudes with a fixed multiplier, so a field that
        // overshot would put terrain outside the world's vertical bounds.
        for (var step = 0; step < 4000; step++)
        {
            var x = step * 0.137f - 200.0f;
            var y = step * 0.071f - 90.0f;
            var z = step * -0.211f + 40.0f;

            Assert.InRange(_noise.Sample(x, y), -1.0f, 1.0f);
            Assert.InRange(_noise.Sample(x, y, z), -1.0f, 1.0f);
            Assert.InRange(_noise.Fractal(x, y, octaves: 5), -1.0f, 1.0f);
        }
    }

    [Fact]
    public void the_field_is_continuous_across_a_lattice_boundary()
    {
        // A field with a discontinuity at the integer grid produces terrain with a cliff every 320
        // blocks, in a perfect square pattern. Comparing either side of a lattice point catches it.
        for (var lattice = -5; lattice <= 5; lattice++)
        {
            var before = _noise.Sample(lattice - 1e-4f, 0.3f);
            var after = _noise.Sample(lattice + 1e-4f, 0.3f);

            // The field's gradient is bounded by about 2 per unit, so a step of 2e-4 across the
            // boundary cannot legitimately change the value by more than a few thousandths.
            Assert.True(float.Abs(after - before) < 0.01f, $"jumped {after - before} at x = {lattice}");
        }
    }
}
