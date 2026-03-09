using System.Collections;
using HarmonyLib;
using MelonLoader;
using PackRat.Helpers;
using PackRat.Networking;

#if MONO
using ScheduleOne.Persistence;
#else
using Il2CppScheduleOne.Persistence;
#endif

namespace PackRat.Patches;

/// <summary>
/// Ensures host collects fresh client backpack snapshots right before save.
/// </summary>
[HarmonyPatch(typeof(SaveManager))]
public static class SaveManagerPatch
{
    private static bool _allowOriginalSave;

    [HarmonyPatch("Save", typeof(string))]
    [HarmonyPrefix]
    public static bool Save(SaveManager __instance, string saveFolderPath)
    {
        if (_allowOriginalSave)
        {
            _allowOriginalSave = false;
            return true;
        }

        if (!BackpackStateSyncManager.BeginHostSaveSync())
            return true;

        MelonCoroutines.Start(WaitForBackpackSyncThenSave(__instance, saveFolderPath));
        return false;
    }

    private static IEnumerator WaitForBackpackSyncThenSave(SaveManager saveManager, string saveFolderPath)
    {
        yield return BackpackStateSyncManager.WaitForHostSaveSync();

        if (saveManager == null)
            yield break;

        try
        {
            _allowOriginalSave = true;
            saveManager.Save(saveFolderPath);
        }
        catch (Exception ex)
        {
            _allowOriginalSave = false;
            ModLogger.Error("Failed to continue save after backpack sync", ex);
        }
    }
}
