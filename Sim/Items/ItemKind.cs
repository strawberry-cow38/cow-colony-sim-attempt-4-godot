namespace CowColonySim.Sim.Items;

// Discriminator for stackable ground items. Wood is the only kind today;
// stone/coal/produce join as the gathering loop expands.
public enum ItemKind
{
    None = 0,
    Wood = 1,
    Wheat = 2,
    // A whole structure compressed back into a portable item, paired
    // with a MinifiedThing component that carries the original DefId
    // and any per-instance settings. Never stacks (Count = 1).
    Minified = 3,
    // Wearable. Per-piece behavior comes from the ItemDef (layer, bonuses).
    Apparel = 4,
    // Equipped weapon. Single instance per colonist.
    Weapon = 5,
    // Mined boulder yield. Stacks like wood; recipes coming as masonry lands.
    Stone = 6,
    // Cooked output of the stove's wheat→bread recipe. Edible later.
    Bread = 7,
}
