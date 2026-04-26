using CowColonySim.Sim.Plants;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;

namespace CowColonySim.Sim.Systems;

// Walks every Plant entity once per tick and:
//   - if sunlight at the plant's tile is at or above the crop's
//     MinSunlightFraction (and one day the temperature is in range —
//     stubbed as always-true until the temperature grid lands), bumps
//     Growth toward 100 by GrowthPerTickAtFullSun * sunlight.
//   - once Growth hits 100, advances Age until LifespanTicks; future
//     phases will wither the plant past lifespan.
//
// Must run after LightingSystem so this tick's sun/emitter values are
// the ones plants react to.
public sealed class PlantGrowthSystem : ITickSystem
{
    private readonly SimWorld _world;
    private readonly LightingSystem _lighting;

    public PlantGrowthSystem(SimWorld world, LightingSystem lighting)
    {
        _world = world;
        _lighting = lighting;
    }

    public void Tick(TickContext ctx)
    {
        var grid = _lighting.Grid;
        foreach (var entity in _world.Store.Query<Plant, TilePosition>().Entities)
        {
            ref var plant = ref entity.GetComponent<Plant>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            var def = CropCatalog.Get(plant.CropDefId);

            if (plant.Growth >= 100f)
            {
                plant.Growth = 100f;
                if (plant.LifespanTicks > 0) plant.Age = Math.Min(plant.LifespanTicks, plant.Age + 1);
                continue;
            }

            var sun = grid.Get(pos.TileX, pos.TileY);
            if (sun < def.MinSunlightFraction) continue;

            // Linear growth scaled by current sunlight. Floor at 0 just in
            // case some crop def ships with a negative growth rate.
            var step = MathF.Max(0f, def.GrowthPerTickAtFullSun * sun);
            plant.Growth = MathF.Min(100f, plant.Growth + step);
        }
    }
}
