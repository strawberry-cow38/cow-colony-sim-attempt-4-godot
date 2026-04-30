namespace CowColonySim.Sim.Designations;

// Coarse user-facing work categories shown in the priority panel. One row
// per WorkType per colonist. WorkKind is the fine-grained engine-side
// classification (HaulItem vs HaulToBlueprint, CutPlant vs HarvestPlant,
// etc.); the priority panel collapses those onto a smaller surface so
// the player isn't toggling 12 separate checkboxes.
public enum WorkType
{
    Construction = 0,
    Hauling = 1,
    Mining = 2,
    WoodCutting = 3,
    Plants = 4,
    StructureWork = 5,
    Cooking = 6,
}

public static class WorkTypes
{
    public const int Count = 7;

    public static readonly WorkType[] All =
    {
        WorkType.Construction,
        WorkType.Hauling,
        WorkType.Mining,
        WorkType.WoodCutting,
        WorkType.Plants,
        WorkType.StructureWork,
        WorkType.Cooking,
    };

    public static readonly string[] DisplayNames =
    {
        "Build",
        "Haul",
        "Mine",
        "Chop",
        "Plants",
        "Tear",
        "Cook",
    };

    public static string DisplayName(WorkType t) => DisplayNames[(int)t];

    // Map every assignable WorkKind onto a user-facing WorkType. None and
    // ForcePickup are intentionally absent — None means "no work" and
    // ForcePickup is player-issued (not auto-assigned), so neither has a
    // priority slot.
    public static bool TryGet(WorkKind k, out WorkType type)
    {
        switch (k)
        {
            case WorkKind.Construct:
            case WorkKind.HaulToBlueprint:
                type = WorkType.Construction;
                return true;
            case WorkKind.HaulItem:
                type = WorkType.Hauling;
                return true;
            case WorkKind.Mine:
                type = WorkType.Mining;
                return true;
            case WorkKind.ChopTree:
                type = WorkType.WoodCutting;
                return true;
            case WorkKind.CutPlant:
            case WorkKind.HarvestPlant:
            case WorkKind.Sow:
                type = WorkType.Plants;
                return true;
            case WorkKind.Uninstall:
            case WorkKind.Deconstruct:
                type = WorkType.StructureWork;
                return true;
            case WorkKind.Cook:
                type = WorkType.Cooking;
                return true;
            default:
                type = default;
                return false;
        }
    }
}
