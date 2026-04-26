namespace CowColonySim.Sim.Zones;

// Per-tile membership mask for a zone whose footprint may not be a
// solid rectangle (e.g. two stockpiles that overlapped and merged).
// Mask is sized to the bbox: index = (y - MinY) * Width + (x - MinX).
// Bbox is always trimmed to populated tiles, so an empty mask means
// the zone has no tiles and should be deleted.
public static class TileMask
{
    public static bool[] Filled(TileRect rect)
    {
        var mask = new bool[rect.Width * rect.Height];
        Array.Fill(mask, true);
        return mask;
    }

    public static bool Get(TileRect rect, bool[] mask, int tx, int ty)
    {
        if (!rect.Contains(tx, ty)) return false;
        return mask[(ty - rect.MinY) * rect.Width + (tx - rect.MinX)];
    }

    public static bool Intersects(TileRect a, bool[] ma, TileRect b, bool[] mb)
    {
        if (a.MaxX < b.MinX || a.MinX > b.MaxX) return false;
        if (a.MaxY < b.MinY || a.MinY > b.MaxY) return false;
        var minX = Math.Max(a.MinX, b.MinX);
        var minY = Math.Max(a.MinY, b.MinY);
        var maxX = Math.Min(a.MaxX, b.MaxX);
        var maxY = Math.Min(a.MaxY, b.MaxY);
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                if (Get(a, ma, x, y) && Get(b, mb, x, y)) return true;
            }
        }
        return false;
    }

    public static (TileRect, bool[]) Union(TileRect a, bool[] ma, TileRect b, bool[] mb)
    {
        var bbox = new TileRect(
            Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY),
            Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY));
        var w = bbox.Width;
        var mask = new bool[w * bbox.Height];
        Stamp(bbox, mask, a, ma);
        Stamp(bbox, mask, b, mb);
        return (bbox, mask);
    }

    // Returns null mask if the subtraction empties the zone. Otherwise
    // returns a tightened bbox + mask trimmed to populated rows/cols.
    public static (TileRect, bool[])? SubtractRect(TileRect rect, bool[] mask, TileRect erase)
    {
        var trimmed = (bool[])mask.Clone();
        var w = rect.Width;
        var minX = Math.Max(rect.MinX, erase.MinX);
        var minY = Math.Max(rect.MinY, erase.MinY);
        var maxX = Math.Min(rect.MaxX, erase.MaxX);
        var maxY = Math.Min(rect.MaxY, erase.MaxY);
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                trimmed[(y - rect.MinY) * w + (x - rect.MinX)] = false;
            }
        }
        return Trim(rect, trimmed);
    }

    private static void Stamp(TileRect dst, bool[] dstMask, TileRect src, bool[] srcMask)
    {
        var w = dst.Width;
        for (var y = src.MinY; y <= src.MaxY; y++)
        {
            for (var x = src.MinX; x <= src.MaxX; x++)
            {
                if (Get(src, srcMask, x, y))
                {
                    dstMask[(y - dst.MinY) * w + (x - dst.MinX)] = true;
                }
            }
        }
    }

    private static (TileRect, bool[])? Trim(TileRect rect, bool[] mask)
    {
        var minX = int.MaxValue; var minY = int.MaxValue;
        var maxX = int.MinValue; var maxY = int.MinValue;
        var w = rect.Width;
        for (var y = rect.MinY; y <= rect.MaxY; y++)
        {
            for (var x = rect.MinX; x <= rect.MaxX; x++)
            {
                if (!mask[(y - rect.MinY) * w + (x - rect.MinX)]) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        if (minX == int.MaxValue) return null;
        if (minX == rect.MinX && minY == rect.MinY && maxX == rect.MaxX && maxY == rect.MaxY)
            return (rect, mask);
        var trimmedRect = new TileRect(minX, minY, maxX, maxY);
        var tw = trimmedRect.Width;
        var trimmed = new bool[tw * trimmedRect.Height];
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                trimmed[(y - minY) * tw + (x - minX)] = mask[(y - rect.MinY) * w + (x - rect.MinX)];
            }
        }
        return (trimmedRect, trimmed);
    }
}
