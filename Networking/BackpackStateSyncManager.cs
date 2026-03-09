using System.Collections;
using System.Text;
using PackRat.Config;
using PackRat.Extensions;
using PackRat.Helpers;
using PackRat.Storage;
using UnityEngine;

#if MONO
using FishNet;
using ScheduleOne.Networking;
using ScheduleOne.Persistence;
using ScheduleOne.Persistence.Datas;
using ScheduleOne.Persistence.Loaders;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Variables;
#else
using Il2CppFishNet;
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.Persistence.Loaders;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Variables;
#endif

namespace PackRat.Networking;

/// <summary>
/// Host-driven backpack state sync for multiplayer saves.
/// The host broadcasts a save-sync request; clients respond with chunked backpack snapshots.
/// </summary>
public static class BackpackStateSyncManager
{
    private const string SyncVariableName = "PackRat_BackpackSync";
    private const string RequestVerb = "REQ";
    private const string ResponseVerb = "RES";
    private const string PullRequestVerb = "PULL_REQ";
    private const string PullResponseVerb = "PULL_RES";
    private const int ChunkSize = 700;

    private static readonly Dictionary<string, BackpackSaveData> _latestSnapshotsByPlayerKey = new Dictionary<string, BackpackSaveData>();
    private static readonly Dictionary<string, PendingResponse> _pendingResponses = new Dictionary<string, PendingResponse>();
    private static readonly HashSet<string> _pendingPlayers = new HashSet<string>();
    private static readonly Dictionary<string, string> _playerKeyAliases = new Dictionary<string, string>();
    private static readonly HashSet<string> _unknownResponseKeysLogged = new HashSet<string>();
    private static readonly List<string> _lastRequestedPlayers = new List<string>();
    private static readonly List<string> _lastReceivedPlayers = new List<string>();
    private static readonly List<string> _lastTimedOutPlayers = new List<string>();

    private static string _activePullNonce;
    private static string _activePullTargetKey;
    private static PendingResponse _pendingPullResponse;
    private static BackpackSaveData _pendingHostSnapshotForLocalPlayer;
    private static float _lastPullRequestTime;

    private static string _activeNonce;
    private static float _syncStartTime;
    private static float _syncTimeoutSeconds;
    private static bool _syncInProgress;

    private sealed class PendingResponse
    {
        public int ChunkCount;
        public string[] Chunks;
        public int ReceivedCount;
    }

    public static bool BeginHostSaveSync(float timeoutSeconds = 5f)
    {
        if (Lobby.Instance == null || !Lobby.Instance.IsHost)
            return false;

        _pendingResponses.Clear();
        _pendingPlayers.Clear();
        _playerKeyAliases.Clear();
        _unknownResponseKeysLogged.Clear();
        _lastRequestedPlayers.Clear();
        _lastReceivedPlayers.Clear();
        _lastTimedOutPlayers.Clear();

        var players = Player.PlayerList;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player == null || player.Owner == null || player.Owner.IsLocalClient)
                continue;

            var playerKey = GetPlayerIdentityKey(player);
            if (string.IsNullOrEmpty(playerKey))
                continue;

