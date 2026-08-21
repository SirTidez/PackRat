using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

#if MONO
using S1ExitAction = ScheduleOne.ExitAction;
using S1GameInput = ScheduleOne.GameInput;
using S1NavigationOverride = ScheduleOne.NavigationOverride<ScheduleOne.UISelectable>;
using S1NavigationOverrideElement = ScheduleOne.NavigationOverride<ScheduleOne.UISelectable>.OverrideElement<ScheduleOne.UISelectable>;
using S1OnScreenKeyboard = ScheduleOne.OnScreenKeyboard;
using S1UIPanel = ScheduleOne.UIPanel;
using S1UISelectable = ScheduleOne.UISelectable;
#else
using Il2CppInterop.Runtime;
using S1ExitAction = Il2CppScheduleOne.ExitAction;
using S1GameInput = Il2CppScheduleOne.GameInput;
using S1NavigationOverride = Il2CppScheduleOne.NavigationOverride<Il2CppScheduleOne.UISelectable>;
using S1NavigationOverrideElement = Il2CppScheduleOne.NavigationOverride<Il2CppScheduleOne.UISelectable>.OverrideElement<Il2CppScheduleOne.UISelectable>;
using S1OnScreenKeyboard = Il2CppScheduleOne.OnScreenKeyboard;
using S1UIPanel = Il2CppScheduleOne.UIPanel;
using S1UISelectable = Il2CppScheduleOne.UISelectable;
#endif

namespace PackRat.Helpers;

/// <summary>
/// Registers PackRat controls with Schedule I's native controller UI system. The game's
/// UISelectable/UIPanel graph owns spatial movement and button submission; PackRat only adds its
/// controls to the same panel as the surrounding game-owned item slots.
/// </summary>
public static class ControllerUiSupport
{
    private sealed class GameSelectableBinding
    {
        public S1UISelectable Selectable;
        public S1UIPanel Panel;
        public bool OwnedByPackRat;
        public bool RegisteredByPackRat;
        public Action TriggerAction;
    }

    private sealed class Surface
    {
        public string Key;
        public RectTransform NavigationRoot;
        public RectTransform SortTabsRoot;
        public readonly List<RectTransform> Roots = new List<RectTransform>();
        public readonly List<Selectable> Controls = new List<Selectable>();
        public readonly Dictionary<Selectable, GameSelectableBinding> GameSelectables =
            new Dictionary<Selectable, GameSelectableBinding>();
        public readonly Dictionary<Selectable, Outline> FocusOutlines = new Dictionary<Selectable, Outline>();
        public Action BackAction;
        public InputField TextInput;
        public Button TextInputProxy;
        public bool RootsWereVisible;
        public bool GamepadActive;
        public bool NavigationPanelUnavailableLogged;
        public bool NavigationOverrideUnavailableLogged;
        public bool TextKeyboardUnavailableLogged;
        public bool TextInputVisited;
        public int Order;
    }

    private static readonly Dictionary<string, Surface> Surfaces = new Dictionary<string, Surface>();
    private static S1GameInput.ExitDelegate _exitDelegate;
    private static bool _exitListenerRegistered;
    private static bool _exitListenerUnavailable;
    private static int _nextOrder;

