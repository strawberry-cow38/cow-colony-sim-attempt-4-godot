using CowColonySim.Sim.World.Components;

namespace CowColonySim.Sim.Snapshots;

// Static need-satisfying spots packed into the snapshot so the game
// side can render markers without touching the entity store.
public readonly record struct SpotView(NeedKind Kind, int TileX, int TileY);
