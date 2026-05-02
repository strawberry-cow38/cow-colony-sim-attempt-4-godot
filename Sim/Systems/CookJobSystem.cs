using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Crafting;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Items;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Systems;

// Drives bills queued on workstation Structure entities. MVP: no haul step
// — colonist walks to the workstation's interaction tile, ticks
// recipe.WorkSeconds, then we delete one matching ingredient stack from
// anywhere in the world and drop the output on the workstation tile. Real
// haul-to-stove + ingredient pickup is a follow-up.
public sealed class CookJobSystem : ITickSystem
{
    private readonly SimWorld _world;
    private readonly PathPlanner _planner;
    private readonly HeightGrid _grid;

    public CookJobSystem(SimWorld world, PathPlanner planner, HeightGrid grid)
    {
        _world = world;
        _planner = planner;
        _grid = grid;
    }

    public void Tick(TickContext ctx)
    {
        var dt = (float)ctx.FixedDeltaSeconds;

        var claimed = new HashSet<int>();
        var query = _world.Store.Query<Colonist, Job, WorkJob, TilePosition, PathFollower>();
        foreach (var entity in query.Entities)
        {
            ref var w = ref entity.GetComponent<WorkJob>();
            if (w.Active && w.Kind == WorkKind.Cook) claimed.Add(w.TargetEntityId);
        }

        foreach (var entity in query.Entities)
        {
            if (entity.HasComponent<Drafted>() && entity.GetComponent<Drafted>().Active) continue;
            ref var job = ref entity.GetComponent<Job>();
            ref var work = ref entity.GetComponent<WorkJob>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            ref var pf = ref entity.GetComponent<PathFollower>();

            if (job.Active) continue;

            if (work.Active && work.Kind == WorkKind.Cook)
            {
                ProgressCook(entity, ref work, ref pf, ref pos, dt);
            }
            else if (!work.Active)
            {
                if (entity.HasComponent<WorkPriorities>() &&
                    entity.GetComponent<WorkPriorities>().Get(WorkType.Cooking) == 0) continue;
                TryAssign(entity, ref work, ref pf, ref pos, claimed);
            }
        }
    }

    private void TryAssign(
        Entity colonist, ref WorkJob work, ref PathFollower pf, ref TilePosition pos,
        HashSet<int> claimed)
    {
        var bestStructure = 0;
        var bestStandX = 0;
        var bestStandY = 0;
        var bestDistSq = int.MaxValue;

        foreach (var ent in _world.Store.Query<Structure, Bills, TilePosition>().Entities)
        {
            if (claimed.Contains(ent.Id)) continue;
            ref var s = ref ent.GetComponent<Structure>();
            ref var b = ref ent.GetComponent<Bills>();
            if (b.Entries is null || b.Entries.Count == 0) continue;
            if (!FirstActiveBill(b, s.DefId, out _, out _)) continue;
            ref var sp = ref ent.GetComponent<TilePosition>();
            if (!TryFindStandTile(s.DefId, s.Rotation, sp.TileX, sp.TileY, pos.TileX, pos.TileY, out var sx, out var sy))
                continue;
            var dx = sx - pos.TileX;
            var dy = sy - pos.TileY;
            var d = dx * dx + dy * dy;
            if (d < bestDistSq)
            {
                bestDistSq = d;
                bestStructure = ent.Id;
                bestStandX = sx;
                bestStandY = sy;
            }
        }
        if (bestStructure == 0) return;

        var stove = _world.Store.GetEntityById(bestStructure);
        ref var stPos = ref stove.GetComponent<TilePosition>();
        work.Active = true;
        work.Kind = WorkKind.Cook;
        work.TargetEntityId = bestStructure;
        work.TargetTileX = bestStandX;
        work.TargetTileY = bestStandY;
        work.DropTileX = stPos.TileX;
        work.DropTileY = stPos.TileY;
        work.Progress = 0f;
        work.Forced = false;
        claimed.Add(bestStructure);

        if (pos.TileX == bestStandX && pos.TileY == bestStandY) return;
        var start = _grid.NodeAtOrFloor(pos.TileX, pos.TileY, pos.TileZ);
        var goal = _grid.At(bestStandX, bestStandY);
        if (start == goal) return;
        pf.Tiles = null;
        pf.Index = 0;
        pf.PendingRequest = true;
        pf.PlayerForced = false;
        _planner.Request(colonist.Id, start, goal);
    }

