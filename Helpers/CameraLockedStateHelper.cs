using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

#if MONO
using ScheduleOne.AvatarFramework;
using ScheduleOne.AvatarFramework.Customization;
using ScheduleOne.DevUtilities;
using ScheduleOne.Money;
using ScheduleOne.PlayerScripts;
using ScheduleOne.UI;
using ScheduleOne.UI.Shop;
using ScheduleOne.TV;
#else
using Il2CppScheduleOne.AvatarFramework;
using Il2CppScheduleOne.AvatarFramework.Customization;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
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
    private static bool _sceneObjectsWarmed;
    private static bool _missingSceneCacheWarningLogged;
    private static CharacterCreator[] _characterCreators = Array.Empty<CharacterCreator>();
    private static CharacterCustomizationShop[] _customizationShops = Array.Empty<CharacterCustomizationShop>();
    private static readonly List<Canvas[]> CustomizationShopCanvases = new List<Canvas[]>();
    private static TVInterface _tvInterface;
    private static readonly List<CachedBooleanState> AtmOpenStates = new List<CachedBooleanState>();
    private static readonly List<CachedBooleanState> ShopOpenStates = new List<CachedBooleanState>();
    private static readonly List<CachedBooleanState> DealOpenStates = new List<CachedBooleanState>();
    private static CachedBooleanState _dialogueManagerOpenState;
    private static CachedValueState _playerVehicleState;

    private sealed class CachedValueState
    {
        private readonly object _target;
        private readonly FieldInfo _field;
        private readonly PropertyInfo _property;

        public CachedValueState(object target, FieldInfo field, PropertyInfo property)
        {
            _target = target;
            _field = field;
            _property = property;
        }

        public object Read()
        {
            if (!IsTargetAlive(_target))
                return null;

            try
            {
                return _field != null ? _field.GetValue(_target) : _property?.GetValue(_target);
            }
            catch
            {
                return null;
            }
        }
    }

    private sealed class CachedBooleanState
    {
        public object Target { get; }
        private readonly CachedValueState _value;

        public CachedBooleanState(object target, CachedValueState value)
        {
            Target = target;
            _value = value;
        }

        public bool IsTrue()
        {
            return _value?.Read() is bool value && value;
        }
    }

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
        if (_sceneObjectsWarmed)
            return;

        CacheSceneObjects();
        _sceneObjectsWarmed = true;
        _missingSceneCacheWarningLogged = false;
    }

    /// <summary>
    /// Rebuilds scene-owned references after a local player is created or replaced within Main.
    /// </summary>
    public static void RefreshSceneCache()
    {
        ResetSceneCache();
        PrewarmCache();
    }

    /// <summary>
    /// Clears references owned by the outgoing Unity scene while retaining stable reflected type metadata.
    /// </summary>
    public static void ResetSceneCache()
    {
        _sceneObjectsWarmed = false;
        _missingSceneCacheWarningLogged = false;
        _characterCreators = Array.Empty<CharacterCreator>();
        _customizationShops = Array.Empty<CharacterCustomizationShop>();
        CustomizationShopCanvases.Clear();
        _tvInterface = null;
        AtmOpenStates.Clear();
        ShopOpenStates.Clear();
        DealOpenStates.Clear();
        _dialogueManagerOpenState = null;
        _playerVehicleState = null;
    }

    /// <summary>
    /// Resolves stable reflection metadata as the Main scene begins loading. Scene-owned objects are
    /// captured later, once the local player and the game's UI singletons have finished spawning.
    /// </summary>
    public static void PrepareForMainSceneLoad()
    {
        ResetSceneCache();
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

            // Never perform scene-wide discovery from a gameplay hotkey. PlayerBackpack warms this
            // cache during local-player initialization; if an unusual lifecycle bypassed that stage,
            // retain the cheap direct checks below and report the missing initialization once.
            if (!_sceneObjectsWarmed && !_missingSceneCacheWarningLogged)
            {
                _missingSceneCacheWarningLogged = true;
                ModLogger.Warn("[BackpackUI] Camera-lock scene cache was not warmed before input; skipping scene scans.");
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
        return AnyCachedStateOpen(AtmOpenStates);
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

            // Scene instances are captured during local-player initialization. Do not search the
            // entire scene from the backpack hotkey.
            for (var i = 0; i < _characterCreators.Length; i++)
            {
                var c = _characterCreators[i];
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
            for (var i = 0; i < _customizationShops.Length; i++)
            {
                var shop = _customizationShops[i];
                if (shop == null || !shop.gameObject.activeInHierarchy)
                    continue;

                if (i >= CustomizationShopCanvases.Count)
                    continue;
                var canvases = CustomizationShopCanvases[i];

                for (var j = 0; j < canvases.Length; j++)
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
            return _tvInterface != null && _tvInterface.IsOpen;
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
            return AnyCachedStateOpen(ShopOpenStates);
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

            return _dialogueManagerOpenState?.IsTrue() == true;
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

            var isInVehicle = _playerVehicleState?.Read();
            if (isInVehicle is bool b)
                return b;
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
            return AnyCachedStateOpen(DealOpenStates);
        }
        catch
        {
            return false;
        }
    }

    private static void CacheSceneObjects()
    {
        AtmOpenStates.Clear();
        ShopOpenStates.Clear();
        DealOpenStates.Clear();
        CustomizationShopCanvases.Clear();
        var characterCreators = new List<CharacterCreator>();
        var customizationShops = new List<CharacterCustomizationShop>();
        _tvInterface = null;

        // This is the one intentionally broad scan. It runs while the local player is being
        // initialized, never as part of a backpack input frame, and covers namespace-shifted ATM
        // interfaces that cannot be referenced by one stable compile-time type.
        var components = Resources.FindObjectsOfTypeAll<Component>();
        for (var i = 0; i < components.Length; i++)
        {
            var component = components[i];
            if (component == null)
                continue;

            var type = component.GetType();
            if (IsAtmInterfaceType(type))
                AddCachedBooleanState(AtmOpenStates, component, "IsOpen", "isOpen");
            if (IsDealWindowType(type))
                AddCachedBooleanState(DealOpenStates, component, "IsOpen", "isOpen");
            if (component is CharacterCreator creator)
                characterCreators.Add(creator);
            if (component is CharacterCustomizationShop customizationShop)
                customizationShops.Add(customizationShop);
            if (_tvInterface == null && component is TVInterface tvInterface)
                _tvInterface = tvInterface;
            if (component is ShopInterface shopInterface)
                AddCachedBooleanState(ShopOpenStates, shopInterface, "IsOpen", "isOpen");
        }

        _characterCreators = characterCreators.ToArray();
        _customizationShops = customizationShops.ToArray();
        for (var i = 0; i < _customizationShops.Length; i++)
        {
            var shop = _customizationShops[i];
            var canvases = shop == null
                ? Array.Empty<Canvas>()
                : Utils.GetAllComponentsInChildrenRecursive<Canvas>(shop.gameObject).ToArray();
            CustomizationShopCanvases.Add(canvases);
        }
        CacheShopStates();
        CacheDialogueState();
        CacheDealSingletonStates();
        CachePlayerVehicleState();
    }

    private static void CacheShopStates()
    {
        try
        {
            if (_shopInterfaceAllShopsField?.GetValue(null) is System.Collections.IEnumerable allShops)
            {
                foreach (var shop in allShops)
                    AddCachedBooleanState(ShopOpenStates, shop, "IsOpen", "isOpen");
            }
        }
        catch
        {
        }

    }

    private static void CacheDialogueState()
    {
        _dialogueManagerOpenState = null;
        if (_dialogueManagerType == null)
            return;

        try
        {
            var instance = _dialogueManagerType
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);
            _dialogueManagerOpenState = CreateCachedBooleanState(instance, "IsActive", "IsOpen", "isOpen");
        }
        catch
        {
        }
    }

    private static void CacheDealSingletonStates()
    {
        if (_dealWindowTypes == null)
            return;

        for (var i = 0; i < _dealWindowTypes.Length; i++)
        {
            try
            {
                var instance = _dealWindowTypes[i]
                    .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null);
                AddCachedBooleanState(DealOpenStates, instance, "IsOpen", "isOpen");
            }
            catch
            {
            }
        }
    }

    private static void CachePlayerVehicleState()
    {
        _playerVehicleState = CreateCachedValueState(Player.Local, "IsInVehicle", "CurrentVehicle");
    }

    private static CachedBooleanState CreateCachedBooleanState(object target, params string[] memberNames)
    {
        var value = CreateCachedValueState(target, memberNames);
        return value == null ? null : new CachedBooleanState(target, value);
    }

    private static CachedValueState CreateCachedValueState(object target, params string[] memberNames)
    {
        if (target == null || memberNames == null)
            return null;

        var type = target.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        for (var i = 0; i < memberNames.Length; i++)
        {
            var field = type.GetField(memberNames[i], flags);
            if (field != null)
                return new CachedValueState(target, field, null);

            var property = type.GetProperty(memberNames[i], flags);
            if (property?.CanRead == true)
                return new CachedValueState(target, null, property);
        }

        return null;
    }

    private static void AddCachedBooleanState(List<CachedBooleanState> states, object target,
        params string[] memberNames)
    {
        if (target == null)
            return;
        for (var i = 0; i < states.Count; i++)
        {
            if (ReferenceEquals(states[i].Target, target))
                return;
        }

        var state = CreateCachedBooleanState(target, memberNames);
        if (state != null)
            states.Add(state);
    }

    private static bool AnyCachedStateOpen(List<CachedBooleanState> states)
    {
        for (var i = 0; i < states.Count; i++)
        {
            if (states[i].IsTrue())
                return true;
        }
        return false;
    }

    private static bool IsAtmInterfaceType(Type type)
    {
        if (type == null)
            return false;
        var fullName = type.FullName ?? string.Empty;
        return type.Name == "ATMInterface"
            || fullName == "ScheduleOne.UI.ATMInterface"
            || fullName == "ScheduleOne.UI.ATM.ATMInterface"
            || fullName == "Il2CppScheduleOne.UI.ATMInterface"
            || fullName == "Il2CppScheduleOne.UI.ATM.ATMInterface";
    }

    private static bool IsDealWindowType(Type type)
    {
        if (type == null || _dealWindowTypes == null)
            return false;
        for (var i = 0; i < _dealWindowTypes.Length; i++)
        {
            if (_dealWindowTypes[i] == type || _dealWindowTypes[i].IsAssignableFrom(type))
                return true;
        }
        return false;
    }

    private static bool IsTargetAlive(object target)
    {
        if (target == null)
            return false;
        return !(target is UnityEngine.Object unityObject) || unityObject != null;
    }
}
