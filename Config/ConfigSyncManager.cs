using System;
using System.Collections;
using System.Text;
using MelonLoader;
using PackRat.Helpers;

#if MONO
using ScheduleOne.Levelling;
using ScheduleOne.Networking;
using Steamworks;
#else
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Networking;
using Il2CppSteamworks;
#endif

namespace PackRat.Config;

/// <summary>
/// Handles synchronization of <see cref="Configuration"/> from host to clients via Steam lobby data.
/// </summary>
public static class ConfigSyncManager
{
    private const string Prefix = "PackRat_Config";
    private static readonly string ModVersion = typeof(PackRat).Assembly.GetName().Version?.ToString() ?? "1.0.1";

    /// <summary>
    /// Raised after a client successfully applies synced config from the host.
    /// Allows other systems (e.g. LevelManagerPatch) to register using host config instead of local defaults.
    /// </summary>
    public static event Action OnConfigSynced;

    /// <summary>
    /// Initiates config sync. The host writes lobby data; clients poll for it.
    /// </summary>
    public static void StartSync()
    {
        var isHost = Lobby.Instance.IsHost;
        var isClient = !isHost && Lobby.Instance.IsInLobby;
        if (isHost)
        {
            SyncToClients();
        }
        else if (isClient)
        {
            MelonCoroutines.Start(WaitForPayload());
        }
    }

    private static void SyncToClients()
    {
        var payload = new StringBuilder();
        payload.Append($"{ModVersion}[");
        for (var i = 0; i < Configuration.BackpackTiers.Length; i++)
        {
            var rank = Configuration.Instance.TierUnlockRanks[i];
            var slots = Configuration.Instance.TierSlotCounts[i];
            var enabled = Configuration.Instance.TierEnabled[i] ? 1 : 0;
            var price = Configuration.Instance.TierPrices[i];
            payload.Append($"{rank.Rank}:{rank.Tier}/{slots}/{enabled}/{price},");
        }
        payload.Append(']');

        ModLogger.Info($"Syncing config payload to clients: {payload}");
        Lobby.Instance.SetLobbyData(Prefix, payload.ToString());
    }

    private static IEnumerator WaitForPayload()
    {
        const int maxAttempts = 10;
        const float waitTime = 1f;
        for (var i = 0; i < maxAttempts; ++i)
        {
            var payload = SteamMatchmaking.GetLobbyData(Lobby.Instance.LobbySteamID, Prefix);
            if (string.IsNullOrEmpty(payload))
            {
                yield return new UnityEngine.WaitForSeconds(waitTime);
                continue;
            }

            ModLogger.Info($"Received config payload from host: {payload}");
            try
            {
                SyncFromHost(payload);
            }
            catch (Exception e)
            {
                ModLogger.Error($"Error while parsing config payload: {payload}", e);
            }

            yield break;
        }
    }

    private static void SyncFromHost(string payload)
    {
        var parts = payload.Split(['[', ']', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1 + Configuration.BackpackTiers.Length)
        {
            ModLogger.Warn($"Invalid config payload format: {payload}");
            return;
        }

        if (parts[0] != ModVersion)
        {
            ModLogger.Warn($"Mod version mismatch: host={parts[0]}, local={ModVersion}");
            return;
        }

        var newUnlockRanks = new FullRank[Configuration.BackpackTiers.Length];
        var newSlotCounts = new int[Configuration.BackpackTiers.Length];
        var newTierEnabled = new bool[Configuration.BackpackTiers.Length];
        var newTierPrices = new float[Configuration.BackpackTiers.Length];
        for (var i = 0; i < Configuration.BackpackTiers.Length; i++)
        {
            newTierPrices[i] = Configuration.Instance.TierPrices != null && i < Configuration.Instance.TierPrices.Length
                ? Configuration.Instance.TierPrices[i]
                : 25f + i * 50f;
        }

        for (var i = 0; i < Configuration.BackpackTiers.Length; i++)
        {
            var entry = parts[1 + i];
            var colonIdx = entry.IndexOf(':');
            var slash1Idx = entry.IndexOf('/');
            if (colonIdx < 0 || slash1Idx < 0 || slash1Idx <= colonIdx)
            {
                ModLogger.Warn($"Invalid tier entry format in payload: {entry}");
                return;
            }

            var slash2Idx = entry.IndexOf('/', slash1Idx + 1);
            var slash3Idx = slash2Idx >= 0 ? entry.IndexOf('/', slash2Idx + 1) : -1;
            var rankStr = entry[..colonIdx];
            var tierStr = entry[(colonIdx + 1)..slash1Idx];
            var slotsStr = slash2Idx >= 0 ? entry[(slash1Idx + 1)..slash2Idx] : entry[(slash1Idx + 1)..];
            var enabledStr = slash2Idx >= 0 ? (slash3Idx >= 0 ? entry[(slash2Idx + 1)..slash3Idx] : entry[(slash2Idx + 1)..]) : "1";
            var priceStr = slash3Idx >= 0 ? entry[(slash3Idx + 1)..] : null;

            if (!Enum.TryParse(rankStr, out ERank rank) || !int.TryParse(tierStr, out var tier) || !int.TryParse(slotsStr, out var slots))
            {
                ModLogger.Warn($"Failed to parse tier entry: {entry}");
                return;
            }

            newUnlockRanks[i] = new FullRank(rank, tier);
            newSlotCounts[i] = slots;
            newTierEnabled[i] = enabledStr == "1";
            if (float.TryParse(priceStr, out var price))
                newTierPrices[i] = Math.Max(0f, price);
        }

        Configuration.Instance.TierUnlockRanks = newUnlockRanks;
        Configuration.Instance.TierSlotCounts = newSlotCounts;
        Configuration.Instance.TierEnabled = newTierEnabled;
        Configuration.Instance.TierPrices = newTierPrices;
        ModLogger.Info("Config synced from host successfully.");
        OnConfigSynced?.Invoke();
    }
}
