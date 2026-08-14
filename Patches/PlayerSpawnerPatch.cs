using HarmonyLib;
using PackRat.Config;
using PackRat.Helpers;
using PackRatUtils = PackRat.Helpers.Utils;
using UnityEngine;

#if MONO
using S1PlayerSpawner = FishNet.Component.Spawning.PlayerSpawner;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Storage;
#else
using S1PlayerSpawner = Il2CppFishNet.Component.Spawning.PlayerSpawner;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Storage;
#endif

namespace PackRat.Patches;

/// <summary>
/// Harmony patch for player spawner initialization.
/// Attaches StorageEntity and PlayerBackpack to the player prefab.
/// </summary>
[HarmonyPatch(typeof(S1PlayerSpawner), "InitializeOnce")]
public static class PlayerSpawnerPatch
{
    [HarmonyPostfix]
    public static void InitializeOnce(object __instance)
    {
        if (__instance == null)
            return;

        if (!TryResolvePlayerPrefab(__instance, out var playerPrefab))
            return;

        var player = playerPrefab.GetComponent<Player>();
        if (player == null)
            return;

        // Player prefabs are cloned for both the local player and remote peers. The storage
        // component belongs on every clone, but PlayerBackpack owns a local-only static instance
        // and is attached by PlayerPatch once ownership is known.
        EnsurePlayerBackpackSetup(player, addLocalBackpackComponent: false);
    }

    public static void EnsurePlayerBackpackSetup(Player player, bool addLocalBackpackComponent)
    {
        if (player == null)
            return;

        var storage = PackRatUtils.GetOrAddComponentSafe<StorageEntity>(player.gameObject);
        if (storage == null)
            return;

        // Allocate enough slots before the game's save loader restores item data. This must use the
        // largest configured tier rather than a fixed ceiling, otherwise legacy or customized bags
        // silently lose items above that ceiling during deserialization.
        var bootstrapSlotCount = GetBootstrapStorageSlotCount();
        storage.SlotCount = bootstrapSlotCount;
        storage.DisplayRowCount = GetDisplayRowCount(bootstrapSlotCount);
        storage.StorageEntityName = PlayerBackpack.StorageName;
        storage.MaxAccessDistance = float.PositiveInfinity;

        if (!addLocalBackpackComponent)
            return;

        var localGameObject = player.LocalGameObject != null ? player.LocalGameObject : player.gameObject;
        PackRatUtils.GetOrAddComponentSafe<PlayerBackpack>(localGameObject);
    }

    private static int GetBootstrapStorageSlotCount()
    {
        var slotCount = PlayerBackpack.MinimumStorageSlots;
        var configuredCounts = Configuration.Instance.TierSlotCounts;
        if (configuredCounts == null)
            return slotCount;

        for (var i = 0; i < configuredCounts.Length; i++)
            slotCount = Math.Max(slotCount, configuredCounts[i]);

        return slotCount;
    }

    private static int GetDisplayRowCount(int slotCount)
    {
        if (slotCount <= 20)
            return (int)Math.Ceiling(slotCount / 5.0);
        if (slotCount <= 80)
            return (int)Math.Ceiling(slotCount / 10.0);
        return (int)Math.Ceiling(slotCount / 16.0);
    }

    private static bool TryResolvePlayerPrefab(object spawnerInstance, out GameObject playerPrefab)
    {
        playerPrefab = null;
        if (spawnerInstance == null)
            return false;

        var candidateMemberNames = new[]
        {
            "_playerPrefab",
            "playerPrefab",
            "PlayerPrefab"
        };

        for (var i = 0; i < candidateMemberNames.Length; i++)
        {
            var prefabObj = ReflectionUtils.TryGetFieldOrProperty(spawnerInstance, candidateMemberNames[i]);
            if (prefabObj == null)
                continue;

            if (prefabObj is GameObject gameObject)
            {
                playerPrefab = gameObject;
                return true;
            }

            if (prefabObj is Component component)
            {
                playerPrefab = component.gameObject;
                return playerPrefab != null;
            }

            if (prefabObj is Transform transform)
            {
                playerPrefab = transform.gameObject;
                return playerPrefab != null;
            }
        }

        return false;
    }
}
