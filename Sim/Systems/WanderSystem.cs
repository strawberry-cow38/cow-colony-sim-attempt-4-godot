using CowColonySim.Sim.World;
using CowColonySim.Sim.World.Components;

namespace CowColonySim.Sim.Systems;

// Drifts each colonist around the ground plane. Direction re-rolls every
// 1-3 seconds; bounces off the configured tile bounds. Pure Sim/, no
// Godot deps.
public sealed class WanderSystem : ITickSystem
{
    private const float SpeedMps = 0.6f;
    private const int MinRerollTicks = 60;
    private const int MaxRerollTicks = 180;

    private readonly SimWorld _world;
    private readonly float _maxMetersX;
    private readonly float _maxMetersY;

    public WanderSystem(SimWorld world, int boundsTilesX, int boundsTilesY)
    {
        _world = world;
        _maxMetersX = boundsTilesX * SimConstants.MetersPerTile;
        _maxMetersY = boundsTilesY * SimConstants.MetersPerTile;
    }

    public void Tick(TickContext ctx)
    {
        var dt = (float)ctx.FixedDeltaSeconds;
        var query = _world.Store.Query<Colonist, TilePosition>();
        foreach (var entity in query.Entities)
        {
            ref var c = ref entity.GetComponent<Colonist>();
            ref var p = ref entity.GetComponent<TilePosition>();

            if (ctx.TickNumber >= c.NextRerollTick)
            {
                var (vx, vy) = SampleUnitDir(ref c.Rng);
                c.VelMpsX = vx * SpeedMps;
                c.VelMpsY = vy * SpeedMps;
                c.NextRerollTick = ctx.TickNumber + RandRange(ref c.Rng, MinRerollTicks, MaxRerollTicks);
            }

            var nx = p.MetersX + c.VelMpsX * dt;
            var ny = p.MetersY + c.VelMpsY * dt;

            if (nx < 0f) { nx = 0f; c.VelMpsX = -c.VelMpsX; }
            else if (nx > _maxMetersX) { nx = _maxMetersX; c.VelMpsX = -c.VelMpsX; }
            if (ny < 0f) { ny = 0f; c.VelMpsY = -c.VelMpsY; }
            else if (ny > _maxMetersY) { ny = _maxMetersY; c.VelMpsY = -c.VelMpsY; }

            WriteMetersXY(ref p, nx, ny);
        }
    }

    private static void WriteMetersXY(ref TilePosition p, float metersX, float metersY)
    {
        var tilesX = metersX / SimConstants.MetersPerTile;
        var tilesY = metersY / SimConstants.MetersPerTile;
        var tx = (int)Math.Floor(tilesX);
        var ty = (int)Math.Floor(tilesY);
        p.TileX = tx;
        p.TileY = ty;
        p.SubX = (float)(tilesX - tx);
        p.SubY = (float)(tilesY - ty);
    }

    private static (float, float) SampleUnitDir(ref uint rng)
    {
        // Two random floats → angle on [0, 2π) → unit vector. Skip rejection
        // sampling; over many rerolls bias is invisible.
        var r = NextFloat01(ref rng);
        var theta = r * MathF.Tau;
        return (MathF.Cos(theta), MathF.Sin(theta));
    }

    private static int RandRange(ref uint rng, int lo, int hiExclusive)
    {
        var span = (uint)(hiExclusive - lo);
        return lo + (int)(NextU32(ref rng) % span);
    }

    private static float NextFloat01(ref uint rng) =>
        (NextU32(ref rng) & 0xFFFFFF) / (float)0x1000000;

    private static uint NextU32(ref uint state)
    {
        var x = state == 0 ? 0xDEADBEEFu : state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        state = x;
        return x;
    }
}
