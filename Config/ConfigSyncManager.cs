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
    private static readonly string ModVersion = typeof(PackRat).Assembly.GetName().Version?.ToString() ?? "1.0.0";

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
        payload.Append($"{Configuration.Instance.UnlockLevel.Rank}:{Configuration.Instance.UnlockLevel.Tier},");
        payload.Append($"{Configuration.Instance.EnableSearch},");
        payload.Append($"{Configuration.Instance.StorageSlots},");
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
        if (parts.Length < 2)
        {
            ModLogger.Warn($"Invalid config payload format: {payload}");
            return;
        }

        if (parts[0] != ModVersion)
        {
            ModLogger.Warn($"Mod version mismatch: host={parts[0]}, local={ModVersion}");
            return;
        }

        var unlockLevel = parts[1].Split(':');
        if (unlockLevel.Length != 2 || !Enum.TryParse(unlockLevel[0], out ERank rank) || !int.TryParse(unlockLevel[1], out var tier))
        {
            ModLogger.Warn($"Invalid unlock level format in payload: {parts[1]}");
            return;
        }

        Configuration.Instance.UnlockLevel = new FullRank(rank, tier);
        Configuration.Instance.EnableSearch = bool.Parse(parts[2]);
        Configuration.Instance.StorageSlots = int.Parse(parts[3]);
        ModLogger.Info("Config synced from host successfully.");
    }
}
