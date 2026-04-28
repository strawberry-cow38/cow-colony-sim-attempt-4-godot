using CowColonySim.Sim.World.Components;

namespace CowColonySim.Sim.Items;

// Stateless helpers that read and mutate Inventory + CarryCaps. Logic
// lives here (not on Inventory the struct) so it can take the caps by
// ref without forcing components to know about each other.
public static class InventoryOps
{
    public const float WeightPerStrength = 5f;

    // Total weight (kg) of all stacks. Equipped doesn't reduce — gear
    // you're wearing still has weight, but EquippedBulkBonus offsets
    // bulk pressure separately.
    public static float TotalWeight(in Inventory inv)
    {
        if (inv.Stacks is null) return 0f;
        var sum = 0f;
        for (var i = 0; i < inv.Stacks.Count; i++)
        {
            var s = inv.Stacks[i];
            sum += ItemCatalog.Get(s.DefId).Weight * s.Count;
        }
        return sum;
    }

    // Bulk consumed. Equipped load-bearing gear costs its own bulk too —
    // the bonus shows up in MaxBulk, not here, so the math stays linear.
    public static float TotalBulk(in Inventory inv)
    {
        if (inv.Stacks is null) return 0f;
        var sum = 0f;
        for (var i = 0; i < inv.Stacks.Count; i++)
        {
            var s = inv.Stacks[i];
            sum += ItemCatalog.Get(s.DefId).Bulk * s.Count;
        }
        return sum;
    }

    public static float MaxWeight(in CarryCaps caps, in Inventory inv)
    {
        var sum = caps.Strength * WeightPerStrength + caps.BonusWeight;
        if (inv.Stacks is not null)
        {
            for (var i = 0; i < inv.Stacks.Count; i++)
            {
                var s = inv.Stacks[i];
                if (!s.Equipped) continue;
                sum += ItemCatalog.Get(s.DefId).EquippedWeightBonus;
            }
        }
        return sum;
    }

    public static float MaxBulk(in CarryCaps caps, in Inventory inv)
    {
        var sum = caps.BaseBulk + caps.BonusBulk;
        if (inv.Stacks is not null)
        {
            for (var i = 0; i < inv.Stacks.Count; i++)
            {
                var s = inv.Stacks[i];
                if (!s.Equipped) continue;
                sum += ItemCatalog.Get(s.DefId).EquippedBulkBonus;
            }
        }
        return sum;
    }

    // How many units of `defId` will fit before hitting either cap.
    // Returns 0 when the first unit already overflows.
    public static int RoomFor(string defId, in CarryCaps caps, in Inventory inv)
    {
        var def = ItemCatalog.Get(defId);
        var w = TotalWeight(inv);
        var b = TotalBulk(inv);
        var maxW = MaxWeight(caps, inv);
        var maxB = MaxBulk(caps, inv);
        var byWeight = def.Weight > 0 ? (int)Math.Floor((maxW - w) / def.Weight) : int.MaxValue;
        var byBulk = def.Bulk > 0 ? (int)Math.Floor((maxB - b) / def.Bulk) : int.MaxValue;
        return Math.Max(0, Math.Min(byWeight, byBulk));
    }

    // Adds up to `count` units. Returns how many actually fit. Stackable
    // defs merge into the first non-equipped, non-locked stack of the
    // same DefId; uniques append a new stack each time. Locked stacks
    // are sealed by the player (force-pickup) — never merge into them
    // here, otherwise haul flow silently feeds locked piles and the
    // dropped count vanishes.
    public static int Add(ref Inventory inv, in CarryCaps caps, string defId, int count)
    {
        if (count <= 0) return 0;
        inv.Stacks ??= new List<InventoryStack>();
        var room = RoomFor(defId, caps, inv);
        var take = Math.Min(room, count);
        if (take <= 0) return 0;

        var def = ItemCatalog.Get(defId);
        if (def.Stackable)
        {
            for (var i = 0; i < inv.Stacks.Count; i++)
            {
                var s = inv.Stacks[i];
                if (s.DefId != defId || s.Equipped || s.Locked) continue;
                s.Count += take;
                inv.Stacks[i] = s;
                return take;
            }
            inv.Stacks.Add(new InventoryStack(defId, take));
            return take;
        }
        // Non-stackable — one stack of count 1 per call.
        inv.Stacks.Add(new InventoryStack(defId, 1));
        return 1;
    }

