using System.Collections.Generic;
using CowColonySim.Sim.World.Components;

namespace CowColonySim.Sim.Blueprints;

// Static, hand-authored blueprint description. Lives in BlueprintCatalog
// keyed by Id. Per-instance state (origin, rotation, build progress)
// lives on the BlueprintGhost component.
//
// FootprintW/H are in tiles, expressed at rotation = 0. Rotation rotates
// the footprint and any FootprintRequirement offsets together.
public sealed record BlueprintDef(
    string Id,
    string DisplayName,
    BlueprintCategory Category,
    PlacementMode Placement,
    int FootprintW,
    int FootprintH,
    bool Rotatable,
    IReadOnlyList<FootprintRequirement> Requirements,
    float HeightMeters = 1.5f,
    IReadOnlyList<MaterialCost>? Materials = null,
    // Power-graph role attached when SpawnStructure materializes this def.
    // null = not a power node. Pylons leave watts at 0 (relays only).
    PowerNodeKind? Power = null,
    float DefaultSupplyW = 0f,
    float DefaultDemandW = 0f,
    float MaxSupplyW = 0f,
    // SpacedDrag spacing in tiles (only meaningful when Placement=SpacedDrag).
    int DragSpacingTiles = 0,
    // When false (default), placing on top of an existing structure/blueprint
    // is rejected — a 2nd pylon/furniture clicked on the same tile won't
    // auto-stack above the first. Walls/doors flip this so multi-tier
    // wall stacks still work via auto-resolved BaseLayer.
    bool Stackable = false,
    // Marks this def as a usable top surface — table-lamps, decor, etc.
    // can be placed atop it. Coupled with RequiresSurface on the consumer.
    bool IsSurface = false,
    // When true, placement is only valid where every footprint tile has a
    // structure/ghost flagged IsSurface ending exactly at the new BaseLayer.
    // Auto-stacks BaseLayer onto the surface top so the player doesn't have
    // to fiddle with Q/E.
    bool RequiresSurface = false)
{
    // Height converted to 0.75 m vertical quanta — matches the terrain
    // quantum + the build-layer step. Quarter wall = 1, half wall = 2,
    // full wall = 4.
    public int HeightQuanta => System.Math.Max(1, (int)System.MathF.Round(HeightMeters / 0.75f));

    public IReadOnlyList<MaterialCost> MaterialsOrEmpty =>
        Materials ?? System.Array.Empty<MaterialCost>();

    // Rotates a footprint-relative offset so it tracks the rotated
    // footprint correctly. Footprint placement only swaps W↔H on rot&1
    // and keeps origin in the +x/+y quadrant — a naive (-x, -y) on rot=2
    // would put the interaction tile on the opposite corner. This helper
    // mirrors the W↔H-swap convention so InteractionSpot / VentSide
    // offsets stay glued to the actual rotated footprint cells.
    public (int x, int y) RotateOffset(int x, int y, int rot) => (rot & 3) switch
    {
        1 => (y, FootprintW - 1 - x),
        2 => (FootprintW - 1 - x, FootprintH - 1 - y),
        3 => (FootprintH - 1 - y, x),
        _ => (x, y),
    };
}
