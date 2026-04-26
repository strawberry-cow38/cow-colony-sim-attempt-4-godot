using CowColonySim.Sim.Pathfinding;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// Reference to the colonist's current path waypoints (tile coords) plus
// the next waypoint index. PendingRequest tracks whether an A* job is in
// flight so the planner doesn't get spammed.
public struct PathFollower : IComponent
{
    public TileCoord[]? Tiles;
    public int Index;
    public bool PendingRequest;
    // True while the active path was issued by a player MoveCommand.
    // Wander/Job paths leave this false. Path overlay only renders
    // player-forced paths.
    public bool PlayerForced;
}
