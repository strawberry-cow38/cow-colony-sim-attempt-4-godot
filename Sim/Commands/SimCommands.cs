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

// Player-issued "prioritize this colonist on this tree" — pins the
// colonist's WorkJob to a specific tree and stamps a chop designation
// at the trunk if one isn't there yet. Tier-2 ASSIGNED in the priority
// model. Cleared automatically when the tree is felled or destroyed.
public readonly record struct PrioritizeChopCommand(int ColonistId, int TreeEntityId) : ISimCommand;

// Pin a colonist to haul a specific item stack to the best stockpile.
// Same model as PrioritizeChopCommand — clears any other colonist
// already targeting the stack, picks a drop tile, sets WorkJob.Forced.
public readonly record struct PrioritizeHaulCommand(int ColonistId, int ItemEntityId) : ISimCommand;

// Flip Item.Forbidden. When set to true, also clears every WorkJob
// that's hauling this item. If a carrier had already picked the stack
// up, the payload is dropped at the carrier's tile so we don't leak.
public readonly record struct SetItemForbiddenCommand(int ItemEntityId, bool Forbidden) : ISimCommand;

public readonly record struct PlaceBlueprintGhostCommand(string DefId, int OriginTileX, int OriginTileY, int Rotation, int BaseLayer) : ISimCommand;

// Cancel a placed blueprint. Drops any deposited material as item stacks
// at the blueprint origin tile, then deletes the ghost.
public readonly record struct CancelBlueprintCommand(int EntityId) : ISimCommand;

// Uninstall a built structure. For now: refunds 100% of the def's
// materials at the structure tile and removes the structure.
// Future: spawn a minified item that can be re-placed elsewhere.
public readonly record struct UninstallStructureCommand(int EntityId) : ISimCommand;

// Deconstruct a built structure. Refunds 50% of the def's materials
// at the structure tile and removes the structure.
// Future: timed work job; for now applied immediately.
public readonly record struct DeconstructStructureCommand(int EntityId) : ISimCommand;

// Wipes any zone/designation/blueprint-ghost entity that overlaps the
// rect. Zones removed if any tile of their rect is inside; designations
// + blueprint ghosts removed if their tile/origin is inside. Colonists,
// need spots, and other gameplay entities are untouched.
public readonly record struct EraseInRectCommand(TileRect Rect) : ISimCommand;

// Pin a colonist to walk to an item entity, pick the entire stack into
// their inventory, and lock it there. Auto-haul + auto-construct skip
// locked stacks — only ForceDropFromInventory releases.
public readonly record struct ForcePickupCommand(int ColonistId, int ItemEntityId) : ISimCommand;

// Drop one inventory stack at the colonist's current tile. Bypasses the
// Locked + Equipped flags — that's what "force" means.
public readonly record struct ForceDropFromInventoryCommand(int ColonistId, int StackIndex) : ISimCommand;

// Equip / unequip an in-inventory stack. Validation lives in InventoryOps.
public readonly record struct EquipFromInventoryCommand(int ColonistId, int StackIndex) : ISimCommand;
public readonly record struct UnequipInventoryCommand(int ColonistId, int StackIndex) : ISimCommand;

// Update a zone's editable fields. Sim re-reads the zone by id and
// rewrites Name + the relevant per-type settings struct. Fields that
// don't apply for the zone's type are ignored.
public readonly record struct SetZoneSettingsCommand(
    int ZoneId,
    string Name,
    int Priority,
    int CropDefId,
    bool AllowSowing,
    bool AllowHarvest) : ISimCommand;

public sealed class CommandBus
{
    private readonly ConcurrentQueue<ISimCommand> _queue = new();

    public void Submit(ISimCommand command) => _queue.Enqueue(command);

    public bool TryDequeue(out ISimCommand command) =>
        _queue.TryDequeue(out command!);
}
