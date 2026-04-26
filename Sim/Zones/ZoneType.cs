namespace CowColonySim.Sim.Zones;

// What kind of zone this is. Drives which settings struct lives on
// the entity and which floor-renderer color/icon shows up in Game.
public enum ZoneType
{
    Stockpile = 0,
    Farm = 1,
}
