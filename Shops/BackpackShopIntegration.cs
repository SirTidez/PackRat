using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using PackRat.Config;
using PackRat.Helpers;
using PackRat.Patches;
using UnityEngine;

#if MONO
using ScheduleOne;
using ScheduleOne.ItemFramework;
using ScheduleOne.UI.Shop;
#else
using Il2CppScheduleOne;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.UI.Shop;
#endif

namespace PackRat.Shops;

/// <summary>
/// Injects backpack tier listings into the Hardware Store and hooks purchase so buying a tier
/// sets the player's highest purchased tier instead of granting a physical item.
/// Reimplements patterns from S1API (no S1API dependency).
/// </summary>
public static class BackpackShopIntegration
{
    public const string BackpackItemIdPrefix = "PackRat_Backpack_Tier_";
    private const string HardwareStoreName = "Hardware Store";

    /// <summary>Short tooltip descriptions for each backpack tier (shown when unlocked in the store).</summary>
    private static readonly string[] TierDescriptions =
    [
        "A basic 8-slot backpack. Good for starting out.",
        "A compact 16-slot pack. Stays under the radar.",
        "24 slots. Police may search this size and above.",
        "32 slots. Sturdy and roomy; draws more attention.",
        "The largest option with 40 slots. Maximum capacity.",
    ];
    /// <summary>Instance IDs of shop instances we have already added backpack listings to (so we add to every hardware store, not just one).</summary>
    private static readonly HashSet<int> _shopsIntegrated = new HashSet<int>();

    /// <summary>
    /// Call when the Main scene has loaded to find all Hardware Stores and add backpack tier listings.
    /// Also invoked from ShopInterfacePatch when any shop awakens/enables.
    /// </summary>
    public static void RunWhenReady()
    {
        MelonCoroutines.Start(WaitAndIntegrate());
    }

    /// <summary>
    /// If the given shop is a hardware store, adds backpack tier listings to it (once per shop instance).
    /// Called from ShopInterface.Awake/OnEnable so we run when each shop is created or shown.
    /// </summary>
    public static bool TryAddBackpackListingsToShop(ShopInterface shop)
    {
        if (shop == null)
            return false;
        if (!TryMatchHardwareStore(shop, out _))
            return false;
        var id = shop.GetInstanceID();
        if (_shopsIntegrated.Contains(id))
            return true;
        if (AddBackpackListings(shop))
        {
            _shopsIntegrated.Add(id);
            ModLogger.Info($"BackpackShopIntegration: Added backpack tier listings to Hardware Store (instance {id}).");
            return true;
        }
        ModLogger.Warn("BackpackShopIntegration: Hardware Store found but adding listings failed (check icons/config).");
        return false;
    }

    private static IEnumerator WaitAndIntegrate()
    {
        const int attempts = 60; // 30 seconds
        for (var i = 0; i < attempts; i++)
        {
            yield return new WaitForSeconds(0.5f);
            AddToAllHardwareStoresInScene();
        }
        if (_shopsIntegrated.Count == 0)
            LogHardwareStoreNotFound();
    }

