using CowColonySim.Sim.Terrain;

namespace CowColonySim.Sim.Pathfinding;

// Walkability + step cost adapter over a Heightfield. Tile (tx, ty) covers
// the four shared corner samples; "tile height" = average of those four.
// A step is walkable when the height delta in quanta is within MaxStepQuanta
// and (for diagonals) both cardinal neighbours are also walkable, so we
// don't squeeze through impassable cliff corners.
//
// Z (floor layer) is derived from the heightfield: Z = round(centerQuanta/2)
// so a 1.5 m vertical step = 1 layer = 2 quanta. The graph is queried
// through LayerCountAt(x, y) + LayerAt(x, y, idx) so that A* can enumerate
// every walkable surface at a tile. Today each (x, y) reports exactly one
// layer (the terrain floor); ramps and stairs will plug in by reporting
// extra layers without changing AStar.
//
// Pure-data immutable view: thread-safe to share across A* workers as long
// as the underlying Heightfield isn't being mutated concurrently.
public sealed class HeightGrid
{
    // 1 quantum = 0.75 m vertical step over a 1.5 m tile = ~26° max grade.
    // Anything steeper is treated as a cliff and pathing routes around it
    // even if a player MoveCommand targeted the top — the existing path
    // is also cancelled when a fresh edit raises the slope past this
    // threshold (see InvalidatePathsInRegion in CommandSystem).
    private const int MaxStepQuanta = 1;
    private const float SlopeCostPerQuanta = 0.4f;

    private readonly Heightfield _field;
    private readonly byte[] _blocked;
    // Sparse extra walkable layers per tile (key = y*Width+x). Ground floor
    // is implicit when !IsBlocked; wall tops, roof tops, ladder tops add to
    // this list. Layers are unique within a tile and not sorted.
    private readonly Dictionary<int, List<int>> _extraLayers = new();
    // Sparse vertical traversal edges per tile (key = y*Width+x). Each (a,b)
    // pair means "you can step from layer a to layer b at this (x,y) and back".
    // Ladders register here on completion.
    private readonly Dictionary<int, List<(int a, int b)>> _ladders = new();
    // Per-tile count of structures overhead that block sun + rain. Rooves
    // bump this on completion; LightingSystem + WeatherSystem multiply
    // sun/rainfall by 0 where the count is non-zero.
    private readonly byte[] _roofCount;

    public int Width { get; }
    public int Height { get; }

    public HeightGrid(Heightfield field)
    {
        _field = field;
        Width = field.VertWidth - 1;
        Height = field.VertHeight - 1;
        _blocked = new byte[Width * Height];
        _roofCount = new byte[Width * Height];
    }

    // Per-tile dynamic blocker. Trees, walls, and other tile-occupiers set
    // this; A* sees blocked tiles as impassable. Mutations happen on the
    // sim thread between path requests — not synchronized for in-flight A*
    // workers, so a tree felled mid-search may produce a one-tick stale
    // path that gets rerouted next path request.
    public bool IsBlocked(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return false;
        return _blocked[y * Width + x] != 0;
    }

