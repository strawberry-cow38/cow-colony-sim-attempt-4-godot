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
    // Set true when DrainResults receives Found=false from the planner.
    // Cleared when a new request is issued. Job systems read this to
    // give up gracefully instead of re-requesting forever.
    public bool LastPathFailed;
    // FIFO of additional draft-move waypoints set by shift-RMB. When the
    // active path completes for a drafted colonist, the planner is asked
    // to route to the head of this queue (and the head is popped). A
    // non-queued MoveCommand wipes this list so the colonist commits to
    // the fresh order. Failed paths also wipe to stop chasing dead ends.
    public List<TileCoord>? WaypointQueue;
}
