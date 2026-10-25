# BrutalShore

An engine, and a voxel game on it, in C# on
[BRUTAL](https://github.com/ahwootech/BRUTAL) — the open-source framework behind
Kitten Space Agency.

![A wooded coastline under a low sun, the seabed showing through the water](docs/coastline.png)

An unbounded world of 32³ chunks, generated from a seed rather than stored,
streamed around the player on worker threads, greedy-meshed with ambient
occlusion, and drawn through a Vulkan renderer written here: a sky with a sun
and a moon crossing it, shadows cast from whichever is up, water you can see
through and swim in, and biomes from desert to tundra whose forests span chunk
boundaries without anything being saved.

![The same coast with the debug overlay open, showing the frame time graph](docs/overlay.png)

## Running it

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download). On macOS it also
needs a Vulkan driver and loader, which BRUTAL does not ship:

```bash
brew install molten-vk vulkan-loader
```

Nothing else: the loader is found and registered from inside the process, so no
environment variables have to be set. If either is missing, the game says so and
names the command above.

```bash
dotnet run --project games/Shorebound -c Release
```

Click `Play`, then `WASD` to move, `space` to jump, `shift` to sprint, `F` to
switch between walking and flying — `space` and `ctrl` go up and down while
flying, and `space` swims up in water. Whichever block is in reach of the
crosshair is outlined: left click breaks it, right click places against the face
being looked at, and the number keys or `Q` and `E` pick which block that is.
`Escape` opens the menu, where the world can be saved; `F3` hides the overlay.

A world is its seed plus the blocks you have changed, so a save holds nothing
else: an untouched world is about a hundred bytes however far you have walked.

### Options

| Flag                               | What it does                                     |
| ---------------------------------- | ------------------------------------------------ |
| `--seed <n>`                       | The world seed. The same seed is the same world. |
| `--view-radius <chunks>`           | How far chunks stay loaded. Default 8.           |
| `--time <hour>`                    | The hour the world starts at. Default 8.         |
| `--day-length <seconds>`           | Real seconds per in-game day; 0 stops the sun.   |
| `--save <path>`                    | Where the world is kept. Default `shorebound.world`. |
| `--no-overlay`                     | Start with the debug overlay hidden.             |
| `--validation`                     | Turn on the Vulkan validation layers.            |
| `--frames <n>`                     | Close after this many frames.                    |
| `--camera <x> <y> <z> <yaw> <pitch>` | Start the camera here, angles in degrees.      |
| `--screenshot <path>`              | Save the last frame as a PNG.                    |

Passing `--frames` or `--camera` goes straight into the world rather than
stopping at the title screen: a run with a fixed frame count is a render check,
and there is nobody there to click past a menu.

The last three together are how graphics changes are checked: a fixed seed and a
fixed camera for a fixed number of frames produce an image that can be compared
against the one before. Add `--day-length 0` to stop the sun as well, or two
runs an hour apart are not comparable.

```bash
dotnet run --project games/Shorebound -c Release -- \
    --frames 400 --camera -140 190 -140 45 -22 --screenshot coastline.png
```

### Checking it without a GPU

The same run works on a machine with no graphics card at all, which is what
makes it usable from CI or a remote shell. Mesa's `lavapipe` is a Vulkan
implementation on the CPU, and GLFW only needs some X server to open a window
on:

```bash
sudo apt install mesa-vulkan-drivers vulkan-validationlayers xvfb
Xvfb :99 -screen 0 1600x900x24 &
export DISPLAY=:99 VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json

dotnet run --project games/Shorebound -c Release -- \
    --frames 400 --camera -140 190 -140 45 -22 --day-length 0 \
    --validation --screenshot coastline.png
```

It renders at a few frames a second, which a fixed frame count makes irrelevant.
Add `--validation` whenever GPU code changes: the validation layers catch the
class of mistake that works anyway on one driver and corrupts memory on the
next, and they found a real one here — a swapchain image being copied from
without having asked for the usage that allows it.

## Layout

`src/` is the engine and `games/` is what is built with it. **Nothing in `src/`
references `games/`** — the engine is what would still make sense if Shorebound
were deleted.

