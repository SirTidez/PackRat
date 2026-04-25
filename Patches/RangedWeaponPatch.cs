using HarmonyLib;

#if MONO
using ScheduleOne.Equipping;
using ScheduleOne.Storage;
#else
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.Equipping;
using Il2CppScheduleOne.Storage;
using Il2CppSystem;
#endif

namespace PackRat.Patches;

/// <summary>
/// Extends ranged weapon reloads so compatible magazines stored in the backpack can be used
/// when the active inventory has no matching ammo.
/// </summary>
[HarmonyPatch(typeof(Equippable_RangedWeapon))]
public static class RangedWeaponPatch
{
    [HarmonyPatch("GetMagazine")]
    [HarmonyPostfix]
    public static void GetMagazine(
        Equippable_RangedWeapon __instance,
        ref StorableItemInstance mag,
        ref bool __result)
    {
        if (__result || __instance == null || __instance.Magazine == null)
            return;

        if (TryGetBackpackMagazine(__instance.Magazine.ID, out var backpackMag))
        {
            mag = backpackMag;
            __result = true;
        }
    }

    private static bool TryGetBackpackMagazine(string magazineId, out StorableItemInstance magazine)
    {
        magazine = null;

        if (string.IsNullOrEmpty(magazineId))
            return false;

        var backpack = PlayerBackpack.Instance;
        if (backpack == null || !backpack.IsUnlocked)
            return false;

        var slots = backpack.ItemSlots;
        if (slots == null)
            return false;

        for (var i = 0; i < slots.Count; i++)
        {
#if !MONO
            var slot = slots[new Index(i)]?.TryCast<Il2CppScheduleOne.ItemFramework.ItemSlot>();
#else
            var slot = slots[i];
#endif
            var item = slot?.ItemInstance;
            if (item == null || item.Quantity <= 0 || item.ID != magazineId)
                continue;

#if !MONO
            magazine = item.TryCast<StorableItemInstance>();
#else
            magazine = item as StorableItemInstance;
#endif
            if (magazine != null)
                return true;
        }

        return false;
    }
}
