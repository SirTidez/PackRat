using System.Collections;
using HarmonyLib;
using MelonLoader;
using PackRat.Config;
using PackRat.Helpers;
using UnityEngine;

#if MONO
using ScheduleOne.Levelling;
using ScheduleOne.Networking;
#else
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Networking;
#endif

namespace PackRat.Patches;

/// <summary>
/// Harmony patches for <see cref="LevelManager"/>.
/// Registers all backpack tiers as unlockables at their configured ranks.
/// </summary>
[HarmonyPatch(typeof(LevelManager))]
public static class LevelManagerPatch
{
    private const string FallbackIconResourceName = "PackRat.assets.icon.png";

    /// <summary>
    /// Embedded resource names for each backpack tier icon (same order as <see cref="Configuration.BackpackTiers"/>).
    /// </summary>
    private static readonly string[] TierIconResourceNames =
    [
        "PackRat.assets.Backpack Icons.rucksack.png",
        "PackRat.assets.Backpack Icons.smallpack.png",
        "PackRat.assets.Backpack Icons.duffelbag.png",
        "PackRat.assets.Backpack Icons.tacticalpack.png",
        "PackRat.assets.Backpack Icons.hikingbackpack.png",
    ];

    private static LevelManager _pendingLevelManager;
    private static bool _clientRegistrationDone;
    private static bool _clientSubscribed;

    [HarmonyPatch("Awake")]
    [HarmonyPostfix]
    public static void Awake(LevelManager __instance)
    {
        if (__instance == null)
        {
            ModLogger.Error("LevelManager instance is null!");
            return;
        }

        var isClientWaitingForSync = Lobby.Instance != null && Lobby.Instance.IsInLobby && !Lobby.Instance.IsHost;
        if (isClientWaitingForSync)
        {
            _pendingLevelManager = __instance;
            if (!_clientSubscribed)
            {
                _clientSubscribed = true;
                ConfigSyncManager.OnConfigSynced += OnClientConfigSynced;
            }
            MelonCoroutines.Start(ClientRegisterTimeout());
            return;
        }

        RegisterBackpackUnlockables(__instance);
    }

    /// <summary>
    /// Called when a client receives config from the host; registers unlockables using synced config.
    /// </summary>
    private static void OnClientConfigSynced()
    {
        if (_clientRegistrationDone || _pendingLevelManager == null)
            return;
        RegisterBackpackUnlockables(_pendingLevelManager);
        _clientRegistrationDone = true;
        _pendingLevelManager = null;
    }

    /// <summary>
    /// If sync never arrives, register after timeout using local config so the client at least sees backpack unlockables.
    /// </summary>
    private static IEnumerator ClientRegisterTimeout()
    {
        const float timeoutSeconds = 11f;
        yield return new WaitForSeconds(timeoutSeconds);
        if (_clientRegistrationDone || _pendingLevelManager == null)
            yield break;
        ModLogger.Warn("Config sync from host did not arrive in time; registering backpack unlockables with local config.");
        RegisterBackpackUnlockables(_pendingLevelManager);
        _clientRegistrationDone = true;
        _pendingLevelManager = null;
    }

    /// <summary>
    /// Registers all enabled backpack tiers as unlockables with the current <see cref="Configuration"/>.
    /// Call after config is ready (on host/single-player from Awake; on client after sync or timeout).
    /// </summary>
    public static void RegisterBackpackUnlockables(LevelManager levelManager)
    {
        if (levelManager == null)
            return;
        if (!TryLoadTexture(FallbackIconResourceName, out var fallbackTexture))
        {
            ModLogger.Error("Failed to load fallback backpack icon texture.");
            return;
        }

        var fallbackSprite = Sprite.Create(
            fallbackTexture,
            new Rect(0, 0, fallbackTexture.width, fallbackTexture.height),
            new Vector2(0.5f, 0.5f)
        );

        var cfg = Configuration.Instance;
        for (var i = 0; i < Configuration.BackpackTiers.Length; i++)
        {
            if (!cfg.TierEnabled[i])
                continue;
            var tier = Configuration.BackpackTiers[i];
            var sprite = GetTierSprite(i, fallbackSprite, fallbackTexture);
            var unlockable = new Unlockable(cfg.TierUnlockRanks[i], tier.Name, sprite);
            levelManager.AddUnlockable(unlockable);
        }
    }

    /// <summary>
    /// Gets the sprite for a backpack tier (per-tier icon, or fallback if load fails).
    /// </summary>
    private static Sprite GetTierSprite(int tierIndex, Sprite fallbackSprite, Texture2D fallbackTexture)
    {
        if (tierIndex < 0 || tierIndex >= TierIconResourceNames.Length)
            return fallbackSprite;
        if (!TryLoadTexture(TierIconResourceNames[tierIndex], out var texture))
            return fallbackSprite;
        return Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f)
        );
    }

    /// <summary>
    /// Loads a <see cref="Texture2D"/> from an embedded assembly resource.
    /// </summary>
    private static bool TryLoadTexture(string resourceName, out Texture2D texture)
    {
        texture = null;
        using var resourceStream = typeof(PackRat).Assembly.GetManifestResourceStream(resourceName);
        if (resourceStream == null)
        {
            ModLogger.Error($"Failed to find embedded resource: {resourceName}");
            return false;
        }

        var buffer = new byte[resourceStream.Length];
        resourceStream.Read(buffer, 0, buffer.Length);
        texture = new Texture2D(0, 0);
        texture.filterMode = FilterMode.Point;
        texture.LoadImage(buffer);
        return true;
    }
}
