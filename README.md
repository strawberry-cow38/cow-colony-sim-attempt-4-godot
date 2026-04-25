# Cow Colony Sim — Attempt 4 (Godot 4 + C#)

Fourth attempt at the colony sim. Cold-start skeleton. Decisions locked in `CLAUDE.md`.

## Stack

- Godot 4.6.2 (mono) + .NET 8
- ECS: [Friflo.Engine.ECS](https://github.com/friflo/Friflo.Engine.ECS)
- Tests: xUnit on the `Sim/` core (no Godot dependency)
- Logging: Serilog (Sim) bridged into Godot

## Layout

```
Sim/      pure C# core (no Godot using statements). engine-agnostic, testable.
Game/     Godot integration (nodes, scenes, render glue).
Tests/    xUnit, references Sim only.
scenes/   .tscn files.
assets/   GLBs, textures, sounds.
data/     resources, balance data.
```

## Build / run

```sh
dotnet test Tests/CowColonySim.Tests.csproj
godot --path .
```

## Phase

Pre-pre-game. Skeleton only. Game phase target: 256x256 vertex point terrain, 8 LOD chunks, GPU-controlled, 3 colonists with full 3D A* multithreaded pathfinding.
