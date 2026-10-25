# BrutalShore — engineering standards

An engine, and a voxel game on it, in C# on
[BRUTAL](https://github.com/ahwootech/BRUTAL) — the framework Kitten Space
Agency is built on. The name is the Forgotten Shore of *Shadow Slave* crossed
with the framework it stands on. It supersedes an attempt at the same game in
Rust: the designs survived, the code did not.

Everything in this file is binding for code in this repository.

## What this is trying to be

An unbounded voxel world, generated rather than stored, streamed around the
player, drawn by a renderer written here rather than taken whole — and on top of
it a game that takes its building from Minecraft, its production chains and its
hordes from Factorio, and its tiers and its look from Satisfactory. You dig
down, the ore gets better and the dark gets worse; you smelt what you find into
what the next tier costs; what you build draws more of what comes out of the
dark.

The engine parts exist to make the game, and when the two goals conflict the
game wins — an engine nobody can ship a game with has failed, however
educational its internals.

## BRUTAL is a dependency, not a fork

BRUTAL is consumed from NuGet, pinned to exact versions in
`Directory.Packages.props`, and never vendored, forked or edited.

It was opened in October 2026, is versioned CalVer with each package on its own
number, and already takes community fixes within days — including the macOS
loader lookup this project depends on. Upstream improvements therefore arrive by
editing one line per package; a fork would turn every one of them into a merge.

### Where its documentation actually is

There is no documentation site, but the packages carry nearly everything:

- **XML doc comments** ship beside each assembly, so signatures and summaries
  show up in the editor without looking anything up.
- **SourceLink** is in the symbols, pinned to the exact commit a package was
  built from, so a debugger steps into the framework's own source and fetches
  it on demand.
- **`docs/brutal-conventions.md`** ships inside `Ahwoo.BRUTAL.Core`, at
  `~/.nuget/packages/ahwoo.brutal.core/<version>/docs/`. It is the rules for
  *consuming* BRUTAL types and it is binding here. Read it rather than having
  it restated — the document asks for exactly that — and re-read it when a
  version is bumped.

Its two load-bearing rules, because breaking them corrupts memory with no
managed signal: a `Ptr<T>` does not keep its memory alive, and a `[Fixed]` span
must not escape the scope that owns the memory. This repository has one place
that touches either — `FrameCapture` maps device memory, reads it and unmaps it
within a single method — and that is the place to look first if anything there
starts crashing.

### The clone

The clone at `../BRUTAL/` is **reference source only**, and strictly a
convenience: nothing builds against the path, and deleting it changes nothing.
What it is good for is what the packages cannot do — grepping the whole
framework at once, and diffing two versions when a package is bumped. It is a
clone of upstream rather than a fork or a snapshot, so it can be moved to the
tag matching whatever is pinned here:

```bash
git -C ../BRUTAL fetch --tags && git -C ../BRUTAL checkout Vulkan/v2026.10.3
```

Nothing is copied out of it. Fork it the day there is something to send
upstream, and not before.

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
src/   the engine — BrutalShore.*, knows nothing about any particular game
  Layer 0  Core      frame clock, render space, the f64/f32 boundary
  Layer 1  Voxel     chunks, block registry, the greedy mesher        (Core)
           Gpu       window, input, device, swapchain, frames, shaders, depth targets  (Core)
           Ui        the palette, the ImGui theme, the shared widgets  (Core)
  Layer 2  World     coordinates, noise, streaming, edits, rays, collision, IChunkGenerator  (Core + Voxel)
  Layer 3  Render    camera, frustum, sun and sky, shadows, the ray marching field, voxel pipeline, chunk meshes  (Core + Voxel + World + Gpu)
           Runtime   the window, the device and the frame loop around an IGame  (Core + Gpu + Ui)

games/ the games — each one an executable that composes the engine
  Shorebound  blocks, terrain, biomes, trees, ore seams, the player, controls,
              breaking, dropping, carrying and crafting, smelters, milestones,
              animals and the horde, the title and settings screens, saving,
              the player's settings, the length of a day, budgets
```

### Where the engine stops and a game starts

**Nothing under `src/` may reference anything under `games/`.** The engine is
what would still make sense if this game were deleted; a game is everything that
would have to be invented again for a different one.

The test, applied to a type or a field: *would it mean anything for a different
game?* A chunk, a mesher, a streaming radius — yes, engine. Stone and grass, a
sea level of 72, a snow line, how fast the player flies — no, game. The engine
reaches a game through exactly two seams: `IChunkGenerator`, which is how a
world gets its blocks, and `IGame`, which is how the host gets something to run.

This is written down because it had already been broken once: `GameBlocks` and
`TerrainGenerator` — this game's block set and this game's terrain — sat inside
`BrutalShore.World` for a while, and the engine's own tests had come to depend
on them. Nothing complains when that happens; it only shows up the day a second
game has to delete half of a layer to get started.

The same line runs through the tests. `tests/BrutalShore.*.Tests` may not name a
game's blocks or its generator — they use a registered block and `FlatGenerator`
— and anything that asserts what *this* world looks like belongs in
`tests/Shorebound.Tests`.

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
| Projects                          | `BrutalShore.<Domain>`                           |
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

- Tests live in `tests/<Project>.Tests`, mirroring the project they cover. xUnit
  v3. The engine's tests and a game's tests are separate projects, for the
  reason given under the layer rule.
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

## Attribution

BRUTAL's licence is MIT-shaped with three conditions on any product distributed
to end users: the copyright notice in the credits screen, the BRUTAL logo shown
before gameplay starts, and `NOTICE.txt` shipped alongside. `NOTICE.txt` is in
the repository; the other two have to exist before any build is handed to
anyone.

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

1. Decide which side of the line it is on first. Engine goes in `src/` as
   `BrutalShore.<Domain>`; a game goes in `games/<Name>/` and is an executable.
2. Write a `.csproj` that declares only its project references and its package
   references — every setting worth sharing is already in
   `Directory.Build.props`.
3. Add it to `BrutalShore.slnx`, in layer order.
4. Add its row to the layer table in this file and in the README.
5. If it is testable without a device, create its test project at the same time.

A new game adds a directory under `games/` and changes nothing in `src/`. If it
cannot be written without reaching into the engine, that is the engine's bug,
not the game's.
