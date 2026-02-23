using System.Collections;
using HarmonyLib;
using MelonLoader;
using PackRat.Config;
using PackRat.Helpers;
using UnityEngine;

#if MONO
using ScheduleOne.NPCs.Behaviour;
using ScheduleOne.UI;
#else
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.UI;
#endif

namespace PackRat.Patches;

/// <summary>
/// Harmony patches for <see cref="BodySearchBehaviour"/>.
/// When backpack search is enabled, intercepts police searches to include backpack contents.
/// </summary>
[HarmonyPatch(typeof(BodySearchBehaviour))]
public static class BodySearchBehaviourPatch
{
    [HarmonyPatch("SearchClean")]
    [HarmonyPrefix]
    public static bool SearchClean(BodySearchBehaviour __instance)
    {
        if (!PlayerBackpack.Instance.IsUnlocked || !Configuration.Instance.EnableSearch)
            return true;

#if !MONO
        BodySearchScreen.Instance.onSearchClear.RemoveListener(new Action(__instance.SearchClean));
        BodySearchScreen.Instance.onSearchFail.RemoveListener(new Action(__instance.SearchFail));
#else
        BodySearchScreen.Instance.onSearchClear.RemoveListener(__instance.SearchClean);
        BodySearchScreen.Instance.onSearchFail.RemoveListener(__instance.SearchFail);
#endif

        // Prevent the inventory search screen from reopening.
        BodySearchScreen.Instance.IsOpen = true;
        MelonCoroutines.Start(CheckForItems(__instance));
        return false;
    }

    private static IEnumerator CheckForItems(BodySearchBehaviour behaviour)
    {
        // DialogueHandler is a property (PascalCase) in both Mono and IL2CPP current game versions.
        behaviour.officer.DialogueHandler.ShowWorldspaceDialogue("Hold on, let me see your backpack as well.", 5f);
        yield return new WaitForSeconds(3f);
        BodySearchScreen.Instance.IsOpen = false;
        behaviour.ConcludeSearch(!PlayerBackpack.Instance.ContainsItemsOfInterest(behaviour.MaxStealthLevel));
    }
}
