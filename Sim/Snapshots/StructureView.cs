namespace CowColonySim.Sim.Snapshots;

// One built structure on a tile. Game side resolves DefId against
// BlueprintCatalog for footprint + height. Bills is non-empty only on
// workstations that carry a Bills component. SwitchOn is null when the
// structure has no LampSwitch — context menu uses that to decide whether
// to show "turn on/off" entries.
public readonly record struct StructureView(
    int EntityId,
    string DefId,
    int TileX,
    int TileY,
    int Rotation,
    int BaseLayer,
    IReadOnlyList<BillView> Bills,
    bool? SwitchOn = null);
