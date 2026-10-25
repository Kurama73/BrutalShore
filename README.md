# BrutalShore

An engine, and a voxel game on it, in C# on
[BRUTAL](https://github.com/ahwootech/BRUTAL) — the open-source framework behind
Kitten Space Agency.

![A coastline seen from above: ocean, a beach, rolling grassland](docs/coastline.png)

An unbounded world of 32³ chunks, generated from a seed rather than stored,
streamed around the player, greedy-meshed with ambient occlusion, and drawn
through a Vulkan renderer written here.

![The same world at eye level, with the debug overlay open](docs/overlay.png)

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

Click to capture the mouse, `WASD` to move, `space` and `ctrl` for up and down,
`shift` to sprint, `escape` to release the mouse, `F3` to hide the overlay.

### Options

| Flag                               | What it does                                     |
| ---------------------------------- | ------------------------------------------------ |
| `--seed <n>`                       | The world seed. The same seed is the same world. |
| `--view-radius <chunks>`           | How far chunks stay loaded. Default 8.           |
| `--time <hour>`                    | The hour the world starts at. Default 8.         |
| `--day-length <seconds>`           | Real seconds per in-game day; 0 stops the sun.   |
| `--validation`                     | Turn on the Vulkan validation layers.            |
| `--frames <n>`                     | Close after this many frames.                    |
| `--camera <x> <y> <z> <yaw> <pitch>` | Start the camera here, angles in degrees.      |
| `--screenshot <path>`              | Save the last frame as a PNG.                    |

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
| `BrutalShore.World`   | Coordinates, gradient noise, streaming, `IChunkGenerator`     |
| `BrutalShore.Render`  | Camera, view frustum, the voxel pipeline, chunk meshes        |
| `BrutalShore.Runtime` | The window, the device and the frame loop around an `IGame`   |

| Game            | What is in it                                        |
| --------------- | ---------------------------------------------------- |
| `Shorebound`    | Its blocks, its terrain and caves, the controls, where the player starts, how much work a frame may spend loading and meshing |

A game is an `IGame` the runtime runs, and an `IChunkGenerator` the world asks
for blocks. Those two interfaces are the whole of the engine's knowledge of it.

Dependencies point downwards only. Everything that does not need a device is
tested without one: `dotnet test` runs the mesher, the noise, the streaming, the
camera, the frustum and Shorebound's own terrain.

## How the world is drawn

A chunk is 32³ block ids. Meshing it is three reductions, in order: faces
between two solid blocks are never built; faces on a chunk boundary are culled
against the neighbouring chunk, which is why a chunk is only meshed once its
whole 3×3×3 neighbourhood is loaded; and the faces that survive are merged
greedily into the largest rectangles that share a colour **and** an ambient
occlusion pattern. A solid chunk goes from 196,608 triangles to 12.

Each corner is eight bytes: position on a 33-point lattice, one of six face
directions, two bits of ambient occlusion, and a colour. There is no texture
coordinate, because the shader derives one from the position — which is also what
makes a merged quad tile per block instead of stretching.

Everything is drawn camera-relative. The camera's position is the origin of
render space, and each chunk's offset from it is subtracted in f64 before being
narrowed to f32, so there is no distance at which vertices start to shimmer.

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
