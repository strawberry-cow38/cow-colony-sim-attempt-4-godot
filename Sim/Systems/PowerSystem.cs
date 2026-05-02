using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;

namespace CowColonySim.Sim.Systems;

// Builds the electricity graph each tick.
//
// Pylons connect to other pylons within CableHopTiles. Sources/Sinks attach
// to the nearest pylon within ServiceRadiusTiles (Euclidean tile distance,
// XY only — Z handled later when multi-floor power lands).
//
// Topology rebuild is gated on SimWorld.PowerVersion changing — placing,
// removing, or toggling a node bumps the version. Per-tick supply/demand
// totals are recomputed unconditionally because IsActive can shift via
// SetGeneratorOutputCommand without a version bump in the same tick.
public sealed class PowerSystem : ITickSystem
{
    public const float CableHopTiles = 8f;
    public const float ServiceRadiusTiles = 6f;
    // Each pylon connects to at most this many nearest neighbors (within
    // CableHopTiles). Recency-biased greedy claim: newest pylon (highest
    // index) picks its nearest 5 unsaturated others first; older pylons
    // fill remaining slots. Cap is enforced per-pylon so degree never
    // exceeds K, but the newest "power line" is guaranteed its picks
    // even if surrounding old pylons would otherwise be saturated.
    public const int MaxPylonNeighbors = 5;

    // Returns unordered (i < j) pylon index pairs that should be connected.
    // Caller convention: input list ordered oldest→newest. The algorithm
    // walks indices n-1 down to 0 so the most recently placed pylon claims
    // its nearest neighbours before any older pylon competes for the same
    // slots. Coords are arbitrary units; hopSqr must match those units squared.
    public static List<(int i, int j)> ComputeNeighborPairs(
        IReadOnlyList<float> px, IReadOnlyList<float> py, float hopSqr, int maxNeighbors = MaxPylonNeighbors)
    {
        var n = px.Count;
        var degree = new int[n];
        var adj = new HashSet<long>();
        var pairs = new List<(int i, int j)>();
        var buf = new List<(float sqr, int idx)>(n);
        for (var k = n - 1; k >= 0; k--)
        {
            if (degree[k] >= maxNeighbors) continue;
            buf.Clear();
            for (var j = 0; j < n; j++)
            {
                if (j == k) continue;
                if (degree[j] >= maxNeighbors) continue;
                var dx = px[k] - px[j];
                var dy = py[k] - py[j];
                var sqr = dx * dx + dy * dy;
                if (sqr > hopSqr) continue;
                buf.Add((sqr, j));
            }
            buf.Sort((a, b) => a.sqr.CompareTo(b.sqr));
            for (var t = 0; t < buf.Count && degree[k] < maxNeighbors; t++)
            {
                var j = buf[t].idx;
                if (degree[j] >= maxNeighbors) continue;
                var lo = System.Math.Min(k, j);
                var hi = System.Math.Max(k, j);
                var key = ((long)lo << 32) | (uint)hi;
                if (!adj.Add(key)) continue;
                degree[k]++; degree[j]++;
                pairs.Add((lo, hi));
            }
        }
        return pairs;
    }

    // Edge between two power graph nodes. Either pylon-pylon (Hop) or
    // pylon-device (Service). Snapshot reads this list to draw cables.
    public readonly record struct PowerEdge(int FromEntityId, int ToEntityId, bool IsHop, int GridId);

    private readonly SimWorld _world;
    private readonly List<PowerEdge> _edges = new();
    private int _lastTopologyVersion = -1;

    public IReadOnlyList<PowerEdge> Edges => _edges;

    public PowerSystem(SimWorld world)
    {
        _world = world;
    }

    public void Tick(TickContext ctx)
    {
        if (_world.PowerVersion != _lastTopologyVersion)
        {
            RebuildTopology();
            _lastTopologyVersion = _world.PowerVersion;
        }
        RecomputeGridTotals();
    }

