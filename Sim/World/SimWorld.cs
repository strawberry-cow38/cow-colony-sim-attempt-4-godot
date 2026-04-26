using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.World.Components;
using CowColonySim.Sim.Zones;
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

    public Entity SpawnColonist(uint rngSeed, int tileX, int tileY)
    {
        var e = Store.CreateEntity();
        e.AddComponent(new TilePosition(tileX, tileY, 0, 0.5f, 0.5f));
        e.AddComponent(new Colonist
        {
            Rng = rngSeed == 0 ? 0xC0FFEE01u : rngSeed,
        });
        e.AddComponent(new PathFollower());
        e.AddComponent(Needs.Full());
        e.AddComponent(new Job());
        e.AddComponent(new WorkJob());
        return e;
    }

    public Entity SpawnNeedSpot(NeedKind kind, int tileX, int tileY, float satisfyPerSec = 25f)
    {
        var e = Store.CreateEntity();
        e.AddComponent(new TilePosition(tileX, tileY, 0, 0.5f, 0.5f));
        e.AddComponent(new NeedSpot { Kind = kind, SatisfyPerSec = satisfyPerSec });
        return e;
    }

    public Entity SpawnZone(int zoneId, ZoneType type, TileRect rect, string name)
    {
        var e = Store.CreateEntity();
        e.AddComponent(new Zone { ZoneId = zoneId, Type = type, Rect = rect, Name = name });
        switch (type)
        {
            case ZoneType.Stockpile: e.AddComponent(new StockpileSettings()); break;
            case ZoneType.Farm:      e.AddComponent(new FarmSettings()); break;
        }
        return e;
    }

    public Entity SpawnTree(int tileX, int tileY, uint variantSeed, int health = 30)
    {
        var e = Store.CreateEntity();
        e.AddComponent(new TilePosition(tileX, tileY, 0, 0.5f, 0.5f));
        e.AddComponent(new Tree { Health = health, VariantSeed = variantSeed });
        return e;
    }

    public Entity SpawnDesignation(int tileX, int tileY, DesignationKind kind)
    {
        var e = Store.CreateEntity();
        e.AddComponent(new TilePosition(tileX, tileY, 0, 0.5f, 0.5f));
        e.AddComponent(new Designation { Kind = kind });
        return e;
    }

    public Entity SpawnBlueprintGhost(string defId, int tileX, int tileY, int rotation = 0, int baseLayer = 0)
    {
        var def = BlueprintCatalog.Get(defId);
        var e = Store.CreateEntity();
        e.AddComponent(new TilePosition(tileX, tileY, 0));
        e.AddComponent(new BlueprintGhost
        {
            DefId = def.Id,
            OriginTileX = tileX,
            OriginTileY = tileY,
            Rotation = rotation,
            BaseLayer = baseLayer,
            BuildProgress = 0f,
        });
        return e;
    }
}
