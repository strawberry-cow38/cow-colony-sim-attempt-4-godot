using CowColonySim.Sim.Designations;
using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// Per-colonist priority for each WorkType. 0 = "won't do" (gates the
// work entirely), 1 = highest priority, 8 = lowest. Job-assigning
// systems iterate priority 1 first so a priority-1 colonist gets first
// pick over a priority-2 colonist on the same work pool.
public struct WorkPriorities : IComponent
{
    // Fixed-size buffer indexed by (int)WorkType. Inline so the
    // component stays a flat struct (Friflo prefers value-only).
    public byte P0;
    public byte P1;
    public byte P2;
    public byte P3;
    public byte P4;
    public byte P5;

    public const byte DefaultPriority = 4;
    public const byte MaxPriority = 8;

    public static WorkPriorities Default()
    {
        return new WorkPriorities
        {
            P0 = DefaultPriority,
            P1 = DefaultPriority,
            P2 = DefaultPriority,
            P3 = DefaultPriority,
            P4 = DefaultPriority,
            P5 = DefaultPriority,
        };
    }

    public byte Get(WorkType t)
    {
        return (int)t switch
        {
            0 => P0,
            1 => P1,
            2 => P2,
            3 => P3,
            4 => P4,
            5 => P5,
            _ => 0,
        };
    }

    public void Set(WorkType t, byte value)
    {
        if (value > MaxPriority) value = MaxPriority;
        switch ((int)t)
        {
            case 0: P0 = value; break;
            case 1: P1 = value; break;
            case 2: P2 = value; break;
            case 3: P3 = value; break;
            case 4: P4 = value; break;
            case 5: P5 = value; break;
        }
    }
}