    // Minified pickup. Inventory entry stores DefId="minified" (generic
    // catalog entry for weight/bulk math) plus the wrapped blueprint id
    // so drop/drain can recreate the right structure. Always non-stackable
    // count=1; multiple minified pickups stack as separate entries.
    public static int AddMinified(ref Inventory inv, in CarryCaps caps, string wrappedDefId)
    {
        if (string.IsNullOrEmpty(wrappedDefId)) return 0;
        inv.Stacks ??= new List<InventoryStack>();
        var genericId = ItemCatalog.DefaultIdFor(ItemKind.Minified);
        var room = RoomFor(genericId, caps, inv);
        if (room <= 0) return 0;
        inv.Stacks.Add(new InventoryStack(genericId, 1, wrappedDefId: wrappedDefId));
        return 1;
    }

    // Force-pickup variant: merges into an existing locked stack of the
    // same DefId, or appends a new locked stack. Keeps the locked pile
    // separate from any auto-haul stack the colonist might already
    // carry, so haul drains don't dump the player's reserved items.
    public static int AddLocked(ref Inventory inv, in CarryCaps caps, string defId, int count)
    {
        if (count <= 0) return 0;
        inv.Stacks ??= new List<InventoryStack>();
        var room = RoomFor(defId, caps, inv);
        var take = Math.Min(room, count);
        if (take <= 0) return 0;

        var def = ItemCatalog.Get(defId);
        if (def.Stackable)
        {
            for (var i = 0; i < inv.Stacks.Count; i++)
            {
                var s = inv.Stacks[i];
                if (s.DefId != defId || s.Equipped || !s.Locked) continue;
                s.Count += take;
                inv.Stacks[i] = s;
                return take;
            }
            inv.Stacks.Add(new InventoryStack(defId, take, locked: true));
            return take;
        }
        inv.Stacks.Add(new InventoryStack(defId, 1, locked: true));
        return 1;
    }

    // Removes up to `count` units of defId from non-equipped stacks.
    // Returns how many were actually removed. Equipped stacks are
    // skipped — caller must Unequip first to drop worn gear.
    public static int Remove(ref Inventory inv, string defId, int count)
    {
        if (count <= 0 || inv.Stacks is null) return 0;
        var removed = 0;
        for (var i = inv.Stacks.Count - 1; i >= 0 && removed < count; i--)
        {
            var s = inv.Stacks[i];
            if (s.DefId != defId || s.Equipped) continue;
            var take = Math.Min(s.Count, count - removed);
            s.Count -= take;
            removed += take;
            if (s.Count <= 0) inv.Stacks.RemoveAt(i);
            else inv.Stacks[i] = s;
        }
        return removed;
    }

    // Force-drop a specific stack regardless of equipped state. Returns
    // the (defId, count, wrappedDefId) the caller should spawn on the
    // ground. wrappedDefId is non-empty only for minified entries.
    public static (string defId, int count, string wrappedDefId) RemoveAt(ref Inventory inv, int index)
    {
        if (inv.Stacks is null || index < 0 || index >= inv.Stacks.Count) return (string.Empty, 0, string.Empty);
        var s = inv.Stacks[index];
        inv.Stacks.RemoveAt(index);
        return (s.DefId, s.Count, s.WrappedDefId ?? string.Empty);
    }

    // Equip the stack at index. Only one weapon equipped at a time
    // (others auto-unequip). Clothing layers conflict only when bits
    // overlap — multiple non-overlapping pieces can equip together.
    public static bool Equip(ref Inventory inv, int index)
    {
        if (inv.Stacks is null || index < 0 || index >= inv.Stacks.Count) return false;
        var s = inv.Stacks[index];
        if (s.Equipped) return true;
        var def = ItemCatalog.Get(s.DefId);
        if (!def.IsWeapon && !def.IsClothing) return false;

        for (var i = 0; i < inv.Stacks.Count; i++)
        {
            if (i == index) continue;
            var other = inv.Stacks[i];
            if (!other.Equipped) continue;
            var od = ItemCatalog.Get(other.DefId);
            var conflict = (def.IsWeapon && od.IsWeapon)
                || (def.IsClothing && od.IsClothing && (od.ClothingLayer & def.ClothingLayer) != 0);
            if (!conflict) continue;
            other.Equipped = false;
            inv.Stacks[i] = other;
        }

        s.Equipped = true;
        inv.Stacks[index] = s;
        return true;
    }

    public static bool Unequip(ref Inventory inv, int index)
    {
        if (inv.Stacks is null || index < 0 || index >= inv.Stacks.Count) return false;
        var s = inv.Stacks[index];
        if (!s.Equipped) return false;
        s.Equipped = false;
        inv.Stacks[index] = s;
        return true;
    }
}
