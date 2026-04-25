using CowColonySim.Sim.Lighting;
using CowColonySim.Sim.Map;
using Friflo.Engine.ECS;
using Xunit;

namespace CowColonySim.Tests;

public class TileLightingApiTests
{
    [Fact]
    public void Total_picks_brighter_of_sun_or_artificial()
    {
        var settings = new MapSettings(Width: 8, Height: 8, MinZ: 0, MaxZ: 4);
        var store = new EntityStore();
        var host = new MapHost(settings, store);

        host.Lighting.SetGlobalSun(200);
        var entity = store.CreateEntity();
        entity.AddComponent(new TileCoord(2, 2, 1));
        entity.AddComponent(new LightEmitter(LightConstants.ArtificialMax, 3));
        host.ArtificialLight.MarkDirty();
        host.ArtificialLight.RebuildIfDirty();

        var sunOnlyTile = host.Lighting.TotalAt(6, 6, 1);
        Assert.Equal(200, sunOnlyTile);

        var bothTile = host.Lighting.TotalAt(2, 2, 1);
        Assert.Equal(200, bothTile);

        host.Lighting.SetGlobalSun(50);
        var artiBrighter = host.Lighting.TotalAt(2, 2, 1);
        Assert.Equal(LightConstants.ArtificialMax, artiBrighter);
    }

    [Fact]
    public void Roofed_tile_gets_no_sun_only_artificial()
    {
        var settings = new MapSettings(Width: 8, Height: 8, MinZ: 0, MaxZ: 4);
        var store = new EntityStore();
        var host = new MapHost(settings, store);

        host.Grid.SetFlag(3, 3, 3, TileFlags.HasRoof, true);
        host.SkyExposure.MarkColumnDirty(3, 3);
        host.SkyExposure.RebuildDirty();
        host.Lighting.SetGlobalSun(255);

        Assert.Equal(0, host.Lighting.SunAt(3, 3, 1));
        Assert.Equal(0, host.Lighting.TotalAt(3, 3, 1));

        var entity = store.CreateEntity();
        entity.AddComponent(new TileCoord(3, 3, 1));
        entity.AddComponent(new LightEmitter(100, 3));
        host.ArtificialLight.MarkDirty();
        host.ArtificialLight.RebuildIfDirty();

        Assert.True(host.Lighting.TotalAt(3, 3, 1) > 0);
    }
}
