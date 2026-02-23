using HarmonyLib;
using PackRat.Config;
using PackRat.Helpers;
using UnityEngine;

#if MONO
using ScheduleOne.Levelling;
#else
using Il2CppScheduleOne.Levelling;
#endif

namespace PackRat.Patches;

/// <summary>
/// Harmony patches for <see cref="LevelManager"/>.
/// Registers the backpack as an unlockable at the configured rank.
/// </summary>
[HarmonyPatch(typeof(LevelManager))]
public static class LevelManagerPatch
{
    private const string IconResourceName = "PackRat.assets.icon.png";

    [HarmonyPatch("Awake")]
    [HarmonyPostfix]
    public static void Awake(LevelManager __instance)
    {
        if (__instance == null)
        {
            ModLogger.Error("LevelManager instance is null!");
            return;
        }

        if (!TryLoadTexture(IconResourceName, out var backpackIcon))
        {
            ModLogger.Error("Failed to load backpack icon texture.");
            return;
        }

        var backpackSprite = Sprite.Create(
            backpackIcon,
            new Rect(0, 0, backpackIcon.width, backpackIcon.height),
            new Vector2(0.5f, 0.5f)
        );

        var unlockable = new Unlockable(Configuration.Instance.UnlockLevel, "Backpack", backpackSprite);
        __instance.AddUnlockable(unlockable);
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