    public void MarkBlocked(int x, int y, bool blocked)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        _blocked[y * Width + x] = blocked ? (byte)1 : (byte)0;
    }

    public bool InBounds(TileCoord t) =>
        (uint)t.X < (uint)Width && (uint)t.Y < (uint)Height;

    // Raw vertex sample in 0.75 m quanta. Used by build/placement validation
    // (level-footprint checks). Bounds check is on the caller.
    public short CornerQuanta(int vx, int vy) => _field.Get(vx, vy);

    // Build a 3D coord at (x, y) with floor Z derived from the heightfield.
    public TileCoord At(int x, int y) => new(x, y, FloorLayer(x, y));

    public int FloorLayer(int x, int y) =>
        (int)MathF.Round(CenterQuanta(x, y) * 0.5f);

    // How many distinct walkable surfaces exist at this tile. Ground floor
    // contributes 1 when not blocked; each registered extra (wall/roof/ladder
    // top) adds 1 more. Used by A* to enumerate candidate destination layers.
    public int LayerCountAt(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return 0;
        var ground = _blocked[y * Width + x] == 0 ? 1 : 0;
        return ground + ExtraLayerCount(x, y);
    }

    // The Z value of the idx-th walkable surface at (x, y). idx must be in
    // [0, LayerCountAt(x, y)).
    public int LayerAt(int x, int y, int idx)
    {
        var groundCount = (_blocked[y * Width + x] == 0) ? 1 : 0;
        if (idx < groundCount) return FloorLayer(x, y);
        return _extraLayers[y * Width + x][idx - groundCount];
    }

    // Convenience: build a TileCoord for the idx-th walkable surface at (x, y).
    public TileCoord NodeAt(int x, int y, int idx) => new(x, y, LayerAt(x, y, idx));

    // True if (x, y) has a walkable surface at the given Z. Used by CanStep
    // to validate horizontal hops onto an elevated surface (wall top, roof,
    // ladder top) without re-enumerating LayerAt.
    public bool HasWalkableLayer(int x, int y, int z)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return false;
        if (_blocked[y * Width + x] == 0 && z == FloorLayer(x, y)) return true;
        if (_extraLayers.TryGetValue(y * Width + x, out var list))
        {
            for (var i = 0; i < list.Count; i++) if (list[i] == z) return true;
        }
        return false;
    }

    public void AddWalkableLayer(int x, int y, int z)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        var key = y * Width + x;
        if (!_extraLayers.TryGetValue(key, out var list))
        {
            list = new List<int>(2);
            _extraLayers[key] = list;
        }
        for (var i = 0; i < list.Count; i++) if (list[i] == z) return;
        list.Add(z);
    }

    public void RemoveWalkableLayer(int x, int y, int z)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        var key = y * Width + x;
        if (!_extraLayers.TryGetValue(key, out var list)) return;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] != z) continue;
            list.RemoveAt(i);
            if (list.Count == 0) _extraLayers.Remove(key);
            return;
        }
    }

    // Vertical traversal edges (ladders). Bidirectional — a ladder registered
    // (a, b) lets a colonist step from layer a→b and from layer b→a at this
    // tile. A* enumerates these as same-tile neighbours of the current node.
    public void AddLadder(int x, int y, int fromZ, int toZ)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        if (fromZ == toZ) return;
        var key = y * Width + x;
        if (!_ladders.TryGetValue(key, out var list))
        {
            list = new List<(int, int)>(1);
            _ladders[key] = list;
        }
        for (var i = 0; i < list.Count; i++)
        {
            var (a, b) = list[i];
            if ((a == fromZ && b == toZ) || (a == toZ && b == fromZ)) return;
        }
        list.Add((fromZ, toZ));
    }

    public void RemoveLadder(int x, int y, int fromZ, int toZ)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        var key = y * Width + x;
        if (!_ladders.TryGetValue(key, out var list)) return;
        for (var i = 0; i < list.Count; i++)
        {
            var (a, b) = list[i];
            if ((a == fromZ && b == toZ) || (a == toZ && b == fromZ))
            {
                list.RemoveAt(i);
                if (list.Count == 0) _ladders.Remove(key);
                return;
            }
        }
    }

    public bool IsRoofed(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return false;
        return _roofCount[y * Width + x] != 0;
    }

    public void AddRoof(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        var i = y * Width + x;
        if (_roofCount[i] < byte.MaxValue) _roofCount[i]++;
    }

    public void RemoveRoof(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        var i = y * Width + x;
        if (_roofCount[i] > 0) _roofCount[i]--;
    }

    public int LadderCountAt(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return 0;
        return _ladders.TryGetValue(y * Width + x, out var list) ? list.Count : 0;
    }

    // Returns the partner layer for the idx-th ladder at (x, y) viewed from
    // currentZ — i.e. if the ladder spans (a, b) and currentZ == a, returns b.
    // -1 if currentZ doesn't sit on either end of the idx-th ladder.
    public int LadderPartnerAt(int x, int y, int currentZ, int idx)
    {
        var list = _ladders[y * Width + x];
        var (a, b) = list[idx];
        if (currentZ == a) return b;
        if (currentZ == b) return a;
        return -1;
    }

    private int ExtraLayerCount(int x, int y)
    {
        return _extraLayers.TryGetValue(y * Width + x, out var list) ? list.Count : 0;
    }

    public bool CanStep(TileCoord from, TileCoord to)
    {
        if (!InBounds(from) || !InBounds(to)) return false;
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        // Same-tile vertical move = ladder traversal. Allowed only when a
        // registered ladder edge connects from.Z and to.Z at this tile.
        if (dx == 0 && dy == 0)
        {
            if (from.Z == to.Z) return false;
            return HasLadderEdge(to.X, to.Y, from.Z, to.Z);
        }
        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1) return false;

        // Destination must expose a walkable surface at the requested Z (ground
        // floor when not blocked, or an extra layer registered by wall/roof/ladder).
        if (!HasWalkableLayer(to.X, to.Y, to.Z)) return false;

        // Elevated → elevated: pure planar at fixed Z. Slope check skipped
        // since heightfield reflects ground, not the structure top.
        var fromIsGround = !IsBlocked(from.X, from.Y) && from.Z == FloorLayer(from.X, from.Y);
        var toIsGround = !IsBlocked(to.X, to.Y) && to.Z == FloorLayer(to.X, to.Y);
        if (!fromIsGround || !toIsGround)
        {
            if (from.Z != to.Z) return false;
            return true;
        }

        // Ground → ground: legacy slope gate.
        var dh = Math.Abs(CenterQuanta(to.X, to.Y) - CenterQuanta(from.X, from.Y));
        if (dh > MaxStepQuanta) return false;
        if (dx != 0 && dy != 0)
        {
            if (Math.Abs(CenterQuanta(from.X + dx, from.Y) - CenterQuanta(from.X, from.Y)) > MaxStepQuanta) return false;
            if (Math.Abs(CenterQuanta(from.X, from.Y + dy) - CenterQuanta(from.X, from.Y)) > MaxStepQuanta) return false;
        }
        return true;
    }

    private bool HasLadderEdge(int x, int y, int a, int b)
    {
        if (!_ladders.TryGetValue(y * Width + x, out var list)) return false;
        for (var i = 0; i < list.Count; i++)
        {
            var (la, lb) = list[i];
            if ((la == a && lb == b) || (la == b && lb == a)) return true;
        }
        return false;
    }

    public float StepCost(TileCoord from, TileCoord to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        // Same-tile ladder climb — flat cost per layer crossed. Tuned so a 2-layer
        // climb is roughly the cost of two diagonal steps; encourages ground routes
        // when available but doesn't make ladders wildly expensive.
        if (dx == 0 && dy == 0) return Math.Abs(to.Z - from.Z) * 1.4f;
        var planar = (dx != 0 && dy != 0) ? MathF.Sqrt(2f) : 1f;
        // Skip terrain slope cost for elevated steps — heightfield doesn't apply.
        var fromIsGround = !IsBlocked(from.X, from.Y) && from.Z == FloorLayer(from.X, from.Y);
        var toIsGround = !IsBlocked(to.X, to.Y) && to.Z == FloorLayer(to.X, to.Y);
        if (!fromIsGround || !toIsGround) return planar;
        var slope = Math.Abs(CenterQuanta(to.X, to.Y) - CenterQuanta(from.X, from.Y));
        return planar + slope * SlopeCostPerQuanta;
    }

    public static float OctileHeuristic(TileCoord a, TileCoord b)
    {
        var dx = Math.Abs(a.X - b.X);
        var dy = Math.Abs(a.Y - b.Y);
        var min = Math.Min(dx, dy);
        var max = Math.Max(dx, dy);
        return (max - min) + min * MathF.Sqrt(2f);
    }

    private float CenterQuanta(int x, int y)
    {
        var a = _field.Get(x, y);
        var b = _field.Get(x + 1, y);
        var c = _field.Get(x, y + 1);
        var d = _field.Get(x + 1, y + 1);
        return (a + b + c + d) * 0.25f;
    }
}
