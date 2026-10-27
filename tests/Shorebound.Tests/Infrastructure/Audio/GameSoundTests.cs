using BrutalShore.Audio;
using Shorebound.Domain.World;
using Shorebound.Infrastructure.Audio;

namespace Shorebound.Tests.Infrastructure.Audio;

/// <summary>
/// Every sound the game makes, looked at rather than listened to: that none is silent, none
/// clips, none goes on too long, and that two materials do not sound the same.
/// </summary>
/// <remarks>
/// The reason a synthesised sound is worth the trouble. A recording can only be checked by
/// playing it; an array of numbers can be checked by a machine, and every one of these failures
/// is the kind nobody notices until they are wearing headphones.
/// </remarks>
public sealed class GameSoundTests
{
    [Fact]
    public void the_game_has_sounds()
    {
        Assert.NotEmpty(GameSounds.Catalogue());
    }

    [Fact]
    public void no_sound_is_silent()
    {
        foreach (var (name, sound) in GameSounds.Catalogue())
            Assert.True(sound.Peak > 0.1f, $"'{name}' is silent at {sound.Peak}");
    }

    [Fact]
    public void no_sound_clips()
    {
        foreach (var (name, sound) in GameSounds.Catalogue())
        {
            Assert.True(sound.Peak <= 1f, $"'{name}' peaks at {sound.Peak}");

            foreach (var sample in sound.Samples)
                Assert.InRange(sample, -1f, 1f);
        }
    }

    [Fact]
    public void every_sound_keeps_some_headroom()
    {
        // Several can play at once. A sound normalised to the very top is one that turns into a
        // crunch the moment a second one lands on the same frame.
        foreach (var (name, sound) in GameSounds.Catalogue())
            Assert.True(sound.Peak <= 0.92f, $"'{name}' peaks at {sound.Peak} with no room above it");
    }

    [Fact]
    public void nothing_goes_on_longer_than_it_should()
    {
        // These are all one-shots over a game. The longest is the milestone, which is allowed to
        // be a second or so because it happens four times in a playthrough.
        foreach (var (name, sound) in GameSounds.Catalogue())
            Assert.InRange(sound.Seconds, 0.02, 1.5);
    }

    [Fact]
    public void a_footfall_is_shorter_than_a_block_breaking()
    {
        // Which is the whole difference between the two: one impact against several.
        foreach (var pattern in Enum.GetValues<BlockPattern>())
            Assert.True(GameSounds.Step(pattern).Seconds < GameSounds.Breaking(pattern).Seconds);
    }

    [Fact]
    public void two_materials_do_not_sound_the_same()
    {
        // The claim that makes the pattern worth reusing for sound. Dirt and snow are drawn
        // differently and have to be heard differently, or the table is decoration.
        var crumbly = GameSounds.Breaking(BlockPattern.Crumbly).Samples.ToArray();
        var glinting = GameSounds.Breaking(BlockPattern.Glinting).Samples.ToArray();

        Assert.NotEqual(crumbly, glinting);
    }

    [Fact]
    public void the_same_block_always_sounds_the_same()
    {
        // Built fresh each time and still identical, which is what lets a sound be asserted
        // about at all. The variation a player hears is pitch at the moment it is played.
        var first = GameSounds.Breaking(BlockPattern.Mottled).Samples.ToArray();
        var second = GameSounds.Breaking(BlockPattern.Mottled).Samples.ToArray();

        Assert.Equal(first, second);
    }

    [Fact]
    public void every_block_pattern_has_a_sound_of_its_own()
    {
        // So that a block type added tomorrow is audible without anyone recording anything.
        var heard = new HashSet<string>();

        foreach (var pattern in Enum.GetValues<BlockPattern>())
        {
            var samples = GameSounds.Breaking(pattern).Samples.ToArray();
            Assert.True(heard.Add(string.Join(',', samples.Take(64))), $"{pattern} sounds like something else");
        }
    }

    [Fact]
    public void being_hurt_does_not_sound_like_anything_else()
    {
        // It has to be unmistakable with the volume down, because it is the one sound that is
        // information rather than texture.
        var hurt = GameSounds.Hurt();

        foreach (var (name, sound) in GameSounds.Catalogue())
        {
            if (name == "hurt")
                continue;

            Assert.NotEqual(hurt.Samples.ToArray(), sound.Samples.ToArray());
        }
    }

    [Fact]
    public void a_device_that_will_not_open_is_silent_rather_than_fatal()
    {
        // The property that keeps this game running on a machine with no sound: making the
        // device, uploading to it and playing through it all have to be safe when it failed.
        using var device = new AudioDevice();
        var sounds = new GameSounds(device, new GameBlocks(new BlockRegistry()).Registry);

        sounds.Crafted();
        sounds.Bitten();
        sounds.PickedUp();
        sounds.StalkerHeard(new float3(1f, 0f, 2f));
    }
}
