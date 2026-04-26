using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// Tile coordinates plus sub-tile float offset in [0, 1) on each axis.
// Sim never thinks in Godot units; Game multiplies by GodotUnitsPerTile
// when rendering. Z is the vertical tile layer.
public struct TilePosition : IComponent
{
    public int TileX;
    public int TileY;
    public int TileZ;

    public float SubX;
    public float SubY;
    public float SubZ;

    public TilePosition(int tileX, int tileY, int tileZ, float subX = 0f, float subY = 0f, float subZ = 0f)
    {
        TileX = tileX;
        TileY = tileY;
        TileZ = tileZ;
        SubX = subX;
        SubY = subY;
        SubZ = subZ;
    }

    public readonly float MetersX => (TileX + SubX) * SimConstants.MetersPerTile;
    public readonly float MetersY => (TileY + SubY) * SimConstants.MetersPerTile;
    public readonly float MetersZ => (TileZ + SubZ) * SimConstants.MetersPerTile;
}
