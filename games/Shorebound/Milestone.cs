using BrutalShore.Voxel;

namespace Shorebound;

/// <summary>One step of the game's progression: what it needs, what it costs, and what it opens up.</summary>
/// <remarks>
/// <para>
/// A milestone is a toll rather than a reward. It takes blocks out of the world permanently and
/// gives back the right to make something — which means the cost is paid in the thing the player
/// has just learned to produce, and a step is only reachable through the ones before it.
/// That is the whole shape of the game's middle: each tier is bought with the output of the last.
/// </para>
/// <para>
/// <see cref="Requires" /> is what makes the set of them a tree rather than a queue. A step names
/// the steps it builds on and nothing else — no position, no tier number — so the shape of the
/// progression is a consequence of the table instead of something kept in step with it.
/// </para>
/// <para>
/// The story line is part of the data and not decoration. Each milestone is the moment something
/// about the world becomes true, and a sentence at that moment is the only place this game has to
/// say what it is about.
/// </para>
/// </remarks>
/// <param name="Name">What the step is called, and how other steps refer to it.</param>
/// <param name="Story">A line of what it means, shown when it is still to do and once it is done.</param>
/// <param name="Requires">The names of the steps that have to be done before this one opens.</param>
/// <param name="Cost">What has to be handed in, by block and count.</param>
/// <param name="Unlocks">The names of the recipes it opens up.</param>
public readonly record struct Milestone(
    string Name,
    string Story,
    IReadOnlyList<string> Requires,
    IReadOnlyList<(BlockId Block, int Count)> Cost,
    IReadOnlyList<string> Unlocks
);
