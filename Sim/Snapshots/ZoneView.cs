using CowColonySim.Sim.Zones;

namespace CowColonySim.Sim.Snapshots;

// One placed zone. Mask is a per-tile bool covering the bbox so
// non-rectangular shapes (after merges or partial erases) round-trip
// to Game intact: mask[(y - MinTileY) * Width + (x - MinTileX)] tells
// you whether that tile is in the zone.
public readonly record struct ZoneView(
    int ZoneId,
    ZoneType Type,
    int MinTileX,
    int MinTileY,
    int MaxTileX,
    int MaxTileY,
    bool[] Mask,
    string Name,
    int Priority,
    int CropDefId,
    bool AllowSowing,
    bool AllowHarvest)
{
    public int Width => MaxTileX - MinTileX + 1;
    public int Height => MaxTileY - MinTileY + 1;

    public bool ContainsTile(int tx, int ty)
    {
        if (tx < MinTileX || tx > MaxTileX || ty < MinTileY || ty > MaxTileY) return false;
        return Mask[(ty - MinTileY) * Width + (tx - MinTileX)];
    }

    public int TileCount
    {
        get
        {
            var n = 0;
            for (var i = 0; i < Mask.Length; i++) if (Mask[i]) n++;
            return n;
        }
    }
}
