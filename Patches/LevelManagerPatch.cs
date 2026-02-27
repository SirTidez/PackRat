using System.Collections;
using System.IO;
using System.Reflection;
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
/// Registers backpack tiers as level-up unlockables so the player sees when each tier becomes
/// available to purchase at the hardware store (rank requirement is informational).
/// </summary>
[HarmonyPatch(typeof(LevelManager))]
public static class LevelManagerPatch
{
    private const string FallbackIconResourceName = "PackRat.assets.icon.png";

    /// <summary>
    /// Display suffix for level-up screen so the player knows where to buy.
    /// </summary>
    private const string HardwareStoreSuffix = " (Hardware Store)";

    /// <summary>
    /// Embedded resource names for each backpack tier icon (same order as <see cref="Configuration.BackpackTiers"/>).
    /// Used by shop integration and level-up unlockables.
    /// </summary>
    private static readonly string[] TierIconResourceNames =
    [
        "PackRat.assets.rucksack.png",
        "PackRat.assets.smallpack.png",
        "PackRat.assets.duffelbag.png",
        "PackRat.assets.tacticalpack.png",
        "PackRat.assets.hikingbackpack.png",
    ];

    private static LevelManager _pendingLevelManager;
    private static bool _clientRegistrationDone;
    private static bool _clientSubscribed;
    private static bool _resourceDiagnosticsLogged;

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
    /// If sync never arrives, register after timeout using local config.
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
    /// Registers enabled backpack tiers as unlockables so the player sees at which level each tier
    /// becomes available to purchase at the hardware store.
    /// </summary>
    private static void RegisterBackpackUnlockables(LevelManager levelManager)
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
            var displayName = tier.Name + HardwareStoreSuffix;
            var unlockable = new Unlockable(cfg.TierUnlockRanks[i], displayName, sprite);
            levelManager.AddUnlockable(unlockable);
        }
    }

    /// <summary>
    /// Tries to load the fallback backpack icon. Used by shop integration when creating tier listings.
    /// </summary>
    public static bool TryGetFallbackIcon(out Texture2D texture, out Sprite sprite)
    {
        sprite = null;
        if (!TryLoadTexture(FallbackIconResourceName, out texture) || texture == null)
            return false;
        sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        return true;
    }

    /// <summary>
    /// Gets the sprite for a backpack tier (per-tier icon, or fallback if load fails).
    /// Used by shop integration.
    /// </summary>
    public static Sprite GetTierSprite(int tierIndex, Sprite fallbackSprite, Texture2D fallbackTexture)
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
        if (!TryGetResourceStream(resourceName, out var resourceStream))
        {
            if (!_resourceDiagnosticsLogged)
            {
                _resourceDiagnosticsLogged = true;
                LogResourceDiagnostics();
            }

            ModLogger.Error($"Failed to find embedded resource: {resourceName}");
            return false;
        }

        using (resourceStream)
        {
            var buffer = new byte[resourceStream.Length];
            resourceStream.Read(buffer, 0, buffer.Length);
            texture = new Texture2D(0, 0);
            texture.filterMode = FilterMode.Point;
            texture.LoadImage(buffer);
            return true;
        }
    }

    private static bool TryGetResourceStream(string resourceName, out Stream stream)
    {
        stream = null;
        if (string.IsNullOrEmpty(resourceName))
            return false;

        var modAsm = typeof(PackRat).Assembly;
        stream = modAsm.GetManifestResourceStream(resourceName);
        if (stream != null)
            return true;

        var loaded = AppDomain.CurrentDomain.GetAssemblies();
        for (var i = 0; i < loaded.Length; i++)
        {
            var asm = loaded[i];
            if (asm == null)
                continue;

            try
            {
                var names = asm.GetManifestResourceNames();
                for (var n = 0; n < names.Length; n++)
                {
                    var name = names[n];
                    if (!string.Equals(name, resourceName, StringComparison.Ordinal))
                        continue;

                    stream = asm.GetManifestResourceStream(name);
                    if (stream != null)
                    {
                        ModLogger.Warn($"Resolved resource '{resourceName}' from assembly '{asm.GetName().Name}'.");
                        return true;
                    }
                }
            }
            catch
            {
            }
        }

        return false;
    }

    private static void LogResourceDiagnostics()
    {
        try
        {
            var modAsm = typeof(PackRat).Assembly;
            var modNames = modAsm.GetManifestResourceNames();
            ModLogger.Warn($"PackRat assembly '{modAsm.FullName}' resource count: {modNames.Length}");
            for (var i = 0; i < modNames.Length; i++)
                ModLogger.Warn($"PackRat resource: {modNames[i]}");

            var loaded = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < loaded.Length; i++)
            {
                var asm = loaded[i];
                if (asm == null)
                    continue;

                string[] names;
                try
                {
                    names = asm.GetManifestResourceNames();
                }
                catch
                {
                    continue;
                }

                if (names == null || names.Length == 0)
                    continue;

                for (var n = 0; n < names.Length; n++)
                {
                    if (names[n].IndexOf("PackRat.assets", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    ModLogger.Warn($"Found candidate resource '{names[n]}' in assembly '{asm.GetName().Name}'.");
                }
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("LevelManagerPatch: resource diagnostics failed", ex);
        }
    }
}
