using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Commands;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using CowColonySim.Sim.Zones;

namespace CowColonySim.Sim.Systems;

// Dispatches queued commands at the top of each tick. Currently only
// MoveCommand: validates the entity, fires an A* request, clears the
// existing path so the colonist commits to the new destination as soon
// as the result lands.
public sealed class CommandSystem : ITickSystem
{
    private readonly CommandBus _bus;
    private readonly SimWorld _world;
    private readonly PathPlanner _planner;
    private readonly HeightGrid _grid;

    public CommandSystem(CommandBus bus, SimWorld world, PathPlanner planner, HeightGrid grid)
    {
        _bus = bus;
        _world = world;
        _planner = planner;
        _grid = grid;
    }

    public void Tick(TickContext ctx)
    {
        while (_bus.TryDequeue(out var command))
        {
            switch (command)
            {
                case MoveCommand move:
                    Apply(move);
                    break;
                case InvalidatePathsInRegion region:
                    Apply(region);
                    break;
                case CreateZoneCommand cz:
                    Apply(cz);
                    break;
                case StampDesignationsCommand sd:
                    Apply(sd);
                    break;
                case PlaceBlueprintGhostCommand pb:
                    Apply(pb);
                    break;
                case EraseInRectCommand er:
                    Apply(er);
                    break;
                case SetZoneSettingsCommand szs:
                    Apply(szs);
                    break;
            }
        }
    }

    private void Apply(EraseInRectCommand cmd)
    {
        var rect = ClampRect(cmd.Rect);
        var toDelete = new List<int>();

        foreach (var entity in _world.Store.Query<Zone>().Entities)
        {
            ref var z = ref entity.GetComponent<Zone>();
            if (RectsOverlap(z.Rect, rect)) toDelete.Add(entity.Id);
        }
        foreach (var entity in _world.Store.Query<Designation, TilePosition>().Entities)
        {
            ref var p = ref entity.GetComponent<TilePosition>();
            if (rect.Contains(p.TileX, p.TileY)) toDelete.Add(entity.Id);
        }
        foreach (var entity in _world.Store.Query<BlueprintGhost>().Entities)
        {
            ref var g = ref entity.GetComponent<BlueprintGhost>();
            if (rect.Contains(g.OriginTileX, g.OriginTileY)) toDelete.Add(entity.Id);
        }

        foreach (var id in toDelete)
        {
            var e = _world.Store.GetEntityById(id);
            if (e != default) e.DeleteEntity();
        }
    }

    private static bool RectsOverlap(TileRect a, TileRect b)
        => a.MinX <= b.MaxX && a.MaxX >= b.MinX
        && a.MinY <= b.MaxY && a.MaxY >= b.MinY;

    private void Apply(CreateZoneCommand cmd)
    {
        var rect = ClampRect(cmd.Rect);
        if (rect.Width <= 0 || rect.Height <= 0) return;
        var entity = _world.SpawnZone(0, cmd.Type, rect, cmd.Name);
        ref var z = ref entity.GetComponent<Zone>();
        z.ZoneId = entity.Id;
    }

    private void Apply(StampDesignationsCommand cmd)
    {
        var rect = ClampRect(cmd.Rect);
        for (var y = rect.MinY; y <= rect.MaxY; y++)
        {
            for (var x = rect.MinX; x <= rect.MaxX; x++)
            {
                _world.SpawnDesignation(x, y, cmd.Kind);
            }
        }
    }

    private void Apply(PlaceBlueprintGhostCommand cmd)
    {
        if (!BlueprintCatalog.TryGet(cmd.DefId, out var def) || def is null) return;
        var (footW, footH) = (cmd.Rotation & 1) == 0
            ? (def.FootprintW, def.FootprintH)
            : (def.FootprintH, def.FootprintW);
        if (!FootprintInBounds(cmd.OriginTileX, cmd.OriginTileY, footW, footH)) return;
        if (!FootprintLevel(cmd.OriginTileX, cmd.OriginTileY, footW, footH)) return;
        if (FootprintObstructed(cmd.OriginTileX, cmd.OriginTileY, footW, footH)) return;
        _world.SpawnBlueprintGhost(cmd.DefId, cmd.OriginTileX, cmd.OriginTileY, cmd.Rotation);
    }

