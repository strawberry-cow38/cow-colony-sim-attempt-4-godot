# CLAUDE.md — Cow Colony Sim (Attempt 4, Godot + C#)

This file is the durable source of locked decisions for this repo. Read it before
making structural changes. Update it when a decision changes — don't let the
CLAUDE.md and the code drift.

## Status

Pre-pre-game. Skeleton only. No gameplay yet. Game phase target documented in
`README.md`.

## Locked decisions

### Stack
- **Godot 4.6.2 (mono) + .NET 8.** Pinned in `CowColonySim.csproj` (`Godot.NET.Sdk/4.6.2`).
- **ECS:** Friflo.Engine.ECS. Has typed entity links (`ILinkComponent` /
  `ILinkRelation`) for relationships like cow→bed, item→container, job→target.
  Built-in JSON serialization makes save format mostly free.
- **Tests:** xUnit on `Sim/`. No Godot dependency in test project. Add GdUnit4
  later only if `Game/` needs scene-level tests.

### Folder convention (do not break)
- `Sim/` — pure C#, **zero Godot using statements**. Engine-agnostic. Fast tests.
- `Game/` — Godot glue (Node3D, scenes, multimesh, input). May reference `Sim/`.
- `Tests/` — xUnit, references `Sim/` only.
- `assets/`, `scenes/`, `data/` — content.

The "no Godot in `Sim/`" rule is the foundation. Breaking it makes the sim
untestable and engine-coupled. If you find yourself wanting Godot types in
`Sim/`, define a Sim-side abstraction and implement it in `Game/`.

### Tick + threading
- **60 Hz fixed step.** `SimConstants.FixedDeltaSeconds`.
- **Hybrid threading model:**
  - Dedicated `SimThread` runs the tick loop and orders systems.
  - Inside a tick, parallel-friendly hot systems (pathfinding, FOV, fluids,
    lighting) fan out via `Parallel.For` / `ThreadPool`.
  - Sequential cheap systems (need, mood, schedule eval) stay on `SimThread`.
  - End of tick: `SimThread` builds an immutable `SimSnapshot`, publishes it.
- **Game reads only snapshots.** Never reach into `EntityStore` from a Godot
  node. This keeps the snapshot the single sync point.

### Sim ↔ Godot interop
- **Bulk entities:** `MultiMeshInstance3D` per archetype, transforms fed from
  the snapshot. Avoid per-entity `Node3D`.
- **Sparse events:** publish/subscribe from Sim → Game (death, message, UI
  notification). Event bus lives in `Sim/`.

### Coordinates
- **1 tile = 1.5 m = 43 Godot units.** `SimConstants.GodotUnitsPerTile`.
- **Sim stores tile coords + sub-tile floats.** Game multiplies by 43 when
  rendering. Sim is engine-agnostic; nothing in `Sim/` knows about Godot units.

### Save format
- **Friflo JSON** for now. Diffable, scriptable, debuggable. Profile before
  switching to binary.
- `SaveResource` carries a version field. Save migrations are skipped during
  pre-alpha — handle missing fields defensively in hydrate, bump version on
  breaking change, document.

### Logging
- **Serilog** in `Sim/` and `Game/`. Console + rolling file sinks. One unified
  log timeline.

### Code style
- `.editorconfig` enforces underscore-prefixed private fields, explicit braces,
  explicit types.
- `Directory.Build.props` sets `TreatWarningsAsErrors=true`. Don't disable it
  to push code through. Fix the warning.

## How to verify

After any structural change:

1. `dotnet test Tests/CowColonySim.Tests.csproj` — must be green.
2. `dotnet build CowColonySim.sln` — must be green with zero warnings.
3. `godot --headless --quit --path .` — must open the project without errors.

Push always. No per-phase confirmation.

## Don't

- Don't add Godot types to `Sim/`.
- Don't mirror `EntityStore` entities as `Node3D` per entity.
- Don't reach into `EntityStore` from a Godot node.
- Don't add features beyond what's asked. Foundation phase = seams only.
- Don't add error handling for impossible cases. Crash early on invariant
  breaks; catch only at real boundaries (file IO, network, user input).
- Don't refactor adjacent code while making a change. Surgical edits only.
