namespace CowColonySim.Sim.Items;

// Body region a wearable occupies. Layered so a colonist can stack
// e.g. shirt (TorsoMid) under a jacket (TorsoOuter) under a backpack
// (OnBack). Flags so a single piece can cover more than one region —
// a tactical vest covers TorsoOuter + OnBack.
[Flags]
public enum ClothingLayer
{
    None      = 0,
    HeadOuter = 1 << 0,
    HeadInner = 1 << 1,
    TorsoOuter= 1 << 2,
    TorsoMid  = 1 << 3,
    TorsoInner= 1 << 4,
    Legs      = 1 << 5,
    Feet      = 1 << 6,
    Hands     = 1 << 7,
    OnBack    = 1 << 8,
    OnHip     = 1 << 9,
}
