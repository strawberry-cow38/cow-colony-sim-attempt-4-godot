using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

public enum NeedKind : byte
{
    Hunger = 0,
    Thirst = 1,
    Energy = 2,
}

// Three core needs. 100 = full, 0 = empty. NeedDecaySystem ticks values
// down; JobSystem assigns matching spots when a value drops below the
// hunt threshold; while a colonist sits on a matching spot, the value
// climbs back up to 100.
public struct Needs : IComponent
{
    public float Hunger;
    public float Thirst;
    public float Energy;

    public static Needs Full() => new() { Hunger = 100f, Thirst = 100f, Energy = 100f };

    public float Get(NeedKind kind) => kind switch
    {
        NeedKind.Hunger => Hunger,
        NeedKind.Thirst => Thirst,
        NeedKind.Energy => Energy,
        _ => 0f,
    };

    public void Set(NeedKind kind, float value)
    {
        switch (kind)
        {
            case NeedKind.Hunger: Hunger = value; break;
            case NeedKind.Thirst: Thirst = value; break;
            case NeedKind.Energy: Energy = value; break;
        }
    }
}
