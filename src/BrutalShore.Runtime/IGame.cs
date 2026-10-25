namespace BrutalShore.Runtime;

/// <summary>What the host needs a game to be.</summary>
/// <remarks>
/// <para>
/// The engine knows a game by these six members and nothing else. Everything a game is actually
/// made of — its blocks, its terrain, its controls, where the player starts — lives on the other
/// side of this interface, which is what lets a second game exist without the engine changing.
/// </para>
/// <para>
/// A game holds GPU resources, so it must be disposed <em>before</em> the host that owns the
/// device. Declaring the host first and the game second in a pair of <c>using</c> statements gets
/// that right, because they are disposed in reverse order of declaration.
/// </para>
/// </remarks>
public interface IGame : IDisposable
{
    /// <summary>One line describing what the run did, printed when the window closes.</summary>
    string Summary { get; }

    /// <summary>Creates everything that needs a device: pipelines, meshes, the world.</summary>
    /// <remarks>Called once, after the window and the device exist and before the first frame.</remarks>
    void Load(GameContext context);

    /// <summary>Advances the simulation by one fixed step.</summary>
    /// <param name="seconds">The step's length, always the same value.</param>
    /// <remarks>
    /// Called zero or more times per frame. Anything whose result must not depend on the frame rate
    /// — movement, physics, anything integrated over time — belongs here rather than in
    /// <see cref="Update" />.
    /// </remarks>
    void Step(double seconds);

    /// <summary>Does the once-a-frame work that is not drawing: streaming, meshing, uploads.</summary>
    /// <remarks>
    /// Called before the frame is begun, so it may still be called on a frame that is never drawn —
    /// which is deliberate, since a minimised window should not stop the world from loading.
    /// </remarks>
    void Update(GameContext context);

    /// <summary>Records the game's draw commands into the frame being built.</summary>
    void Draw(GameContext context);

    /// <summary>Adds the game's own rows to the debug overlay.</summary>
    /// <remarks>
    /// Called inside the overlay's window, between the host's rows and its end. The host reports
    /// what it owns — the device, the frame rate — and nothing else, because it has no idea what
    /// this game would want to show.
    /// </remarks>
    void DrawOverlay();
}