    private bool FootprintInBounds(int ox, int oy, int w, int h)
        => ox >= 0 && oy >= 0 && ox + w <= _grid.Width && oy + h <= _grid.Height;

    // All vertex corners spanning the footprint must share one quantum value
    // — the 4-unshared-corners-per-tile rendering means a footprint over a
    // sloped tile would visibly hover, and pathing on top of it would lie.
    private bool FootprintLevel(int ox, int oy, int w, int h)
    {
        var anchor = _grid.CornerQuanta(ox, oy);
        for (var vy = oy; vy <= oy + h; vy++)
        {
            for (var vx = ox; vx <= ox + w; vx++)
            {
                if (_grid.CornerQuanta(vx, vy) != anchor) return false;
            }
        }
        return true;
    }

    private bool FootprintObstructed(int ox, int oy, int w, int h)
    {
        var foot = new TileRect(ox, oy, ox + w - 1, oy + h - 1);
        foreach (var entity in _world.Store.Query<BlueprintGhost>().Entities)
        {
            ref var g = ref entity.GetComponent<BlueprintGhost>();
            if (!BlueprintCatalog.TryGet(g.DefId, out var od) || od is null) continue;
            var (ow, oh) = (g.Rotation & 1) == 0
                ? (od.FootprintW, od.FootprintH)
                : (od.FootprintH, od.FootprintW);
            var other = new TileRect(g.OriginTileX, g.OriginTileY, g.OriginTileX + ow - 1, g.OriginTileY + oh - 1);
            if (RectsOverlap(foot, other)) return true;
        }
        return false;
    }

    private void Apply(SetZoneSettingsCommand cmd)
    {
        var entity = _world.Store.GetEntityById(cmd.ZoneId);
        if (entity == default || !entity.HasComponent<Zone>()) return;
        ref var z = ref entity.GetComponent<Zone>();
        z.Name = cmd.Name ?? string.Empty;
        if (entity.HasComponent<StockpileSettings>())
        {
            ref var s = ref entity.GetComponent<StockpileSettings>();
            s.Priority = cmd.Priority;
        }
        if (entity.HasComponent<FarmSettings>())
        {
            ref var f = ref entity.GetComponent<FarmSettings>();
            f.CropDefId = cmd.CropDefId;
        }
    }

    private TileRect ClampRect(TileRect rect)
    {
        var w = _grid.Width;
        var h = _grid.Height;
        var minX = Math.Clamp(rect.MinX, 0, w - 1);
        var minY = Math.Clamp(rect.MinY, 0, h - 1);
        var maxX = Math.Clamp(rect.MaxX, 0, w - 1);
        var maxY = Math.Clamp(rect.MaxY, 0, h - 1);
        return new TileRect(minX, minY, maxX, maxY);
    }

    private void Apply(InvalidatePathsInRegion region)
    {
        var query = _world.Store.Query<PathFollower>();
        foreach (var entity in query.Entities)
        {
            ref var pf = ref entity.GetComponent<PathFollower>();
            if (pf.Tiles is null) continue;
            for (var i = pf.Index; i < pf.Tiles.Length; i++)
            {
                var t = pf.Tiles[i];
                if (t.X >= region.MinTileX && t.X <= region.MaxTileX
                    && t.Y >= region.MinTileY && t.Y <= region.MaxTileY)
                {
                    pf.Tiles = null;
                    pf.Index = 0;
                    break;
                }
            }
        }
    }

    private void Apply(MoveCommand move)
    {
        var entity = _world.Store.GetEntityById(move.EntityId);
        if (entity == default) return;
        if (!entity.HasComponent<PathFollower>()) return;
        if (!entity.HasComponent<TilePosition>()) return;
        if (!_grid.InBounds(move.Target)) return;

        ref var pos = ref entity.GetComponent<TilePosition>();
        ref var pf = ref entity.GetComponent<PathFollower>();
        var start = _grid.At(
            Math.Clamp(pos.TileX, 0, _grid.Width - 1),
            Math.Clamp(pos.TileY, 0, _grid.Height - 1));
        var goal = _grid.At(move.Target.X, move.Target.Y);
        pf.Tiles = null;
        pf.Index = 0;
        pf.PendingRequest = true;
        pf.PlayerForced = true;
        _planner.Request(entity.Id, start, goal);
    }
}
