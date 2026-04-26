using CowColonySim.Sim.Zones;

namespace CowColonySim.Sim.Snapshots;

// One placed zone. Game side draws the rect on the ground and shows
// the name / type. Snapshot copy avoids handing Game a live entity.
// Priority/CropDefId are per-type settings; unused fields read 0.
public readonly record struct ZoneView(
    int ZoneId,
    ZoneType Type,
    int MinTileX,
    int MinTileY,
    int MaxTileX,
    int MaxTileY,
    string Name,
    int Priority,
    int CropDefId);
