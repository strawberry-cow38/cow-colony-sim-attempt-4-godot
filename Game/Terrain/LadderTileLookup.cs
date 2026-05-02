using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Snapshots;

namespace CowColonySim.Game.Terrain;

// Per-tile "is there a ladder here" check. The renderer needs this so a
// colonist mid-climb is allowed to ride sim-Z above the heightfield ground;
// any other tile snaps the colonist to terrain so a slope-walk with stale
// sim TileZ can't leave them floating.
public static class LadderTileLookup
{
    public static Func<int, int, bool> Build(SimSnapshot snap)
    {
        var tiles = new HashSet<long>();
        for (var i = 0; i < snap.Structures.Count; i++)
        {
            var st = snap.Structures[i];
            if (!BlueprintCatalog.TryGet(st.DefId, out var sd) || sd is null) continue;
            if (!sd.IsLadder) continue;
            tiles.Add(((long)st.TileX << 32) | (uint)st.TileY);
        }
        return (tx, ty) => tiles.Contains(((long)tx << 32) | (uint)ty);
    }
}
