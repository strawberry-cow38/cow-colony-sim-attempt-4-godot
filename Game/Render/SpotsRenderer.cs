using CowColonySim.Sim;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using CowColonySim.Sim.World.Components;
using Godot;

namespace CowColonySim.Game.Render;

// Reads SimSnapshot.Spots each frame and updates 3 MultiMeshInstance3D
// children (one per NeedKind) with cylinder markers color-coded by kind.
// Hunger=green, Thirst=blue, Energy=yellow.
public partial class SpotsRenderer : Node3D
{
    private const float MarkerHeightMeters = 0.6f;
    private const float MarkerRadiusMeters = 0.35f;

    private SnapshotPublisher _publisher = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;
    private float _unitsPerQuanta;

    private MultiMeshInstance3D _hunger = null!;
    private MultiMeshInstance3D _thirst = null!;
    private MultiMeshInstance3D _energy = null!;

    public void Configure(SnapshotPublisher publisher, Heightfield heightfield)
    {
        _publisher = publisher;
        _heightfield = heightfield;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
        _unitsPerQuanta = TerrainConstants.VerticalQuantumMetres * _unitsPerMeter;

        _hunger = MakeBucket("HungerSpots", new Color(0.3f, 0.85f, 0.35f));
        _thirst = MakeBucket("ThirstSpots", new Color(0.3f, 0.55f, 0.95f));
        _energy = MakeBucket("EnergySpots", new Color(0.95f, 0.85f, 0.25f));
        AddChild(_hunger);
        AddChild(_thirst);
        AddChild(_energy);
    }

    private MultiMeshInstance3D MakeBucket(string name, Color color)
    {
        var cyl = new CylinderMesh
        {
            TopRadius = MarkerRadiusMeters * _unitsPerMeter,
            BottomRadius = MarkerRadiusMeters * _unitsPerMeter,
            Height = MarkerHeightMeters * _unitsPerMeter,
            RadialSegments = 16,
            Material = new StandardMaterial3D
            {
                AlbedoColor = color,
                Roughness = 0.6f,
                EmissionEnabled = true,
                Emission = color,
                EmissionEnergyMultiplier = 0.4f,
            },
        };
        return new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = cyl,
                InstanceCount = 0,
            },
        };
    }

    public override void _Process(double delta)
    {
        var snap = _publisher.Current;
        var spots = snap.Spots;

        var hungerCount = 0;
        var thirstCount = 0;
        var energyCount = 0;
        for (var i = 0; i < spots.Count; i++)
        {
            switch (spots[i].Kind)
            {
                case NeedKind.Hunger: hungerCount++; break;
                case NeedKind.Thirst: thirstCount++; break;
                case NeedKind.Energy: energyCount++; break;
            }
        }
        EnsureCount(_hunger, hungerCount);
        EnsureCount(_thirst, thirstCount);
        EnsureCount(_energy, energyCount);

        var halfHeightUnits = MarkerHeightMeters * 0.5f * _unitsPerMeter;
        var hi = 0;
        var ti = 0;
        var ei = 0;
        for (var i = 0; i < spots.Count; i++)
        {
            var s = spots[i];
            var metersX = (s.TileX + 0.5f) * SimConstants.MetersPerTile;
            var metersY = (s.TileY + 0.5f) * SimConstants.MetersPerTile;
            var x = metersX * _unitsPerMeter;
            var z = metersY * _unitsPerMeter;
            var y = SampleGround(metersX, metersY) + halfHeightUnits;
            var xf = new Transform3D(Basis.Identity, new Vector3(x, y, z));
            switch (s.Kind)
            {
                case NeedKind.Hunger: _hunger.Multimesh.SetInstanceTransform(hi++, xf); break;
                case NeedKind.Thirst: _thirst.Multimesh.SetInstanceTransform(ti++, xf); break;
                case NeedKind.Energy: _energy.Multimesh.SetInstanceTransform(ei++, xf); break;
            }
        }
    }

    private static void EnsureCount(MultiMeshInstance3D mmi, int count)
    {
        if (mmi.Multimesh.InstanceCount != count)
        {
            mmi.Multimesh.InstanceCount = count;
        }
    }

    private float SampleGround(float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        var vx = Mathf.Clamp((int)MathF.Round(tilesX), 0, _heightfield.VertWidth - 1);
        var vy = Mathf.Clamp((int)MathF.Round(tilesY), 0, _heightfield.VertHeight - 1);
        return _heightfield.Get(vx, vy) * _unitsPerQuanta;
    }
}
