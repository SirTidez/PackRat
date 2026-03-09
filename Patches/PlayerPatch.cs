using HarmonyLib;
using PackRat.Extensions;
using PackRat.Helpers;
using PackRat.Networking;
using PackRat.Storage;

#if MONO
using ScheduleOne.Networking;
using ScheduleOne.Persistence;
using ScheduleOne.Persistence.Datas;
using ScheduleOne.PlayerScripts;
#else
using Il2CppScheduleOne.Networking;
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
        PlayerSpawnerPatch.EnsurePlayerBackpackSetup(__instance, __instance.IsOwner);

        if (__instance.IsOwner)
        {
            BackpackStateSyncManager.RequestHostSnapshotForLocalPlayer(__instance);
            BackpackStateSyncManager.TryApplyPendingHostSnapshotToLocalPlayer("awake fallback");
        }

        if (ShouldSkipLocalBackpackPersistence(__instance))
        {
            __instance.LocalExtraFiles.Remove("Backpack");
            return;
        }

        if (__instance.LocalExtraFiles.Contains("Backpack"))
            return;

        ModLogger.Info("Registering backpack save file for player.");
        __instance.LocalExtraFiles.Add("Backpack");
    }

    [HarmonyPatch("WriteData")]
    [HarmonyPostfix]
    public static void WriteData(Player __instance, string parentFolderPath)
    {
        if (ShouldSkipLocalBackpackPersistence(__instance))
            return;

        var backpackStorage = __instance.GetBackpackStorage();
        var contents = new ItemSet(backpackStorage.ItemSlots).GetJSON();

        var tierIndex = -1;
        if (__instance.IsOwner && PlayerBackpack.Instance != null)
        {
            tierIndex = PlayerBackpack.Instance.HighestPurchasedTierIndex;
        }
        else if (BackpackStateSyncManager.TryGetLatestSnapshotForPlayer(__instance, out var syncedData) && syncedData != null)
        {
            if (!string.IsNullOrEmpty(syncedData.Contents))
                contents = syncedData.Contents;
            tierIndex = syncedData.HighestPurchasedTierIndex;
        }
        else
        {
            TryReadExistingData(parentFolderPath, out var existingContents, out tierIndex);
            if (!string.IsNullOrEmpty(existingContents))
                contents = existingContents;
        }

        var data = new BackpackSaveData { Contents = contents, HighestPurchasedTierIndex = tierIndex };
        var json = JsonHelper.SerializeObject(data);

#if !MONO
        __instance.Cast<ISaveable>().WriteSubfile(parentFolderPath, "Backpack", json);
#else
        ISaveable instance = __instance;
        instance.WriteSubfile(parentFolderPath, "Backpack", json);
#endif
    }

    private static void TryReadExistingData(string parentFolderPath, out string contents, out int tierIndex)
    {
        contents = null;
        tierIndex = -1;
        try
        {
            var path = System.IO.Path.Combine(parentFolderPath, "Backpack");
            if (!System.IO.File.Exists(path))
                return;
            var json = System.IO.File.ReadAllText(path);
            var data = JsonHelper.DeserializeObject<BackpackSaveData>(json);
            if (data != null)
            {
                contents = data.Contents;
                tierIndex = data.HighestPurchasedTierIndex;
            }
        }
        catch
        {
            // ignore; keep existing defaults
        }
    }

    [HarmonyPatch("Load", typeof(PlayerData), typeof(string))]
    [HarmonyPrefix]
    public static void Load(Player __instance, PlayerData data, string containerPath)
    {
        if (ShouldSkipLocalBackpackPersistence(__instance))
            return;

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

            if (__instance.IsOwner)
                ApplyTierToLocalBackpack(__instance, tierIndex);
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
        if (ShouldSkipLocalBackpackPersistence(__instance))
            BackpackStateSyncManager.RequestHostSnapshotForLocalPlayer(__instance);

        if (!__instance.IsOwner)
            return;

        var backpackData = string.Empty;
        if (!string.IsNullOrEmpty(contentsString))
        {
            var parts = contentsString.Split(["|||"], StringSplitOptions.None);
            if (parts.Length >= 2)
            {
                contentsString = parts[0];
                backpackData = parts[1];
                ModLogger.Info("Loading backpack data from network payload.");
            }
        }

        BackpackSaveData hostSnapshot = null;
        if (BackpackStateSyncManager.TryGetPendingHostSnapshotForLocalPlayer(out var pendingHostSnapshot))
            hostSnapshot = pendingHostSnapshot;

        if (hostSnapshot == null && string.IsNullOrEmpty(backpackData))
            return;

        try
        {
            var backpackStorage = __instance.GetBackpackStorage();
            var contents = backpackData;
            var tierIndex = -1;

            if (hostSnapshot != null)
            {
                contents = hostSnapshot.Contents ?? string.Empty;
                tierIndex = hostSnapshot.HighestPurchasedTierIndex;
                ModLogger.Info("Loading backpack data from host snapshot request.");
            }
            else
            {
                var saveData = JsonHelper.DeserializeObject<BackpackSaveData>(backpackData);
                if (saveData != null && saveData.Contents != null)
                {
                    contents = saveData.Contents;
                    tierIndex = saveData.HighestPurchasedTierIndex;
                }
            }

            if (!ItemSet.TryDeserialize(contents, out var itemSet))
            {
                ModLogger.Error("Failed to deserialize network backpack data.");
                return;
            }

            itemSet.LoadTo(backpackStorage.ItemSlots);

            ApplyTierToLocalBackpack(__instance, tierIndex);

            if (hostSnapshot != null)
                BackpackStateSyncManager.ClearPendingHostSnapshotForLocalPlayer();
        }
        catch (Exception e)
        {
            ModLogger.Error("Error loading network backpack data", e);
        }
    }

    [HarmonyPatch("RequestSavePlayer")]
    [HarmonyPrefix]
    public static void RequestSavePlayer(Player __instance)
    {
        if (__instance != null && __instance.IsOwner)
            BackpackStateSyncManager.RequestHostSnapshotForLocalPlayer(__instance);
    }

    [HarmonyPatch("Activate")]
    [HarmonyPrefix]
    public static void Activate(Player __instance)
    {
        PlayerSpawnerPatch.EnsurePlayerBackpackSetup(__instance, __instance.IsOwner);

        if (ShouldSkipLocalBackpackPersistence(__instance))
        {
            BackpackStateSyncManager.RequestHostSnapshotForLocalPlayer(__instance);
            BackpackStateSyncManager.TryApplyPendingHostSnapshotToLocalPlayer("activate fallback");
        }

        ModLogger.Info("Activating backpack.");
        PlayerBackpack.Instance?.SetBackpackEnabled(true);
    }

    [HarmonyPatch("Update")]
    [HarmonyPrefix]
    public static void Update(Player __instance)
    {
        if (ShouldSkipLocalBackpackPersistence(__instance))
            BackpackStateSyncManager.TryApplyPendingHostSnapshotToLocalPlayer("update fallback");
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

    private static bool ShouldSkipLocalBackpackPersistence(Player player)
    {
        return player != null
            && player.IsOwner
            && Lobby.Instance != null
            && Lobby.Instance.IsInLobby
            && !Lobby.Instance.IsHost;
    }

    private static void ApplyTierToLocalBackpack(Player player, int tierIndex)
    {
        var backpack = PlayerBackpack.Instance;
        if (backpack == null && player != null)
        {
            var localGameObject = player.LocalGameObject != null ? player.LocalGameObject : player.gameObject;
            backpack = Utils.GetComponentSafe<PlayerBackpack>(localGameObject);
        }

        if (backpack == null)
            return;

        backpack.SetHighestPurchasedTierIndex(tierIndex);
    }
}