    private void ProgressCook(
        Entity colonist, ref WorkJob work, ref PathFollower pf, ref TilePosition pos, float dt)
    {
        var stove = _world.Store.GetEntityById(work.TargetEntityId);
        if (stove == default || !stove.HasComponent<Bills>() || !stove.HasComponent<Structure>())
        {
            ClearWork(ref work, ref pf);
            return;
        }
        ref var s = ref stove.GetComponent<Structure>();
        ref var bills = ref stove.GetComponent<Bills>();
        if (!FirstActiveBill(bills, s.DefId, out var billIdx, out var recipe))
        {
            ClearWork(ref work, ref pf);
            return;
        }

        if (pos.TileX != work.TargetTileX || pos.TileY != work.TargetTileY)
        {
            if (pf.Tiles is null && !pf.PendingRequest)
            {
                var start = _grid.NodeAtOrFloor(pos.TileX, pos.TileY, pos.TileZ);
                var goal = _grid.At(work.TargetTileX, work.TargetTileY);
                if (start == goal) return;
                pf.PendingRequest = true;
                pf.PlayerForced = false;
                _planner.Request(colonist.Id, start, goal);
            }
            return;
        }

        work.Progress += dt;
        if (work.Progress < recipe!.WorkSeconds) return;

        if (!ConsumeIngredients(recipe.Inputs))
        {
            // Ingredient vanished mid-cook. Pause this bill and bail.
            ClearWork(ref work, ref pf);
            return;
        }
        var (dropX, dropY) = FindOutputDropTile(work.DropTileX, work.DropTileY);
        _world.AddOrMergeItem(dropX, dropY, recipe.OutputKind, recipe.OutputCount);

        var b = bills.Entries[billIdx];
        b.DoneCount++;
        if (b.RepeatMode == BillRepeatMode.DoX && b.DoneCount >= b.TargetCount)
        {
            b.Suspended = true;
        }
        bills.Entries[billIdx] = b;
        bills.Version++;
        ClearWork(ref work, ref pf);
    }

    // Pick the first un-suspended bill on this stove whose recipe is
    // allowed here, has ingredients in the world, and (for UntilCount)
    // hasn't already produced enough. Returns the bill index + recipe.
    private bool FirstActiveBill(in Bills bills, string defId, out int idx, out RecipeDef? recipe)
    {
        idx = -1;
        recipe = null;
        if (bills.Entries is null) return false;
        for (var i = 0; i < bills.Entries.Count; i++)
        {
            var b = bills.Entries[i];
            if (b.Suspended) continue;
            if (!RecipeCatalog.TryGet(b.RecipeId, out var r) || r is null) continue;
            var allowed = false;
            for (var k = 0; k < r.AllowedWorkstations.Count; k++)
                if (r.AllowedWorkstations[k] == defId) { allowed = true; break; }
            if (!allowed) continue;
            if (!IngredientsAvailable(r.Inputs)) continue;
            if (b.RepeatMode == BillRepeatMode.UntilCount && WorldCountOf(r.OutputKind) >= b.TargetCount) continue;
            idx = i;
            recipe = r;
            return true;
        }
        return false;
    }

    private bool IngredientsAvailable(IReadOnlyList<RecipeIngredient> inputs)
    {
        for (var i = 0; i < inputs.Count; i++)
        {
            var need = inputs[i].Count;
            foreach (var ent in _world.Store.Query<Item, TilePosition>().Entities)
            {
                ref var it = ref ent.GetComponent<Item>();
                if (it.Forbidden) continue;
                if (it.Kind != inputs[i].Kind) continue;
                need -= it.Count;
                if (need <= 0) break;
            }
            if (need > 0) return false;
        }
        return true;
    }

    private int WorldCountOf(ItemKind kind)
    {
        var n = 0;
        foreach (var ent in _world.Store.Query<Item, TilePosition>().Entities)
        {
            ref var it = ref ent.GetComponent<Item>();
            if (it.Kind != kind) continue;
            n += it.Count;
        }
        return n;
    }

