using HarmonyLib;
using PackRat.Helpers;
using PackRatUtils = PackRat.Helpers.Utils;

#if MONO
using FishNet.Component.Spawning;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Storage;
#else
using Il2CppFishNet.Component.Spawning;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Storage;
using Il2CppVLB;
#endif

namespace PackRat.Patches;

/// <summary>
/// Harmony patches for <see cref="PlayerSpawner"/>.
/// Attaches <see cref="StorageEntity"/> and <see cref="PlayerBackpack"/> to the player prefab on initialization.
/// </summary>
[HarmonyPatch(typeof(PlayerSpawner))]
public static class PlayerSpawnerPatch
{
    [HarmonyPatch("InitializeOnce")]
    [HarmonyPostfix]
    public static void InitializeOnce(PlayerSpawner __instance)
    {
        var playerPrefab = __instance._playerPrefab;
        if (!playerPrefab)
        {
            ModLogger.Error("Player prefab is null!");
            return;
        }

        var player = playerPrefab.GetComponent<Player>();
        if (player == null)
        {
            ModLogger.Error("Player prefab does not have a Player component!");
            return;
        }

        ModLogger.Info("Adding backpack storage to player prefab...");
        var storage = PackRatUtils.GetOrAddComponentSafe<StorageEntity>(player.gameObject);
        if (storage == null)
        {
            ModLogger.Error("Failed to get or add StorageEntity to player prefab!");
            return;
        }

        storage.SlotCount = PlayerBackpack.MaxStorageSlots;
        storage.DisplayRowCount = 8;
        storage.StorageEntityName = PlayerBackpack.StorageName;
        storage.MaxAccessDistance = float.PositiveInfinity;
        PackRatUtils.GetOrAddComponentSafe<PlayerBackpack>(player.LocalGameObject);
    }
}
