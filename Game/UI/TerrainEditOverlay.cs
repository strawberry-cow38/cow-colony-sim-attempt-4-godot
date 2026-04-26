using CowColonySim.Sim;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.UI;

// Cursor-locked overlay that lights up while a debug_terrain tool is
// active. Red dots = heightfield vertices in a small radius around the
// mouse; green dot = the snapped/selected vertex (closest to the cursor
// ground projection). Hidden whenever no tool is active so it doesn't
// clutter normal play.
public partial class TerrainEditOverlay : Node3D
{
    private const int RadiusTiles = 4;
    private const float RedDotRadius = 1.6f;
    private const float GreenDotRadius = 2.2f;

    private BuildToolService _tools = null!;
    private Heightfield _field = null!;
    private float _unitsPerTile;
    private float _unitsPerQuanta;

    private MultiMeshInstance3D _redDots = null!;
    private MeshInstance3D _greenDot = null!;
    private bool _active;

    public Vector2I? SnappedVertex { get; private set; }

    public void Configure(BuildToolService tools, Heightfield field)
    {
        _tools = tools;
        _field = field;
    }

    public override void _Ready()
    {
        _unitsPerTile = SimConstants.GodotUnitsPerTile;
        _unitsPerQuanta = TerrainConstants.VerticalQuantumMetres
                        * (SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile);

        _redDots = MakeRedDotPool();
        AddChild(_redDots);

        _greenDot = MakeGreenDot();
        AddChild(_greenDot);

        Visible = false;
        _tools.ToolChanged += OnToolChanged;
        UpdateActive(_tools.ActiveToolId);
    }

    private MultiMeshInstance3D MakeRedDotPool()
    {
        var sphere = new SphereMesh
        {
            Radius = RedDotRadius,
            Height = RedDotRadius * 2f,
            RadialSegments = 8,
            Rings = 4,
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.15f, 0.15f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        var side = RadiusTiles * 2 + 1;
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = sphere,
            InstanceCount = side * side,
            VisibleInstanceCount = 0,
        };
        return new MultiMeshInstance3D
        {
            Multimesh = mm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static MeshInstance3D MakeGreenDot()
    {
        var sphere = new SphereMesh
        {
            Radius = GreenDotRadius,
            Height = GreenDotRadius * 2f,
            RadialSegments = 12,
            Rings = 6,
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.25f, 1f, 0.35f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        return new MeshInstance3D
        {
            Mesh = sphere,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
    }

    private void OnToolChanged(string toolId) => UpdateActive(toolId);

    private void UpdateActive(string toolId)
    {
        _active = !string.IsNullOrEmpty(toolId) && toolId.StartsWith("debug_terrain.");
        Visible = _active;
        if (!_active) SnappedVertex = null;
    }

    public override void _Process(double delta)
    {
        if (!_active) return;

        var camera = GetViewport().GetCamera3D();
        if (camera is null) { HideAll(); return; }

        var mousePos = GetViewport().GetMousePosition();
        var ground = ProjectMouseToGround(camera, mousePos);
        if (ground is null) { HideAll(); return; }

        var vx = (int)MathF.Round(ground.Value.X / _unitsPerTile);
        var vy = (int)MathF.Round(ground.Value.Z / _unitsPerTile);
        if (!_field.InBounds(vx, vy)) { HideAll(); return; }

        SnappedVertex = new Vector2I(vx, vy);
        PlaceGreenDot(vx, vy);
        PlaceRedDots(vx, vy);
    }

    private void PlaceGreenDot(int vx, int vy)
    {
        var p = VertexPos(vx, vy);
        _greenDot.Position = p;
        _greenDot.Visible = true;
    }

    private void PlaceRedDots(int centerX, int centerY)
    {
        var mm = _redDots.Multimesh;
        var idx = 0;
        for (var dy = -RadiusTiles; dy <= RadiusTiles; dy++)
        {
            for (var dx = -RadiusTiles; dx <= RadiusTiles; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var vx = centerX + dx;
                var vy = centerY + dy;
                if (!_field.InBounds(vx, vy)) continue;
                var p = VertexPos(vx, vy);
                mm.SetInstanceTransform(idx++, new Transform3D(Basis.Identity, p));
            }
        }
        mm.VisibleInstanceCount = idx;
    }

    private Vector3 VertexPos(int vx, int vy)
    {
        var h = _field.Get(vx, vy) * _unitsPerQuanta;
        return new Vector3(vx * _unitsPerTile, h, vy * _unitsPerTile);
    }

    private void HideAll()
    {
        SnappedVertex = null;
        _greenDot.Visible = false;
        _redDots.Multimesh.VisibleInstanceCount = 0;
    }

    private static Vector3? ProjectMouseToGround(Camera3D camera, Vector2 mousePos)
    {
        var origin = camera.ProjectRayOrigin(mousePos);
        var dir = camera.ProjectRayNormal(mousePos);
        if (MathF.Abs(dir.Y) < 1e-5f) return null;
        var t = -origin.Y / dir.Y;
        if (t <= 0f) return null;
        return origin + dir * t;
    }
}