    // Delete `count` of `kind` from world stacks, picking nearest-first
    // is fine but MVP just sweeps whatever the query yields.
    private bool ConsumeIngredients(IReadOnlyList<RecipeIngredient> inputs)
    {
        for (var i = 0; i < inputs.Count; i++)
        {
            var need = inputs[i].Count;
            var toDelete = new List<int>();
            foreach (var ent in _world.Store.Query<Item, TilePosition>().Entities)
            {
                if (need <= 0) break;
                ref var it = ref ent.GetComponent<Item>();
                if (it.Forbidden) continue;
                if (it.Kind != inputs[i].Kind) continue;
                if (it.Count <= need)
                {
                    need -= it.Count;
                    toDelete.Add(ent.Id);
                }
                else
                {
                    it.Count -= need;
                    need = 0;
                }
            }
            if (need > 0) return false;
            for (var d = 0; d < toDelete.Count; d++)
            {
                var e = _world.Store.GetEntityById(toDelete[d]);
                if (e != default) e.DeleteEntity();
            }
        }
        return true;
    }

    // Drop bread on the stove tile if it's free, otherwise spill to the
    // first walkable adjacent tile.
    private (int x, int y) FindOutputDropTile(int sx, int sy)
    {
        if ((uint)sx < (uint)_grid.Width && (uint)sy < (uint)_grid.Height && !_grid.IsBlocked(sx, sy))
            return (sx, sy);
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            var nx = sx + dx;
            var ny = sy + dy;
            if ((uint)nx >= (uint)_grid.Width || (uint)ny >= (uint)_grid.Height) continue;
            if (_grid.IsBlocked(nx, ny)) continue;
            return (nx, ny);
        }
        return (sx, sy);
    }

    // Pick the first declared InteractionSpot on the stove, rotated to
    // match. Falls back to any walkable tile adjacent to the footprint.
    private bool TryFindStandTile(
        string defId, int rotation, int originX, int originY, int fromX, int fromY,
        out int outX, out int outY)
    {
        outX = 0;
        outY = 0;
        if (!BlueprintCatalog.TryGet(defId, out var def) || def is null) return false;

        for (var i = 0; i < def.Requirements.Count; i++)
        {
            var req = def.Requirements[i];
            if (req.Kind != FootprintRequirementKind.InteractionSpot) continue;
            var (rx, ry) = def.RotateOffset(req.OffsetX, req.OffsetY, rotation);
            var tx = originX + rx;
            var ty = originY + ry;
            if ((uint)tx >= (uint)_grid.Width || (uint)ty >= (uint)_grid.Height) continue;
            if (_grid.IsBlocked(tx, ty)) continue;
            outX = tx;
            outY = ty;
            return true;
        }

        // Fallback: any walkable tile adjacent to footprint.
        var (footW, footH) = (rotation & 1) == 0
            ? (def.FootprintW, def.FootprintH)
            : (def.FootprintH, def.FootprintW);
        var bestDist = int.MaxValue;
        var found = false;
        for (var dy = -1; dy <= footH; dy++)
        for (var dx = -1; dx <= footW; dx++)
        {
            var inside = dx >= 0 && dx < footW && dy >= 0 && dy < footH;
            if (inside) continue;
            var nx = originX + dx;
            var ny = originY + dy;
            if ((uint)nx >= (uint)_grid.Width || (uint)ny >= (uint)_grid.Height) continue;
            if (_grid.IsBlocked(nx, ny)) continue;
            var ddx = nx - fromX;
            var ddy = ny - fromY;
            var d = ddx * ddx + ddy * ddy;
            if (d < bestDist)
            {
                bestDist = d;
                outX = nx;
                outY = ny;
                found = true;
            }
        }
        return found;
    }

    private static void ClearWork(ref WorkJob work, ref PathFollower pf)
    {
        work.Active = false;
        work.Kind = WorkKind.None;
        work.TargetEntityId = 0;
        work.Progress = 0f;
        work.Forced = false;
        pf.Tiles = null;
        pf.Index = 0;
    }
}
