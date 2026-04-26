using CowColonySim.Sim.Terrain;
using Xunit;

namespace CowColonySim.Tests;

public class HeightfieldTests
{
    [Fact]
    public void Vertex_grid_is_one_larger_than_tile_grid()
    {
        var hf = new Heightfield(tileWidth: 8, tileHeight: 4);
        Assert.Equal(9, hf.VertWidth);
        Assert.Equal(5, hf.VertHeight);
        Assert.Equal(0, hf.Version);
    }

    [Fact]
    public void Set_clamps_to_quanta_range()
    {
        var hf = new Heightfield(2, 2);
        hf.Set(0, 0, (short)(TerrainConstants.MaxQuanta + 100));
        Assert.Equal(TerrainConstants.MaxQuanta, hf.Get(0, 0));

        hf.Set(0, 0, (short)(TerrainConstants.MinQuanta - 100));
        Assert.Equal(TerrainConstants.MinQuanta, hf.Get(0, 0));
    }

    [Fact]
    public void Set_bumps_version_only_on_change()
    {
        var hf = new Heightfield(2, 2);
        hf.Set(1, 1, 5);
        var v1 = hf.Version;
        hf.Set(1, 1, 5);
        Assert.Equal(v1, hf.Version);
        hf.Set(1, 1, 6);
        Assert.True(hf.Version > v1);
    }

    [Fact]
    public void Metres_uses_vertical_quantum()
    {
        var hf = new Heightfield(2, 2);
        hf.Set(0, 0, 4);
        Assert.Equal(4 * TerrainConstants.VerticalQuantumMetres, hf.MetresAt(0, 0));
    }

    [Fact]
    public void Fill_writes_every_corner()
    {
        var hf = new Heightfield(3, 3);
        hf.Fill(2);
        for (var y = 0; y < hf.VertHeight; y++)
        {
            for (var x = 0; x < hf.VertWidth; x++)
            {
                Assert.Equal(2, hf.Get(x, y));
            }
        }
    }

    [Fact]
    public void Negative_dimensions_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Heightfield(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Heightfield(4, -1));
    }

    [Fact]
    public void In_bounds_matches_vertex_grid()
    {
        var hf = new Heightfield(2, 2);
        Assert.True(hf.InBounds(0, 0));
        Assert.True(hf.InBounds(2, 2));
        Assert.False(hf.InBounds(3, 0));
        Assert.False(hf.InBounds(0, 3));
        Assert.False(hf.InBounds(-1, 0));
    }

    [Fact]
    public void Surface_metres_returns_corner_height_at_corners()
    {
        var hf = new Heightfield(2, 2);
        hf.Set(0, 0, 4);
        hf.Set(1, 0, 8);
        hf.Set(0, 1, 12);
        hf.Set(1, 1, 0);
        var q = TerrainConstants.VerticalQuantumMetres;
        Assert.Equal(4 * q, hf.SurfaceMetresAt(0f, 0f), 4);
        Assert.Equal(8 * q, hf.SurfaceMetresAt(1f, 0f), 4);
        Assert.Equal(12 * q, hf.SurfaceMetresAt(0f, 1f), 4);
        Assert.Equal(0f, hf.SurfaceMetresAt(1f, 1f), 4);
    }

    [Fact]
    public void Surface_metres_interpolates_inside_triangle_TL_TR_BL()
    {
        var hf = new Heightfield(2, 2);
        hf.Set(0, 0, 0);
        hf.Set(1, 0, 10);
        hf.Set(0, 1, 20);
        hf.Set(1, 1, 99);
        var q = TerrainConstants.VerticalQuantumMetres;
        // (u, v) = (0.25, 0.25), u + v <= 1 → triangle TL/TR/BL only
        Assert.Equal((0 + 0.25f * 10 + 0.25f * 20) * q, hf.SurfaceMetresAt(0.25f, 0.25f), 4);
    }

    [Fact]
    public void Surface_metres_interpolates_inside_triangle_TR_BR_BL()
    {
        var hf = new Heightfield(2, 2);
        hf.Set(0, 0, 99);
        hf.Set(1, 0, 10);
        hf.Set(0, 1, 20);
        hf.Set(1, 1, 4);
        var q = TerrainConstants.VerticalQuantumMetres;
        // (u, v) = (0.75, 0.75), u + v > 1 → triangle TR/BR/BL only.
        // Barycentric: w_TR = 1-v, w_BR = u+v-1, w_BL = 1-u.
        const float u = 0.75f;
        const float v = 0.75f;
        var expected = ((1f - v) * 10 + (u + v - 1f) * 4 + (1f - u) * 20) * q;
        Assert.Equal(expected, hf.SurfaceMetresAt(u, v), 4);
    }

    [Fact]
    public void Surface_metres_clamps_out_of_range_input()
    {
        var hf = new Heightfield(2, 2);
        hf.Set(0, 0, 7);
        hf.Set(2, 2, 9);
        var q = TerrainConstants.VerticalQuantumMetres;
        Assert.Equal(7 * q, hf.SurfaceMetresAt(-5f, -5f), 4);
        Assert.Equal(9 * q, hf.SurfaceMetresAt(99f, 99f), 4);
    }

    [Fact]
    public void Surface_metres_is_continuous_across_diagonal()
    {
        var hf = new Heightfield(2, 2);
        hf.Set(0, 0, 0);
        hf.Set(1, 0, 30);
        hf.Set(0, 1, 70);
        hf.Set(1, 1, 100);
        // Approach diagonal u + v = 1 from both sides — heights must agree
        // in the limit. Use a very small epsilon so the 60*eps gap is below
        // the assertion tolerance.
        var a = hf.SurfaceMetresAt(0.5f - 1e-6f, 0.5f);
        var b = hf.SurfaceMetresAt(0.5f + 1e-6f, 0.5f);
        Assert.Equal(a, b, 3);
    }

    [Fact]
    public void Dirty_region_starts_empty_and_clears_on_consume()
    {
        var hf = new Heightfield(8, 8);
        Assert.False(hf.HasDirtyRegion);
        Assert.False(hf.TryConsumeDirtyRegion(out _, out _, out _, out _));
    }

    [Fact]
    public void Dirty_region_tracks_bbox_of_changed_vertices()
    {
        var hf = new Heightfield(8, 8);
        hf.Set(2, 3, 5);
        hf.Set(6, 1, 5);
        hf.Set(4, 4, 5);
        Assert.True(hf.HasDirtyRegion);
        Assert.True(hf.TryConsumeDirtyRegion(out var minX, out var minY, out var maxX, out var maxY));
        Assert.Equal(2, minX);
        Assert.Equal(1, minY);
        Assert.Equal(6, maxX);
        Assert.Equal(4, maxY);
        Assert.False(hf.HasDirtyRegion);
    }

    [Fact]
    public void Dirty_region_ignores_no_op_sets()
    {
        var hf = new Heightfield(4, 4);
        hf.Set(2, 2, 0);
        Assert.False(hf.HasDirtyRegion);
    }

    [Fact]
    public void Dirty_region_after_consume_only_tracks_subsequent_writes()
    {
        var hf = new Heightfield(8, 8);
        hf.Set(0, 0, 5);
        hf.TryConsumeDirtyRegion(out _, out _, out _, out _);
        hf.Set(7, 7, 5);
        Assert.True(hf.TryConsumeDirtyRegion(out var minX, out var minY, out var maxX, out var maxY));
        Assert.Equal(7, minX);
        Assert.Equal(7, minY);
        Assert.Equal(7, maxX);
        Assert.Equal(7, maxY);
    }
}
