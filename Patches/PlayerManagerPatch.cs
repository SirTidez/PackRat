using HarmonyLib;
using PackRat.Helpers;

#if MONO
using ScheduleOne.Persistence.Datas;
using ScheduleOne.Persistence.Loaders;
using ScheduleOne.PlayerScripts;
#else
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.Persistence.Loaders;
using Il2CppScheduleOne.PlayerScripts;
#endif

namespace PackRat.Patches;

/// <summary>
/// Harmony patches for <see cref="PlayerManager"/>.
/// Appends backpack save data to the inventory string so it is transmitted to connecting players.
/// </summary>
[HarmonyPatch(typeof(PlayerManager))]
public static class PlayerManagerPatch
{
    [HarmonyPatch("TryGetPlayerData")]
    [HarmonyPostfix]
    public static void TryGetPlayerData(PlayerManager __instance, PlayerData data, ref string inventoryString)
    {
        if (data == null)
            return;

        var index = __instance.loadedPlayerData.IndexOf(data);
        if (index < 0 || index >= __instance.loadedPlayerDataPaths.Count)
        {
            ModLogger.Warn("Failed to resolve loaded player data path for backpack sync payload.");
            return;
        }

#if !MONO
        var dataPath = (Il2CppSystem.String)__instance.loadedPlayerDataPaths[new Index(index)];
#else
        var dataPath = __instance.loadedPlayerDataPaths[index];
#endif
        var loader = new PlayerLoader();
        if (!loader.TryLoadFile(dataPath, "Backpack", out var backpackString))
        {
            ModLogger.Warn($"Failed to load player backpack data under: {dataPath}");
            return;
        }

        inventoryString += "|||" + backpackString;
    }
}
