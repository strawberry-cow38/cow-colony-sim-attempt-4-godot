using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// A placed-but-unbuilt blueprint. Carries the def id (lookup in
// BlueprintCatalog), the origin tile (footprint min-corner at rotation 0),
// rotation in 90° steps (0..3), and build progress in [0,1]. When
// progress hits 1 a system swaps this for the real built entity.
public struct BlueprintGhost : IComponent
{
    public string DefId;
    public int OriginTileX;
    public int OriginTileY;
    public int Rotation;
    public float BuildProgress;
}
