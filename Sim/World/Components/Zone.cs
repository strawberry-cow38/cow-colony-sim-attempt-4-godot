using CowColonySim.Sim.Zones;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// A named zone with a per-tile mask so non-rectangular shapes (after
// merges or partial erases) survive intact. Rect is the tight bbox
// around the populated mask. Stockpiles and farms ride on this
// component; per-type settings are separate components
// (StockpileSettings, FarmSettings).
public struct Zone : IComponent
{
    public int ZoneId;
    public ZoneType Type;
    public TileRect Rect;
    public bool[] Mask;
    public string Name;

    public Zone()
    {
        Mask = Array.Empty<bool>();
        Name = string.Empty;
    }

    public bool ContainsTile(int tx, int ty) => TileMask.Get(Rect, Mask, tx, ty);
}
