using CowColonySim.Game.Terrain;
using CowColonySim.Game.UI;
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
    private BuildToolService? _tools;
    private float _unitsPerMeter;

    public int? SelectedEntityId { get; private set; }
    public int? SelectedZoneId { get; private set; }
    public int? SelectedTreeId { get; private set; }
    public Vector2? SelectedGroundXZUnits { get; private set; }

    public event System.Action? SelectionChanged;

    public void Configure(SnapshotPublisher publisher, CommandBus commands, Heightfield heightfield)
    {
        _publisher = publisher;
        _commands = commands;
        _heightfield = heightfield;
    }

    public void SetBuildTools(BuildToolService tools) => _tools = tools;

    public override void _Ready()
    {
        _unitsPerMeter = SimConstants.GodotUnitsPerTile / SimConstants.MetersPerTile;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is not InputEventMouseButton mb || !mb.Pressed) return;
        if (_tools is not null && !string.IsNullOrEmpty(_tools.ActiveToolId)) return;

        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;

        var groundHit = TerrainRayCast.Project(camera, mb.Position, _heightfield);
        if (groundHit is null) return;
        var hit = groundHit.Value;

        if (mb.ButtonIndex == MouseButton.Left)
        {
            if (SelectColonistNearRay(camera, mb.Position)) return;
            if (SelectTreeNearRay(camera, mb.Position)) return;
            var tx = (int)MathF.Floor(hit.X / SimConstants.GodotUnitsPerTile);
            var ty = (int)MathF.Floor(hit.Z / SimConstants.GodotUnitsPerTile);
            SelectZoneAtTile(tx, ty);
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

    private void SelectZoneAtTile(int tx, int ty)
    {
        var snap = _publisher.Current;
        for (var i = 0; i < snap.Zones.Count; i++)
        {
            var z = snap.Zones[i];
            if (tx < z.MinTileX || tx > z.MaxTileX) continue;
            if (ty < z.MinTileY || ty > z.MaxTileY) continue;
            ClearOthersExceptZone();
            if (SelectedZoneId == z.ZoneId) return;
            SelectedZoneId = z.ZoneId;
            SelectionChanged?.Invoke();
            return;
        }
        ClearAll();
    }

    private void ClearOthersExceptZone()
    {
        var changed = false;
        if (SelectedEntityId is not null) { SelectedEntityId = null; changed = true; }
        if (SelectedTreeId is not null) { SelectedTreeId = null; changed = true; }
        if (changed) SelectionChanged?.Invoke();
    }

    private void ClearAll()
    {
        var changed = SelectedEntityId is not null || SelectedZoneId is not null || SelectedTreeId is not null;
        SelectedEntityId = null;
        SelectedZoneId = null;
        SelectedTreeId = null;
        if (changed) SelectionChanged?.Invoke();
    }

    private bool SelectTreeNearRay(Camera3D camera, Vector2 mousePos)
    {
        var origin = camera.ProjectRayOrigin(mousePos);
        var dir = camera.ProjectRayNormal(mousePos).Normalized();

        var snap = _publisher.Current;
        var best = -1;
        var bestDist = float.PositiveInfinity;

        // Tree picking: probe ray against an axis-aligned cylinder ~0.7m
        // radius * 5m tall around the trunk. Cheaper than triangulating the
        // multimesh and good enough for click-to-select.
        var radiusUnits = 0.7f * _unitsPerMeter;
        var heightUnits = 5.0f * _unitsPerMeter;
        for (var i = 0; i < snap.Trees.Count; i++)
        {
            var t = snap.Trees[i];
            var metersX = (t.TileX + 0.5f) * SimConstants.MetersPerTile;
            var metersY = (t.TileY + 0.5f) * SimConstants.MetersPerTile;
            var x = metersX * _unitsPerMeter;
            var z = metersY * _unitsPerMeter;
            var groundY = SampleGroundUnits(metersX, metersY);
            var center = new Vector3(x, groundY + heightUnits * 0.5f, z);
            var toC = center - origin;
            var tParam = toC.Dot(dir);
            if (tParam < 0f) continue;
            var closest = origin + dir * tParam;
            var planar = MathF.Sqrt(
                (closest.X - center.X) * (closest.X - center.X) +
                (closest.Z - center.Z) * (closest.Z - center.Z));
            var verticalOk = MathF.Abs(closest.Y - center.Y) <= heightUnits * 0.5f;
            if (planar > radiusUnits || !verticalOk) continue;
            if (tParam < bestDist)
            {
                bestDist = tParam;
                best = t.EntityId;
            }
        }

        if (best == -1) return false;
        if (SelectedTreeId == best) return true;
        SelectedTreeId = best;
        if (SelectedEntityId is not null) SelectedEntityId = null;
        if (SelectedZoneId is not null) SelectedZoneId = null;
        SelectionChanged?.Invoke();
        return true;
    }

    private bool SelectColonistNearRay(Camera3D camera, Vector2 mousePos)
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

        if (best == -1) return false;
        SelectedEntityId = best;
        if (SelectedZoneId is not null) SelectedZoneId = null;
        if (SelectedTreeId is not null) SelectedTreeId = null;
        SelectionChanged?.Invoke();
        return true;
    }

    private float SampleGroundUnits(float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        return _heightfield.SurfaceMetresAt(tilesX, tilesY) * _unitsPerMeter;
    }
}
