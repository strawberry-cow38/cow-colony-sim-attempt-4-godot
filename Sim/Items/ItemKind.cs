namespace CowColonySim.Sim.Items;

// Discriminator for stackable ground items. Wood is the only kind today;
// stone/coal/produce join as the gathering loop expands.
public enum ItemKind
{
    None = 0,
    Wood = 1,
    Wheat = 2,
}
