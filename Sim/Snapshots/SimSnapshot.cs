using CowColonySim.Sim.Pathfinding;

namespace CowColonySim.Sim.Snapshots;

// Immutable end-of-tick view of the simulation. Game-side code only ever
// reads these — never reaches into the EntityStore directly.
public sealed record SimSnapshot(
    long TickNumber,
    double ElapsedSeconds,
    int EntityCount,
    IReadOnlyList<ColonistView> Colonists,
    IReadOnlyList<SpotView> Spots,
    IReadOnlyList<PathView> Paths,
    IReadOnlyList<ZoneView> Zones,
    IReadOnlyList<DesignationView> Designations,
    IReadOnlyList<BlueprintGhostView> BlueprintGhosts,
    IReadOnlyList<TreeView> Trees,
    IReadOnlyList<ItemView> Items,
    IReadOnlyList<StructureView> Structures,
    IReadOnlyList<TileCoord> TreeFalls,
    LightingView Lighting,
    WeatherView Weather)
{
    public static SimSnapshot Empty { get; } =
        new(0, 0.0, 0,
            Array.Empty<ColonistView>(),
            Array.Empty<SpotView>(),
            Array.Empty<PathView>(),
            Array.Empty<ZoneView>(),
            Array.Empty<DesignationView>(),
            Array.Empty<BlueprintGhostView>(),
            Array.Empty<TreeView>(),
            Array.Empty<ItemView>(),
            Array.Empty<StructureView>(),
            Array.Empty<TileCoord>(),
            LightingView.Empty,
            WeatherView.Empty);
}
