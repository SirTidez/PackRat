using HarmonyLib;

#if MONO
using ScheduleOne.ItemFramework;
using ScheduleOne.PlayerScripts;
using ScheduleOne.UI.Shop;
#else
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI.Shop;
#endif

namespace PackRat.Patches;

/// <summary>
/// Harmony patches for <see cref="Cart"/>.
/// Adjusts the insufficient-space warning to account for backpack capacity.
/// </summary>
[HarmonyPatch(typeof(Cart))]
public static class CartPatch
{
    [HarmonyPatch("GetWarning")]
    [HarmonyPostfix]
    public static void GetWarning(Cart __instance, ref bool __result, ref string warning)
    {
        if (!PlayerBackpack.Instance.IsUnlocked)
            return;

        if (warning.StartsWith("Vehicle") || !__result)
            return;

        var items = PlayerBackpack.Instance.ItemSlots;
#if !MONO
        items.InsertRange(0, PlayerInventory.Instance.hotbarSlots.Cast<Il2CppSystem.Collections.Generic.IEnumerable<ItemSlot>>());
#else
        items.InsertRange(0, PlayerInventory.Instance.hotbarSlots);
#endif

        if (!__instance.Shop.WillCartFit(items))
            return;

        warning = "Inventory won't fit everything. Some items will be placed in your backpack.";
    }
}
