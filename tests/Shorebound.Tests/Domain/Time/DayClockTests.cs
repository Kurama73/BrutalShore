using Shorebound.Domain.Time;

namespace Shorebound.Tests.Domain.Time;

/// <summary>Pins down how fast a day passes, and that it can be stopped.</summary>
public sealed class DayClockTests
{
    [Fact]
    public void the_clock_starts_at_the_hour_it_was_given()
    {
        var clock = new DayClock(startHour: 8.0);

        Assert.Equal(8.0, clock.Hour, 1e-9);
        Assert.Equal("08:00", clock.ToString());
    }

    [Fact]
    public void a_whole_day_length_takes_the_clock_right_round()
    {
        var clock = new DayClock(startHour: 0.0, dayLengthSeconds: 60.0);

        for (var tick = 0; tick < 60; tick++)
            clock.Step(1.0);

        // Counting whole days rather than wrapping is what lets the sun be asked for a fraction
        // without anything having to handle midnight.
        Assert.Equal(1.0, clock.DayFraction, 1e-9);
        Assert.Equal(0.0, clock.Hour, 1e-9);
    }

    [Fact]
    public void a_day_length_of_zero_stops_the_sun_where_it_is()
    {
        // Which is what --day-length 0 is for: two screenshots an hour apart are not comparable.
        var clock = new DayClock(startHour: 12.0, dayLengthSeconds: 0.0);

        for (var tick = 0; tick < 600; tick++)
            clock.Step(1.0);

        Assert.Equal(12.0, clock.Hour, 1e-9);
    }

    [Fact]
    public void a_frozen_clock_does_not_move_and_then_moves_again()
    {
        var clock = new DayClock(startHour: 6.0, dayLengthSeconds: 24.0) { IsFrozen = true };

        clock.Step(6.0);
        Assert.Equal(6.0, clock.Hour, 1e-9);

        clock.IsFrozen = false;
        clock.Step(6.0);
        Assert.Equal(12.0, clock.Hour, 1e-9);
    }

    [Fact]
    public void a_loaded_world_puts_the_clock_back_where_it_was()
    {
        var clock = new DayClock();

        clock.SetDayFraction(3.75);

        Assert.Equal(3.75, clock.DayFraction, 1e-9);
        Assert.Equal(18.0, clock.Hour, 1e-9);
    }
}
