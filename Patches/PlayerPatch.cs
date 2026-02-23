using HarmonyLib;
using PackRat.Extensions;
using PackRat.Helpers;

#if MONO
using ScheduleOne.Persistence;
using ScheduleOne.Persistence.Datas;
using ScheduleOne.PlayerScripts;
#else
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.PlayerScripts;
#endif

namespace PackRat.Patches;

/// <summary>
/// Harmony patches for <see cref="Player"/>.
/// Hooks into save/load and lifecycle events to persist and restore backpack state.
/// </summary>
[HarmonyPatch(typeof(Player))]
public static class PlayerPatch
{
    [HarmonyPatch("Awake")]
    [HarmonyPrefix]
    public static void Awake(Player __instance)
    {
        if (__instance.LocalExtraFiles.Contains("Backpack"))
            return;

        ModLogger.Info("Registering backpack save file for player.");
        __instance.LocalExtraFiles.Add("Backpack");
    }

    [HarmonyPatch("WriteData")]
    [HarmonyPostfix]
    public static void WriteData(Player __instance, string parentFolderPath)
    {
        var backpackStorage = __instance.GetBackpackStorage();
        var contents = new ItemSet(backpackStorage.ItemSlots).GetJSON();

#if !MONO
        __instance.Cast<ISaveable>().WriteSubfile(parentFolderPath, "Backpack", contents);
#else
        ISaveable instance = __instance;
        instance.WriteSubfile(parentFolderPath, "Backpack", contents);
#endif
    }

    [HarmonyPatch("Load", typeof(PlayerData), typeof(string))]
    [HarmonyPrefix]
    public static void Load(Player __instance, PlayerData data, string containerPath)
    {
        if (!__instance.Loader.TryLoadFile(containerPath, "Backpack", out var backpackData))
            return;

        ModLogger.Info("Loading local backpack data.");
        try
        {
            var backpackStorage = __instance.GetBackpackStorage();
            if (!ItemSet.TryDeserialize(backpackData, out var itemSet))
            {
                ModLogger.Error("Failed to deserialize backpack data.");
                return;
            }

            itemSet.LoadTo(backpackStorage.ItemSlots);
        }
        catch (Exception e)
        {
            ModLogger.Error("Error loading backpack data", e);
        }
    }

    [HarmonyPatch("LoadInventory")]
    [HarmonyPrefix]
    public static void LoadInventory(Player __instance, ref string contentsString)
    {
        if (string.IsNullOrEmpty(contentsString))
            return;

        if (!__instance.IsOwner)
        {
            ModLogger.Info("Not the owner, skipping network backpack data load.");
            return;
        }

        var parts = contentsString.Split(["|||"], StringSplitOptions.None);
        if (parts.Length < 2)
            return;

        contentsString = parts[0];
        var backpackData = parts[1];
        ModLogger.Info("Loading backpack data from network payload.");
        try
        {
            var backpackStorage = __instance.GetBackpackStorage();
            if (!ItemSet.TryDeserialize(backpackData, out var itemSet))
            {
                ModLogger.Error("Failed to deserialize network backpack data.");
                return;
            }

            itemSet.LoadTo(backpackStorage.ItemSlots);
        }
        catch (Exception e)
        {
            ModLogger.Error("Error loading network backpack data", e);
        }
    }

    [HarmonyPatch("Activate")]
    [HarmonyPrefix]
    public static void Activate()
    {
        ModLogger.Info("Activating backpack.");
        PlayerBackpack.Instance?.SetBackpackEnabled(true);
    }

    [HarmonyPatch("Deactivate")]
    [HarmonyPrefix]
    public static void Deactivate()
    {
        ModLogger.Info("Deactivating backpack.");
        PlayerBackpack.Instance?.SetBackpackEnabled(false);
    }

    [HarmonyPatch("ExitAll")]
    [HarmonyPrefix]
    public static void ExitAll()
    {
        ModLogger.Info("ExitAll: disabling backpack.");
        PlayerBackpack.Instance?.SetBackpackEnabled(false);
    }

    [HarmonyPatch("OnDied")]
    [HarmonyPrefix]
    public static void OnDied(Player __instance)
    {
        if (!__instance.Owner.IsLocalClient)
            return;

        ModLogger.Info("Player died, disabling backpack.");
        PlayerBackpack.Instance?.SetBackpackEnabled(false);
    }
}
