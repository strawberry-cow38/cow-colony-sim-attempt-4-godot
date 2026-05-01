using System.Collections.Concurrent;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.World.Components;
using CowColonySim.Sim.Zones;

namespace CowColonySim.Sim.Commands;

// Discriminated union of player-issued commands. Game/* enqueues; the
// CommandSystem drains the bus at the top of each tick. The bus is the
// only sanctioned Game → Sim channel — Game still must not touch ECS
// directly.
public interface ISimCommand { }

// Queue=true (shift-RMB on a drafted colonist) appends Target to the
// PathFollower.WaypointQueue so the colonist routes there after their
// current path completes. Queue=false replaces both the active path and
// the queue, committing the colonist to the new order immediately.
public readonly record struct MoveCommand(int EntityId, TileCoord Target, bool Queue = false) : ISimCommand;

// Toggle Drafted.Active on the listed colonists. Drafted colonists
// stand still (no auto-jobs / hauls / wander) and only follow direct
// MoveCommand orders. Undrafted colonists ignore MoveCommand orders.
// Multi-id form so a multi-select R-press flips them as one batch
// without one mid-batch tick wedging the group out of sync.
public readonly record struct SetDraftedCommand(IReadOnlyList<int> EntityIds, bool Drafted) : ISimCommand;

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

// Same shape as PrioritizeChopCommand but for boulders + the Mine work
// kind. Stamps a Mine designation at the boulder tile if one isn't there
// yet, pre-empts any other colonist already targeting the boulder, and
// pins WorkJob.Forced so the colonist won't drop it for needs.
public readonly record struct PrioritizeMineCommand(int ColonistId, int BoulderEntityId) : ISimCommand;

// Pin a colonist to haul a specific item stack to the best stockpile.
// Same model as PrioritizeChopCommand — clears any other colonist
// already targeting the stack, picks a drop tile, sets WorkJob.Forced.
public readonly record struct PrioritizeHaulCommand(int ColonistId, int ItemEntityId) : ISimCommand;

// Pin a colonist to a blueprint. If the blueprint still needs material,
// the colonist is sent to haul wood (or a matching minified) toward it;
// once material is in, they switch to constructing. Pre-empts any other
// colonist mid-haul/mid-construct on the same blueprint.
public readonly record struct PrioritizeBuildCommand(int ColonistId, int BlueprintEntityId) : ISimCommand;

// Flip Item.Forbidden. When set to true, also clears every WorkJob
// that's hauling this item. If a carrier had already picked the stack
// up, the payload is dropped at the carrier's tile so we don't leak.
public readonly record struct SetItemForbiddenCommand(int ItemEntityId, bool Forbidden) : ISimCommand;

// Instant=true skips the ghost/haul/build pipeline and spawns the
// finished structure immediately. Used by god-mode placement.
public readonly record struct PlaceBlueprintGhostCommand(string DefId, int OriginTileX, int OriginTileY, int Rotation, int BaseLayer, bool Instant = false) : ISimCommand;

// Cancel a placed blueprint. Drops any deposited material as item stacks
// at the blueprint origin tile, then deletes the ghost.
public readonly record struct CancelBlueprintCommand(int EntityId) : ISimCommand;

// Uninstall a built structure. For now: refunds 100% of the def's
// materials at the structure tile and removes the structure.
// Future: spawn a minified item that can be re-placed elsewhere.
// Instant=true (god mode) removes the structure immediately and
// drops a minified thing — same end-state as a colonist completing
// the work, just without the worker step.
public readonly record struct UninstallStructureCommand(int EntityId, bool Instant = false) : ISimCommand;

// Deconstruct a built structure. Refunds 50% of the def's materials
// at the structure tile and removes the structure.
// Future: timed work job; for now applied immediately.
public readonly record struct DeconstructStructureCommand(int EntityId, bool Instant = false) : ISimCommand;

// Drag-rect deconstruct designator. Walks every structure whose footprint
// overlaps the rect and applies the same path as DeconstructStructureCommand
// to it: queues a Deconstruct designation in normal mode, instantly removes
// (with refund) when Instant=true.
public readonly record struct DeconstructInRectCommand(TileRect Rect, bool Instant = false) : ISimCommand;

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
// Set a colonist's priority for a single WorkType row in the priority
// panel. value 0 = "won't do this work", 1-8 = priority (1 highest).
// Out-of-range values clamp at MaxPriority.
public readonly record struct SetWorkPriorityCommand(int ColonistId, WorkType WorkType, byte Priority) : ISimCommand;

// Tweak a generator's output watts and on/off state. Watts clamps to the
// def's MaxSupplyW. Bumps SimWorld.PowerVersion so PowerSystem refreshes
// totals on the next tick.
public readonly record struct SetGeneratorOutputCommand(int EntityId, float Watts, bool IsOn) : ISimCommand;

public readonly record struct SetZoneSettingsCommand(
    int ZoneId,
    string Name,
    int Priority,
    int CropDefId,
    bool AllowSowing,
    bool AllowHarvest,
    ulong AllowedKindsMask) : ISimCommand;

// Append a new bill to a workstation. Recipe must be allowed on the
// workstation's def. No-op if not. Bills append in order; cook system
// runs the first unsuspended bill that has ingredients.
public readonly record struct AddBillCommand(int StructureId, string RecipeId) : ISimCommand;

public readonly record struct RemoveBillCommand(int StructureId, int BillIndex) : ISimCommand;

public readonly record struct ToggleBillSuspendCommand(int StructureId, int BillIndex) : ISimCommand;

// Cycle Forever → DoX → UntilCount → Forever. UI calls this on a click.
public readonly record struct CycleBillRepeatModeCommand(int StructureId, int BillIndex) : ISimCommand;

public readonly record struct SetBillTargetCountCommand(int StructureId, int BillIndex, int TargetCount) : ISimCommand;

public sealed class CommandBus
{
    private readonly ConcurrentQueue<ISimCommand> _queue = new();

    public void Submit(ISimCommand command) => _queue.Enqueue(command);

    public bool TryDequeue(out ISimCommand command) =>
        _queue.TryDequeue(out command!);
}
