namespace Shorebound.Tests;

/// <summary>
/// Health as a thing a horde takes away: what lands, what does not, and how it comes back.
/// </summary>
/// <remarks>
/// The grace window is the claim worth pinning down. Without it a creature standing inside the
/// player does its damage once a frame, which makes the whole fight a function of the frame rate
/// and kills a player on a fast machine in a tenth of a second.
/// </remarks>
public sealed class VitalityTests
{
    [Fact]
    public void a_fresh_player_is_whole()
    {
        var vitality = new Vitality();

        Assert.Equal(Vitality.Whole, vitality.Health);
        Assert.Equal(1f, vitality.Fraction);
        Assert.True(vitality.IsAlive);
    }

    [Fact]
    public void a_hit_takes_health()
    {
        var vitality = new Vitality();

        Assert.True(vitality.Hurt(3f));
        Assert.Equal(Vitality.Whole - 3f, vitality.Health);
    }

    [Fact]
    public void a_second_hit_inside_the_grace_window_does_not_land()
    {
        var vitality = new Vitality();
        vitality.Hurt(3f);

        Assert.False(vitality.Hurt(3f));
        Assert.Equal(Vitality.Whole - 3f, vitality.Health);
    }

    [Fact]
    public void the_grace_window_runs_out()
    {
        var vitality = new Vitality();
        vitality.Hurt(3f);
        vitality.Step(Vitality.GraceSeconds + 0.01);

        Assert.True(vitality.Hurt(3f));
    }

    [Fact]
    public void damage_does_not_depend_on_how_often_the_world_is_stepped()
    {
        // The property the grace window exists for. Two players taking the same beating over the
        // same ten seconds end up in the same state whatever the frame rate.
        var coarse = new Vitality();
        var fine = new Vitality();

        for (var step = 0; step < 100; step++)
        {
            coarse.Hurt(1f);
            coarse.Step(0.1);
        }

        for (var step = 0; step < 1000; step++)
        {
            fine.Hurt(1f);
            fine.Step(0.01);
        }

        Assert.Equal(coarse.Health, fine.Health, 0.2f);
    }

    [Fact]
    public void health_comes_back_once_nothing_has_landed_for_a_while()
    {
        var vitality = new Vitality();
        vitality.Hurt(8f);

        // Not yet: a player who healed the instant they stepped back would never build a wall.
        vitality.Step(3.0);
        Assert.Equal(Vitality.Whole - 8f, vitality.Health);

        vitality.Step(20.0);
        Assert.True(vitality.Health > Vitality.Whole - 8f);
    }

    [Fact]
    public void healing_stops_at_whole()
    {
        var vitality = new Vitality();
        vitality.Hurt(2f);
        vitality.Step(300.0);

        Assert.Equal(Vitality.Whole, vitality.Health);
    }

    [Fact]
    public void enough_of_it_kills()
    {
        var vitality = new Vitality();

        while (vitality.IsAlive)
        {
            vitality.Hurt(5f);
            vitality.Step(Vitality.GraceSeconds + 0.01);
        }

        Assert.Equal(0f, vitality.Health);
        Assert.False(vitality.Hurt(1f));
    }

    [Fact]
    public void the_dead_do_not_heal()
    {
        var vitality = new Vitality();
        while (vitality.IsAlive)
        {
            vitality.Hurt(Vitality.Whole);
            vitality.Step(Vitality.GraceSeconds + 0.01);
        }

        vitality.Step(300.0);

        Assert.False(vitality.IsAlive);
    }

    [Fact]
    public void respawning_puts_them_back_together()
    {
        var vitality = new Vitality();
        vitality.Hurt(Vitality.Whole);
        vitality.Restore();

        Assert.True(vitality.IsAlive);
        Assert.Equal(Vitality.Whole, vitality.Health);
    }

    [Fact]
    public void a_save_never_opens_on_a_dead_player()
    {
        var vitality = new Vitality();
        vitality.Restore(0f);

        Assert.True(vitality.IsAlive);
    }
}
