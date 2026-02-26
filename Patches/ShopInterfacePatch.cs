using HarmonyLib;
using PackRat.Shops;

#if MONO
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.PlayerScripts;
using ScheduleOne.UI.Shop;
#else
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI.Shop;
#endif

namespace PackRat.Patches;

/// <summary>
/// Harmony patches for <see cref="ShopInterface"/>.
/// Injects backpack item slots into the available slots list so items can be sold from the backpack.
/// When any shop awakens, injects backpack tier listings into the hardware store so they appear in the UI.
/// </summary>
[HarmonyPatch(typeof(ShopInterface))]
public static class ShopInterfacePatch
{
    [HarmonyPatch("Awake")]
    [HarmonyPostfix]
    public static void Awake(ShopInterface __instance)
    {
        BackpackShopIntegration.TryAddBackpackListingsToShop(__instance);
    }

    [HarmonyPatch("GetAvailableSlots")]
    [HarmonyPostfix]
#if !MONO
    public static void GetAvailableSlots(ShopInterface __instance, ref Il2CppSystem.Collections.Generic.List<ItemSlot> __result)
#else
    public static void GetAvailableSlots(ShopInterface __instance, ref List<ItemSlot> __result)
#endif
    {
        if (!PlayerBackpack.Instance.IsUnlocked)
            return;

        var loadingBayVehicle = __instance.GetLoadingBayVehicle();
        if (loadingBayVehicle != null && __instance.Cart.LoadVehicleToggle.isOn)
            return;

        var insertIndex = PlayerSingleton<PlayerInventory>.Instance.hotbarSlots.Count;
        var items = PlayerBackpack.Instance.ItemSlots;
        for (var i = 0; i < items.Count; i++)
        {
#if !MONO
            var itemSlot = items[new Index(i)].TryCast<ItemSlot>();
#else
            var itemSlot = items[i];
#endif
            if (itemSlot == null)
                continue;

            __result.Insert(i + insertIndex, itemSlot);
        }
    }
}