    private static void AddToAllHardwareStoresInScene()
    {
        try
        {
            var shopType = typeof(ShopInterface);
            var allShopsField = shopType.GetField("AllShops", BindingFlags.Public | BindingFlags.Static);
            if (allShopsField != null && allShopsField.GetValue(null) is System.Collections.IEnumerable enumerable)
            {
                foreach (var s in enumerable)
                {
                    if (s == null) continue;
                    if (TryMatchHardwareStore(s, out var shop))
                        TryAddBackpackListingsToShop(shop);
                }
            }
            var inScene = Utils.FindObjectsOfTypeSafe<ShopInterface>();
            if (inScene != null)
            {
                for (var i = 0; i < inScene.Length; i++)
                {
                    if (inScene[i] != null && TryMatchHardwareStore(inScene[i], out var shop))
                        TryAddBackpackListingsToShop(shop);
                }
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("BackpackShopIntegration: AddToAllHardwareStoresInScene", ex);
        }
    }

    private static void LogHardwareStoreNotFound()
    {
        var names = new System.Collections.Generic.List<string>();
        try
        {
            var shopType = typeof(ShopInterface);
            var allShopsField = shopType.GetField("AllShops", BindingFlags.Public | BindingFlags.Static);
            if (allShopsField != null)
            {
                var allShops = allShopsField.GetValue(null);
                if (allShops != null && allShops is System.Collections.IEnumerable enumerable)
                {
                    foreach (var s in enumerable)
                    {
                        if (s == null) continue;
                        var n = GetShopDisplayName(s);
                        if (!string.IsNullOrEmpty(n) && !names.Contains(n))
                            names.Add(n);
                    }
                }
            }
            if (names.Count == 0)
            {
                var inScene = Utils.FindObjectsOfTypeSafe<ShopInterface>();
                if (inScene != null)
                    for (var i = 0; i < inScene.Length; i++)
                    {
                        var n = GetShopDisplayName(inScene[i]);
                        if (!string.IsNullOrEmpty(n) && !names.Contains(n))
                            names.Add(n);
                    }
            }
        }
        catch { }
        if (names.Count > 0)
            ModLogger.Warn($"BackpackShopIntegration: Hardware Store not found after 30s. Shop names in game: [{string.Join(", ", names)}]. If your hardware store uses a different name, we can add a match for it.");
        else
            ModLogger.Warn("BackpackShopIntegration: Hardware Store not found after 30s (no ShopInterface instances with a name found). Shops may load later when you open them.");
    }

    private static bool TryGetHardwareStore(out ShopInterface shop)
    {
        shop = null;
        try
        {
            // 1) Try static AllShops list (may be populated when shop UIs are created)
            var shopType = typeof(ShopInterface);
            var allShopsField = shopType.GetField("AllShops", BindingFlags.Public | BindingFlags.Static);
            if (allShopsField != null)
            {
                var allShops = allShopsField.GetValue(null);
                if (allShops != null && allShops is System.Collections.IEnumerable enumerable)
                {
                    var count = 0;
                    foreach (var s in enumerable)
                    {
                        if (s == null) continue;
                        count++;
                        if (TryMatchHardwareStore(s, out shop))
                            return true;
                    }
#if DEBUG
                    if (count == 0)
                        ModLogger.Debug("BackpackShopIntegration: AllShops list is empty.");
                    else
                        ModLogger.Debug($"BackpackShopIntegration: AllShops has {count} entries; no Hardware Store name matched.");
#endif
                }
            }

            // 2) Fallback: find all ShopInterface in scene (shops may exist before AllShops is populated)
            var inScene = Utils.FindObjectsOfTypeSafe<ShopInterface>();
            if (inScene != null && inScene.Length > 0)
            {
                for (var i = 0; i < inScene.Length; i++)
                {
                    var s = inScene[i];
                    if (s == null) continue;
                    if (TryMatchHardwareStore(s, out shop))
                        return true;
                }
#if DEBUG
                ModLogger.Debug($"BackpackShopIntegration: Found {inScene.Length} ShopInterface in scene; none matched Hardware Store.");
#endif
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("BackpackShopIntegration: Error finding Hardware Store", ex);
        }
        return false;
    }

    /// <summary>
    /// Returns true if the shop instance is the Hardware Store (by name). Tries multiple name field variants.
    /// </summary>
    private static bool TryMatchHardwareStore(object shopObj, out ShopInterface shop)
    {
        shop = null;
        if (shopObj == null) return false;
        var nameStr = GetShopDisplayName(shopObj);
        if (string.IsNullOrEmpty(nameStr)) return false;
        // Match exact "Hardware Store" or substring "Hardware" (in case of localization or trailing space)
        var isHardware = string.Equals(nameStr, HardwareStoreName, StringComparison.OrdinalIgnoreCase)
            || nameStr.IndexOf("Hardware", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!isHardware) return false;
        try
        {
            shop = (ShopInterface)shopObj;
            return true;
        }
        catch { return false; }
    }

    private static string GetShopDisplayName(object shopObj)
    {
        if (shopObj == null) return null;
        foreach (var candidate in new[] { "ShopName", "shopName", "Name", "name", "DisplayName" })
        {
            var v = ReflectionUtils.TryGetFieldOrProperty(shopObj, candidate);
            if (v is string s && !string.IsNullOrWhiteSpace(s))
                return s.Trim();
        }
        return null;
    }

    private static bool AddBackpackListings(ShopInterface shop)
    {
        if (shop == null)
            return false;
        var cfg = Configuration.Instance;
        if (!LevelManagerPatch.TryGetFallbackIcon(out var fallbackTexture, out var fallbackSprite))
        {
            ModLogger.Error("BackpackShopIntegration: Could not load fallback icon.");
            return false;
        }
        // Only show tiers the player has not already purchased (avoid showing tier 0 if they already have it, etc.)
        var currentHighest = PlayerBackpack.Instance != null ? PlayerBackpack.Instance.HighestPurchasedTierIndex : -1;
        for (var i = 0; i < Configuration.BackpackTiers.Length; i++)
        {
            if (i <= currentHighest)
                continue;
            if (!cfg.TierEnabled[i])
                continue;
            var itemId = BackpackItemIdPrefix + i;
            if (ShopHasItem(shop, itemId))
                continue;
            var tierSprite = LevelManagerPatch.GetTierSprite(i, fallbackSprite, fallbackTexture);
            var def = CreateBackpackTierDefinition(i, itemId, cfg, tierSprite);
            if (def == null)
                continue;
            if (!RegisterDefinition(def))
            {
                ModLogger.Warn($"BackpackShopIntegration: Could not register definition for tier {i}.");
                continue;
            }
            if (!AddListingToShop(shop, def, fallbackSprite, fallbackTexture))
                ModLogger.Warn($"BackpackShopIntegration: Could not add listing for tier {i}.");
        }
        ModLogger.Info("BackpackShopIntegration: Added backpack tier listings to Hardware Store.");
        return true;
    }

    private static bool ShopHasItem(ShopInterface shop, string itemId)
    {
        try
        {
            if (shop.Listings == null)
                return false;
            foreach (var listing in shop.Listings)
            {
                if (listing?.Item != null && listing.Item.ID == itemId)
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static StorableItemDefinition CreateBackpackTierDefinition(int tierIndex, string itemId, Configuration cfg, Sprite iconSprite)
    {
        try
        {
#if MONO
            var def = (StorableItemDefinition)ScriptableObject.CreateInstance(typeof(StorableItemDefinition));
#else
            var def = (StorableItemDefinition)ScriptableObject.CreateInstance("Il2CppScheduleOne.ItemFramework.StorableItemDefinition");
#endif
            if (def == null)
                return null;
            if (!ReflectionUtils.TrySetFieldOrProperty(def, "ID", itemId))
                ReflectionUtils.TrySetFieldOrProperty(def, "id", itemId);
            var displayName = Configuration.BackpackTiers[tierIndex].Name;
            if (!ReflectionUtils.TrySetFieldOrProperty(def, "Name", displayName))
                ReflectionUtils.TrySetFieldOrProperty(def, "name", displayName);
            if (iconSprite != null)
            {
                foreach (var name in new[] { "Icon", "icon", "Sprite", "sprite", "ItemIcon", "itemIcon", "DisplayIcon" })
                {
                    if (ReflectionUtils.TrySetFieldOrProperty(def, name, iconSprite))
                        break;
                }
            }
            var price = tierIndex < cfg.TierPrices.Length ? cfg.TierPrices[tierIndex] : 25f + tierIndex * 50f;
            ReflectionUtils.TrySetFieldOrProperty(def, "BasePurchasePrice", price);
            var rank = tierIndex < cfg.TierUnlockRanks.Length ? cfg.TierUnlockRanks[tierIndex] : Configuration.BackpackTiers[tierIndex].DefaultUnlockRank;
            ReflectionUtils.TrySetFieldOrProperty(def, "RequiredRank", rank);
            ReflectionUtils.TrySetFieldOrProperty(def, "RequiresLevelToPurchase", true);
            var description = tierIndex < TierDescriptions.Length ? TierDescriptions[tierIndex] : null;
            if (!string.IsNullOrEmpty(description))
            {
                foreach (var name in new[] { "Description", "description", "TooltipText", "tooltipText", "FlavorText", "flavorText", "ItemDescription", "itemDescription" })
                {
                    if (ReflectionUtils.TrySetFieldOrProperty(def, name, description))
                        break;
                }
            }
            return def;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"BackpackShopIntegration: CreateBackpackTierDefinition tier {tierIndex}", ex);
            return null;
        }
    }

    private static bool RegisterDefinition(StorableItemDefinition def)
    {
        try
        {
#if MONO
            var registry = Registry.Instance;
            if (registry == null)
                return false;
            var addMethod = registry.GetType().GetMethod("AddToRegistry", BindingFlags.Public | BindingFlags.Instance);
            if (addMethod == null)
                return false;
            addMethod.Invoke(registry, new object[] { def });
            return true;
#else
            var registry = Registry.Instance;
            if (registry == null)
                return false;
            registry.AddToRegistry(def);
            return true;
#endif
        }
        catch (Exception ex)
        {
            ModLogger.Error("BackpackShopIntegration: RegisterDefinition", ex);
            return false;
        }
    }

    private static bool AddListingToShop(ShopInterface shop, StorableItemDefinition def, Sprite fallbackSprite, Texture2D fallbackTexture)
    {
        try
        {
            var listing = new ShopListing
            {
                Item = def,
                name = def.Name
            };
            shop.Listings.Add(listing);
            listing.Initialize(shop);
            var sprite = LevelManagerPatch.GetTierSprite(ParseTierFromItemId(def.ID), fallbackSprite, fallbackTexture);
            CreateListingUI(shop, listing, sprite);
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error("BackpackShopIntegration: AddListingToShop", ex);
            return false;
        }
    }

    private static int ParseTierFromItemId(string itemId)
    {
        if (string.IsNullOrEmpty(itemId) || !itemId.StartsWith(BackpackItemIdPrefix, StringComparison.Ordinal))
            return -1;
        var suffix = itemId.Substring(BackpackItemIdPrefix.Length);
        return int.TryParse(suffix, out var i) ? i : -1;
    }

    private static void CreateListingUI(ShopInterface shop, ShopListing listing, Sprite iconSprite)
    {
        try
        {
            var listingUIPrefab = ReflectionUtils.TryGetFieldOrProperty(shop, "ListingUIPrefab");
            var listingContainer = ReflectionUtils.TryGetFieldOrProperty(shop, "ListingContainer");
            if (listingUIPrefab == null || listingContainer == null)
            {
                ModLogger.Warn("BackpackShopIntegration: Shop missing ListingUIPrefab or ListingContainer.");
                return;
            }
            var prefabGo = (listingUIPrefab as UnityEngine.Component)?.gameObject;
            var container = listingContainer as Transform;
            if (prefabGo == null || container == null)
                return;
            var uiGo = UnityEngine.Object.Instantiate(prefabGo, container);
            var listingUI = uiGo.GetComponent<ListingUI>();
            if (listingUI == null)
            {
                UnityEngine.Object.Destroy(uiGo);
                return;
            }
            listingUI.Initialize(listing);
            if (iconSprite != null && listingUI.Icon != null)
                listingUI.Icon.sprite = iconSprite;
            BindListingUIEvents(shop, listingUI);
            AddToListingUICollection(shop, listingUI);
        }
        catch (Exception ex)
        {
            ModLogger.Error("BackpackShopIntegration: CreateListingUI", ex);
        }
    }

    private static void BindListingUIEvents(ShopInterface shop, ListingUI listingUI)
    {
        try
        {
#if !MONO
            listingUI.onClicked = (Il2CppSystem.Action)(() => shop.ListingClicked(listingUI));
            listingUI.onDropdownClicked = (Il2CppSystem.Action)(() => shop.DropdownClicked(listingUI));
            listingUI.hoverStart = (Il2CppSystem.Action)(() => shop.EntryHovered(listingUI));
            listingUI.hoverEnd = (Il2CppSystem.Action)(() => shop.EntryUnhovered());
#else
            var shopType = typeof(ShopInterface);
            var listingClicked = shopType.GetMethod("ListingClicked", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            var dropdownClicked = shopType.GetMethod("DropdownClicked", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            var entryHovered = shopType.GetMethod("EntryHovered", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            var entryUnhovered = shopType.GetMethod("EntryUnhovered", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (listingClicked != null)
                listingUI.onClicked = (Action)Delegate.Combine(listingUI.onClicked, (Action)(() => listingClicked.Invoke(shop, new object[] { listingUI })));
            if (dropdownClicked != null)
                listingUI.onDropdownClicked = (Action)Delegate.Combine(listingUI.onDropdownClicked, (Action)(() => dropdownClicked.Invoke(shop, new object[] { listingUI })));
            if (entryHovered != null)
                listingUI.hoverStart = (Action)Delegate.Combine(listingUI.hoverStart, (Action)(() => entryHovered.Invoke(shop, new object[] { listingUI })));
            if (entryUnhovered != null)
                listingUI.hoverEnd = (Action)Delegate.Combine(listingUI.hoverEnd, (Action)(() => entryUnhovered.Invoke(shop, null)));
#endif
        }
        catch (Exception ex)
        {
            ModLogger.Error("BackpackShopIntegration: BindListingUIEvents", ex);
        }
    }

    private static void AddToListingUICollection(ShopInterface shop, ListingUI listingUI)
    {
        try
        {
            var field = typeof(ShopInterface).GetField("listingUI", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                return;
            var list = field.GetValue(shop);
            if (list == null)
                return;
#if MONO
            var addMethod = list.GetType().GetMethod("Add", new[] { typeof(ListingUI) });
            addMethod?.Invoke(list, new object[] { listingUI });
#else
            var listTyped = list as Il2CppSystem.Collections.Generic.List<ListingUI>;
            listTyped?.Add(listingUI);
#endif
        }
        catch (Exception ex)
        {
            ModLogger.Error("BackpackShopIntegration: AddToListingUICollection", ex);
        }
    }

    /// <summary>
    /// Returns true if the item ID is a PackRat backpack tier purchase (and parses the tier index).
    /// </summary>
    public static bool IsBackpackTierPurchase(string itemId, out int tierIndex)
    {
        tierIndex = ParseTierFromItemId(itemId ?? "");
        return tierIndex >= 0;
    }

    /// <summary>
    /// Removes the given tier's listing from all hardware stores we have integrated.
    /// Call this after the player purchases (uses) a tier so it no longer appears in the store.
    /// </summary>
    public static void RemoveTierListingFromAllShops(int tierIndex)
    {
        if (tierIndex < 0 || tierIndex >= Configuration.BackpackTiers.Length)
            return;
        var itemId = BackpackItemIdPrefix + tierIndex;
        var shopIds = new List<int>(_shopsIntegrated);
        foreach (var id in shopIds)
        {
            var shop = FindShopByInstanceId(id);
            if (shop != null)
                RemoveTierListingFromShop(shop, itemId);
        }
    }

    private static ShopInterface FindShopByInstanceId(int instanceId)
    {
        try
        {
            var shopType = typeof(ShopInterface);
            var allShopsField = shopType.GetField("AllShops", BindingFlags.Public | BindingFlags.Static);
            if (allShopsField != null && allShopsField.GetValue(null) is System.Collections.IEnumerable enumerable)
            {
                foreach (var s in enumerable)
                {
                    if (s != null && (s as UnityEngine.Object)?.GetInstanceID() == instanceId)
                        return (ShopInterface)s;
                }
            }
            var inScene = Utils.FindObjectsOfTypeSafe<ShopInterface>();
            if (inScene != null)
            {
                for (var i = 0; i < inScene.Length; i++)
                {
                    if (inScene[i] != null && inScene[i].GetInstanceID() == instanceId)
                        return inScene[i];
                }
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("BackpackShopIntegration: FindShopByInstanceId", ex);
        }
        return null;
    }

    private static void RemoveTierListingFromShop(ShopInterface shop, string itemId)
    {
        if (shop?.Listings == null)
            return;
        try
        {
            object listingToRemove = null;
            foreach (var listing in shop.Listings)
            {
                if (listing?.Item != null && listing.Item.ID == itemId)
                {
                    listingToRemove = listing;
                    break;
                }
            }
            if (listingToRemove == null)
                return;

            RemoveListingFromList(shop.Listings, listingToRemove);
            RemoveAndDestroyListingUI(shop, listingToRemove);
        }
        catch (Exception ex)
        {
            ModLogger.Error("BackpackShopIntegration: RemoveTierListingFromShop", ex);
        }
    }

    private static int GetListCount(object list)
    {
        if (list == null) return 0;
        var count = ReflectionUtils.TryGetFieldOrProperty(list, "Count") ?? ReflectionUtils.TryGetFieldOrProperty(list, "count");
        if (count is int i) return i;
        if (count is long l) return (int)l;
        return 0;
    }

    private static void RemoveListingFromList(object list, object listing)
    {
        if (list == null || listing == null)
            return;
        try
        {
#if MONO
            var removeMethod = list.GetType().GetMethod("Remove", new[] { listing.GetType() });
            removeMethod?.Invoke(list, new[] { listing });
#else
            var il2List = list as Il2CppSystem.Collections.IList;
            if (il2List != null)
            {
                var listCount = GetListCount(il2List);
                for (var i = 0; i < listCount; i++)
                {
                    var elem = il2List[i];
                    if (elem != null && (elem.Equals(listing) || object.ReferenceEquals(elem, listing)))
                    {
                        il2List.RemoveAt(i);
                        return;
                    }
                }
            }
#endif
        }
        catch (Exception ex)
        {
            ModLogger.Error("BackpackShopIntegration: RemoveListingFromList", ex);
        }
    }

    private static void RemoveAndDestroyListingUI(ShopInterface shop, object listing)
    {
        if (shop == null || listing == null)
            return;
        try
        {
            var list = typeof(ShopInterface).GetField("listingUI", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(shop);
            if (list == null)
                return;
            var count = ReflectionUtils.TryGetFieldOrProperty(list, "Count") ?? ReflectionUtils.TryGetFieldOrProperty(list, "count");
            var countInt = count is int c ? c : 0;
            if (count is long l)
                countInt = (int)l;
            for (var i = countInt - 1; i >= 0; i--)
            {
                var getItem = list.GetType().GetMethod("get_Item", new[] { typeof(int) }) ?? list.GetType().GetMethod("Get", new[] { typeof(int) });
                var listingUI = getItem?.Invoke(list, new object[] { i });
                if (listingUI == null)
                    continue;
                var uiListing = ReflectionUtils.TryGetFieldOrProperty(listingUI, "Listing") ?? ReflectionUtils.TryGetFieldOrProperty(listingUI, "listing");
                if (uiListing != listing)
                    continue;
                var go = (listingUI as UnityEngine.Component)?.gameObject;
                if (go != null)
                    UnityEngine.Object.Destroy(go);
#if MONO
                var removeAt = list.GetType().GetMethod("RemoveAt", new[] { typeof(int) });
                removeAt?.Invoke(list, new object[] { i });
#else
                var il2List = list as Il2CppSystem.Collections.IList;
                il2List?.RemoveAt(i);
#endif
                return;
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("BackpackShopIntegration: RemoveAndDestroyListingUI", ex);
        }
    }
}
