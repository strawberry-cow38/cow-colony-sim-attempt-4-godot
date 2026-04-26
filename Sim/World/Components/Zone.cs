using CowColonySim.Sim.Zones;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// A named, rect-shaped zone that exists in the world. Stockpiles and
// farms ride on this component; per-type settings are separate
// components (StockpileSettings, FarmSettings) so we don't pay for
// fields a zone doesn't use.
public struct Zone : IComponent
{
    public int ZoneId;
    public ZoneType Type;
    public TileRect Rect;
    public string Name = string.Empty;

    public Zone() { }
}
