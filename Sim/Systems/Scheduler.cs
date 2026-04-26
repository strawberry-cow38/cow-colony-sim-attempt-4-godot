namespace CowColonySim.Sim.Systems;

public sealed class Scheduler
{
    private readonly List<ITickSystem> _systems = new();

    public IReadOnlyList<ITickSystem> Systems => _systems;

    public void Register(ITickSystem system)
    {
        _systems.Add(system);
    }

    public void Tick(TickContext ctx)
    {
        for (var i = 0; i < _systems.Count; i++)
        {
            _systems[i].Tick(ctx);
        }
    }
}
