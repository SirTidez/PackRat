using UnityEngine;

#if !MONO
using Il2CppScheduleOne.PlayerScripts;
using Il2CppFishNet.Object;
#else
using ScheduleOne.PlayerScripts;
using FishNet.Object;
#endif

namespace PackRat.Helpers;

/// <summary>
/// Helper utilities for FishNet networking operations.
/// Provides safe, consistent methods for network ID handling.
/// </summary>
public static class NetworkHelper
{
    /// <summary>
    /// Safely gets the network ObjectId from a Player.
    /// Returns -1 if Player or NetworkObject is null.
    /// </summary>
    public static int GetPlayerNetworkId(Player player)
    {
        if (player == null)
        {
            ModLogger.Warn("Cannot get network ID - player is null");
            return -1;
        }

        if (player.NetworkObject == null)
        {
            ModLogger.Warn($"Cannot get network ID - player {player.name} has null NetworkObject");
            return -1;
        }

        return player.NetworkObject.ObjectId;
    }

    /// <summary>
    /// Safely gets the network ObjectId as a string from a Player.
    /// Returns empty string if Player or NetworkObject is null.
    /// </summary>
    public static string GetPlayerNetworkIdString(Player player)
    {
        int objectId = GetPlayerNetworkId(player);
        if (objectId == -1)
        {
            return "";
        }

        return objectId.ToString();
    }

    /// <summary>
    /// Checks if a player has a valid NetworkObject.
    /// </summary>
    public static bool HasValidNetworkObject(Player player)
    {
        return player != null && player.NetworkObject != null;
    }
}
