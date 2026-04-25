namespace CowColonySim.Sim.Climate;

// Biome field is present-but-inert until the world map exists.
// No biome-specific temperature, rainfall, or vegetation effects yet.
public enum Biome : byte
{
    TemperateForest = 0,
    Tundra,
    Taiga,
    Grassland,
    Desert,
    Savanna,
    TropicalRainforest,
    Wetland,
}
