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
}
