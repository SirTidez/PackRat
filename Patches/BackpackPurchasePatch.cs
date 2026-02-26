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
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI.Shop;
#endif

namespace PackRat.Patches;

/// <summary>
/// Intercepts the shop's ListingClicked only to detect backpack tier listings.
/// We do NOT run any purchase logic here: the first click is "select for purchase" (not confirm),
/// and the game uses account funds (not cash) when the player confirms. So we let the game handle
/// the full flow: select → confirm → deduct account funds → add item. Our cleanup in PlayerBackpack
/// (RemoveBackpackTierItemsFromPlayerInventory) removes the placeholder item and applies the tier.
/// </summary>
[HarmonyPatch(typeof(ShopInterface))]
public static class BackpackPurchasePatch
{
    [HarmonyPatch("ListingClicked", typeof(ListingUI))]
    [HarmonyPrefix]
    public static bool ListingClicked_Prefix(ShopInterface __instance, ListingUI listingUI)
    {
        try
        {
            if (listingUI == null)
                return true;

            var listing = ReflectionUtils.TryGetFieldOrProperty(listingUI, "Listing")
                ?? ReflectionUtils.TryGetFieldOrProperty(listingUI, "listing");
            if (listing == null)
                return true;

            var item = ReflectionUtils.TryGetFieldOrProperty(listing, "Item") as StorableItemDefinition;
            if (item?.ID == null || !item.ID.StartsWith(BackpackShopIntegration.BackpackItemIdPrefix, StringComparison.Ordinal))
                return true;

            // This is a backpack tier listing. Do not attempt any purchase or deduction here:
            // this click is "select to purchase", and the game uses account funds on confirm.
            // Let the game handle select and confirm; our cleanup will remove the placeholder item and apply the tier.
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error("BackpackPurchasePatch: ListingClicked prefix error", ex);
            return true;
        }
    }
}
