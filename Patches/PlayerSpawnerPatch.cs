using System.Reflection;
using HarmonyLib;
using PackRat.Helpers;
using PackRatUtils = PackRat.Helpers.Utils;
using UnityEngine;

#if MONO
using ScheduleOne.PlayerScripts;
using ScheduleOne.Storage;
#else
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Storage;
#endif

namespace PackRat.Patches;

/// <summary>
/// Harmony patch for player spawner initialization.
/// Attaches StorageEntity and PlayerBackpack to the player prefab.
/// </summary>
[HarmonyPatch]
public static class PlayerSpawnerPatch
{
    private static readonly string[] CandidateSpawnerTypeNames =
    {
        "FishNet.Component.Spawning.PlayerSpawner",
        "Il2CppFishNet.Component.Spawning.PlayerSpawner",
        "ScheduleOne.PlayerScripts.PlayerSpawner",
        "Il2CppScheduleOne.PlayerScripts.PlayerSpawner"
    };

    [HarmonyTargetMethod]
    public static MethodBase TargetMethod()
    {
        for (var i = 0; i < CandidateSpawnerTypeNames.Length; i++)
        {
            var type = AccessTools.TypeByName(CandidateSpawnerTypeNames[i]);
            if (type == null)
                continue;

            var method = AccessTools.Method(type, "InitializeOnce");
            if (method != null)
                return method;
        }

        ModLogger.Warn("Failed to resolve PlayerSpawner.InitializeOnce for backpack prefab setup patch.");
        return null;
    }

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

        EnsurePlayerBackpackSetup(player, addLocalBackpackComponent: true);
    }

    public static void EnsurePlayerBackpackSetup(Player player, bool addLocalBackpackComponent)
    {
        if (player == null)
            return;

        var storage = PackRatUtils.GetOrAddComponentSafe<StorageEntity>(player.gameObject);
        if (storage == null)
            return;

        storage.SlotCount = PlayerBackpack.MaxStorageSlots;
        storage.DisplayRowCount = 8;
        storage.StorageEntityName = PlayerBackpack.StorageName;
        storage.MaxAccessDistance = float.PositiveInfinity;

        if (!addLocalBackpackComponent)
            return;

        var localGameObject = player.LocalGameObject != null ? player.LocalGameObject : player.gameObject;
        PackRatUtils.GetOrAddComponentSafe<PlayerBackpack>(localGameObject);
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
