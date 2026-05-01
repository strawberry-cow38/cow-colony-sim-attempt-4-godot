using CowColonySim.Sim.Pathfinding;

namespace CowColonySim.Sim.Snapshots;

// Player-forced path for one colonist, exposed to Game so the overlay
// can render the line + destination ring. RemainingTiles[0] is the
// next waypoint the colonist is walking toward; the last entry is the
// active leg's destination. QueuedTiles holds shift-RMB waypoints not
// yet requested from the planner — drawn as a separate strip so the
// player can see the chained future legs.
public readonly record struct PathView(
    int EntityId,
    TileCoord[] RemainingTiles,
    TileCoord[] QueuedTiles);
