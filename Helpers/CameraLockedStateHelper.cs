using System;
using System.Reflection;
using UnityEngine;

#if MONO
using ScheduleOne.AvatarFramework;
using ScheduleOne.AvatarFramework.Customization;
using ScheduleOne.DevUtilities;
using ScheduleOne.PlayerScripts;
using ScheduleOne.UI.ATM;
using ScheduleOne.UI.Shop;
using ScheduleOne.TV;
#else
using Il2CppScheduleOne.AvatarFramework;
using Il2CppScheduleOne.AvatarFramework.Customization;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI.ATM;
using Il2CppScheduleOne.UI.Shop;
using Il2CppScheduleOne.TV;
#endif

namespace PackRat.Helpers;

/// <summary>
/// Detects when the player is in a UI state that locks the camera (TV, ATM, dialogue, etc.).
/// Opening the backpack in these states causes cursor/camera glitches on close, so we block it.
/// </summary>
internal static class CameraLockedStateHelper
{
    // Cached type resolution - GetTypeByName is extremely expensive (scans all assemblies).
    // Resolve once, reuse forever.
    private static Type _dialogueManagerType;
    private static Type _dialogueHandlerType;
    private static FieldInfo _dialogueHandlerActiveDialogueField;
    private static FieldInfo _shopInterfaceAllShopsField;
    private static Type[] _dealWindowTypes;
    private static bool _typesResolved;

    private static void EnsureTypesResolved()
    {
        if (_typesResolved)
            return;

        _typesResolved = true;
        _dialogueManagerType = ReflectionUtils.GetTypeByName("ScheduleOne.Dialogue.DialogueManager");
        _dialogueHandlerType = ReflectionUtils.GetTypeByName("ScheduleOne.Dialogue.DialogueHandler");
        if (_dialogueHandlerType != null)
            _dialogueHandlerActiveDialogueField = _dialogueHandlerType.GetField("activeDialogue", BindingFlags.Public | BindingFlags.Static);

        var shopInterfaceType = typeof(ShopInterface);
        _shopInterfaceAllShopsField = shopInterfaceType.GetField("AllShops", BindingFlags.Public | BindingFlags.Static);

        var typeNames = new[] { "ScheduleOne.Economy.DealWindow", "ScheduleOne.UI.DealCanvas", "ScheduleOne.UI.FreeSampleCanvas", "ScheduleOne.Economy.DealInterface" };
        var list = new System.Collections.Generic.List<Type>();
        foreach (var name in typeNames)
        {
            var t = ReflectionUtils.GetTypeByName(name);
            if (t != null)
                list.Add(t);
        }
        _dealWindowTypes = list.ToArray();
    }

    /// <summary>
    /// Pre-resolves types in the background so the first keypress doesn't stall.
    /// Call once when the player spawns.
    /// </summary>
    public static void PrewarmCache()
    {
        EnsureTypesResolved();
    }

