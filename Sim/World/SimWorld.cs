using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Items;
using CowColonySim.Sim.Pathfinding;
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

    // Dirty-versioned counter for stockpile topology — bumped when a
    // stockpile zone is created, deleted, resized, or has its settings
    // changed. HaulSystem reads it to decide whether to rebuild its
    // per-tick stockpileTiles + acceptedKindsUnion caches. Bump from
    // anywhere stockpile zones materially change; HaulSystem detects the
    // version delta and refreshes once. Avoids re-scanning every Zone +
    // every tile, every tick, when nothing about stockpiles moved.
    public int StockpileVersion { get; private set; }
    public void BumpStockpileVersion() => StockpileVersion++;

    private readonly List<TileCoord> _pendingTreeFalls = new();

    // ChopJobSystem appends here when a tree is felled; SimRuntime drains the
    // list once per tick into the published snapshot so Game-side audio can
    // fire a one-shot at each fall position.
    public void RecordTreeFall(int tileX, int tileY) =>
        _pendingTreeFalls.Add(new TileCoord(tileX, tileY, 0));

    public TileCoord[] DrainTreeFalls()
    {
        if (_pendingTreeFalls.Count == 0) return Array.Empty<TileCoord>();
        var arr = _pendingTreeFalls.ToArray();
        _pendingTreeFalls.Clear();
        return arr;
    }

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
        e.AddComponent(Inventory.New());
        e.AddComponent(CarryCaps.Default());
        e.AddComponent(new Drafted { Active = false });
        e.AddComponent(WorkPriorities.Default());
        return e;
    }

    public Entity SpawnNeedSpot(NeedKind kind, int tileX, int tileY, float satisfyPerSec = 25f)
    {
        var e = Store.CreateEntity();
        e.AddComponent(new TilePosition(tileX, tileY, 0, 0.5f, 0.5f));
        e.AddComponent(new NeedSpot { Kind = kind, SatisfyPerSec = satisfyPerSec });
        return e;
    }

    public Entity SpawnZone(int zoneId, ZoneType type, TileRect rect, bool[] mask, string name)
    {
        var e = Store.CreateEntity();
        e.AddComponent(new Zone { ZoneId = zoneId, Type = type, Rect = rect, Mask = mask, Name = name });
        switch (type)
        {
            case ZoneType.Stockpile:
                e.AddComponent(new StockpileSettings());
                BumpStockpileVersion();
                break;
            case ZoneType.Farm:      e.AddComponent(new FarmSettings()); break;
        }
        return e;
    }

    public Entity SpawnTree(int tileX, int tileY, uint variantSeed, int health = 30, float growth = 100f)
    {
        var e = Store.CreateEntity();
        e.AddComponent(new TilePosition(tileX, tileY, 0, 0.5f, 0.5f));
        e.AddComponent(new Tree { Health = health, VariantSeed = variantSeed });
        e.AddComponent(new Plant
        {
            Growth = growth,
            Age = 0,
            LifespanTicks = Plants.CropCatalog.Get(Plants.CropDefIds.Tree).LifespanTicks,
            CropDefId = Plants.CropDefIds.Tree,
            IsTree = true,
        });
        return e;
    }

    public Entity SpawnBoulder(int tileX, int tileY, uint variantSeed, int variant, int health = 30)
    {
        var e = Store.CreateEntity();
        e.AddComponent(new TilePosition(tileX, tileY, 0, 0.5f, 0.5f));
        e.AddComponent(new Boulder
        {
            Health = health,
            VariantSeed = variantSeed,
            Variant = variant,
        });
        return e;
    }

    // Drop a fresh stack of `kind` on the tile, or merge into the first
    // existing stack at that tile that has room. Returns the affected
    // entity (new or updated). Mirrors attempt-2's addItemToTile so chop
    // yields collapse onto one stack instead of pebble-spamming entities.
    public Entity AddOrMergeItem(int tileX, int tileY, ItemKind kind, int count, int capacity = 0)
    {
        if (count <= 0) return default;
        if (capacity <= 0) capacity = ItemCatalog.DefaultFor(kind).StackCapacity;
        var query = Store.Query<Item, TilePosition>();
        foreach (var entity in query.Entities)
        {
            ref var it = ref entity.GetComponent<Item>();
            if (it.Kind != kind) continue;
            ref var pos = ref entity.GetComponent<TilePosition>();
            if (pos.TileX != tileX || pos.TileY != tileY) continue;
            var room = it.Capacity - it.Count;
            if (room <= 0) continue;
            var add = Math.Min(room, count);
            it.Count += add;
            count -= add;
            if (count <= 0) return entity;
        }
        var e = Store.CreateEntity();
        e.AddComponent(new TilePosition(tileX, tileY, 0, 0.5f, 0.5f));
        e.AddComponent(new Item { Kind = kind, Count = Math.Min(count, capacity), Capacity = capacity });
        return e;
    }

    public Entity SpawnDesignation(int tileX, int tileY, DesignationKind kind)
    {
        var e = Store.CreateEntity();
        e.AddComponent(new TilePosition(tileX, tileY, 0, 0.5f, 0.5f));
        e.AddComponent(new Designation { Kind = kind });
        return e;
    }

    public Entity SpawnMinifiedThing(string defId, int tileX, int tileY, int rotation, int baseLayer)
    {
        var def = BlueprintCatalog.Get(defId);
        var e = Store.CreateEntity();
        e.AddComponent(new TilePosition(tileX, tileY, 0, 0.5f, 0.5f));
        e.AddComponent(new Item { Kind = ItemKind.Minified, Count = 1, Capacity = 1 });
        e.AddComponent(new MinifiedThing { DefId = def.Id, Rotation = rotation, BaseLayer = baseLayer });
        return e;
    }

    public Entity SpawnStructure(string defId, int tileX, int tileY, int rotation = 0, int baseLayer = 0)
    {
        var def = BlueprintCatalog.Get(defId);
        var e = Store.CreateEntity();
        e.AddComponent(new TilePosition(tileX, tileY, 0));
        e.AddComponent(new Structure { DefId = def.Id, Rotation = rotation, BaseLayer = baseLayer });
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