| Engine project        | What is in it                                               |
| --------------------- | ----------------------------------------------------------- |
| `BrutalShore.Core`    | The frame clock and the f64 world to f32 render-space boundary |
| `BrutalShore.Voxel`   | Chunks, the block registry, the greedy mesher                 |
| `BrutalShore.Gpu`     | Window, Vulkan device, swapchain, frames in flight, shaders   |
| `BrutalShore.Ui`      | What every panel looks like: the palette, the ImGui theme, the shared widgets |
| `BrutalShore.World`   | Coordinates, gradient noise, streaming, block edits, ray casting and collision, `IChunkGenerator` |
| `BrutalShore.Render`  | Camera, view frustum, the sun and sky, shadows, the voxel pipeline, chunk meshes |
| `BrutalShore.Runtime` | The window, the device and the frame loop around an `IGame`   |

| Game            | What is in it                                        |
| --------------- | ---------------------------------------------------- |
| `Shorebound`    | Its blocks, its terrain, caves and biomes, its trees, the player and how they move, the controls, the hotbar, the menus, the save file, how long a day lasts, how much work a frame may spend loading and meshing |

A game is an `IGame` the runtime runs, and an `IChunkGenerator` the world asks
for blocks. Those two interfaces are the whole of the engine's knowledge of it.

Dependencies point downwards only. Everything that does not need a device is
tested without one: `dotnet test` runs the mesher, the noise, the streaming, the
block edits, the ray cast and the collision, the camera, the frustum, the sun's
arc, the shadow box, and Shorebound's own terrain, biomes, trees, player and
save file.

## How the world is drawn

A chunk is 32³ block ids. Meshing it is three reductions, in order: a face is
only built if what is in front of it does not hide it — air hides nothing, an
opaque block hides everything, and water hides only more water, which is what
makes a sea a surface rather than a lattice; faces on a chunk boundary are
culled against the neighbouring chunk, which is why a chunk is only meshed once
its whole 3×3×3 neighbourhood is loaded; and the faces that survive are merged
greedily into the largest rectangles that share a colour, an opacity **and** an
ambient occlusion pattern. A solid chunk goes from 196,608 triangles to 12.

Each corner is eight bytes: position on a 33-point lattice, one of six face
directions, two bits of ambient occlusion, a colour and an opacity. There is no
texture coordinate, because the shader derives one from the position — which is
also what makes a merged quad tile per block instead of stretching. Nothing has
a texture at all; what breaks up a field of grass is a few per cent of shade per
block, hashed from where that block is in the world.

A frame is three passes. The sun's shadow map first, off-screen, with the solid
half of every chunk the light can see — snapped to a texel grid so the shadows
do not crawl as the camera moves. Then the sky, a single triangle with the sun,
the moon and the stars on it. Then the world: the solid half of each chunk with
the depth buffer sorting it, and the see-through half blended over the top, far
to near.

Everything is drawn camera-relative. The camera's position is the origin of
render space, and each chunk's offset from it is subtracted in f64 before being
narrowed to f32, so there is no distance at which vertices start to shimmer.

## Where the work happens

Generating a chunk and meshing it are both pure functions of a coordinate and a
seed, so both run on worker threads and neither is on the frame. What is left on
the frame is the handover — applying the player's edits to a chunk that has just
arrived, and copying a finished mesh into a GPU buffer — which the overlay's
breakdown puts at a couple of milliseconds against a view of half a million
triangles.

Trees are the reason the generator's contract is worth reading. A trunk at one
coordinate puts leaves in chunks the trunk is not in, and no chunk may ask
another what it contains. So every chunk works out from the seed alone which
trees could reach it, builds each of them in full, and keeps the part that falls
inside itself. Both sides of a boundary agree because both did the same
arithmetic.

Biomes work the same way, out of two noise fields a kilometre or so across — how
warm a place is and how wet — which pick what the ground is made of and how much
of the forest grows there. The density is weighted rather than switched, against
the same hash that decides whether a cell holds a tree at all, so a forest thins
out into a plain over a few hundred metres instead of stopping at a line no
terrain feature explains.

## BRUTAL

BRUTAL is consumed from NuGet and pinned in `Directory.Packages.props`. It is
not forked and not vendored: the framework is weeks old and improving quickly,
and an upstream release should cost one line here rather than a merge. A clone
of the upstream repository sits beside this one as reference source, since there
is no documentation site — reading the framework is how its API is learned.

Its licence is MIT-shaped with an attribution requirement on anything shipped to
end users: the copyright notice in the credits, the BRUTAL logo before gameplay,
and `NOTICE.txt` alongside the build. `NOTICE.txt` is here; the credits screen
and the logo have to exist before any build is distributed.
