namespace CowColonySim.Sim.Climate;

// Atomic snapshot of climate state for a tick. Embedded into SimSnapshot.
// Tile-specific temperature is derived on demand via TemperatureModel.TileC
// to avoid re-snapshotting an entire 3D field.
public sealed record ClimateSnapshot(
    double GlobalSurfaceC,
    Season Season,
    Biome Biome,
    double WindDegrees,
    double WindSpeedMps,
    CompassDirection WindDirection,
    WindCategory WindCategory)
{
    public static ClimateSnapshot Empty { get; } = new(
        GlobalSurfaceC: 0.0,
        Season: Season.Spring,
        Biome: Biome.TemperateForest,
        WindDegrees: 0.0,
        WindSpeedMps: 0.0,
        WindDirection: CompassDirection.N,
        WindCategory: WindCategory.Calm);
}
