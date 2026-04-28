using Friflo.Engine.ECS;

namespace CowColonySim.Sim.World.Components;

// Inputs to a colonist's carry caps. MaxWeight is mostly Strength-driven
// (5 kg per point, baseline) with bonuses from late-game gear. MaxBulk
// has a small base and grows with equipped load-bearing apparel
// (backpacks, vests) — those bonuses are read off ItemCatalog defs.
//
// Bionics, exoskeletons, and power armor all add into Bonus* fields
// without touching this struct's shape. Phase-1 lives without them.
public struct CarryCaps : IComponent
{
    public int Strength;
    public float BaseBulk;
    public float BonusWeight;
    public float BonusBulk;

    public static CarryCaps Default() => new()
    {
        Strength = 16,
        BaseBulk = 30f,
        BonusWeight = 0f,
        BonusBulk = 0f,
    };
}
