namespace CowColonySim.Sim.Systems;

public interface ITickSystem
{
    void Tick(TickContext ctx);
}