    private void RebuildTopology()
    {
        _edges.Clear();
        _world.Power.Clear();

        // Snapshot pylon entities + their tile coords. ComputeNeighborPairs
        // expects oldest→newest order so the recency-biased greedy claim
        // walks newest pylons first — sort by entity id so newer ids land
        // last in the list.
        var pylonRows = new List<(int id, int tx, int ty)>();
        foreach (var entity in _world.Store.Query<PowerNode, TilePosition>().Entities)
        {
            ref var node = ref entity.GetComponent<PowerNode>();
            if (node.Kind != PowerNodeKind.Pylon) continue;
            ref var pos = ref entity.GetComponent<TilePosition>();
            pylonRows.Add((entity.Id, pos.TileX, pos.TileY));
        }
        pylonRows.Sort((a, b) => a.id.CompareTo(b.id));
        var pylonIds = new List<int>(pylonRows.Count);
        var pylonTx = new List<int>(pylonRows.Count);
        var pylonTy = new List<int>(pylonRows.Count);
        for (var r = 0; r < pylonRows.Count; r++)
        {
            pylonIds.Add(pylonRows[r].id);
            pylonTx.Add(pylonRows[r].tx);
            pylonTy.Add(pylonRows[r].ty);
        }

        // Union-find across pylons within cable-hop range.
        var n = pylonIds.Count;
        var parent = new int[n];
        for (var i = 0; i < n; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { var ra = Find(a); var rb = Find(b); if (ra != rb) parent[ra] = rb; }

        var hopSqr = CableHopTiles * CableHopTiles;
        var pxBuf = new List<float>(n);
        var pyBuf = new List<float>(n);
        for (var i = 0; i < n; i++) { pxBuf.Add(pylonTx[i]); pyBuf.Add(pylonTy[i]); }
        var neighborPairs = ComputeNeighborPairs(pxBuf, pyBuf, hopSqr);
        for (var k = 0; k < neighborPairs.Count; k++) Union(neighborPairs[k].i, neighborPairs[k].j);

        // Assign grid ids per union-find root.
        var rootToGrid = new Dictionary<int, int>();
        var pylonGridId = new int[n];
        for (var i = 0; i < n; i++)
        {
            var r = Find(i);
            if (!rootToGrid.TryGetValue(r, out var gid))
            {
                gid = _world.Power.AllocateGridId();
                rootToGrid[r] = gid;
            }
            pylonGridId[i] = gid;
        }

        // Initialize per-grid counters.
        var grids = new Dictionary<int, PowerGrid>();
        foreach (var gid in rootToGrid.Values)
        {
            grids[gid] = new PowerGrid { Id = gid, Status = GridStatus.Online };
        }

        // Write GridId back to pylon components, count pylons per grid, and
        // emit pylon-pylon edges (one per shortest pair within range).
        for (var i = 0; i < n; i++)
        {
            var gid = pylonGridId[i];
            var entity = _world.Store.GetEntityById(pylonIds[i]);
            if (entity == default) continue;
            ref var node = ref entity.GetComponent<PowerNode>();
            node.GridId = gid;
            node.ServedByPylonId = 0;
            node.IsPowered = false;
            var g = grids[gid]; g.PylonCount++; grids[gid] = g;
        }
        for (var k = 0; k < neighborPairs.Count; k++)
        {
            var (i, j) = neighborPairs[k];
            if (pylonGridId[i] != pylonGridId[j]) continue;
            _edges.Add(new PowerEdge(pylonIds[i], pylonIds[j], IsHop: true, pylonGridId[i]));
        }

        // Attach sources + sinks to nearest pylon within service radius.
        var serviceSqr = ServiceRadiusTiles * ServiceRadiusTiles;
        foreach (var entity in _world.Store.Query<PowerNode, TilePosition>().Entities)
        {
            ref var node = ref entity.GetComponent<PowerNode>();
            if (node.Kind == PowerNodeKind.Pylon) continue;
            ref var pos = ref entity.GetComponent<TilePosition>();
            var bestIdx = -1;
            var bestSqr = float.MaxValue;
            for (var i = 0; i < n; i++)
            {
                var dx = pylonTx[i] - pos.TileX;
                var dy = pylonTy[i] - pos.TileY;
                var sqr = (float)(dx * dx + dy * dy);
                if (sqr > serviceSqr) continue;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                bestIdx = i;
            }
            if (bestIdx < 0)
            {
                node.GridId = -1;
                node.ServedByPylonId = 0;
                node.IsPowered = false;
                continue;
            }
            node.GridId = pylonGridId[bestIdx];
            node.ServedByPylonId = pylonIds[bestIdx];
            node.IsPowered = false;
            _edges.Add(new PowerEdge(entity.Id, pylonIds[bestIdx], IsHop: false, node.GridId));
            var g = grids[node.GridId];
            if (node.Kind == PowerNodeKind.Source) g.SourceCount++; else g.SinkCount++;
            grids[node.GridId] = g;
        }

        foreach (var (id, g) in grids) _world.Power.Set(g);
    }

    private void RecomputeGridTotals()
    {
        // Reset per-grid totals before summing.
        var grids = new Dictionary<int, PowerGrid>();
        foreach (var (id, g) in _world.Power.Grids)
        {
            grids[id] = g with { TotalSupplyW = 0f, TotalDemandW = 0f, Status = GridStatus.Online };
        }

        foreach (var entity in _world.Store.Query<PowerNode>().Entities)
        {
            ref var node = ref entity.GetComponent<PowerNode>();
            if (node.GridId < 0 || !grids.ContainsKey(node.GridId)) { node.IsPowered = false; continue; }
            var g = grids[node.GridId];
            if (node.Kind == PowerNodeKind.Source && node.IsActive) g.TotalSupplyW += node.SupplyW;
            // Sinks count as demand. Pylons with built-in load (lamp pylon)
            // also draw their DemandW from the grid even though their Kind
            // stays Pylon for topology purposes. A switched-off lamp draws
            // nothing — the grid still shows online for everyone else.
            var switchedOff = entity.HasComponent<LampSwitch>() && !entity.GetComponent<LampSwitch>().On;
            if (!switchedOff)
            {
                if (node.Kind == PowerNodeKind.Sink) g.TotalDemandW += node.DemandW;
                else if (node.Kind == PowerNodeKind.Pylon && node.DemandW > 0f) g.TotalDemandW += node.DemandW;
            }
            grids[node.GridId] = g;
        }

        foreach (var (id, gIn) in grids)
        {
            var g = gIn;
            if (g.TotalSupplyW <= 0f) g.Status = GridStatus.Blackout;
            else if (g.TotalSupplyW < g.TotalDemandW) g.Status = GridStatus.Brownout;
            else g.Status = GridStatus.Online;
            grids[id] = g;
            _world.Power.Set(g);
        }

        // Stamp IsPowered on each node from its grid status. Brownout still
        // leaves IsPowered true for now — consumer-side blackout handling
        // is plumbed but not implemented (per design call).
        foreach (var entity in _world.Store.Query<PowerNode>().Entities)
        {
            ref var node = ref entity.GetComponent<PowerNode>();
            if (node.GridId < 0 || !grids.TryGetValue(node.GridId, out var g))
            {
                node.IsPowered = false;
                continue;
            }
            node.IsPowered = g.Status != GridStatus.Blackout;
            // Switched-off lamp stays dark even on a healthy grid.
            if (entity.HasComponent<LampSwitch>() && !entity.GetComponent<LampSwitch>().On)
                node.IsPowered = false;
        }
    }
}
