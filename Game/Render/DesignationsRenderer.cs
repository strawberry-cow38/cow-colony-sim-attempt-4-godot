using CowColonySim.Sim;
using CowColonySim.Sim.Designations;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Render;

// Reads SimSnapshot.Designations each frame and floats a colored cube
// over each designated tile. Color picks per DesignationKind — red
// for ChopTree, gray for Mine, yellow for Harvest. Dummy renderer;
// real version will draw kind-specific glyphs.
public partial class DesignationsRenderer : Node3D
{
    private const float MarkerSizeMeters = 0.35f;
    private const float HoverMeters = 1.2f;

    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;

    private MultiMeshInstance3D _chop = null!;
    private MultiMeshInstance3D _mine = null!;
    private MultiMeshInstance3D _harvest = null!;

    public void Configure(SnapshotPublisher publisher, Heightfield heightfield)
    {
        _publisher = publisher;
        _heightfield = heightfield;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
        _chop = MakeBucket("ChopMarkers", new Color(0.95f, 0.25f, 0.20f));
        _mine = MakeBucket("MineMarkers", new Color(0.55f, 0.55f, 0.6f));
        _harvest = MakeBucket("HarvestMarkers", new Color(0.95f, 0.85f, 0.30f));
        AddChild(_chop);
        AddChild(_mine);
        AddChild(_harvest);
    }

    private MultiMeshInstance3D MakeBucket(string name, Color color)
    {
        var sizeUnits = MarkerSizeMeters * _unitsPerMeter;
        var box = new BoxMesh
        {
            Size = new Vector3(sizeUnits, sizeUnits, sizeUnits),
            Material = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        return new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = box,
                InstanceCount = 0,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    public override void _Process(double delta)
    {
        var snap = _publisher.Current;
        var ds = snap.Designations;

        var chopN = 0; var mineN = 0; var harvestN = 0;
        for (var i = 0; i < ds.Count; i++)
        {
            switch (ds[i].Kind)
            {
                case DesignationKind.ChopTree: chopN++; break;
                case DesignationKind.Mine: mineN++; break;
                case DesignationKind.Harvest: harvestN++; break;
            }
        }
        EnsureCount(_chop, chopN);
        EnsureCount(_mine, mineN);
        EnsureCount(_harvest, harvestN);

        var hoverUnits = HoverMeters * _unitsPerMeter;
        var ci = 0; var mi = 0; var hi = 0;
        for (var i = 0; i < ds.Count; i++)
        {
            var d = ds[i];
            var metersX = (d.TileX + 0.5f) * SimConstants.MetersPerTile;
            var metersY = (d.TileY + 0.5f) * SimConstants.MetersPerTile;
            var x = metersX * _unitsPerMeter;
            var z = metersY * _unitsPerMeter;
            var y = SampleGround(metersX, metersY) + hoverUnits;
            var xf = new Transform3D(Basis.Identity, new Vector3(x, y, z));
            switch (d.Kind)
            {
                case DesignationKind.ChopTree: _chop.Multimesh.SetInstanceTransform(ci++, xf); break;
                case DesignationKind.Mine: _mine.Multimesh.SetInstanceTransform(mi++, xf); break;
                case DesignationKind.Harvest: _harvest.Multimesh.SetInstanceTransform(hi++, xf); break;
            }
        }
    }

    private static void EnsureCount(MultiMeshInstance3D mmi, int count)
    {
        if (mmi.Multimesh.InstanceCount != count) mmi.Multimesh.InstanceCount = count;
    }

    private float SampleGround(float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        return _heightfield.SurfaceMetresAt(tilesX, tilesY) * _unitsPerMeter;
    }
}
