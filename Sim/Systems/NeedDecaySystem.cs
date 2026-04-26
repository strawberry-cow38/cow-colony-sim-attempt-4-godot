using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;

namespace CowColonySim.Sim.Systems;

// Drains each colonist's needs at fixed per-second rates. Clamped at 0
// so a fully-neglected need won't go negative.
public sealed class NeedDecaySystem : ITickSystem
{
    private const float HungerPerSec = 0.5f;
    private const float ThirstPerSec = 0.7f;
    private const float EnergyPerSec = 0.3f;

    private readonly SimWorld _world;

    public NeedDecaySystem(SimWorld world) => _world = world;

    public void Tick(TickContext ctx)
    {
        var dt = (float)ctx.FixedDeltaSeconds;
        var query = _world.Store.Query<Colonist, Needs>();
        foreach (var entity in query.Entities)
        {
            ref var n = ref entity.GetComponent<Needs>();
            n.Hunger = MathF.Max(0f, n.Hunger - HungerPerSec * dt);
            n.Thirst = MathF.Max(0f, n.Thirst - ThirstPerSec * dt);
            n.Energy = MathF.Max(0f, n.Energy - EnergyPerSec * dt);
        }
    }
}
