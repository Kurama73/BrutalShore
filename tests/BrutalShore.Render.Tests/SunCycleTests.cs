namespace BrutalShore.Render.Tests;

/// <summary>
/// Pins down the shape of a day: where the sun is, and which way the palette moves with it.
/// </summary>
/// <remarks>
/// These assert the properties a renderer depends on — the sun stays on the unit sphere, it never
/// quite reaches the zenith, night is dark — rather than the exact colours, which are art and are
/// meant to be tuned without breaking a test.
/// </remarks>
public sealed class SunCycleTests
{
    /// <summary>
    /// The tilt used where a test needs a known one rather than whatever the default happens to be.
    /// </summary>
    private const float Tilt = 0.4f;

    [Fact]
    public void the_sun_sits_on_the_horizon_at_sunrise_and_at_sunset()
    {
        // The arc is built so that this holds for any tilt, which is the point of building it
        // that way; 1e-6 is a few f32 epsilons on a unit-length result.
        Assert.Equal(0f, SunCycle.SunDirectionAt(SunCycle.Sunrise, Tilt).Y, 1e-6f);
        Assert.Equal(0f, SunCycle.SunDirectionAt(SunCycle.Sunset, Tilt).Y, 1e-6f);
    }

    [Fact]
    public void the_sun_rises_in_the_east_and_sets_in_the_west()
    {
        Assert.True(SunCycle.SunDirectionAt(SunCycle.Sunrise, Tilt).X > 0.99f);
        Assert.True(SunCycle.SunDirectionAt(SunCycle.Sunset, Tilt).X < -0.99f);
    }

    [Fact]
    public void the_sun_is_highest_at_noon_and_lowest_at_midnight()
    {
        Assert.True(SunCycle.SunDirectionAt(0.5, Tilt).Y > 0.9f);
        Assert.True(SunCycle.SunDirectionAt(0.0, Tilt).Y < -0.9f);
    }

    [Fact]
    public void the_sun_stops_short_of_the_zenith_by_the_tilt_it_was_given()
    {
        var noon = SunCycle.SunDirectionAt(0.5, Tilt);

        // Stopping short is what keeps the four vertical sides of a block lit differently at
        // noon; without it the world reads flat for an hour.
        Assert.Equal(float.Cos(Tilt), noon.Y, 1e-6f);
        Assert.True(noon.Y < 1f);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.13)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.77)]
    [InlineData(0.99)]
    public void the_sun_direction_stays_on_the_unit_sphere_all_day(double dayFraction)
    {
        // Shaders normalise nothing: the direction goes straight into a dot product. 1e-6 is the
        // most a sine and a cosine in f32 can drift from unit length.
        Assert.Equal(1f, SunCycle.SunDirectionAt(dayFraction, Tilt).Length(), 1e-6f);
    }

    [Fact]
    public void a_day_fraction_beyond_one_day_wraps_onto_the_same_sky()
    {
        var first = SunCycle.At(0.31);
        var later = SunCycle.At(97.31);

        // A clock that only counts up is a valid argument, and has to land on the same sun. The
        // tolerance covers reducing a larger double onto the same f32 angle.
        Assert.Equal(first.SunDirection.X, later.SunDirection.X, 1e-6f);
        Assert.Equal(first.SunDirection.Y, later.SunDirection.Y, 1e-6f);
        Assert.Equal(first.SunDirection.Z, later.SunDirection.Z, 1e-6f);
    }

    [Fact]
    public void the_moon_stands_opposite_the_sun()
    {
        var sky = SunCycle.At(0.18);

        Assert.Equal(-sky.SunDirection.X, sky.MoonDirection.X, 1e-6f);
        Assert.Equal(-sky.SunDirection.Y, sky.MoonDirection.Y, 1e-6f);
        Assert.Equal(-sky.SunDirection.Z, sky.MoonDirection.Z, 1e-6f);
    }

    [Fact]
    public void daylight_runs_from_nothing_at_midnight_to_everything_at_noon()
    {
        Assert.Equal(0f, SunCycle.At(0.0).Daylight);
        Assert.Equal(1f, SunCycle.At(0.5).Daylight);
        Assert.True(SunCycle.At(0.0).StarOpacity > SunCycle.At(0.5).StarOpacity);
    }

    [Fact]
    public void neither_light_shines_while_it_is_below_the_horizon()
    {
        var midnight = SunCycle.At(0.0);
        var noon = SunCycle.At(0.5);

        Assert.Equal(0f, midnight.SunColor.Length());
        Assert.Equal(0f, noon.MoonColor.Length());
        Assert.True(midnight.MoonColor.Length() > 0f);
        Assert.True(noon.SunColor.Length() > 0f);
    }

    [Fact]
    public void the_shadow_casting_light_is_always_the_one_that_is_up()
    {
        for (var step = 0; step < 48; step++)
        {
            var sky = SunCycle.At(step / 48.0);

            Assert.True(sky.KeyDirection.Y >= 0f, $"the key light dipped below the horizon at {step / 48.0}");
        }
    }

    [Fact]
    public void the_horizon_reddens_at_sunset_and_not_at_noon()
    {
        var dusk = SunCycle.At(0.74).HorizonColor;
        var noon = SunCycle.At(0.5).HorizonColor;

        Assert.True(dusk.X > dusk.Z, "dusk should be warmer than it is blue");
        Assert.True(noon.Z > noon.X, "noon should be bluer than it is warm");
    }

    [Fact]
    public void the_sky_is_darker_overhead_than_at_the_horizon_in_both_day_and_night()
    {
        // A sky that is brighter overhead than at the horizon reads as a ceiling, not a sky.
        foreach (var dayFraction in new[] { 0.0, 0.5 })
        {
            var sky = SunCycle.At(dayFraction);

            Assert.True(
                sky.ZenithColor.Length() < sky.HorizonColor.Length(),
                $"the zenith outshone the horizon at {dayFraction}"
            );
        }
    }
}
