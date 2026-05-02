using CowColonySim.Sim.Lighting;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;

namespace CowColonySim.Sim.Systems;

// Recomputes per-tile lighting once per tick.
//
// Sun: SunCurve.FractionAtTick gives a 0..1 base value that everywhere
// without a roof picks up. Roof occlusion will hook in here once a
// roof system lands — for now every surface tile is treated as exposed.
//
// Player-built lights: any entity with LightEmitter + TilePosition adds
// a per-tile contribution that linearly falls off to zero at RangeTiles
// from its tile. Each individual light's contribution is capped at 0.5
// (player lights never hit 100% on their own); the final per-tile value
// is max(sun, brightest emitter contribution at that tile) clamped to 1.
public sealed class LightingSystem : ITickSystem
{
    private const float MaxEmitterContribution = 0.5f;

    private readonly SimWorld _world;
    private readonly HeightGrid _heightGrid;
    public LightGrid Grid { get; }
    public float SunFraction { get; private set; }

    public LightingSystem(SimWorld world, HeightGrid heightGrid, int width, int height)
    {
        _world = world;
        _heightGrid = heightGrid;
        Grid = new LightGrid(width, height);
    }

    public void Tick(TickContext ctx)
    {
        SunFraction = SunCurve.FractionAtTick(ctx.TickNumber);
        var sunByte = (byte)Math.Clamp((int)MathF.Round(SunFraction * 255f), 0, 255);
        Grid.Fill(sunByte);
        // Roofed tiles get no sun. Player lights below still light their tile
        // through the per-emitter pass (light passes through ceilings cheaply
        // — fine until indoor lighting sims demand more nuance).
        for (var y = 0; y < Grid.Height; y++)
        {
            for (var x = 0; x < Grid.Width; x++)
            {
                if (_heightGrid.IsRoofed(x, y)) Grid.Values[y * Grid.Width + x] = 0;
            }
        }

        foreach (var entity in _world.Store.Query<LightEmitter, TilePosition>().Entities)
        {
            ref var emitter = ref entity.GetComponent<LightEmitter>();
            ref var pos = ref entity.GetComponent<TilePosition>();
            ApplyEmitter(pos.TileX, pos.TileY, emitter.RangeTiles, emitter.Intensity);
        }
    }

    private void ApplyEmitter(int cx, int cy, float range, float intensity)
    {
        if (range <= 0f || intensity <= 0f) return;
        var capped = MathF.Min(intensity, MaxEmitterContribution);
        var radius = (int)MathF.Ceiling(range);
        var minX = Math.Max(0, cx - radius);
        var minY = Math.Max(0, cy - radius);
        var maxX = Math.Min(Grid.Width - 1, cx + radius);
        var maxY = Math.Min(Grid.Height - 1, cy + radius);
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                var d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > range) continue;
                var falloff = 1f - d / range;
                var v = capped * falloff;
                var b = (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
                Grid.ApplyMax(x, y, b);
            }
        }
    }
}
