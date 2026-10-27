using BrutalShore.Gpu;

namespace Shorebound.Application.Automation;

/// <summary>A session that plays Shorebound from the title screen, with nobody at the keyboard.</summary>
/// <remarks>
/// <para>
/// Written against the real build, driven through the real input seam, and run with
/// <c>--playtest</c>. It is the one check in this repository that exercises what a player
/// actually touches — the title screen, the mouse look, holding a button to break a block,
/// walking over what fell, opening the panels — all of which are invisible to every test that
/// runs without a window.
/// </para>
/// <para>
/// What it is not is a judgement of whether the game is any good. It can say that a session of
/// several minutes runs without falling over, that the screens open and close, and that mining
/// puts something in the row; whether that is fun is the one question nothing here can answer.
/// </para>
/// <para>
/// The marks are the point of reading a run back: every stage writes a line, so the log lines up
/// against the script, and a session that stops early stops at a line that says where.
/// </para>
/// </remarks>
public static class Playtest
{
    /// <summary>Builds the session.</summary>
    /// <returns>A script the host can play.</returns>
    public static SessionScript Session()
    {
        var script = new SessionScript();

        script
            .Mark("waiting for the world")
            .Wait(6.0)

            // The title screen's Play is where the menu column starts, which is a little under
            // halfway down the left of the screen.
            .Mark("clicking play")
            .Click(GlfwMouseButton.Left)
            .Wait(10.0)

            .Mark("walking on the ground")
            .Wait(6.0)

            // Look down at the ground in front, which is where the first block comes from.
            .Mark("looking down")
            .Look(new double2(0.0, 260.0))
            .Wait(2.0);

        // Mine a few blocks, stepping onto each one as it falls so that it is picked up.
        for (var block = 0; block < 6; block++)
        {
            script
                .Mark($"breaking block {block + 1}")
                .HoldButton(GlfwMouseButton.Left, 2.4)
                .Hold(GlfwKey.W, 0.7)
                .Wait(0.6)
                .Hold(GlfwKey.S, 0.7)
                .Wait(0.6);
        }

        script
            .Mark("opening what I am carrying")
            .Tap(GlfwKey.I)
            .Wait(4.0)
            .Mark("closing it")
            .Tap(GlfwKey.I)
            .Wait(2.0)

            .Mark("reading what the shore asks")
            .Tap(GlfwKey.M)
            .Wait(8.0)
            .Mark("closing it")
            .Tap(GlfwKey.M)
            .Wait(2.0)

            .Mark("walking about")
            .Hold(GlfwKey.W, 3.0)
            .Look(new double2(400.0, -120.0))
            .Hold(GlfwKey.W, 3.0)
            .Wait(1.0)

            .Mark("picking along the row")
            .Scroll(1.0)
            .Wait(0.5)
            .Scroll(-1.0)
            .Wait(0.5)
            .Tap(GlfwKey.Number3)
            .Wait(1.0)

            .Mark("flying up for a look")
            .Tap(GlfwKey.F)
            .Hold(GlfwKey.Space, 3.0)
            .Look(new double2(0.0, -180.0))
            .Wait(2.0)

            .Mark("pausing")
            .Tap(GlfwKey.Escape)
            .Wait(3.0)
            .Mark("back in")
            .Tap(GlfwKey.Escape)
            .Wait(2.0)

            .Mark("done");

        return script;
    }
}
