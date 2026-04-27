using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Plants;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using CowColonySim.Sim.Zones;

namespace CowColonySim.Sim.Systems;

// Walks every Farm zone each tick and stamps designations so the
// existing chop/cut/harvest job systems pick the work up:
//   - plant matches CropDefId, mature (>=100% growth), AllowHarvest:
//       trees → ChopTree designation (felled like wild trees)
//       non-trees → Harvest designation
//   - plant doesn't match CropDefId, AllowSowing:
//       any growth → CutPlant designation (clear before sowing)
// Empty tiles are left for SowJobSystem.
//
// We never delete existing designations here. Player-stamped or
// previously auto-stamped designations are left in place; if a plant
// disappears, ChopJobSystem / PlantJobSystem already drop the
// designation when work completes.
public sealed class FarmAutoDesignateSystem : ITickSystem
{
    private readonly SimWorld _world;

    public FarmAutoDesignateSystem(SimWorld world)
    {
        _world = world;
    }

    public void Tick(TickContext ctx)
    {
        // Snapshot designation tiles by kind so we don't double-stamp.
        var existing = new Dictionary<(int x, int y, DesignationKind kind), bool>();
        foreach (var entity in _world.Store.Query<Designation, TilePosition>().Entities)
        {
            ref var d = ref entity.GetComponent<Designation>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            existing[(pos.TileX, pos.TileY, d.Kind)] = true;
        }

        // Index plants by tile for O(1) lookup per zone tile.
        var plantByTile = new Dictionary<(int, int), int>();
        foreach (var entity in _world.Store.Query<Plant, TilePosition>().Entities)
        {
            ref var pos = ref entity.GetComponent<TilePosition>();
            plantByTile[(pos.TileX, pos.TileY)] = entity.Id;
        }

        var toStamp = new List<(int x, int y, DesignationKind kind)>();
        foreach (var entity in _world.Store.Query<Zone, FarmSettings>().Entities)
        {
            ref var z = ref entity.GetComponent<Zone>();
            if (z.Type != ZoneType.Farm) continue;
            ref var f = ref entity.GetComponent<FarmSettings>();

            for (var ty = z.Rect.MinY; ty <= z.Rect.MaxY; ty++)
            {
                for (var tx = z.Rect.MinX; tx <= z.Rect.MaxX; tx++)
                {
                    if (!z.ContainsTile(tx, ty)) continue;
                    if (!plantByTile.TryGetValue((tx, ty), out var plantId)) continue;
                    var plant = _world.Store.GetEntityById(plantId);
                    if (plant == default) continue;
                    ref var p = ref plant.GetComponent<Plant>();

                    var matches = p.CropDefId == f.CropDefId;
                    if (matches)
                    {
                        if (!f.AllowHarvest) continue;
                        if (p.Growth < 100f) continue;
                        var kind = p.IsTree ? DesignationKind.ChopTree : DesignationKind.Harvest;
                        if (existing.ContainsKey((tx, ty, kind))) continue;
                        toStamp.Add((tx, ty, kind));
                    }
                    else
                    {
                        if (!f.AllowSowing) continue;
                        var kind = DesignationKind.CutPlant;
                        if (existing.ContainsKey((tx, ty, kind))) continue;
                        toStamp.Add((tx, ty, kind));
                    }
                }
            }
        }

        // Spawn outside the iteration to avoid mutating the archetype
        // while we hold component refs.
        for (var i = 0; i < toStamp.Count; i++)
        {
            var s = toStamp[i];
            _world.SpawnDesignation(s.x, s.y, s.kind);
        }
    }
}
