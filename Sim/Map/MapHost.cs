using CowColonySim.Sim.Lighting;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.Map;

public sealed class MapHost
{
    public MapSettings Settings { get; }
    public TileGrid Grid { get; }
    public SkyExposureSystem SkyExposure { get; }
    public ArtificialLightSystem ArtificialLight { get; }
    public TileLightingApi Lighting { get; }
    public LightingTickSystem LightingTick { get; }

    public MapHost(MapSettings settings, EntityStore store)
    {
        Settings = settings;
        Grid = new TileGrid(settings);
        SkyExposure = new SkyExposureSystem(Grid);
        ArtificialLight = new ArtificialLightSystem(Grid, store);
        Lighting = new TileLightingApi(Grid);
        LightingTick = new LightingTickSystem(settings, Lighting, SkyExposure, ArtificialLight);
        SkyExposure.RebuildAll();
    }
}
