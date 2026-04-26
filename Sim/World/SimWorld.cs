using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World;

// Thin wrapper around Friflo's EntityStore so the rest of the sim talks
// through SimWorld instead of taking a hard dep on Friflo at every call site.
// Game/* must never touch this directly — see CLAUDE.md (game reads snapshots).
public sealed class SimWorld
{
    public EntityStore Store { get; } = new();

    public int EntityCount => Store.Count;

    public Entity CreateEntity() => Store.CreateEntity();
}
