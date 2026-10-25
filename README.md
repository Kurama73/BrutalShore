# RustSpace

A voxel game in C# on [BRUTAL](https://github.com/Ahwoo/BRUTAL), the open-source
framework behind Kitten Space Agency.

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
dotnet run --project src/RustSpace.App -c Release
```

Click to capture the mouse, `WASD` to move, `space` and `ctrl` for up and down,
`shift` to sprint, `escape` to release the mouse, `F3` to hide the overlay.

### Options

| Flag                               | What it does                                     |
| ---------------------------------- | ------------------------------------------------ |
| `--seed <n>`                       | The world seed. The same seed is the same world. |
| `--view-radius <chunks>`           | How far chunks stay loaded. Default 8.           |
| `--validation`                     | Turn on the Vulkan validation layers.            |
| `--frames <n>`                     | Close after this many frames.                    |
| `--camera <x> <y> <z> <yaw> <pitch>` | Start the camera here, angles in degrees.      |
| `--screenshot <path>`              | Save the last frame as a PNG.                    |

The last three together are how graphics changes are checked: a fixed seed and a
fixed camera for a fixed number of frames produce an image that can be compared
against the one before.

```bash
dotnet run --project src/RustSpace.App -c Release -- \
    --frames 400 --camera -140 190 -140 45 -22 --screenshot coastline.png
```

## Layout

| Project            | What is in it                                               |
| ------------------ | ----------------------------------------------------------- |
| `RustSpace.Core`   | The frame clock and the f64 world to f32 render-space boundary |
| `RustSpace.Voxel`  | Chunks, the block registry, the greedy mesher                 |
| `RustSpace.Gpu`    | Window, Vulkan device, swapchain, frames in flight, shaders   |
| `RustSpace.World`  | Coordinates, gradient noise, terrain and caves, streaming     |
| `RustSpace.Render` | Camera, view frustum, the voxel pipeline, chunk meshes        |
| `RustSpace.App`    | The loop, the controls, the debug overlay                     |

Dependencies point downwards only. Everything above `Gpu` can be tested without
a device, and is: `dotnet test` runs the mesher, the noise, the generator, the
streaming, the camera and the frustum.

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
and an upstream release should cost one line here rather than a merge.

Its licence is MIT with an attribution requirement — a credits notice, the logo
before gameplay, and `NOTICE.txt` shipped with the build. None of that is in
place yet, and all of it has to be before anything is distributed.
