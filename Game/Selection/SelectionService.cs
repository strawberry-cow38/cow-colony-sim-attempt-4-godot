using CowColonySim.Sim;
using CowColonySim.Sim.Commands;
using CowColonySim.Sim.Logging;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.Snapshots;
using CowColonySim.Sim.Terrain;
using Godot;

namespace CowColonySim.Game.Selection;

// Click-to-select + right-click-to-move. Reads the latest snapshot to
// pick the colonist nearest the mouse ray; pushes MoveCommand back to
// Sim through the CommandBus. Selection state is Game-side only.
public partial class SelectionService : Node
{
    private const float ColonistPickRadiusUnits = 30f;
    private const int TilesPerCell = 256;

    private SnapshotPublisher _publisher = null!;
    private CommandBus _commands = null!;
    private Heightfield _heightfield = null!;
    private float _unitsPerMeter;

    public int? SelectedEntityId { get; private set; }
    public Vector2? SelectedGroundXZUnits { get; private set; }

    public event System.Action? SelectionChanged;

    public void Configure(SnapshotPublisher publisher, CommandBus commands, Heightfield heightfield)
    {
        _publisher = publisher;
        _commands = commands;
        _heightfield = heightfield;
    }

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is not InputEventMouseButton mb || !mb.Pressed) return;

        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;

        var groundHit = ProjectMouseToGround(camera, mb.Position);
        if (groundHit is null) return;
        var hit = groundHit.Value;

        if (mb.ButtonIndex == MouseButton.Left)
        {
            SelectColonistNearRay(camera, mb.Position);
        }
        else if (mb.ButtonIndex == MouseButton.Right && SelectedEntityId is int id)
        {
            var tx = (int)MathF.Floor(hit.X / SimConstants.GodotUnitsPerTile);
            var ty = (int)MathF.Floor(hit.Z / SimConstants.GodotUnitsPerTile);
            tx = Mathf.Clamp(tx, 0, TilesPerCell - 1);
            ty = Mathf.Clamp(ty, 0, TilesPerCell - 1);
            _commands.Submit(new MoveCommand(id, new TileCoord(tx, ty)));
            SimLog.Logger.Information("Move command for entity {Id} -> ({TX},{TY}).", id, tx, ty);
        }
    }

    private void SelectColonistNearRay(Camera3D camera, Vector2 mousePos)
    {
        var origin = camera.ProjectRayOrigin(mousePos);
        var dir = camera.ProjectRayNormal(mousePos).Normalized();

        var snap = _publisher.Current;
        var best = -1;
        var bestDist = float.PositiveInfinity;

        for (var i = 0; i < snap.Colonists.Count; i++)
        {
            var c = snap.Colonists[i];
            var x = c.MetersX * _unitsPerMeter;
            var z = c.MetersY * _unitsPerMeter;
            var y = SampleGroundUnits(c.MetersX, c.MetersY) + 24f;
            var p = new Vector3(x, y, z);
            var toP = p - origin;
            var t = toP.Dot(dir);
            if (t < 0f) continue;
            var closest = origin + dir * t;
            var d = closest.DistanceTo(p);
            if (d < bestDist && d < ColonistPickRadiusUnits)
            {
                bestDist = d;
                best = c.EntityId;
            }
        }

        if (best == -1)
        {
            if (SelectedEntityId is null) return;
            SelectedEntityId = null;
            SelectionChanged?.Invoke();
            return;
        }
        SelectedEntityId = best;
        SelectionChanged?.Invoke();
    }

    private Vector3? ProjectMouseToGround(Camera3D camera, Vector2 mousePos)
    {
        var origin = camera.ProjectRayOrigin(mousePos);
        var dir = camera.ProjectRayNormal(mousePos);
        if (MathF.Abs(dir.Y) < 1e-5f) return null;
        var t = -origin.Y / dir.Y;
        if (t <= 0f) return null;
        return origin + dir * t;
    }

    private float SampleGroundUnits(float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        return _heightfield.SurfaceMetresAt(tilesX, tilesY) * _unitsPerMeter;
    }
}
