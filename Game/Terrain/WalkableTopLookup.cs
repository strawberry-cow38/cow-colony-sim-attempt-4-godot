using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Snapshots;

namespace CowColonySim.Game.Terrain;

// Per-tile elevated walkable top in metres (max across all built structures
// at that tile). Used by terrain raycasts so RMB pathing and placement
// previews land on wall tops / roofs / ladder summits instead of the floor
// beneath. Ghosts are excluded — pathing can't traverse them yet, so picking
// up an unfinished surface would just produce an unreachable goal.
public static class WalkableTopLookup
{
    public static Func<int, int, float> Build(SimSnapshot snap)
    {
        var lookup = new Dictionary<long, float>();
        for (var i = 0; i < snap.Structures.Count; i++)
        {
            var st = snap.Structures[i];
            if (!BlueprintCatalog.TryGet(st.DefId, out var sd) || sd is null) continue;
            if (!sd.WalkableTop) continue;
            var (sw, sh) = (st.Rotation & 1) == 0 ? (sd.FootprintW, sd.FootprintH) : (sd.FootprintH, sd.FootprintW);
            var topMetres = (st.BaseLayer + sd.HeightQuanta) * 0.75f;
            for (var dy = 0; dy < sh; dy++)
            for (var dx = 0; dx < sw; dx++)
            {
                var key = ((long)(st.TileX + dx) << 32) | (uint)(st.TileY + dy);
                if (!lookup.TryGetValue(key, out var prev) || topMetres > prev) lookup[key] = topMetres;
            }
        }
        return (tx, ty) =>
        {
            var key = ((long)tx << 32) | (uint)ty;
            return lookup.TryGetValue(key, out var v) ? v : 0f;
        };
    }
}
