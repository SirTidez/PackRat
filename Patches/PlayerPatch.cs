using HarmonyLib;
using PackRat.Extensions;
using PackRat.Helpers;
using PackRat.Storage;

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

        var tierIndex = -1;
        if (__instance.IsOwner && PlayerBackpack.Instance != null)
            tierIndex = PlayerBackpack.Instance.HighestPurchasedTierIndex;
        else
            TryReadExistingTier(parentFolderPath, out tierIndex);

        var data = new BackpackSaveData { Contents = contents, HighestPurchasedTierIndex = tierIndex };
        var json = JsonHelper.SerializeObject(data);

#if !MONO
        __instance.Cast<ISaveable>().WriteSubfile(parentFolderPath, "Backpack", json);
#else
        ISaveable instance = __instance;
        instance.WriteSubfile(parentFolderPath, "Backpack", json);
#endif
    }

    private static void TryReadExistingTier(string parentFolderPath, out int tierIndex)
    {
        tierIndex = -1;
        try
        {
            var path = System.IO.Path.Combine(parentFolderPath, "Backpack");
            if (!System.IO.File.Exists(path))
                return;
            var json = System.IO.File.ReadAllText(path);
            var data = JsonHelper.DeserializeObject<BackpackSaveData>(json);
            if (data != null)
                tierIndex = data.HighestPurchasedTierIndex;
        }
        catch
        {
            // ignore; keep -1
        }
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
            var contents = backpackData;
            var tierIndex = -1;

            var saveData = JsonHelper.DeserializeObject<BackpackSaveData>(backpackData);
            if (saveData != null && saveData.Contents != null)
            {
                contents = saveData.Contents;
                tierIndex = saveData.HighestPurchasedTierIndex;
            }

            if (!ItemSet.TryDeserialize(contents, out var itemSet))
            {
                ModLogger.Error("Failed to deserialize backpack data.");
                return;
            }

            itemSet.LoadTo(backpackStorage.ItemSlots);

            if (__instance.IsOwner && PlayerBackpack.Instance != null)
                PlayerBackpack.Instance.SetHighestPurchasedTierIndex(tierIndex);
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
            var contents = backpackData;
            var tierIndex = -1;

            var saveData = JsonHelper.DeserializeObject<BackpackSaveData>(backpackData);
            if (saveData != null && saveData.Contents != null)
            {
                contents = saveData.Contents;
                tierIndex = saveData.HighestPurchasedTierIndex;
            }

            if (!ItemSet.TryDeserialize(contents, out var itemSet))
            {
                ModLogger.Error("Failed to deserialize network backpack data.");
                return;
            }

            itemSet.LoadTo(backpackStorage.ItemSlots);

            if (PlayerBackpack.Instance != null)
                PlayerBackpack.Instance.SetHighestPurchasedTierIndex(tierIndex);
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