    /// <summary>
    /// Returns true if the player is currently in any UI that locks the camera.
    /// When true, the backpack should not be opened.
    /// </summary>
    public static bool IsCameraLockedByUI()
    {
        try
        {
            // Catch-all: when the cursor is visible, the game is in a "point-and-click" UI (dialogue, shop,
            // tattoo, ATM, etc.). Opening the backpack then can leave the cursor stuck or relocked when closed.
            if (Cursor.visible)
            {
                ModLogger.Debug("Camera locked: Cursor visible (UI with mouse).");
                return true;
            }

            if (IsATMOpen())
            {
                ModLogger.Debug("Camera locked: ATM screen open.");
                return true;
            }
            if (IsCharacterCreatorOpen())
            {
                ModLogger.Debug("Camera locked: Appearance/Character Creator open.");
                return true;
            }
            if (IsCharacterCustomizationShopOpen())
            {
                ModLogger.Debug("Camera locked: Tattoo/customization shop open.");
                return true;
            }
            if (IsTVInterfaceOpen())
            {
                ModLogger.Debug("Camera locked: TV screen open.");
                return true;
            }
            if (IsShopInterfaceOpen())
            {
                ModLogger.Debug("Camera locked: Shop screen open.");
                return true;
            }
            if (IsDialogueActive())
            {
                ModLogger.Debug("Camera locked: Dialogue active.");
                return true;
            }
            if (IsInVehicle())
            {
                ModLogger.Debug("Camera locked: Player in vehicle.");
                return true;
            }
            if (IsDealOrFreeSampleOpen())
            {
                ModLogger.Debug("Camera locked: Deal or free sample screen open.");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            ModLogger.Error("Error checking camera-locked UI state", ex);
            return false; // Fail open: allow backpack if we can't determine
        }
    }

    private static bool IsATMOpen()
    {
        try
        {
            var atm = Utils.FindObjectOfTypeSafe<ATMInterface>();
            if (atm == null)
                return false;

            var isOpen = ReflectionUtils.TryGetFieldOrProperty(atm, "isOpen");
            return isOpen is bool b && b;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsCharacterCreatorOpen()
    {
        try
        {
            // Singleton (e.g. main menu / appearance screen)
            if (Singleton<CharacterCreator>.InstanceExists)
            {
                var creator = Singleton<CharacterCreator>.Instance;
                if (creator != null && creator.IsOpen)
                    return true;
            }

            // Any CharacterCreator in scene (e.g. tattoo parlor, barber) - same camera-lock UI
            var allCreators = Utils.FindObjectsOfTypeSafe<CharacterCreator>();
            if (allCreators == null)
                return false;
            for (var i = 0; i < allCreators.Length; i++)
            {
                var c = allCreators[i];
                if (c != null && c.IsOpen)
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Tattoo parlor / barber / character customization shop (e.g. CharacterCustomizationShop).
    /// When the player is in the shop's customization UI, the shop has an active Canvas in hierarchy.
    /// </summary>
    private static bool IsCharacterCustomizationShopOpen()
    {
        try
        {
            var shops = Utils.FindObjectsOfTypeSafe<CharacterCustomizationShop>();
            if (shops == null)
                return false;

            for (var i = 0; i < shops.Length; i++)
            {
                var shop = shops[i];
                if (shop == null || !shop.gameObject.activeInHierarchy)
                    continue;

                var canvases = Utils.GetAllComponentsInChildrenRecursive<Canvas>(shop.gameObject);
                if (canvases == null)
                    continue;

                for (var j = 0; j < canvases.Count; j++)
                {
                    var canvas = canvases[j];
                    if (canvas != null && canvas.enabled && canvas.gameObject.activeInHierarchy)
                        return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTVInterfaceOpen()
    {
        try
        {
            var tv = Utils.FindObjectOfTypeSafe<TVInterface>();
            return tv != null && tv.IsOpen;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsShopInterfaceOpen()
    {
        try
        {
            // ShopInterface.AllShops is a static list of all shop UIs; check if any is open.
            if (_shopInterfaceAllShopsField != null)
            {
                var allShops = _shopInterfaceAllShopsField.GetValue(null);
                if (allShops != null && allShops is System.Collections.IEnumerable enumerable)
                {
                    foreach (var s in enumerable)
                    {
                        if (s == null)
                            continue;
                        var isOpen = ReflectionUtils.TryGetFieldOrProperty(s, "IsOpen");
                        if (isOpen is bool b && b)
                            return true;
                    }
                }
            }

            // Fallback: find any ShopInterface in scene and check IsOpen
            var fallbackShop = Utils.FindObjectOfTypeSafe<ShopInterface>();
            if (fallbackShop == null)
                return false;

            var open = ReflectionUtils.TryGetFieldOrProperty(fallbackShop, "IsOpen");
            if (open is bool b2)
                return b2;
            open = ReflectionUtils.TryGetFieldOrProperty(fallbackShop, "isOpen");
            return open is bool b3 && b3;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDialogueActive()
    {
        try
        {
            EnsureTypesResolved();

            // DialogueHandler.activeDialogue is set when dialogue UI is showing; null when not.
            if (_dialogueHandlerActiveDialogueField != null)
            {
                var activeDialogue = _dialogueHandlerActiveDialogueField.GetValue(null);
                if (activeDialogue != null)
                    return true;
            }

            // Fallback: DialogueManager.Instance may expose IsActive / IsOpen
            if (_dialogueManagerType == null)
                return false;

            var instanceProp = _dialogueManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceProp == null)
                return false;

            var instance = instanceProp.GetValue(null);
            if (instance == null)
                return false;

            var isActive = ReflectionUtils.TryGetFieldOrProperty(instance, "IsActive");
            if (isActive is bool b)
                return b;

            isActive = ReflectionUtils.TryGetFieldOrProperty(instance, "IsOpen");
            return isActive is bool b2 && b2;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsInVehicle()
    {
        try
        {
            if (Player.Local == null)
                return false;

            var isInVehicle = ReflectionUtils.TryGetFieldOrProperty(Player.Local, "IsInVehicle");
            if (isInVehicle is bool b)
                return b;

            isInVehicle = ReflectionUtils.TryGetFieldOrProperty(Player.Local, "CurrentVehicle");
            return isInVehicle != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDealOrFreeSampleOpen()
    {
        try
        {
            EnsureTypesResolved();
            if (_dealWindowTypes == null || _dealWindowTypes.Length == 0)
                return false;

            foreach (var type in _dealWindowTypes)
            {

                var instanceProp = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProp == null)
                    continue;

                var instance = instanceProp.GetValue(null);
                if (instance == null)
                    continue;

                var isOpen = ReflectionUtils.TryGetFieldOrProperty(instance, "IsOpen");
                if (isOpen is bool b && b)
                    return true;

                isOpen = ReflectionUtils.TryGetFieldOrProperty(instance, "isOpen");
                if (isOpen is bool b2 && b2)
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
