# RustSpace — engineering standards

A voxel game in C# on [BRUTAL](https://github.com/Ahwoo/BRUTAL), the framework
Kitten Space Agency is built on. The name is inherited from a previous attempt
in Rust; the designs survived, the code did not.

Everything in this file is binding for code in this repository.

## What this is trying to be

A Minecraft-like that is worth reading as much as playing: an unbounded voxel
world, generated rather than stored, streamed around the player, drawn by a
renderer written here rather than taken whole. The engine parts exist to make
the game, and when the two goals conflict the game wins — an engine nobody can
ship a game with has failed, however educational its internals.

## BRUTAL is a dependency, not a fork

BRUTAL is consumed from NuGet, pinned to exact versions in
`Directory.Packages.props`, and never vendored, forked or edited.

It is nine days old as of this writing, versioned CalVer with each package on
its own number, and already taking community fixes within days — including the
macOS loader lookup this project depends on. Upstream improvements therefore
arrive by editing one line per package; a fork would turn every one of them into
a merge.

The checkout at `../BRUTAL-2026.10.1/` is **reference source only**. There is no
API documentation site, so reading the framework's own code is how its API is
learned. Nothing is copied out of it, and nothing in this repository builds
against the path.

## Language

- **Code, comments, doc comments, test names, commit messages, changelog:
  English.** No exceptions, including in throwaway examples.
- Conversation with the maintainer happens in French; that never leaks into the
  repository.

## Architecture

### The layer rule

Projects are organized in layers. **Dependencies point downwards only — never
upwards, never sideways between projects of the same layer.**

```
Layer 0  RustSpace.Core     frame clock, render space, the f64/f32 boundary
Layer 1  RustSpace.Voxel    chunks, block registry, the greedy mesher   (Core)
         RustSpace.Gpu      window, device, swapchain, frames, shaders  (Core)
Layer 2  RustSpace.World    coordinates, noise, generation, streaming   (Core + Voxel)
Layer 3  RustSpace.Render   camera, frustum, voxel pipeline, chunk meshes
                            (Core + Voxel + World + Gpu)
Layer 4  RustSpace.App      the loop, input, the debug overlay
```

`Voxel` must not know what a device is: a mesh is an array of vertices, and the
proof that the boundary holds is that the mesher is unit-tested without a
window. `World` must not know what a pipeline is. `Gpu` must not know what a
block is — it draws buffers, and everything voxel-shaped lives one layer up.

Everything that carries no GPU dependency — `Core`, `Voxel`, `World` — must stay
runnable under `dotnet test` without a window. That is not a formality: it is
what keeps generation, meshing and streaming testable.

### Where to abstract, and where not to

**The rule of two:** an abstraction may only be introduced once a *second*
concrete use case exists. `IVoxelSource` and `IChunkGenerator` both earned their
place by having two implementations, one of them a test double.

Specifically, do not introduce a wrapper over BRUTAL's Vulkan layer. It is
already the abstraction over Vulkan; a second one would only be a second thing
to keep in step.

### The world is generated, not stored

A chunk nobody has touched is regenerated from its coordinate and the seed, not
loaded. That contract is written into `IChunkGenerator`, whose signature takes
nothing but a position: as long as it holds, the world is unbounded and costs
nothing to not visit.

### Precision

Simulation state is `double`, GPU data is `float`. Converting goes through
`RenderSpace.ToRenderSpace`, which subtracts in f64 **before** narrowing. Never
narrow to f32 and subtract afterwards. The camera is the render-space origin, so
geometry a kilometre away has the same precision as geometry underfoot.

## Naming

| Item                              | Convention                                     |
| --------------------------------- | ---------------------------------------------- |
| Projects                          | `RustSpace.<Domain>`                           |
| Types, methods, properties        | `UpperCamelCase`                               |
| Parameters, locals                | `lowerCamelCase`                               |
| Private fields                    | `_lowerCamelCase`                              |
| Constants                         | `UpperCamelCase`                               |
| Test methods                      | a sentence stating the property being asserted |

Further rules:

- No abbreviations except the ones universally used in graphics (`gpu`, `uv`,
  `dpi`, `srgb`, `aabb`, `fps`). Write `descriptor`, not `desc`.
- Units belong in the name whenever they are not obvious: `StepSeconds`,
  `radiusChunks`, `worldMeters`.
- Test names read as claims: `a_boundary_face_is_culled_against_its_neighbour`,
  not `TestMesher`.

## Comments

Comment the **why**, never the what. If a line needs a comment to say what it
does, rename something instead.

- Every public item gets a doc comment. This is enforced:
  `GenerateDocumentationFile` plus `TreatWarningsAsErrors` makes an undocumented
  public member a build failure.
- Every file starts with a type-level `<remarks>` saying what belongs in it and,
  when useful, what deliberately does not.
- Non-obvious decisions get an inline comment giving the reason: why this cap,
  why this order, why this type. Numeric constants that are not self-evident
  cite their source.
- No commented-out code.

## Testing

- Tests live in `tests/RustSpace.<Domain>.Tests`, mirroring the project they
  cover. xUnit v3.
- Test the **property**, not the implementation. A test should survive a rewrite
  of the method it covers.
- Every numerical routine gets at least one test pinning down its precision
  limit, with the tolerance justified by a comment.
- Every bug fixed gets a regression test in the same commit.
- Code that needs a device is not unit-tested. It is validated by running the
  app with `--frames N --screenshot <path>`, which renders a fixed number of
  frames from a fixed seed and leaves an image behind; the reusable logic is
  factored out into something that can be tested headless. The front-face
  winding bug was found exactly this way, and `CameraTests` pins down the part
  of it that can be asserted without a GPU.

## Quality gates

These must pass before every commit:

```bash
dotnet build
```

```bash
dotnet test
```

The build is the lint: `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild` and
`GenerateDocumentationFile` are set once in `Directory.Build.props` and inherited
by every project. BRUTAL ships its own analyzers, and they are errors too — when
one says to prefer a UTF-8 literal, it means the string would otherwise be
re-encoded on the way to a native call.

External dependencies are declared **once**, in `Directory.Packages.props`, with
an exact version. Never put a version in a project file.

## Commits

Conventional Commits, **one line, no body** unless the maintainer asks for one.

```
<type>(<scope>): <imperative summary in lower case>
```

- Types: `feat`, `fix`, `refactor`, `perf`, `docs`, `test`, `chore`, `build`.
- Scope is the project without its prefix (`core`, `voxel`, `world`, `gpu`,
  `render`, `app`), or omitted for repository-wide changes.
- One logical change per commit. A new feature and the refactor that made room
  for it are two commits.
- Never mention Claude, Claude Code, or any AI assistant in a commit message, a
  changelog entry, or a pull request description. No `Co-Authored-By` trailer.
- Commit as soon as a change is coherent and the gates pass; do not batch a day
  of work into one commit.

## Adding a project

1. Create the directory under `src/` and a `.csproj` that declares only its
   project references and its package references — every setting worth sharing
   is already in `Directory.Build.props`.
2. Add it to `RustSpace.slnx`, in layer order.
3. Add its row to the layer table in this file and in the README.
4. If it is testable without a device, create its test project at the same time.
