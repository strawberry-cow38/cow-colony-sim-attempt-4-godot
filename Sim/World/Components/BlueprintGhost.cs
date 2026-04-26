using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// A placed-but-unbuilt blueprint. Carries the def id (lookup in
// BlueprintCatalog), the origin tile (footprint min-corner at rotation 0),
// rotation in 90° steps (0..3), build progress in [0,1], and a base
// layer (0 = on terrain; +1 = sits on the next floor up, used for
// stacking on wall-tops or upper storeys).
public struct BlueprintGhost : IComponent
{
    public string DefId = string.Empty;
    public int OriginTileX;
    public int OriginTileY;
    public int Rotation;
    public int BaseLayer;
    public float BuildProgress;

    public BlueprintGhost() { }
}
