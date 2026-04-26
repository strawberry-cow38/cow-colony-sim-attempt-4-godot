using CowColonySim.Sim.Pathfinding;

namespace CowColonySim.Sim.Snapshots;

// Player-forced path for one colonist, exposed to Game so the overlay
// can render the line + destination ring. RemainingTiles[0] is the
// next waypoint the colonist is walking toward; the last entry is the
// destination. Empty/missing means no player path active.
public readonly record struct PathView(int EntityId, TileCoord[] RemainingTiles);