            _pendingPlayers.Add(playerKey);
            _lastRequestedPlayers.Add(playerKey);
            RegisterPlayerAlias(playerKey, playerKey);
            RegisterPlayerAlias(playerKey, player.PlayerCode);
            RegisterPlayerAlias(playerKey, player.SaveFolderName);
        }

        if (_pendingPlayers.Count == 0)
            return false;

        _activeNonce = Guid.NewGuid().ToString("N");
        _syncStartTime = Time.unscaledTime;
        _syncTimeoutSeconds = Mathf.Max(0.25f, timeoutSeconds);
        _syncInProgress = true;

        if (!SendSyncMessage($"{RequestVerb}|{_activeNonce}"))
        {
            _syncInProgress = false;
            return false;
        }

        DebugLog($"Backpack sync: requesting {_pendingPlayers.Count} client snapshots (nonce={_activeNonce}).");
        DebugLog($"Backpack sync targets: {string.Join(", ", _lastRequestedPlayers)}");
        return true;
    }

    public static IEnumerator WaitForHostSaveSync()
    {
        if (!_syncInProgress)
            yield break;

        while (_syncInProgress && _pendingPlayers.Count > 0)
        {
            if (Time.unscaledTime - _syncStartTime >= _syncTimeoutSeconds)
            {
                _lastTimedOutPlayers.Clear();
                _lastTimedOutPlayers.AddRange(_pendingPlayers);
                ModLogger.Warn($"Backpack sync timeout. Missing responses from: {string.Join(", ", _pendingPlayers)}");
                _syncInProgress = false;
                break;
            }

            yield return null;
        }

        _syncInProgress = false;
        LogLastSyncSummary();
    }

    public static string GetLastSyncSummary()
    {
        if (_lastRequestedPlayers.Count == 0)
            return "Backpack sync summary: no remote players requested.";

        return $"Backpack sync summary: requested={_lastRequestedPlayers.Count}, received={_lastReceivedPlayers.Count}, timedOut={_lastTimedOutPlayers.Count}.";
    }

    public static bool TryGetLatestSnapshotForPlayer(Player player, out BackpackSaveData snapshot)
    {
        snapshot = null;
        if (player == null)
            return false;

        var key = GetPlayerIdentityKey(player);
        if (!string.IsNullOrEmpty(key) && _latestSnapshotsByPlayerKey.TryGetValue(key, out snapshot) && snapshot != null)
            return true;

        var fallbackKey = NormalizePlayerKey(player.PlayerCode);
        return !string.IsNullOrEmpty(fallbackKey) && _latestSnapshotsByPlayerKey.TryGetValue(fallbackKey, out snapshot) && snapshot != null;
    }

    public static void RequestHostSnapshotForLocalPlayer(Player localPlayer = null)
    {
        if (Lobby.Instance == null || !Lobby.Instance.IsInLobby || Lobby.Instance.IsHost)
            return;

        if (Time.unscaledTime - _lastPullRequestTime < 0.5f)
            return;

        localPlayer ??= Player.Local;
        if (localPlayer == null)
            return;

        var targetKey = GetPlayerIdentityKey(localPlayer);
        if (string.IsNullOrEmpty(targetKey))
            return;

        var nonce = Guid.NewGuid().ToString("N");
        if (!SendSyncMessage($"{PullRequestVerb}|{nonce}|{targetKey}"))
            return;

        _activePullNonce = nonce;
        _activePullTargetKey = targetKey;
        _pendingPullResponse = null;
        _lastPullRequestTime = Time.unscaledTime;

        DebugLog($"Backpack pull: requesting host snapshot for {targetKey} (nonce={_activePullNonce}).");
    }

    public static bool TryGetPendingHostSnapshotForLocalPlayer(out BackpackSaveData snapshot)
    {
        snapshot = _pendingHostSnapshotForLocalPlayer;
        return snapshot != null;
    }

    public static void ClearPendingHostSnapshotForLocalPlayer()
    {
        _pendingHostSnapshotForLocalPlayer = null;
    }

    public static bool TryApplyPendingHostSnapshotToLocalPlayer(string source = "pending host snapshot")
    {
        if (_pendingHostSnapshotForLocalPlayer == null)
            return false;

        if (!ApplySnapshotToLocalPlayer(_pendingHostSnapshotForLocalPlayer, source))
            return false;

        _pendingHostSnapshotForLocalPlayer = null;
        return true;
    }

    /// <summary>
    /// Called by a VariableDatabase RPC patch when networked sync messages arrive.
    /// </summary>
    public static bool HandleVariableSyncMessage(string variableName, string value)
    {
        if (!string.Equals(variableName, SyncVariableName, StringComparison.Ordinal))
            return false;

        if (string.IsNullOrEmpty(value))
            return true;

        try
        {
            if (value.StartsWith(RequestVerb + "|", StringComparison.Ordinal))
            {
                HandleRequest(value);
                return true;
            }

            if (value.StartsWith(ResponseVerb + "|", StringComparison.Ordinal))
            {
                HandleResponse(value);
                return true;
            }

            if (value.StartsWith(PullRequestVerb + "|", StringComparison.Ordinal))
            {
                HandlePullRequest(value);
                return true;
            }

            if (value.StartsWith(PullResponseVerb + "|", StringComparison.Ordinal))
            {
                HandlePullResponse(value);
                return true;
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("Backpack sync message handling failed", ex);
        }

        return true;
    }

    private static void HandleRequest(string payload)
    {
        if (Lobby.Instance == null || !Lobby.Instance.IsInLobby || Lobby.Instance.IsHost)
            return;

        var parts = payload.Split(['|'], 2, StringSplitOptions.None);
        if (parts.Length < 2 || string.IsNullOrEmpty(parts[1]))
            return;

        var nonce = parts[1];
        var player = Player.Local;
        if (player == null)
            return;

        var playerKey = GetPlayerIdentityKey(player);
        if (string.IsNullOrEmpty(playerKey))
            return;

        var responseKey = NormalizePlayerKey(player.PlayerCode) ?? playerKey;

        DebugLog($"Backpack sync request received on client for nonce={nonce}.");

        var snapshot = BuildLocalSnapshot();
        var json = JsonHelper.SerializeObject(snapshot) ?? "";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var chunkCount = Math.Max(1, (int)Math.Ceiling(encoded.Length / (float)ChunkSize));

        for (var i = 0; i < chunkCount; i++)
        {
            var start = i * ChunkSize;
            var length = Math.Min(ChunkSize, encoded.Length - start);
            var chunk = encoded.Substring(start, length);
            _ = SendSyncMessage($"{ResponseVerb}|{nonce}|{responseKey}|{i}|{chunkCount}|{chunk}");
        }

        DebugLog($"Backpack sync response sent from {responseKey}: chunks={chunkCount}, bytes={encoded.Length}.");
    }

    private static BackpackSaveData BuildLocalSnapshot()
    {
        var contents = string.Empty;
        var tierIndex = -1;

        try
        {
            var localPlayer = Player.Local;
            if (localPlayer != null)
            {
                var storage = localPlayer.GetBackpackStorage();
                if (storage != null)
                    contents = new ItemSet(storage.ItemSlots).GetJSON();
            }

            if (PlayerBackpack.Instance != null)
                tierIndex = PlayerBackpack.Instance.HighestPurchasedTierIndex;
        }
        catch (Exception ex)
        {
            ModLogger.Error("Failed to build local backpack snapshot", ex);
        }

        return new BackpackSaveData
        {
            Contents = contents,
            HighestPurchasedTierIndex = tierIndex
        };
    }

    private static void HandleResponse(string payload)
    {
        if (!_syncInProgress || Lobby.Instance == null || !Lobby.Instance.IsHost)
            return;

        var parts = payload.Split(['|'], 6, StringSplitOptions.None);
        if (parts.Length < 6)
            return;

        var nonce = parts[1];
        var rawPlayerKey = parts[2];
        var playerKey = ResolvePendingPlayerKey(rawPlayerKey);
        if (!string.Equals(nonce, _activeNonce, StringComparison.Ordinal) || string.IsNullOrEmpty(playerKey) || !_pendingPlayers.Contains(playerKey))
        {
            if (string.Equals(nonce, _activeNonce, StringComparison.Ordinal)
                && !string.IsNullOrEmpty(rawPlayerKey)
                && _unknownResponseKeysLogged.Add(rawPlayerKey))
                DebugLog($"Backpack sync: unresolved response key '{rawPlayerKey}' for nonce={nonce}.");
            return;
        }

        if (!int.TryParse(parts[3], out var chunkIndex) || !int.TryParse(parts[4], out var chunkCount))
            return;

        if (chunkCount < 1 || chunkIndex < 0 || chunkIndex >= chunkCount)
            return;

        var chunkData = parts[5] ?? "";
        var key = nonce + "|" + playerKey;
        if (!_pendingResponses.TryGetValue(key, out var response))
        {
            response = new PendingResponse
            {
                ChunkCount = chunkCount,
                Chunks = new string[chunkCount],
                ReceivedCount = 0
            };
            _pendingResponses[key] = response;
        }

        if (response.ChunkCount != chunkCount)
            return;

        if (response.Chunks[chunkIndex] == null)
            response.ReceivedCount++;

        response.Chunks[chunkIndex] = chunkData;

        if (response.ReceivedCount < response.ChunkCount)
            return;

        var encoded = string.Concat(response.Chunks);
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var snapshot = JsonHelper.DeserializeObject<BackpackSaveData>(json);
            if (snapshot != null)
            {
                _latestSnapshotsByPlayerKey[playerKey] = snapshot;
                if (!_lastReceivedPlayers.Contains(playerKey))
                    _lastReceivedPlayers.Add(playerKey);
                DebugLog($"Backpack sync: received snapshot from {playerKey}.");
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Backpack sync: failed to decode snapshot from {playerKey}", ex);
        }

        _pendingResponses.Remove(key);
        _pendingPlayers.Remove(playerKey);

        if (_pendingPlayers.Count == 0)
        {
            DebugLog("Backpack sync: all client snapshots received.");
            _syncInProgress = false;
        }
    }

    private static void HandlePullRequest(string payload)
    {
        if (Lobby.Instance == null || !Lobby.Instance.IsInLobby || !Lobby.Instance.IsHost)
            return;

        var parts = payload.Split(['|'], 3, StringSplitOptions.None);
        if (parts.Length < 3)
            return;

        var nonce = parts[1];
        var requestedPlayerKey = parts[2];
        if (string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(requestedPlayerKey))
            return;

        if (!TryLoadHostSavedSnapshot(requestedPlayerKey, out var snapshot) || snapshot == null)
        {
            snapshot = new BackpackSaveData
            {
                Contents = string.Empty,
                HighestPurchasedTierIndex = -1
            };
            DebugLog($"Backpack pull: no saved snapshot found for {requestedPlayerKey}; returning empty snapshot.");
        }

        var json = JsonHelper.SerializeObject(snapshot) ?? string.Empty;
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var chunkCount = Math.Max(1, (int)Math.Ceiling(encoded.Length / (float)ChunkSize));

        for (var i = 0; i < chunkCount; i++)
        {
            var start = i * ChunkSize;
            var length = Math.Min(ChunkSize, encoded.Length - start);
            var chunk = encoded.Substring(start, length);
            _ = SendSyncMessage($"{PullResponseVerb}|{nonce}|{requestedPlayerKey}|{i}|{chunkCount}|{chunk}");
        }

        DebugLog($"Backpack pull: sent host snapshot for {requestedPlayerKey}, chunks={chunkCount}, bytes={encoded.Length}.");
    }

    private static void HandlePullResponse(string payload)
    {
        if (Lobby.Instance == null || !Lobby.Instance.IsInLobby || Lobby.Instance.IsHost)
            return;

        var parts = payload.Split(['|'], 6, StringSplitOptions.None);
        if (parts.Length < 6)
            return;

        var nonce = parts[1];
        var targetPlayerKey = parts[2];
        if (string.IsNullOrEmpty(nonce) || !string.Equals(nonce, _activePullNonce, StringComparison.Ordinal))
            return;

        if (!IsLocalTargetKey(targetPlayerKey))
            return;

        if (!int.TryParse(parts[3], out var chunkIndex) || !int.TryParse(parts[4], out var chunkCount))
            return;

        if (chunkCount < 1 || chunkIndex < 0 || chunkIndex >= chunkCount)
            return;

        var chunkData = parts[5] ?? string.Empty;
        if (_pendingPullResponse == null || _pendingPullResponse.ChunkCount != chunkCount)
        {
            _pendingPullResponse = new PendingResponse
            {
                ChunkCount = chunkCount,
                Chunks = new string[chunkCount],
                ReceivedCount = 0
            };
        }

        if (_pendingPullResponse.Chunks[chunkIndex] == null)
            _pendingPullResponse.ReceivedCount++;

        _pendingPullResponse.Chunks[chunkIndex] = chunkData;

        if (_pendingPullResponse.ReceivedCount < _pendingPullResponse.ChunkCount)
            return;

        try
        {
            var encoded = string.Concat(_pendingPullResponse.Chunks);
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var snapshot = JsonHelper.DeserializeObject<BackpackSaveData>(json);
            if (snapshot != null)
            {
                _pendingHostSnapshotForLocalPlayer = snapshot;
                DebugLog($"Backpack pull: received host snapshot for {_activePullTargetKey}.");
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("Backpack pull: failed to decode host snapshot", ex);
        }

        _pendingPullResponse = null;
        _activePullNonce = null;
    }

    private static bool TryLoadHostSavedSnapshot(string requestedPlayerKey, out BackpackSaveData snapshot)
    {
        snapshot = null;

        var manager = PlayerManager.Instance;
        if (manager == null || string.IsNullOrWhiteSpace(requestedPlayerKey))
            return false;

        var candidates = BuildPlayerCodeCandidates(requestedPlayerKey);
        for (var i = 0; i < candidates.Count; i++)
        {
            var playerCode = candidates[i];
            if (string.IsNullOrEmpty(playerCode))
                continue;

            if (!manager.TryGetPlayerData(playerCode, out var data, out var inventoryString, out _, out _, out _))
                continue;

            if (TryExtractSnapshotFromInventoryString(inventoryString, out snapshot))
                return true;

            if (TryLoadBackpackSubfileForData(manager, data, out var backpackString)
                && TryExtractSnapshotFromBackpackString(backpackString, out snapshot))
                return true;
        }

        var normalizedKey = NormalizePlayerKey(requestedPlayerKey);
        if (!string.IsNullOrEmpty(normalizedKey) && _latestSnapshotsByPlayerKey.TryGetValue(normalizedKey, out snapshot) && snapshot != null)
            return true;

        if (TryLoadBackpackByPathSearch(manager, requestedPlayerKey, out snapshot))
            return true;

        return false;
    }

    private static List<string> BuildPlayerCodeCandidates(string requestedPlayerKey)
    {
        var result = new List<string>();
        AddCandidate(result, requestedPlayerKey);

        var normalized = NormalizePlayerKey(requestedPlayerKey);
        if (string.IsNullOrEmpty(normalized))
            return result;

        AddCandidate(result, normalized);
        if (normalized.StartsWith("player_", StringComparison.Ordinal))
            AddCandidate(result, normalized.Substring("player_".Length));
        else
            AddCandidate(result, "player_" + normalized);

        return result;
    }

    private static void AddCandidate(List<string> candidates, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return;

        for (var i = 0; i < candidates.Count; i++)
        {
            if (string.Equals(candidates[i], candidate, StringComparison.OrdinalIgnoreCase))
                return;
        }

        candidates.Add(candidate);
    }

    private static bool TryExtractSnapshotFromInventoryString(string inventoryString, out BackpackSaveData snapshot)
    {
        snapshot = null;
        if (string.IsNullOrEmpty(inventoryString))
            return false;

        var split = inventoryString.Split(["|||"], StringSplitOptions.None);
        if (split.Length < 2)
            return false;

        return TryExtractSnapshotFromBackpackString(split[^1], out snapshot);
    }

    private static bool TryExtractSnapshotFromBackpackString(string backpackString, out BackpackSaveData snapshot)
    {
        snapshot = null;
        if (string.IsNullOrEmpty(backpackString))
            return false;

        snapshot = JsonHelper.DeserializeObject<BackpackSaveData>(backpackString);
        if (snapshot != null && snapshot.Contents != null)
            return true;

        snapshot = new BackpackSaveData
        {
            Contents = backpackString,
            HighestPurchasedTierIndex = -1
        };
        return true;
    }

    private static bool TryLoadBackpackSubfileForData(PlayerManager manager, PlayerData data, out string backpackString)
    {
        backpackString = null;
        if (manager == null || data == null)
            return false;

        var index = manager.loadedPlayerData.IndexOf(data);
        if (index < 0)
            return false;

#if !MONO
        var dataPath = manager.loadedPlayerDataPaths[new Index(index)].ToString();
#else
        var dataPath = manager.loadedPlayerDataPaths[index];
#endif
        if (string.IsNullOrEmpty(dataPath))
            return false;

        var loader = new PlayerLoader();
        return loader.TryLoadFile(dataPath, "Backpack", out backpackString);
    }

    private static bool TryLoadBackpackByPathSearch(PlayerManager manager, string requestedPlayerKey, out BackpackSaveData snapshot)
    {
        snapshot = null;
        if (manager == null)
            return false;

        var tokens = BuildPlayerCodeCandidates(requestedPlayerKey);
        if (tokens.Count == 0)
            return false;

        var pathsCount = manager.loadedPlayerDataPaths.Count;
        var loader = new PlayerLoader();
        for (var i = 0; i < pathsCount; i++)
        {
#if !MONO
            var dataPath = manager.loadedPlayerDataPaths[new Index(i)].ToString();
#else
            var dataPath = manager.loadedPlayerDataPaths[i];
#endif
            if (string.IsNullOrEmpty(dataPath))
                continue;

            var normalizedPath = dataPath.ToLowerInvariant();
            var matches = false;
            for (var t = 0; t < tokens.Count; t++)
            {
                var token = tokens[t];
                if (string.IsNullOrEmpty(token))
                    continue;

                if (normalizedPath.Contains(token.ToLowerInvariant()))
                {
                    matches = true;
                    break;
                }
            }

            if (!matches)
                continue;

            if (!loader.TryLoadFile(dataPath, "Backpack", out var backpackString))
                continue;

            if (TryExtractSnapshotFromBackpackString(backpackString, out snapshot))
                return true;
        }

        return false;
    }

    private static bool ApplySnapshotToLocalPlayer(BackpackSaveData snapshot, string source)
    {
        if (snapshot == null)
            return false;

        try
        {
            var localPlayer = Player.Local;
            if (localPlayer == null)
                return false;

            var backpackStorage = localPlayer.GetBackpackStorage();
            if (backpackStorage == null)
                return false;

            var contents = snapshot.Contents ?? string.Empty;
            if (!ItemSet.TryDeserialize(contents, out var itemSet))
            {
                ModLogger.Warn($"Backpack pull: failed to deserialize snapshot contents from {source}.");
                return false;
            }

            itemSet.LoadTo(backpackStorage.ItemSlots);
            var backpack = PlayerBackpack.Instance;
            if (backpack == null)
            {
                var localGameObject = localPlayer.LocalGameObject != null ? localPlayer.LocalGameObject : localPlayer.gameObject;
                backpack = Utils.GetComponentSafe<PlayerBackpack>(localGameObject);
            }

            if (backpack != null)
                backpack.SetHighestPurchasedTierIndex(snapshot.HighestPurchasedTierIndex);

            DebugLog($"Backpack pull: applied snapshot from {source}.");
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Backpack pull: failed to apply snapshot from {source}", ex);
            return false;
        }
    }

    private static bool IsLocalTargetKey(string targetPlayerKey)
    {
        var normalizedTarget = NormalizePlayerKey(targetPlayerKey);
        if (string.IsNullOrEmpty(normalizedTarget))
            return false;

        var localPlayer = Player.Local;
        if (localPlayer == null)
            return false;

        var localIdentity = GetPlayerIdentityKey(localPlayer);
        var localPlayerCode = NormalizePlayerKey(localPlayer.PlayerCode);

        if (string.Equals(normalizedTarget, localIdentity, StringComparison.Ordinal)
            || string.Equals(normalizedTarget, localPlayerCode, StringComparison.Ordinal))
            return true;

        if (normalizedTarget.StartsWith("player_", StringComparison.Ordinal))
        {
            var stripped = NormalizePlayerKey(normalizedTarget.Substring("player_".Length));
            return string.Equals(stripped, localIdentity, StringComparison.Ordinal)
                || string.Equals(stripped, localPlayerCode, StringComparison.Ordinal);
        }

        return string.Equals("player_" + normalizedTarget, localIdentity, StringComparison.Ordinal)
            || string.Equals("player_" + normalizedTarget, localPlayerCode, StringComparison.Ordinal);
    }

    private static string GetPlayerIdentityKey(Player player)
    {
        if (player == null)
            return null;

        var saveFolderName = NormalizePlayerKey(player.SaveFolderName);
        if (!string.IsNullOrEmpty(saveFolderName))
            return saveFolderName;

        return NormalizePlayerKey(player.PlayerCode);
    }

    private static string NormalizePlayerKey(string key)
    {
        return string.IsNullOrWhiteSpace(key) ? null : key.Trim().ToLowerInvariant();
    }

    private static void RegisterPlayerAlias(string canonicalKey, string alias)
    {
        if (string.IsNullOrEmpty(canonicalKey))
            return;

        var normalizedAlias = NormalizePlayerKey(alias);
        if (string.IsNullOrEmpty(normalizedAlias))
            return;

        _playerKeyAliases[normalizedAlias] = canonicalKey;

        if (normalizedAlias.StartsWith("player_", StringComparison.Ordinal))
        {
            var stripped = NormalizePlayerKey(normalizedAlias.Substring("player_".Length));
            if (!string.IsNullOrEmpty(stripped))
                _playerKeyAliases[stripped] = canonicalKey;
        }
        else
        {
            var prefixed = "player_" + normalizedAlias;
            _playerKeyAliases[prefixed] = canonicalKey;
        }
    }

    private static string ResolvePendingPlayerKey(string playerKeyOrAlias)
    {
        var normalized = NormalizePlayerKey(playerKeyOrAlias);
        if (string.IsNullOrEmpty(normalized))
            return null;

        if (_pendingPlayers.Contains(normalized))
            return normalized;

        if (_playerKeyAliases.TryGetValue(normalized, out var resolved))
            return resolved;

        if (normalized.StartsWith("player_", StringComparison.Ordinal))
        {
            var stripped = NormalizePlayerKey(normalized.Substring("player_".Length));
            if (!string.IsNullOrEmpty(stripped) && _playerKeyAliases.TryGetValue(stripped, out resolved))
                return resolved;
        }
        else
        {
            var prefixed = "player_" + normalized;
            if (_playerKeyAliases.TryGetValue(prefixed, out resolved))
                return resolved;
        }

        return null;
    }

    private static void LogLastSyncSummary()
    {
        DebugLog(GetLastSyncSummary());
        if (_lastReceivedPlayers.Count > 0)
            DebugLog($"Backpack sync received players: {string.Join(", ", _lastReceivedPlayers)}");
        if (_lastTimedOutPlayers.Count > 0)
            DebugLog($"Backpack sync timed out players: {string.Join(", ", _lastTimedOutPlayers)}");
    }

    private static void DebugLog(string message)
    {
        if (!Configuration.Instance.BackpackSyncDebugLogging)
            return;

        ModLogger.Debug(message);
    }

    private static bool SendSyncMessage(string payload)
    {
        if (string.IsNullOrEmpty(payload))
            return false;

        var variableDatabase = VariableDatabase.Instance;
        if (variableDatabase == null)
            return false;

        variableDatabase.SendValue(null, SyncVariableName, payload);
        return true;
    }
}
