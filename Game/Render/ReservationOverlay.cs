using CowColonySim.Game.Selection;
using CowColonySim.Sim;
using CowColonySim.Sim.Blueprints;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Render;

// Draws reservation pads (InteractionSpot, VentSide) on whichever
// blueprint ghost or built structure is currently selected. Mirrors
// the cursor-time pads in BlueprintGhostPreview but bound to selection
// rather than the placement tool.
public partial class ReservationOverlay : Node3D
{
    private SelectionService _selection = null!;
    private SnapshotPublisher _publisher = null!;
    private Heightfield _field = null!;
    private float _unitsPerMeter;
    private readonly List<MeshInstance3D> _tiles = new();

    public void Configure(SelectionService selection, SnapshotPublisher publisher, Heightfield field)
    {
        _selection = selection;
        _publisher = publisher;
        _field = field;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
    }

    public override void _Process(double delta)
    {
        var snap = _publisher.Current;
        if (TryDrawForGhost(snap)) return;
        if (TryDrawForStructure(snap)) return;
        HideAll();
    }

    private bool TryDrawForGhost(SimSnapshot snap)
    {
        if (_selection.SelectedBlueprintId is not int id) return false;
        for (var i = 0; i < snap.BlueprintGhosts.Count; i++)
        {
            var g = snap.BlueprintGhosts[i];
            if (g.EntityId != id) continue;
            if (!BlueprintCatalog.TryGet(g.DefId, out var def) || def is null) continue;
            Draw(def, g.Rotation, g.OriginTileX, g.OriginTileY);
            return true;
        }
        return false;
    }

    private bool TryDrawForStructure(SimSnapshot snap)
    {
        if (_selection.SelectedStructureId is not int id) return false;
        for (var i = 0; i < snap.Structures.Count; i++)
        {
            var s = snap.Structures[i];
            if (s.EntityId != id) continue;
            if (!BlueprintCatalog.TryGet(s.DefId, out var def) || def is null) continue;
            Draw(def, s.Rotation, s.TileX, s.TileY);
            return true;
        }
        return false;
    }

    private void Draw(BlueprintDef def, int rot, int originX, int originY)
    {
        var reqs = def.Requirements;
        EnsureCount(reqs.Count);
        for (var i = 0; i < reqs.Count; i++)
        {
            var r = reqs[i];
            var (offX, offY) = def.RotateOffset(r.OffsetX, r.OffsetY, rot);
            var tx = originX + offX;
            var ty = originY + offY;
            var unitsPerTile = SimConstants.GodotUnitsPerTile;
            var tile = _tiles[i];
            var mat = (StandardMaterial3D)((BoxMesh)tile.Mesh).Material;
            mat.AlbedoColor = ColorFor(r.Kind);
            var ground = _field.SurfaceMetresAt(tx + 0.5f, ty + 0.5f) * _unitsPerMeter;
            tile.Position = new Vector3((tx + 0.5f) * unitsPerTile, ground + 1.5f, (ty + 0.5f) * unitsPerTile);
            tile.Visible = true;
        }
        for (var i = reqs.Count; i < _tiles.Count; i++) _tiles[i].Visible = false;
    }

    private void HideAll()
    {
        for (var i = 0; i < _tiles.Count; i++) _tiles[i].Visible = false;
    }

    private void EnsureCount(int count)
    {
        while (_tiles.Count < count)
        {
            var unitsPerTile = SimConstants.GodotUnitsPerTile;
            var tile = new MeshInstance3D
            {
                Mesh = new BoxMesh
                {
                    Size = new Vector3(unitsPerTile * 0.85f, 1.5f, unitsPerTile * 0.85f),
                    Material = new StandardMaterial3D
                    {
                        AlbedoColor = new Color(0.3f, 0.85f, 0.85f, 0.55f),
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    },
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            _tiles.Add(tile);
            AddChild(tile);
        }
    }


    private static Color ColorFor(FootprintRequirementKind kind) => kind switch
    {
        FootprintRequirementKind.InteractionSpot => new Color(0.30f, 0.85f, 0.85f, 0.55f),
        FootprintRequirementKind.VentSide => new Color(0.95f, 0.40f, 0.85f, 0.55f),
        _ => new Color(0.85f, 0.85f, 0.85f, 0.55f),
    };
}