    /// <summary>
    /// Presents a PackRat UI surface to the game's controller navigation system. The
    /// <paramref name="navigationRoot"/> must include one of the game-owned UISelectables that
    /// already belongs to the active controller panel, normally the item-slot container.
    /// </summary>
    public static void Present(string key, Action backAction, InputField textInput, RectTransform navigationRoot,
        RectTransform sortTabsRoot, params RectTransform[] roots)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        try
        {
            if (!Surfaces.TryGetValue(key, out var surface))
            {
                surface = new Surface { Key = key };
                Surfaces[key] = surface;
            }

            var rootsChanged = SetRoots(surface, navigationRoot, sortTabsRoot, roots);
            surface.BackAction = backAction;
            surface.TextInput = textInput;

            var visible = IsVisible(surface);
            var becameVisible = visible && !surface.RootsWereVisible;
            if (rootsChanged || becameVisible)
            {
                surface.TextInputVisited = false;
                surface.Order = ++_nextOrder;
            }
            surface.RootsWereVisible = visible;

            if (backAction != null)
                EnsureExitListener();

            if (IsGamepadCurrent() && visible)
                RefreshControls(surface);
        }
        catch (Exception ex)
        {
            ModLogger.Error("ControllerUiSupport.Present", ex);
        }
    }

    /// <summary>
    /// Removes PackRat controls from the active game UI panel when a surface closes or changes
    /// owner. No game-owned slot navigation is modified.
    /// </summary>
    public static void Dismiss(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !Surfaces.TryGetValue(key, out var surface))
            return;

        try
        {
            RestoreSurface(surface);
            Surfaces.Remove(key);
            ReleaseExitListenerIfUnused();
        }
        catch (Exception ex)
        {
            ModLogger.Error("ControllerUiSupport.Dismiss", ex);
        }
    }

    /// <summary>
    /// Advances controller presentation for the top-most visible PackRat surface.
    /// </summary>
    public static void Tick()
    {
        if (Surfaces.Count == 0)
            return;

        try
        {
            RemoveInvalidSurfaces();

            if (!IsGamepadCurrent())
            {
                foreach (var surface in Surfaces.Values)
                {
                    if (surface.GamepadActive)
                        RestoreSurface(surface);
                }
                return;
            }

            var activeSurface = GetTopVisibleSurface();
            foreach (var surface in Surfaces.Values)
            {
                if (surface != activeSurface && surface.GamepadActive)
                    RestoreSurface(surface);
            }

            if (activeSurface == null)
                return;

            // Dropdowns, paging, and settings create or disable controls at runtime. Re-scan at
            // a small cadence while controller navigation is active.
            if (!activeSurface.GamepadActive || Time.frameCount % 6 == 0)
                RefreshControls(activeSurface);

            RefreshFocusVisual(activeSurface);
        }
        catch (Exception ex)
        {
            ModLogger.Error("ControllerUiSupport.Tick", ex);
        }
    }

    /// <summary>
    /// Restores controller-panel registration during mod shutdown before the game disposes UI.
    /// </summary>
    public static void Shutdown()
    {
        try
        {
            foreach (var surface in Surfaces.Values)
                RestoreSurface(surface);
            Surfaces.Clear();
            ReleaseExitListener();
        }
        catch (Exception ex)
        {
            ModLogger.Error("ControllerUiSupport.Shutdown", ex);
        }
    }

    private static bool SetRoots(Surface surface, RectTransform navigationRoot, RectTransform sortTabsRoot,
        RectTransform[] roots)
    {
        var candidates = new List<RectTransform>();
        if (roots != null)
        {
            for (var i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                if (root == null || candidates.Contains(root))
                    continue;
                candidates.Add(root);
            }
        }

        var changed = navigationRoot != surface.NavigationRoot || sortTabsRoot != surface.SortTabsRoot ||
                      candidates.Count != surface.Roots.Count;
        if (!changed)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] == surface.Roots[i])
                    continue;
                changed = true;
                break;
            }
        }

        if (!changed)
            return false;

        RestoreSurface(surface);
        surface.NavigationRoot = navigationRoot;
        surface.SortTabsRoot = sortTabsRoot;
        surface.Roots.Clear();
        surface.Roots.AddRange(candidates);
        return true;
    }

    private static void RefreshControls(Surface surface)
    {
        if (surface == null)
            return;

        EnsureTextInputControllerProxy(surface);
        var currentControls = CollectControls(surface);
        RemoveAbsentControls(surface, currentControls);

        surface.Controls.Clear();
        surface.Controls.AddRange(currentControls);

        var panel = FindNativeNavigationPanel(surface);
        if (panel == null)
        {
            if (!surface.NavigationPanelUnavailableLogged)
            {
                surface.NavigationPanelUnavailableLogged = true;
                ModLogger.Warn($"[ControllerUI] No native UI panel found for '{surface.Key}'.");
            }
            return;
        }

        surface.NavigationPanelUnavailableLogged = false;
        for (var i = 0; i < surface.Controls.Count; i++)
            RegisterControlWithPanel(surface, surface.Controls[i], panel);

        ConfigurePackRatNavigation(surface);

        surface.GamepadActive = true;
    }

    private static List<Selectable> CollectControls(Surface surface)
    {
        var controls = new List<Selectable>();
        for (var rootIndex = 0; rootIndex < surface.Roots.Count; rootIndex++)
        {
            var root = surface.Roots[rootIndex];
            if (root == null || !root.gameObject.activeInHierarchy)
                continue;

            var selectables = root.GetComponentsInChildren<Selectable>(includeInactive: false);
            for (var controlIndex = 0; controlIndex < selectables.Length; controlIndex++)
            {
                var selectable = selectables[controlIndex];
                if (selectable == null || !selectable.isActiveAndEnabled || !selectable.interactable ||
                    selectable == surface.TextInput || controls.Contains(selectable))
                {
                    continue;
                }

                controls.Add(selectable);
            }
        }

        return controls;
    }

    /// <summary>
    /// InputField's internal focus handling does not consistently remain a native UISelectable
    /// after runtime injection. Use an invisible, controller-only Button over the same rect as a
    /// stable navigation target. It has no graphic or raycast target, so mouse interaction stays
    /// entirely with the original InputField.
    /// </summary>
    private static void EnsureTextInputControllerProxy(Surface surface)
    {
        if (surface?.TextInput == null || !surface.TextInput.gameObject.activeInHierarchy)
        {
            DestroyTextInputControllerProxy(surface);
            return;
        }

        if (surface.TextInputProxy != null && surface.TextInputProxy.transform.parent == surface.TextInput.transform)
            return;

        DestroyTextInputControllerProxy(surface);
        var proxyGo = new GameObject("PackRat_ControllerSearchTarget");
        var proxyRect = proxyGo.AddComponent<RectTransform>();
        proxyRect.SetParent(surface.TextInput.transform, worldPositionStays: false);
        proxyRect.anchorMin = Vector2.zero;
        proxyRect.anchorMax = Vector2.one;
        proxyRect.offsetMin = Vector2.zero;
        proxyRect.offsetMax = Vector2.zero;
        proxyRect.SetAsLastSibling();

        var proxy = Utils.AddComponentSafe<Button>(proxyGo);
        if (proxy == null)
        {
            UnityEngine.Object.Destroy(proxyGo);
            return;
        }

        proxy.transition = Selectable.Transition.None;
        surface.TextInputProxy = proxy;
    }

    private static void DestroyTextInputControllerProxy(Surface surface)
    {
        if (surface == null)
            return;

        if (surface.TextInputProxy != null)
            UnityEngine.Object.Destroy(surface.TextInputProxy.gameObject);
        surface.TextInputProxy = null;
    }

    private static void RemoveAbsentControls(Surface surface, List<Selectable> currentControls)
    {
        var removed = new List<Selectable>();
        foreach (var pair in surface.GameSelectables)
        {
            if (!currentControls.Contains(pair.Key))
                removed.Add(pair.Key);
        }

        for (var i = 0; i < removed.Count; i++)
        {
            var control = removed[i];
            if (surface.GameSelectables.TryGetValue(control, out var binding))
                RemoveGameSelectableBinding(binding);
            surface.GameSelectables.Remove(control);
            if (surface.FocusOutlines.TryGetValue(control, out var outline) && outline != null)
                UnityEngine.Object.Destroy(outline);
            surface.FocusOutlines.Remove(control);
        }
    }

    private static S1UIPanel FindNativeNavigationPanel(Surface surface)
    {
        var root = surface?.NavigationRoot;
        if (root == null)
            return null;

        var selectables = root.GetComponentsInChildren<S1UISelectable>(includeInactive: false);
        for (var i = 0; i < selectables.Length; i++)
        {
            var selectable = selectables[i];
            if (selectable == null || IsPackRatSelectable(surface, selectable) || selectable.ParentPanel == null)
                continue;
            return selectable.ParentPanel;
        }

        return null;
    }

    private static bool IsPackRatSelectable(Surface surface, S1UISelectable selectable)
    {
        foreach (var binding in surface.GameSelectables.Values)
        {
            if (binding?.Selectable == selectable)
                return true;
        }

        return false;
    }

    private static void RegisterControlWithPanel(Surface surface, Selectable control, S1UIPanel panel)
    {
        if (surface == null || control == null || panel == null)
            return;

        if (!surface.GameSelectables.TryGetValue(control, out var binding))
        {
            var gameSelectable = Utils.GetComponentSafe<S1UISelectable>(control.gameObject);
            var ownedByPackRat = gameSelectable == null;
            if (gameSelectable == null)
                gameSelectable = Utils.AddComponentSafe<S1UISelectable>(control.gameObject);
            if (gameSelectable == null)
                return;

            binding = new GameSelectableBinding
            {
                Selectable = gameSelectable,
                OwnedByPackRat = ownedByPackRat
            };
            surface.GameSelectables[control] = binding;
        }

        if (binding.Selectable == null)
            return;

        if (binding.Selectable.ParentPanel == null && panel.AddSelectable(binding.Selectable))
        {
            binding.Panel = panel;
            binding.RegisteredByPackRat = true;
        }

        if (control == surface.TextInputProxy)
            BindTextInputSubmit(surface, binding);
    }

    /// <summary>
    /// UITrigger.OnTrigger is the native callback Schedule I raises after a controller submit.
    /// Binding the text proxy there avoids relying on a UGUI Button pointer-click callback, which
    /// is not consistently dispatched by the game's controller UI module.
    /// </summary>
    private static void BindTextInputSubmit(Surface surface, GameSelectableBinding binding)
    {
        if (surface?.TextInput == null || binding?.Selectable == null || binding.TriggerAction != null)
            return;

        try
        {
            var trigger = binding.Selectable.OnTrigger;
            if (trigger == null)
            {
                trigger = new UnityEvent();
                binding.Selectable.OnTrigger = trigger;
            }

            Action submit = () =>
            {
                if (surface.TextInput != null)
                    surface.TextInput.ActivateInputField();
                OpenTextKeyboard(surface);
            };
            EventHelper.AddListener(submit, trigger);
            binding.TriggerAction = submit;
        }
        catch (Exception ex)
        {
            ModLogger.Error("ControllerUiSupport.BindTextInputSubmit", ex);
        }
    }

    /// <summary>
    /// Runtime-added UISelectables do not receive the serialized NavigationOverride object that
    /// Schedule I's prefabs receive in the editor. The native navigation path dereferences that
    /// object before attempting its global spatial search. Populate it and keep PackRat controls
    /// in a local graph so a direction press cannot jump to unrelated vanilla UI such as the
    /// hotbar.
    /// </summary>
    private static void ConfigurePackRatNavigation(Surface surface)
    {
        if (surface == null)
            return;

        var packRatSelectables = new List<S1UISelectable>();
        foreach (var binding in surface.GameSelectables.Values)
        {
            if (binding?.Selectable == null || !binding.OwnedByPackRat || !binding.RegisteredByPackRat ||
                !binding.Selectable.isActiveAndEnabled || !binding.Selectable.CanBeSelected)
            {
                continue;
            }

            packRatSelectables.Add(binding.Selectable);
        }

        if (packRatSelectables.Count == 0)
            return;

        var slotSelectables = CollectNativeSelectables(surface);
        var configuredAny = false;
        for (var i = 0; i < packRatSelectables.Count; i++)
        {
            var selectable = packRatSelectables[i];
            if (!EnsureNavigationOverride(selectable))
                continue;

            configuredAny = true;
            ConfigureNavigationDirection(surface, selectable, "Up", Vector2.up, packRatSelectables, slotSelectables);
            ConfigureNavigationDirection(surface, selectable, "Down", Vector2.down, packRatSelectables,
                slotSelectables);
            ConfigureNavigationDirection(surface, selectable, "Left", Vector2.left, packRatSelectables,
                slotSelectables);
            ConfigureNavigationDirection(surface, selectable, "Right", Vector2.right, packRatSelectables,
                slotSelectables);
        }

        if (!configuredAny && !surface.NavigationOverrideUnavailableLogged)
        {
            surface.NavigationOverrideUnavailableLogged = true;
            ModLogger.Warn($"[ControllerUI] Could not initialize controller navigation for '{surface.Key}'.");
        }
        else if (configuredAny)
        {
            surface.NavigationOverrideUnavailableLogged = false;
        }
    }

    private static List<S1UISelectable> CollectNativeSelectables(Surface surface)
    {
        var selectables = new List<S1UISelectable>();
        var root = surface?.NavigationRoot;
        if (root == null)
            return selectables;

        var candidates = root.GetComponentsInChildren<S1UISelectable>(includeInactive: false);
        for (var i = 0; i < candidates.Length; i++)
        {
            var selectable = candidates[i];
            if (selectable == null || IsPackRatSelectable(surface, selectable) || selectable.ParentPanel == null ||
                !selectable.isActiveAndEnabled || !selectable.CanBeSelected)
            {
                continue;
            }

            selectables.Add(selectable);
        }

        return selectables;
    }

    private static bool EnsureNavigationOverride(S1UISelectable selectable)
    {
        if (selectable == null)
            return false;

        try
        {
            var navigationOverride = selectable.NavigationOverride;
            if (navigationOverride == null)
            {
                navigationOverride = new S1NavigationOverride();
                if (!ReflectionUtils.TrySetFieldOrProperty(selectable, "navigationOverride", navigationOverride))
                    return false;
            }

            if (navigationOverride.Up == null)
                navigationOverride.Up = new S1NavigationOverrideElement();
            if (navigationOverride.Down == null)
                navigationOverride.Down = new S1NavigationOverrideElement();
            if (navigationOverride.Left == null)
                navigationOverride.Left = new S1NavigationOverrideElement();
            if (navigationOverride.Right == null)
                navigationOverride.Right = new S1NavigationOverrideElement();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ConfigureNavigationDirection(Surface surface, S1UISelectable source, string directionName,
        Vector2 direction, List<S1UISelectable> packRatSelectables, List<S1UISelectable> slotSelectables)
    {
        if (source == null)
            return;

        // The sort strip is directly above the backpack grid, but its seven narrow buttons are
        // offset from the full-width search field. Pure spatial matching therefore preferred the
        // nearby metrics tab over the slots and could not consistently reach Search. These are
        // deliberate UI lanes, not a geometry guess: every sort tab has the same upward search
        // destination and returns directly to its nearest slot when navigating down.
        if (IsSortTab(surface, source))
        {
            if (directionName == "Up")
            {
                var search = GetSearchSelectable(surface);
                if (search != null)
                {
                    SetNavigationOverride(source, directionName, search);
                    return;
                }
            }
            else if (directionName == "Down")
            {
                SetNavigationOverride(source, directionName, FindNearestSelectable(source, slotSelectables));
                return;
            }
        }

        // Search is intentionally a single full-width target above the tab strip. Its downward
        // edge is reciprocal so players can enter, submit text, cancel the keyboard, and resume
        // ordinary browser navigation without depending on a diagonal spatial match.
        if (directionName == "Down" && source == GetSearchSelectable(surface))
        {
            var sortTab = FindNearestSortTab(surface, source, packRatSelectables);
            if (sortTab != null)
            {
                SetNavigationOverride(source, directionName, sortTab);
                return;
            }
        }

        // Prefer another PackRat control. Only leave PackRat's local graph when no PackRat
        // control exists in that direction, then use the nearest actual backpack slot.
        var target = FindDirectionalSelectable(source, direction, packRatSelectables) ??
                     FindDirectionalSelectable(source, direction, slotSelectables);
        SetNavigationOverride(source, directionName, target);
    }

    private static bool IsSortTab(Surface surface, S1UISelectable selectable)
    {
        if (selectable == null)
            return false;

        var sortTabsRoot = surface?.SortTabsRoot;
        if (sortTabsRoot != null)
            return selectable.transform.IsChildOf(sortTabsRoot);

        // The fallback browser and the editor-authored bundle both use this stable contract
        // name. Keep the fallback only for a surface that did not expose its root yet.
        for (var current = selectable.transform; current != null; current = current.parent)
        {
            if (string.Equals(current.name, "SortTabs", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static S1UISelectable GetSearchSelectable(Surface surface)
    {
        var searchControl = GetSearchControllerControl(surface);
        if (searchControl == null || !surface.GameSelectables.TryGetValue(searchControl, out var binding) ||
            binding?.Selectable == null || !binding.Selectable.isActiveAndEnabled || !binding.Selectable.CanBeSelected)
        {
            return null;
        }

        return binding.Selectable;
    }

    private static Selectable GetSearchControllerControl(Surface surface)
    {
        if (surface?.TextInputProxy != null)
            return surface.TextInputProxy;
        return surface?.TextInput;
    }

    private static S1UISelectable FindNearestSortTab(Surface surface, S1UISelectable source,
        List<S1UISelectable> candidates)
    {
        var sortTabs = new List<S1UISelectable>();
        if (candidates != null)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (IsSortTab(surface, candidate))
                    sortTabs.Add(candidate);
            }
        }

        return FindNearestSelectable(source, sortTabs);
    }

    private static S1UISelectable FindNearestSelectable(S1UISelectable source, List<S1UISelectable> candidates)
    {
        if (source?.RectTransform == null || candidates == null)
            return null;

        var sourcePosition3 = source.RectTransform.TransformPoint(source.RectTransform.rect.center);
        var sourcePosition = new Vector2(sourcePosition3.x, sourcePosition3.y);
        S1UISelectable best = null;
        var bestDistanceSquared = float.MaxValue;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate == null || candidate == source || candidate.RectTransform == null ||
                !candidate.isActiveAndEnabled || !candidate.CanBeSelected)
            {
                continue;
            }

            var candidatePosition3 = candidate.RectTransform.TransformPoint(candidate.RectTransform.rect.center);
            var delta = new Vector2(candidatePosition3.x, candidatePosition3.y) - sourcePosition;
            var distanceSquared = delta.sqrMagnitude;
            if (distanceSquared >= bestDistanceSquared)
                continue;

            best = candidate;
            bestDistanceSquared = distanceSquared;
        }

        return best;
    }

    private static S1UISelectable FindDirectionalSelectable(S1UISelectable source, Vector2 direction,
        List<S1UISelectable> candidates)
    {
        if (source?.RectTransform == null || candidates == null)
            return null;

        var sourcePosition3 = source.RectTransform.TransformPoint(source.RectTransform.rect.center);
        var sourcePosition = new Vector2(sourcePosition3.x, sourcePosition3.y);
        S1UISelectable best = null;
        var bestScore = float.MinValue;

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate == null || candidate == source || candidate.RectTransform == null ||
                !candidate.isActiveAndEnabled || !candidate.CanBeSelected)
            {
                continue;
            }

            var candidatePosition3 = candidate.RectTransform.TransformPoint(candidate.RectTransform.rect.center);
            var delta = new Vector2(candidatePosition3.x, candidatePosition3.y) - sourcePosition;
            var distance = delta.magnitude;
            if (distance < 0.01f)
                continue;

            var directionMatch = Vector2.Dot(delta / distance, direction);
            // Match Schedule I's own directional threshold. A button only connects to an
            // element that is genuinely in the requested direction, avoiding diagonal hops.
            if (directionMatch < 0.6f)
                continue;

            var score = directionMatch * 1000f - distance;
            if (score <= bestScore)
                continue;

            best = candidate;
            bestScore = score;
        }

        return best;
    }

    private static void SetNavigationOverride(S1UISelectable source, string directionName, S1UISelectable target)
    {
        var navigationOverride = source?.NavigationOverride;
        if (navigationOverride == null)
            return;

        S1NavigationOverrideElement element = null;
        switch (directionName)
        {
            case "Up":
                element = navigationOverride.Up;
                break;
            case "Down":
                element = navigationOverride.Down;
                break;
            case "Left":
                element = navigationOverride.Left;
                break;
            case "Right":
                element = navigationOverride.Right;
                break;
        }

        if (element == null)
            return;

        element.Element = target;
        element.IsExplicit = true;
        element.IsReciprocated = false;
    }

    private static void RefreshFocusVisual(Surface surface)
    {
        if (surface == null)
            return;

        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        for (var i = 0; i < surface.Controls.Count; i++)
        {
            var control = surface.Controls[i];
            if (control == null)
                continue;

            // The controller proxy is intentionally invisible. Focus the real search surface so
            // controller focus is visible without changing how the InputField handles the mouse.
            var visualControl = control == surface.TextInputProxy && surface.TextInput != null
                ? (Selectable)surface.TextInput
                : control;

            if (!surface.FocusOutlines.TryGetValue(visualControl, out var outline) || outline == null)
            {
                outline = Utils.AddComponentSafe<Outline>(visualControl.gameObject);
                if (outline == null)
                    continue;
                outline.effectColor = new Color32(255, 178, 54, 235);
                outline.effectDistance = new Vector2(2f, -2f);
                outline.useGraphicAlpha = false;
                outline.enabled = false;
                surface.FocusOutlines[visualControl] = outline;
            }

            outline.enabled = selected == control.gameObject;
        }
    }

    /// <summary>
    /// Mirrors Schedule I's UISelectable_OSK behavior: controller focus identifies the text
    /// field, while the controller submit action opens Steam's gamepad text input. Showing the
    /// keyboard from focus alone made the event timing dependent on the UI panel update order.
    /// </summary>
    private static void OpenTextKeyboard(Surface surface)
    {
        if (surface?.TextInput == null || !surface.TextInput.gameObject.activeInHierarchy)
            return;

        if (S1OnScreenKeyboard.IsOpen)
            return;

        if (!IsGamepadCurrent())
        {
            if (!surface.TextKeyboardUnavailableLogged)
            {
                surface.TextKeyboardUnavailableLogged = true;
                ModLogger.Warn("[ControllerUI] Search submit received while the game input device is not a gamepad.");
            }
            return;
        }

        if (!S1OnScreenKeyboard.IsOSKAvailable())
        {
            if (!surface.TextKeyboardUnavailableLogged)
            {
                surface.TextKeyboardUnavailableLogged = true;
                ModLogger.Warn("[ControllerUI] Steam's gamepad keyboard is unavailable. Enable the Steam overlay and use Big Picture mode or a Steam Deck.");
            }
            return;
        }

        surface.TextKeyboardUnavailableLogged = false;
        surface.TextInputVisited = true;
        var input = surface.TextInput;
        var characterLimit = input.characterLimit > 0 ? (uint)input.characterLimit : 64u;
        var defaultText = input.text ?? string.Empty;
        Action<string> submit = value =>
        {
            if (input != null)
                input.text = value ?? string.Empty;
        };
        Action cancel = () => { };

        try
        {
            ModLogger.Debug("[ControllerUI] Opening Steam keyboard for backpack search.");
#if MONO
            S1OnScreenKeyboard.Show(submit, cancel, "Search backpack", characterLimit, defaultText);
#else
            S1OnScreenKeyboard.Show(
                DelegateSupport.ConvertDelegate<Il2CppSystem.Action<string>>(submit),
                DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(cancel),
                "Search backpack", characterLimit, defaultText);
#endif
        }
        catch (Exception ex)
        {
            ModLogger.Error("ControllerUiSupport.OpenTextKeyboard", ex);
        }
    }

    private static Surface GetTopVisibleSurface()
    {
        Surface top = null;
        foreach (var surface in Surfaces.Values)
        {
            if (!IsVisible(surface) || (top != null && surface.Order <= top.Order))
                continue;
            top = surface;
        }

        return top;
    }

    private static bool IsVisible(Surface surface)
    {
        if (surface == null)
            return false;

        for (var i = 0; i < surface.Roots.Count; i++)
        {
            var root = surface.Roots[i];
            if (root != null && root.gameObject.activeInHierarchy)
                return true;
        }

        return false;
    }

    private static void RemoveInvalidSurfaces()
    {
        var staleKeys = new List<string>();
        foreach (var pair in Surfaces)
        {
            var surface = pair.Value;
            var hasValidRoot = false;
            for (var i = 0; i < surface.Roots.Count; i++)
            {
                if (surface.Roots[i] == null)
                    continue;
                hasValidRoot = true;
                break;
            }

            if (!hasValidRoot)
                staleKeys.Add(pair.Key);
        }

        for (var i = 0; i < staleKeys.Count; i++)
        {
            var key = staleKeys[i];
            if (!Surfaces.TryGetValue(key, out var surface))
                continue;
            RestoreSurface(surface);
            Surfaces.Remove(key);
        }

        if (staleKeys.Count > 0)
            ReleaseExitListenerIfUnused();
    }

    private static void RestoreSurface(Surface surface)
    {
        if (surface == null)
            return;

        foreach (var binding in surface.GameSelectables.Values)
            RemoveGameSelectableBinding(binding);
        surface.GameSelectables.Clear();
        DestroyTextInputControllerProxy(surface);
        ClearFocusOutlines(surface);
        surface.Controls.Clear();
        surface.GamepadActive = false;
        surface.TextInputVisited = false;
    }

    private static void RemoveGameSelectableBinding(GameSelectableBinding binding)
    {
        if (binding == null)
            return;

        try
        {
            if (binding.TriggerAction != null && binding.Selectable != null)
                EventHelper.RemoveListener(binding.TriggerAction, binding.Selectable.OnTrigger);
            if (binding.RegisteredByPackRat && binding.Panel != null && binding.Selectable != null)
                binding.Panel.RemoveSelectable(binding.Selectable, autoFallback: true);
        }
        catch (Exception ex)
        {
            ModLogger.Error("ControllerUiSupport.RemoveGameSelectableBinding", ex);
        }
        finally
        {
            if (binding.OwnedByPackRat && binding.Selectable != null)
                UnityEngine.Object.Destroy(binding.Selectable);
        }
    }

    private static void ClearFocusOutlines(Surface surface)
    {
        if (surface == null)
            return;

        foreach (var outline in surface.FocusOutlines.Values)
        {
            if (outline != null)
                UnityEngine.Object.Destroy(outline);
        }

        surface.FocusOutlines.Clear();
    }

    private static bool IsGamepadCurrent()
    {
        try
        {
            return S1GameInput.GetCurrentInputDeviceIsGamepad();
        }
        catch
        {
            // GameInput is unavailable during early boot and scene teardown. Those are not
            // controller failures and should not create a per-frame log source.
            return false;
        }
    }

    private static void EnsureExitListener()
    {
        if (_exitListenerRegistered || _exitListenerUnavailable)
            return;

        try
        {
#if MONO
            _exitDelegate = new S1GameInput.ExitDelegate(HandleExit);
#else
            _exitDelegate = DelegateSupport.ConvertDelegate<S1GameInput.ExitDelegate>(
                new Action<S1ExitAction>(HandleExit));
#endif
            S1GameInput.RegisterExitListener(_exitDelegate, 1);
            _exitListenerRegistered = true;
        }
        catch (Exception ex)
        {
            _exitListenerUnavailable = true;
            ModLogger.Error("ControllerUiSupport.EnsureExitListener", ex);
        }
    }

    private static void HandleExit(S1ExitAction exitAction)
    {
        var surface = GetTopVisibleSurface();
        if (surface?.BackAction == null)
            return;

        try
        {
            surface.BackAction.Invoke();
            exitAction?.Use();
        }
        catch (Exception ex)
        {
            ModLogger.Error("ControllerUiSupport.HandleExit", ex);
        }
    }

    private static void ReleaseExitListenerIfUnused()
    {
        foreach (var surface in Surfaces.Values)
        {
            if (surface.BackAction != null)
                return;
        }

        ReleaseExitListener();
    }

    private static void ReleaseExitListener()
    {
        if (!_exitListenerRegistered || _exitDelegate == null)
            return;

        try
        {
            S1GameInput.DeregisterExitListener(_exitDelegate);
        }
        catch (Exception ex)
        {
            ModLogger.Error("ControllerUiSupport.ReleaseExitListener", ex);
        }
        finally
        {
            _exitDelegate = null;
            _exitListenerRegistered = false;
        }
    }
}
