using HarmonyLib;
using PackRat.Config;
using PackRat.Helpers;
using PackRat.Shops;

#if MONO
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.Levelling;
using ScheduleOne.PlayerScripts;
using ScheduleOne.UI.Shop;
#else
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI.Shop;
#endif

namespace PackRat.Patches;

/// <summary>
/// Intercepts the shop's AddItem action only to detect backpack tier listings.
/// The game owns the purchase flow; PackRat enforces one copy of each tier in the cart and
/// prevents purchase of a tier the player already owns or has surpassed.
/// </summary>
[HarmonyPatch(typeof(ShopInterface))]
public static class BackpackPurchasePatch
{
    [HarmonyPatch("AddItem", typeof(ListingUI))]
    [HarmonyPrefix]
    public static bool AddItem_Prefix(ShopInterface __instance, ListingUI ui)
    {
        try
        {
            if (ui == null)
                return true;

            var listing = ReflectionUtils.TryGetFieldOrProperty(ui, "Listing")
                ?? ReflectionUtils.TryGetFieldOrProperty(ui, "listing");
            if (listing == null)
                return true;

            var itemObj = ReflectionUtils.TryGetFieldOrProperty(listing, "Item");
#if !MONO
            StorableItemDefinition item = null;
            if (itemObj is Il2CppSystem.Object il2CppItem)
                item = il2CppItem.TryCast<StorableItemDefinition>();
            else
                item = itemObj as StorableItemDefinition;
#else
            var item = itemObj as StorableItemDefinition;
#endif
            if (item?.ID == null || !item.ID.StartsWith(BackpackShopIntegration.BackpackItemIdPrefix, StringComparison.Ordinal))
                return true;

            if (!BackpackShopIntegration.IsBackpackTierPurchase(item.ID, out var tierIndex))
                return true;

            var backpack = PlayerBackpack.Instance;
            if (backpack != null && tierIndex <= backpack.EquippedTierIndex)
            {
                ModLogger.Info($"Blocked purchase selection for backpack tier {tierIndex}; player already owns tier {backpack.EquippedTierIndex} or better.");
                return false;
            }

            var quantityInCart = ReflectionUtils.TryGetFieldOrProperty(listing, "QuantityInCart")
                ?? ReflectionUtils.TryGetFieldOrProperty(listing, "quantityInCart");
            if (quantityInCart is int quantity && quantity >= 1)
            {
                ModLogger.Debug($"[Shop] Blocked duplicate cart quantity for backpack tier {tierIndex}.");
                return false;
            }

            // Let the game add the purchased tier item; PlayerBackpack consumes it and applies the tier.
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error("BackpackPurchasePatch: AddItem prefix error", ex);
            return true;
        }
    }

    [HarmonyPatch("SetAmount", typeof(ListingUI), typeof(int))]
    [HarmonyPrefix]
    public static void SetAmount_Prefix(ListingUI ui, ref int amount)
    {
        try
        {
            if (amount <= 1 || ui == null)
                return;

            var listing = ReflectionUtils.TryGetFieldOrProperty(ui, "Listing")
                ?? ReflectionUtils.TryGetFieldOrProperty(ui, "listing");
            var itemObj = ReflectionUtils.TryGetFieldOrProperty(listing, "Item");
#if !MONO
            var item = itemObj is Il2CppSystem.Object il2CppItem
                ? il2CppItem.TryCast<StorableItemDefinition>()
                : itemObj as StorableItemDefinition;
#else
            var item = itemObj as StorableItemDefinition;
#endif
            if (item?.ID == null || !BackpackShopIntegration.IsBackpackTierPurchase(item.ID, out _))
                return;

            amount = 1;
            ModLogger.Debug("[Shop] Clamped backpack tier quantity to one.");
        }
        catch (Exception ex)
        {
            ModLogger.Error("BackpackPurchasePatch: SetAmount prefix error", ex);
        }
    }
}
