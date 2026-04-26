using CowColonySim.Sim.World;
using Friflo.Engine.ECS;
using Xunit;

namespace CowColonySim.Tests;

public class SimWorldSmokeTests
{
    [Fact]
    public void Creates_and_counts_entities()
    {
        var world = new SimWorld();
        Assert.Equal(0, world.EntityCount);

        world.CreateEntity();
        world.CreateEntity();

        Assert.Equal(2, world.EntityCount);
    }

    [Fact]
    public void Adds_and_reads_a_component()
    {
        var world = new SimWorld();
        var entity = world.CreateEntity();

        entity.AddComponent(new Heartbeat(7));

        Assert.True(entity.HasComponent<Heartbeat>());
        Assert.Equal(7, entity.GetComponent<Heartbeat>().Value);
    }

    [Fact]
    public void Queries_match_components()
    {
        var world = new SimWorld();
        for (var i = 0; i < 3; i++)
        {
            var e = world.CreateEntity();
            e.AddComponent(new Heartbeat(i));
        }

        var query = world.Store.Query<Heartbeat>();
        var seen = 0;
        foreach (var (heartbeats, _) in query.Chunks)
        {
            seen += heartbeats.Length;
        }
        Assert.Equal(3, seen);
    }

    private struct Heartbeat : IComponent
    {
        public int Value;
        public Heartbeat(int value)
        {
            Value = value;
        }
    }
}
