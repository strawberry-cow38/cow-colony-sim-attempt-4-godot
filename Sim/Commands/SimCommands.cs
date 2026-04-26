using System.Collections.Concurrent;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.Zones;

namespace CowColonySim.Sim.Commands;

// Discriminated union of player-issued commands. Game/* enqueues; the
// CommandSystem drains the bus at the top of each tick. The bus is the
// only sanctioned Game → Sim channel — Game still must not touch ECS
// directly.
public interface ISimCommand { }

public readonly record struct MoveCommand(int EntityId, TileCoord Target) : ISimCommand;

// Tile bbox (inclusive) where the heightfield just changed. CommandSystem
// drops any active path intersecting it so colonists re-plan instead of
// happily walking into a freshly-raised cliff.
public readonly record struct InvalidatePathsInRegion(int MinTileX, int MinTileY, int MaxTileX, int MaxTileY) : ISimCommand;

public readonly record struct CreateZoneCommand(ZoneType Type, TileRect Rect, string Name) : ISimCommand;

public readonly record struct StampDesignationsCommand(DesignationKind Kind, TileRect Rect) : ISimCommand;

public readonly record struct PlaceBlueprintGhostCommand(string DefId, int OriginTileX, int OriginTileY, int Rotation, int BaseLayer) : ISimCommand;

// Wipes any zone/designation/blueprint-ghost entity that overlaps the
// rect. Zones removed if any tile of their rect is inside; designations
// + blueprint ghosts removed if their tile/origin is inside. Colonists,
// need spots, and other gameplay entities are untouched.
public readonly record struct EraseInRectCommand(TileRect Rect) : ISimCommand;

// Update a zone's editable fields. Sim re-reads the zone by id and
// rewrites Name + the relevant per-type settings struct. Fields that
// don't apply for the zone's type are ignored.
public readonly record struct SetZoneSettingsCommand(
    int ZoneId,
    string Name,
    int Priority,
    int CropDefId) : ISimCommand;

public sealed class CommandBus
{
    private readonly ConcurrentQueue<ISimCommand> _queue = new();

    public void Submit(ISimCommand command) => _queue.Enqueue(command);

    public bool TryDequeue(out ISimCommand command) =>
        _queue.TryDequeue(out command!);
}
