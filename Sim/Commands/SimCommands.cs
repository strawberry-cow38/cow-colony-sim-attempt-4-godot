using System.Collections.Concurrent;
using CowColonySim.Sim.Pathfinding;

namespace CowColonySim.Sim.Commands;

// Discriminated union of player-issued commands. Game/* enqueues; the
// CommandSystem drains the bus at the top of each tick. The bus is the
// only sanctioned Game → Sim channel — Game still must not touch ECS
// directly.
public interface ISimCommand { }

public readonly record struct MoveCommand(int EntityId, TileCoord Target) : ISimCommand;

public sealed class CommandBus
{
    private readonly ConcurrentQueue<ISimCommand> _queue = new();

    public void Submit(ISimCommand command) => _queue.Enqueue(command);

    public bool TryDequeue(out ISimCommand command) =>
        _queue.TryDequeue(out command!);
}
