using CowColonySim.Sim.Items;
using CowColonySim.Sim.World.Components;
using Xunit;

namespace CowColonySim.Tests;

public class InventoryOpsTests
{
    private static (Inventory inv, CarryCaps caps) Fresh(int strength = 10, float baseBulk = 10f)
    {
        return (Inventory.New(), new CarryCaps { Strength = strength, BaseBulk = baseBulk });
    }

    [Fact]
    public void Empty_inventory_has_zero_weight_and_bulk()
    {
        var (inv, _) = Fresh();
        Assert.Equal(0f, InventoryOps.TotalWeight(inv));
        Assert.Equal(0f, InventoryOps.TotalBulk(inv));
    }

    [Fact]
    public void Default_caps_match_strength_times_five()
    {
        var (inv, caps) = Fresh(strength: 8);
        Assert.Equal(40f, InventoryOps.MaxWeight(caps, inv));
    }

    [Fact]
    public void Add_stackable_merges_into_existing_stack()
    {
        var (inv, caps) = Fresh();
        InventoryOps.Add(ref inv, caps, "wood", 1);
        InventoryOps.Add(ref inv, caps, "wood", 1);
        Assert.Single(inv.Stacks);
        Assert.Equal(2, inv.Stacks[0].Count);
    }

    [Fact]
    public void Add_caps_at_room_for_weight()
    {
        // 10 strength -> 50 kg cap. wood = 1 kg. exactly 50 fit.
        var (inv, caps) = Fresh(strength: 10, baseBulk: 1000f);
        var added = InventoryOps.Add(ref inv, caps, "wood", 999);
        Assert.Equal(50, added);
    }

    [Fact]
    public void Add_caps_at_room_for_bulk()
    {
        // wood = 0.4 L. base bulk 10 L -> 25 fit before bulk caps.
        var (inv, caps) = Fresh(strength: 1000, baseBulk: 10f);
        var added = InventoryOps.Add(ref inv, caps, "wood", 999);
        Assert.Equal(25, added);
    }

    [Fact]
    public void Equipping_backpack_raises_max_bulk()
    {
        var (inv, caps) = Fresh(baseBulk: 10f);
        InventoryOps.Add(ref inv, caps, "apparel.backpack", 1);
        var before = InventoryOps.MaxBulk(caps, inv);
        var idx = inv.Stacks.FindIndex(s => s.DefId == "apparel.backpack");
        Assert.True(InventoryOps.Equip(ref inv, idx));
        var after = InventoryOps.MaxBulk(caps, inv);
        Assert.Equal(before + 30f, after);
    }

    [Fact]
    public void Equipping_second_weapon_unequips_the_first()
    {
        var (inv, caps) = Fresh();
        InventoryOps.Add(ref inv, caps, "weapon.club", 1);
        InventoryOps.Add(ref inv, caps, "weapon.club", 1);
        Assert.True(InventoryOps.Equip(ref inv, 0));
        Assert.True(InventoryOps.Equip(ref inv, 1));
        Assert.False(inv.Stacks[0].Equipped);
        Assert.True(inv.Stacks[1].Equipped);
    }

    [Fact]
    public void Equipping_clothes_with_disjoint_layers_both_stay_on()
    {
        // shirt = TorsoMid, backpack = OnBack. No overlap.
        var (inv, caps) = Fresh();
        InventoryOps.Add(ref inv, caps, "apparel.shirt", 1);
        InventoryOps.Add(ref inv, caps, "apparel.backpack", 1);
        Assert.True(InventoryOps.Equip(ref inv, 0));
        Assert.True(InventoryOps.Equip(ref inv, 1));
        Assert.True(inv.Stacks[0].Equipped);
        Assert.True(inv.Stacks[1].Equipped);
    }

    [Fact]
    public void Remove_skips_equipped_stacks()
    {
        var (inv, caps) = Fresh();
        InventoryOps.Add(ref inv, caps, "weapon.club", 1);
        InventoryOps.Equip(ref inv, 0);
        var removed = InventoryOps.Remove(ref inv, "weapon.club", 1);
        Assert.Equal(0, removed);
        Assert.Single(inv.Stacks);
    }

    [Fact]
    public void RemoveAt_force_drops_even_equipped_gear()
    {
        var (inv, caps) = Fresh();
        InventoryOps.Add(ref inv, caps, "weapon.club", 1);
        InventoryOps.Equip(ref inv, 0);
        var (defId, count, _) = InventoryOps.RemoveAt(ref inv, 0);
        Assert.Equal("weapon.club", defId);
        Assert.Equal(1, count);
        Assert.Empty(inv.Stacks);
    }

    [Fact]
    public void Cannot_equip_a_plain_stackable()
    {
        var (inv, caps) = Fresh();
        InventoryOps.Add(ref inv, caps, "wood", 1);
        Assert.False(InventoryOps.Equip(ref inv, 0));
    }

    [Fact]
    public void Add_skips_locked_stack_and_creates_new_unlocked_one()
    {
        // Existing locked (force-picked) wood must NOT absorb auto-haul
        // wood — otherwise haul-drained items vanish into the locked
        // pile.
        var (inv, caps) = Fresh(strength: 1000, baseBulk: 1000f);
        InventoryOps.AddLocked(ref inv, caps, "wood", 5);
        InventoryOps.Add(ref inv, caps, "wood", 3);
        Assert.Equal(2, inv.Stacks.Count);
        Assert.True(inv.Stacks[0].Locked);
        Assert.Equal(5, inv.Stacks[0].Count);
        Assert.False(inv.Stacks[1].Locked);
        Assert.Equal(3, inv.Stacks[1].Count);
    }

    [Fact]
    public void AddLocked_skips_unlocked_stack_and_creates_new_locked_one()
    {
        // Reverse: haul stack already there, force-pickup must NOT merge
        // into it. Otherwise the force-locked pile silently includes
        // un-locked auto-haul items, and locking the merged stack
        // strands those items.
        var (inv, caps) = Fresh(strength: 1000, baseBulk: 1000f);
        InventoryOps.Add(ref inv, caps, "wood", 5);
        InventoryOps.AddLocked(ref inv, caps, "wood", 3);
        Assert.Equal(2, inv.Stacks.Count);
        Assert.False(inv.Stacks[0].Locked);
        Assert.Equal(5, inv.Stacks[0].Count);
        Assert.True(inv.Stacks[1].Locked);
        Assert.Equal(3, inv.Stacks[1].Count);
    }

    [Fact]
    public void AddLocked_merges_into_existing_locked_stack()
    {
        var (inv, caps) = Fresh(strength: 1000, baseBulk: 1000f);
        InventoryOps.AddLocked(ref inv, caps, "wood", 5);
        InventoryOps.AddLocked(ref inv, caps, "wood", 4);
        Assert.Single(inv.Stacks);
        Assert.True(inv.Stacks[0].Locked);
        Assert.Equal(9, inv.Stacks[0].Count);
    }
}
