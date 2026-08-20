using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using PackRat.Config;
using PackRat.Extensions;
using PackRat.Helpers;
using PackRat.Logic;
using PackRat.Profiling;
using UnityEngine;
using UnityEngine.UI;

#if MONO
using TMPro;
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Product;
using ScheduleOne.Storage;
using ScheduleOne.UI;
using ScheduleOne.UI.Handover;
using ScheduleOne.UI.Items;
using S1LandVehicle = ScheduleOne.Vehicles.LandVehicle;
using S1TMP = TMPro.TextMeshProUGUI;
#else
using Il2CppTMPro;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Handover;
using Il2CppScheduleOne.UI.Items;
using S1LandVehicle = Il2CppScheduleOne.Vehicles.LandVehicle;
using S1TMP = Il2CppTMPro.TextMeshProUGUI;
#endif

namespace PackRat.Patches;

/// <summary>
/// Adds backpack storage support to the handover screen with paging.
/// </summary>
[HarmonyPatch(typeof(HandoverScreen))]
public static class HandoverScreenPatch
{
    private const float VehicleMaxDistance = 20f;
    private const float BackpackGridScale = 0.74f;
    private const float BackpackContentCenterY = 125f;
    private static readonly Vector2 BackpackCardSize = new Vector2(420f, 660f);
    private const string VehicleHeaderTitle = "Vehicle";
    private const string VehicleHeaderSubtitle = "This is the vehicle you last drove.\nMust be within 20 meters.";

    private static BackpackUiThemePalette GetCurrentBackpackThemePalette()
    {
        var config = Configuration.Instance;
        return BackpackUiThemes.Get(config.BackpackUiTheme, config.CustomBackpackUiPrimaryColor);
    }

    private sealed class PanelState
    {
        public RectTransform BackpackContainer;
        public RectTransform BackpackSlotContainer;
        public RectTransform BackpackHeaderRoot;
        public RectTransform BackpackVisualRoot;
        public Canvas DedicatedCanvas;
        public EditorUiDedicatedCanvasBinding EditorDedicatedCanvas;
        public RectTransform DedicatedCard;
        // The shared browser normalizes its supplied host to local position zero on every
        // refresh. Keep that normalization inside this child so it can never reposition the
        // card itself after a handover callback (for example, a successful auto-fill).
        public RectTransform DedicatedBrowserHost;
        public RectTransform DedicatedGrid;
        public ItemSlotUI SlotPrefab;
        public RectTransform PagingRoot;
        public RectTransform VehicleContainer;
        public Component SourceTitleLabel;
        public Component SourceSubtitleLabel;
        public Component ClonedTitleLabel;
        public Component ClonedSubtitleLabel;
        public Component OverlayTitleLabel;
        public Component OverlaySubtitleLabel;
        public ItemSlotUI[] SlotUIs;
        public Button PrevButton;
        public Button NextButton;
        public Button ToggleButton;
        public Button DedicatedToggleButton;
        public RectTransform DedicatedToggleRoot;
        public RectTransform TransferRoot;
        public Button TransferButton;
        public Button BoundTransferButton;
        public Button EditorBackpackModeButton;
        public Button EditorVehicleModeButton;
        public Text TransferStatusLabel;
        public Text PageLabel;
        public Text VisualTitleLabel;
        public Text VisualMetaLabel;
        public Action PrevAction;
        public Action NextAction;
        public Action ToggleAction;
        public Action TransferAction;
        public Action ShowBackpackAction;
        public Action ShowVehicleAction;
        public S1LandVehicle NearbyVehicle;
        public Vector2 VehicleOriginalAnchoredPos;
        public int CurrentPage;
        public int SlotsPerPage;
        public bool ShowingVehicle;
        public bool IsOpen;
        public int LastPageInputFrame;
        public int NextVehicleProbeFrame;
        public bool Initialized;
        public string TransferStatus;
    }

    private sealed class HandoverRequirement
    {
        public string ProductId;
        public string Quality;
        public int QualityRank;
        public int Remaining;
    }

    private sealed class HandoverTransferSource
    {
        public string Name;
        public List<ItemSlot> Slots;
        public int MovedUnits;
    }

    private sealed class HeaderCandidate
    {
        public Component Label;
        public float LocalY;
        public float FontSize;
    }

    private static readonly Dictionary<int, PanelState> States = new Dictionary<int, PanelState>();
    private const int HeaderReapplyFrameCount = 3;

    /// <summary>
    /// Reapplies the handover backpack layout after a live preference change.
    /// </summary>
    public static void RefreshActiveLayouts()
    {
        try
        {
            foreach (var state in States.Values)
            {
                if (state == null)
                    continue;

                var dedicatedOverlayVisible = state.DedicatedCanvas != null && state.DedicatedCanvas.gameObject.activeSelf;
                var fallbackPanelVisible = state.BackpackContainer != null && state.BackpackContainer.gameObject.activeSelf;
                if (!dedicatedOverlayVisible && !fallbackPanelVisible)
                    continue;

                if (state.DedicatedCard != null)
                    UpdateDedicatedOverlayLayout(FindOwningScreen(state), state);
                else
                    ConfigureCompactBackpackLayout(FindOwningScreen(state), state);
                UpdatePagingLayout(state);
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("HandoverScreenPatch.RefreshActiveLayouts", ex);
        }
    }

    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    public static void Start(HandoverScreen __instance)
    {
        try
        {
            ModLogger.Info($"[HandoverUI] Start receipt: id={__instance?.GetInstanceID()}");
            PruneDeadStates();
            EnsurePanel(__instance);
        }
        catch (Exception ex)
        {
            ModLogger.Error("HandoverScreenPatch.Start", ex);
        }
    }

    [HarmonyPatch("Open")]
    [HarmonyPostfix]
    public static void Open(HandoverScreen __instance)
    {
        using var profile = UiProfiler.Measure("handover", "open", $"id={__instance?.GetInstanceID()}");
        try
        {
            ModLogger.Info($"[HandoverUI] Open receipt: id={__instance?.GetInstanceID()}, hasBackpack={HasBackpack()}");
            PruneDeadStates();
            if (!HasBackpack())
            {
                HidePanelAndRestoreVehicle(__instance);
                return;
            }

            var panel = EnsurePanel(__instance);
            if (panel == null || (panel.BackpackContainer == null && panel.DedicatedCanvas == null))
                return;

            var nearbyVehicle = ResolveNearbyVehicleStorage(panel, forceRefresh: true);
            var hasVehicle = nearbyVehicle != null;
            LogVehicleSelector(nearbyVehicle);
            EnsureDedicatedVehicleToggle(__instance, panel, hasVehicle);

            panel.CurrentPage = 0;
            panel.SlotsPerPage = panel.SlotUIs != null ? panel.SlotUIs.Length : 0;
            panel.ShowingVehicle = false;
            panel.TransferStatus = null;
            panel.IsOpen = true;
            if (panel.DedicatedCanvas == null && panel.BackpackContainer != null)
                panel.BackpackContainer.gameObject.SetActive(true);
            if (panel.PagingRoot != null)
                panel.PagingRoot.gameObject.SetActive(panel.DedicatedCanvas == null);

            UpdateBackpackHeaderTexts(panel);

            ApplyVisibleStorageMode(panel, hasVehicle);

            if (panel.DedicatedCanvas == null && panel.BackpackContainer != null)
                panel.BackpackContainer.gameObject.SetActive(true);
            if (panel.DedicatedCanvas == null && panel.BackpackSlotContainer != null)
                panel.BackpackSlotContainer.gameObject.SetActive(true);
            if (panel.VehicleContainer != null)
                panel.VehicleContainer.gameObject.SetActive(false);

            ApplyPrimaryHeaderForMode(__instance, panel, panel.ShowingVehicle);

            // Re-apply header next frame so backpack title wins if the game resets the label to "Vehicle" after Open.
            MelonLoader.MelonCoroutines.Start(ReapplyHeaderNextFrame(__instance, panel));

            if (panel.DedicatedCanvas == null)
                ApplyBackpackPage(panel);
            else
            {
                UpdateDedicatedOverlayLayout(__instance, panel);
                EnsureDedicatedVehicleToggle(__instance, panel, hasVehicle);
                UpdateDedicatedVehicleToggle(__instance, panel, hasVehicle);
            }

            RebuildQuickMove(__instance, nearbyVehicle);
        }
        catch (Exception ex)
        {
            ModLogger.Error("HandoverScreenPatch.Open", ex);
        }
    }

    private static IEnumerator ReapplyHeaderNextFrame(HandoverScreen screen, PanelState panel)
    {
        if (screen == null || panel == null)
            yield break;
        for (var i = 0; i < HeaderReapplyFrameCount; i++)
        {
            yield return null;
            if (screen == null || panel == null || panel.BackpackContainer == null || !panel.BackpackContainer.gameObject.activeSelf)
                yield break;
            if (panel.ShowingVehicle)
                yield break;
            if (panel.VehicleContainer != null && panel.VehicleContainer.gameObject.activeSelf)
                panel.VehicleContainer.gameObject.SetActive(false);
            ApplyPrimaryHeaderForMode(screen, panel, false);
        }
    }

    [HarmonyPatch("Update")]
    [HarmonyPostfix]
    public static void Update(HandoverScreen __instance)
    {
        PruneDeadStates();
        if (__instance == null || !States.TryGetValue(__instance.GetInstanceID(), out var panel))
            return;
        if (panel.IsOpen && !__instance.IsOpen)
        {
            ModLogger.Info("[HandoverUI] Owner screen closed outside Close(); dismissing dedicated backpack overlay.");
            HidePanelAndRestoreVehicle(__instance);
            return;
        }
        if (!panel.IsOpen)
            return;

        // The handover screen can reactivate vehicle storage after Open when a nearby vehicle
        // exists. Keep it opt-in: our dedicated toggle is the only path that permits it to show.
        if (panel.DedicatedCanvas != null)
        {
            if (!panel.ShowingVehicle && panel.VehicleContainer != null && panel.VehicleContainer.gameObject.activeSelf)
                panel.VehicleContainer.gameObject.SetActive(false);

            UpdateDedicatedVehicleToggle(__instance, panel, ResolveNearbyVehicleStorage(panel) != null);
            return;
        }

        if (panel.BackpackContainer == null || !panel.BackpackContainer.gameObject.activeSelf)
            return;
        if (panel.ShowingVehicle)
            return;
        if (panel.VehicleContainer != null && panel.VehicleContainer.gameObject.activeSelf)
            panel.VehicleContainer.gameObject.SetActive(false);
        ConfigureCompactBackpackLayout(__instance, panel);
        ApplyPrimaryHeaderForMode(__instance, panel, false);
        if (panel.BackpackHeaderRoot != null && panel.BackpackHeaderRoot.gameObject.activeSelf)
        {
            panel.BackpackHeaderRoot.SetAsLastSibling();
            var headerCanvas = panel.BackpackHeaderRoot.GetComponent<Canvas>();
            if (headerCanvas != null && headerCanvas.overrideSorting && headerCanvas.sortingOrder != 9999)
                headerCanvas.sortingOrder = 9999;
        }
    }

    [HarmonyPatch("Close")]
    [HarmonyPostfix]
    public static void Close(HandoverScreen __instance)
    {
        using var profile = UiProfiler.Measure("handover", "close", $"id={__instance?.GetInstanceID()}");
        try
        {
            if (!States.TryGetValue(__instance.GetInstanceID(), out var panel))
                return;

            ClearSlotAssignments(panel);
            panel.IsOpen = false;
            if (panel.BackpackContainer != null)
                panel.BackpackContainer.gameObject.SetActive(false);
            if (panel.PagingRoot != null)
                panel.PagingRoot.gameObject.SetActive(false);
            if (panel.DedicatedToggleRoot != null)
                panel.DedicatedToggleRoot.gameObject.SetActive(false);
            if (panel.TransferRoot != null)
                panel.TransferRoot.gameObject.SetActive(false);
            if (panel.DedicatedCanvas != null)
                panel.DedicatedCanvas.gameObject.SetActive(false);
            SetBackpackVisualVisible(panel, false);
            HideOverlayHeader(panel);
            SetHeaderPairActive(panel.SourceTitleLabel, panel.SourceSubtitleLabel, true);
            if (panel.VehicleContainer != null)
                panel.VehicleContainer.anchoredPosition = panel.VehicleOriginalAnchoredPos;
        }
        catch (Exception ex)
        {
            ModLogger.Error("HandoverScreenPatch.Close", ex);
        }
    }

    private static PanelState EnsurePanel(HandoverScreen screen)
    {
        using var profile = UiProfiler.Measure("handover", "ensure_panel", $"id={screen?.GetInstanceID()}");
        PruneDeadStates();
        if (screen == null)
            return null;

        var id = screen.GetInstanceID();
        if (States.TryGetValue(id, out var existing)
            && existing.Initialized
            && IsComponentAlive(existing.BackpackContainer)
            && IsComponentAlive(existing.VehicleContainer)
            && (existing.BackpackHeaderRoot == null || IsComponentAlive(existing.BackpackHeaderRoot))
            && IsComponentAlive(existing.PagingRoot)
            && IsComponentAlive(existing.PrevButton)
            && IsComponentAlive(existing.NextButton)
            && IsComponentAlive(existing.ToggleButton)
            && IsComponentAlive(existing.PageLabel))
        {
            RefreshHeaderBindings(existing, screen);
            EnsureDedicatedBackpackOverlay(screen, existing);
            return existing;
        }

        var state = existing ?? new PanelState();
        state.VehicleContainer = screen.VehicleContainer;
        if (state.VehicleContainer == null)
            return null;

        state.VehicleOriginalAnchoredPos = state.VehicleContainer.anchoredPosition;

        if (!IsComponentAlive(state.BackpackContainer))
            state.BackpackContainer = null;

        if (state.BackpackContainer == null)
        {
            var clone = UnityEngine.Object.Instantiate(state.VehicleContainer, state.VehicleContainer.parent);
            clone.name = "BackpackContainer";
            clone.gameObject.SetActive(false);
            state.BackpackContainer = clone;
        }

        CenterBackpackContainer(state);
        state.BackpackSlotContainer = FindMatchingRectTransform(state.BackpackContainer, screen.VehicleSlotContainer);
        var slotSearchRoot = state.BackpackSlotContainer != null ? state.BackpackSlotContainer : state.BackpackContainer;
        var sourceSlotUis = slotSearchRoot.GetComponentsInChildren<ItemSlotUI>(includeInactive: false);
        if (sourceSlotUis == null || sourceSlotUis.Length == 0)
            sourceSlotUis = slotSearchRoot.GetComponentsInChildren<ItemSlotUI>(includeInactive: true);

        // Once the dedicated surface exists, its slots are the only slot views PackRat may bind.
        // Replacing this with the vehicle hierarchy on a later Open was the reason the vehicle
        // UI was being cleared/rebound and the dedicated browser rendered without any slots.
        var dedicatedSlotPrefab = GetDedicatedSlotTemplate(sourceSlotUis != null && sourceSlotUis.Length > 0
            ? sourceSlotUis[0]
            : null);
        if (state.DedicatedCanvas == null)
        {
            state.SlotUIs = sourceSlotUis;
            state.SlotPrefab = dedicatedSlotPrefab;
        }
        else if (state.SlotPrefab == null)
        {
            state.SlotPrefab = dedicatedSlotPrefab;
        }
        RefreshHeaderBindings(state, screen);
        EnsureDedicatedBackpackOverlay(screen, state);
        if (state.DedicatedCard == null)
        {
            EnsureBackpackVisuals(state);
            ConfigureCompactBackpackLayout(screen, state);
        }
        EnsurePagingControls(state);
        state.Initialized = true;
        States[id] = state;
        return state;
    }

    private static void PruneDeadStates()
    {
        if (States.Count == 0)
            return;

        var staleIds = new List<int>();
        foreach (var entry in States)
        {
            var state = entry.Value;
            if (state == null)
            {
                staleIds.Add(entry.Key);
                continue;
            }

            if (!IsComponentAlive(state.VehicleContainer)
                && !IsComponentAlive(state.BackpackContainer)
                && !IsComponentAlive(state.PagingRoot)
                && !IsComponentAlive(state.BackpackHeaderRoot))
            {
                staleIds.Add(entry.Key);
            }
        }

        for (var i = 0; i < staleIds.Count; i++)
        {
            if (States.TryGetValue(staleIds[i], out var stale) && stale?.DedicatedCanvas != null)
                UnityEngine.Object.Destroy(stale.DedicatedCanvas.gameObject);
            States.Remove(staleIds[i]);
        }
    }

    private static void RefreshHeaderBindings(PanelState state, HandoverScreen screen)
    {
        if (state == null)
            return;

        ResolveLabels(state, screen);
        EnsureBackpackHeader(state);
    }

    private static void ResolveLabels(PanelState state, HandoverScreen screen)
    {
        ResolveHeaderPairInContainer(state.VehicleContainer, screen?.VehicleSlotContainer, null, null,
            out state.SourceTitleLabel, out state.SourceSubtitleLabel);
        ResolveHeaderPairInContainer(state.BackpackContainer, state.BackpackSlotContainer, state.PagingRoot, state.BackpackHeaderRoot,
            out state.ClonedTitleLabel, out state.ClonedSubtitleLabel);
        if (IsUnderTransform(state.ClonedTitleLabel, state.BackpackVisualRoot))
            state.ClonedTitleLabel = null;
        if (IsUnderTransform(state.ClonedSubtitleLabel, state.BackpackVisualRoot))
            state.ClonedSubtitleLabel = null;
    }

    private static void ResolveHeaderPairInContainer(
        RectTransform container,
        Transform slotContainer,
        Transform pagingRoot,
        Transform overlayRoot,
        out Component titleLabel,
        out Component subtitleLabel)
    {
        titleLabel = null;
        subtitleLabel = null;

        if (container == null)
            return;

        var allComponents = container.GetComponentsInChildren<Component>(true);
        var labels = new List<Component>();
        for (var i = 0; i < allComponents.Length; i++)
        {
            var label = allComponents[i];
            if (label == null)
                continue;
            if (!IsTextLikeComponent(label))
                continue;
            if (IsUnderTransform(label, slotContainer))
                continue;
            if (IsUnderTransform(label, pagingRoot))
                continue;
            if (IsUnderTransform(label, overlayRoot))
                continue;

            labels.Add(label);
        }

        if (labels.Count == 0)
            return;

        titleLabel = FindNamedHeaderLabel(container, slotContainer, pagingRoot, overlayRoot, "Title");
        subtitleLabel = FindNamedHeaderLabel(container, slotContainer, pagingRoot, overlayRoot, "Subtitle");

        foreach (var label in labels)
        {
            var text = NormalizeLabelText(GetLabelText(label));
            if (titleLabel == null && text.Equals(VehicleHeaderTitle, StringComparison.OrdinalIgnoreCase))
            {
                titleLabel = label;
                continue;
            }

            if (subtitleLabel == null
                && (text.Contains("vehicle you last drove", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("within 20 meters", StringComparison.OrdinalIgnoreCase)))
            {
                subtitleLabel = label;
            }
        }

        if (titleLabel != null && subtitleLabel != null)
            return;

        var ranked = new List<HeaderCandidate>(labels.Count);
        for (var i = 0; i < labels.Count; i++)
        {
            var label = labels[i];
            ranked.Add(new HeaderCandidate
            {
                Label = label,
                LocalY = GetLocalY(container, label),
                FontSize = GetFontSize(label)
            });
        }

        ranked.Sort(CompareHeaderCandidates);

        if (titleLabel == null)
        {
            if (ranked.Count > 0)
                titleLabel = ranked[0].Label;
        }

        if (subtitleLabel == null)
        {
            for (var i = 0; i < ranked.Count; i++)
            {
                var label = ranked[i].Label;
                if (label == null || label == titleLabel)
                    continue;

                subtitleLabel = label;
                break;
            }
        }
    }

    private static Component FindNamedHeaderLabel(
        RectTransform container,
        Transform slotContainer,
        Transform pagingRoot,
        Transform overlayRoot,
        string targetName)
    {
        if (container == null || string.IsNullOrEmpty(targetName))
            return null;

        var transforms = container.GetComponentsInChildren<Transform>(true);
        for (var i = 0; i < transforms.Length; i++)
        {
            var transform = transforms[i];
            if (transform == null)
                continue;
            if (!string.Equals(transform.name, targetName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (slotContainer != null && (transform == slotContainer || transform.IsChildOf(slotContainer)))
                continue;
            if (pagingRoot != null && (transform == pagingRoot || transform.IsChildOf(pagingRoot)))
                continue;
            if (overlayRoot != null && (transform == overlayRoot || transform.IsChildOf(overlayRoot)))
                continue;

            var directLabel = GetTextLikeComponent(transform.gameObject);
            if (directLabel != null)
                return directLabel;

            var childLabel = FindFirstTextLikeInSubtree(transform);
            if (childLabel != null)
                return childLabel;
        }

        return null;
    }

    private static Component FindFirstTextLikeInSubtree(Transform root)
    {
        if (root == null)
            return null;

        var components = root.GetComponentsInChildren<Component>(true);
        for (var i = 0; i < components.Length; i++)
        {
            var component = components[i];
            if (component != null && IsTextLikeComponent(component))
                return component;
        }

        return null;
    }

    private static int CompareHeaderCandidates(HeaderCandidate left, HeaderCandidate right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left == null)
            return 1;
        if (right == null)
            return -1;

        var yComparison = right.LocalY.CompareTo(left.LocalY);
        if (yComparison != 0)
            return yComparison;

        return right.FontSize.CompareTo(left.FontSize);
    }

    private static float GetLocalY(RectTransform container, Component component)
    {
        if (container == null || component == null || component.transform == null)
            return float.MinValue;

        try
        {
            var localPosition = container.InverseTransformPoint(component.transform.position);
            return localPosition.y;
        }
        catch
        {
            return float.MinValue;
        }
    }

    private static float GetFontSize(Component label)
    {
        if (label == null)
            return 0f;

        if (label is S1TMP tmpLabel)
        {
            try
            {
                return tmpLabel.fontSize;
            }
            catch
            {
            }
        }

        if (label is Text uiText)
            return uiText.fontSize;

        var value = ReflectionUtils.TryGetFieldOrProperty(label, "fontSize");
        if (value is float floatSize)
            return floatSize;
        if (value is int intSize)
            return intSize;

        return 0f;
    }

    private static Component GetTextLikeComponent(GameObject gameObject)
    {
        if (gameObject == null)
            return null;

        var tmpLabel = GetTmpLabel(gameObject);
        if (tmpLabel != null)
            return tmpLabel;

#if !MONO
        var uiText = Utils.GetComponentSafe<Text>(gameObject);
#else
        var uiText = gameObject.GetComponent<Text>();
#endif
        if (uiText != null)
            return uiText;

        var components = gameObject.GetComponents<Component>();
        for (var i = 0; i < components.Length; i++)
        {
            var component = components[i];
            if (component != null && IsTextLikeComponent(component))
                return component;
        }

        return null;
    }

    private static void EnsureBackpackHeader(PanelState state)
    {
        if (state?.BackpackContainer == null || state.SourceTitleLabel == null || state.SourceSubtitleLabel == null)
            return;

        var overlayParent = state.BackpackContainer;

        RectTransform headerRoot = null;
        if (state.BackpackHeaderRoot != null && IsComponentAlive(state.BackpackHeaderRoot))
            headerRoot = state.BackpackHeaderRoot;

        if (headerRoot != null && headerRoot.parent != overlayParent)
            headerRoot.SetParent(overlayParent, worldPositionStays: false);

        if (headerRoot == null)
        {
            var existingRoot = overlayParent.Find("PackRat_BackpackHeader");
            headerRoot = existingRoot as RectTransform;
        }

        if (headerRoot == null)
        {
            var rootGo = new GameObject("PackRat_BackpackHeader");
            headerRoot = rootGo.AddComponent<RectTransform>();
            headerRoot.SetParent(overlayParent, worldPositionStays: false);
        }

        state.BackpackHeaderRoot = headerRoot;
        EnsureIgnoredByLayout(headerRoot);
        ResetHeaderOverlayRoot(headerRoot);
        state.OverlayTitleLabel = EnsureOverlayLabel(headerRoot, "PackRat_BackpackTitle", state.SourceTitleLabel);
        state.OverlaySubtitleLabel = EnsureOverlayLabel(headerRoot, "PackRat_BackpackSubtitle", state.SourceSubtitleLabel);
        UpdateBackpackHeaderLayout(state);

        if (TryGetGameObject(headerRoot, out var headerObject)
            && TryGetGameObject(overlayParent, out var containerObject))
        {
            SetLayerRecursively(headerObject, containerObject.layer);
            headerRoot.SetAsLastSibling();

            var parentCanvas = overlayParent.GetComponentInParent<Canvas>();
            var headerCanvas = headerObject.GetComponent<Canvas>();
            if (headerCanvas == null)
                headerCanvas = headerObject.AddComponent<Canvas>();

            headerCanvas.overrideSorting = true;
            if (parentCanvas != null)
            {
                headerCanvas.sortingLayerID = parentCanvas.sortingLayerID;
                headerCanvas.sortingOrder = parentCanvas.sortingOrder + 210;
            }
            else
            {
                headerCanvas.sortingOrder = 5010;
            }
        }

        UpdateBackpackHeaderTexts(state);
    }

    private static void UpdateBackpackHeaderLayout(PanelState state)
    {
        if (state?.BackpackHeaderRoot == null || state.BackpackContainer == null)
            return;

        var headerRoot = state.BackpackHeaderRoot;
        EnsureIgnoredByLayout(headerRoot);
        headerRoot.anchorMin = new Vector2(0.5f, 1f);
        headerRoot.anchorMax = new Vector2(0.5f, 1f);
        headerRoot.pivot = new Vector2(0.5f, 1f);
        headerRoot.anchoredPosition = new Vector2(0f, -8f);
        headerRoot.localScale = Vector3.one;
    }

    private static void ResetHeaderOverlayRoot(RectTransform headerRoot)
    {
        if (headerRoot == null)
            return;

        headerRoot.anchorMin = new Vector2(0.5f, 0.5f);
        headerRoot.anchorMax = new Vector2(0.5f, 0.5f);
        headerRoot.pivot = new Vector2(0.5f, 0.5f);
        headerRoot.anchoredPosition3D = Vector3.zero;
        headerRoot.localRotation = Quaternion.identity;
        headerRoot.localScale = Vector3.one;
        headerRoot.sizeDelta = Vector2.zero;
        headerRoot.offsetMin = Vector2.zero;
        headerRoot.offsetMax = Vector2.zero;
    }

    private static Component EnsureOverlayLabel(RectTransform parent, string name, Component sourceLabel)
    {
        if (parent == null || sourceLabel == null)
            return null;

        var child = parent.Find(name);
        Component targetLabel = null;
        if (child != null)
            targetLabel = GetTextLikeComponent(child.gameObject);

        if (targetLabel == null)
        {
            var sourceObject = sourceLabel.gameObject;
            if (sourceObject == null)
                return null;

            var cloneObject = UnityEngine.Object.Instantiate(sourceObject, parent);
            cloneObject.name = name;
            targetLabel = GetTextLikeComponent(cloneObject);
        }

        if (targetLabel == null)
            return null;

        if (TryGetRectTransform(sourceLabel, out var sourceRect) && TryGetRectTransform(targetLabel, out var targetRect))
            CopyRectTransform(sourceRect, targetRect);
        CopyLabelPresentation(sourceLabel, targetLabel);
        return targetLabel;
    }

    private static void CopyRectTransform(RectTransform source, RectTransform target)
    {
        if (source == null || target == null)
            return;

        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.anchoredPosition3D = source.anchoredPosition3D;
        target.sizeDelta = source.sizeDelta;
        target.offsetMin = source.offsetMin;
        target.offsetMax = source.offsetMax;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
    }

    private static void CopyLabelPresentation(Component source, Component target)
    {
        if (source == null || target == null)
            return;

        if (source is S1TMP sourceTmp && target is S1TMP targetTmp)
        {
            targetTmp.font = sourceTmp.font;
            targetTmp.fontSize = sourceTmp.fontSize;
            targetTmp.fontStyle = sourceTmp.fontStyle;
            targetTmp.alignment = sourceTmp.alignment;
            targetTmp.color = sourceTmp.color;
            targetTmp.raycastTarget = false;
            return;
        }

        if (source is Text sourceText && target is Text targetText)
        {
            targetText.font = sourceText.font;
            targetText.fontSize = sourceText.fontSize;
            targetText.fontStyle = sourceText.fontStyle;
            targetText.alignment = sourceText.alignment;
            targetText.color = sourceText.color;
            targetText.raycastTarget = false;
        }
    }

    private static void EnsureIgnoredByLayout(RectTransform rectTransform)
    {
        if (rectTransform == null)
            return;

        LayoutElement layoutElement;
#if !MONO
        layoutElement = Utils.GetOrAddComponentSafe<LayoutElement>(rectTransform.gameObject);
#else
        layoutElement = rectTransform.GetComponent<LayoutElement>();
        if (layoutElement == null)
            layoutElement = rectTransform.gameObject.AddComponent<LayoutElement>();
#endif

        if (layoutElement != null)
            layoutElement.ignoreLayout = true;
    }

    private static string GetBackpackDisplayName()
    {
        var instance = PlayerBackpack.Instance;
        var name = instance?.CurrentTier?.Name;
        if (!string.IsNullOrEmpty(name))
            return name;
        var tierIdx = instance?.CurrentTierIndex ?? -1;
        if (tierIdx >= 0 && tierIdx < Configuration.BackpackTiers.Length)
            return Configuration.BackpackTiers[tierIdx].Name;
        return PlayerBackpack.StorageName;
    }

    /// <summary>
    /// Replaces any "Vehicle" text in the container with the backpack display name so the correct title shows even if the visible label isn't our BackpackHeaderRoot.
    /// </summary>
    private static void ReplaceVehicleTextInContainer(RectTransform container, string backpackTitle)
    {
        if (container == null || string.IsNullOrEmpty(backpackTitle))
            return;
        var components = container.GetComponentsInChildren<Component>(true);
        for (var i = 0; i < components.Length; i++)
        {
            var c = components[i];
            if (c == null || !IsTextLikeComponent(c))
                continue;
            var current = NormalizeLabelText(GetLabelText(c));
            if (!current.Equals(VehicleHeaderTitle, StringComparison.OrdinalIgnoreCase))
                continue;
            SetLabelText(c, backpackTitle);
        }
    }

    /// <summary>
    /// Replaces "Vehicle" labels in known local containers only.
    /// Avoids full-root scans, which are expensive in handover scenes.
    /// </summary>
    private static void ReplaceVehicleTextEverywhere(PanelState panel, string backpackTitle)
    {
        if (panel == null || string.IsNullOrEmpty(backpackTitle))
            return;
        if (panel.BackpackContainer != null)
            ReplaceVehicleTextInContainer(panel.BackpackContainer, backpackTitle);
    }

    private static void UpdateBackpackHeaderTexts(PanelState state)
    {
        if (state == null)
            return;

        if (state.OverlayTitleLabel != null)
            SetLabelText(state.OverlayTitleLabel, GetBackpackDisplayName());

        if (state.OverlaySubtitleLabel != null)
            SetLabelText(state.OverlaySubtitleLabel, "Items from your backpack.");

        UpdateBackpackVisuals(state);
    }

    private static void CenterBackpackContainer(PanelState state)
    {
        if (state?.BackpackContainer == null)
            return;

        var container = state.BackpackContainer;
        container.anchorMin = new Vector2(0.5f, 0.5f);
        container.anchorMax = new Vector2(0.5f, 0.5f);
        container.pivot = new Vector2(0.5f, 0.5f);
        container.localScale = Vector3.one;
        container.anchoredPosition = GetHandoverBackpackPosition(state);
    }

    /// <summary>
    /// Uses the same dedicated overlay-canvas approach as established S1 mods rather than
    /// attaching new layout to HandoverScreen's animated vehicle hierarchy. Native ItemSlotUI
    /// prefabs are cloned into a fixed grid, preserving item rendering and click behavior.
    /// </summary>
    private static void EnsureDedicatedBackpackOverlay(HandoverScreen screen, PanelState state)
    {
        if (screen == null || state == null || state.SlotPrefab == null)
            return;

        if (state.DedicatedCanvas == null)
        {
            GameObject canvasGo;
            Canvas canvas;
            UnityEngine.UI.CanvasScaler scaler;
            GraphicRaycaster raycaster;
            if (EditorUiAssetBundleBindings.TryCreateDedicatedCanvas(screen.transform,
                    out var editorCanvas))
            {
                state.EditorDedicatedCanvas = editorCanvas;
                canvasGo = editorCanvas.Root.gameObject;
                canvas = editorCanvas.Canvas;
                scaler = editorCanvas.Scaler;
                raycaster = editorCanvas.Raycaster;
                // Match the established detached-overlay lifetime. The handover close hooks own
                // visibility explicitly, so animated screen ancestors cannot scale or clip it.
                editorCanvas.Root.SetParent(null, worldPositionStays: false);
                ModLogger.Info("[EditorUI] Bound the editor-authored dedicated handover canvas.");
            }
            else
            {
                canvasGo = new GameObject("PackRat_HandoverBackpackCanvas");
#if !MONO
                canvas = Utils.AddComponentSafe<Canvas>(canvasGo);
                scaler = Utils.AddComponentSafe<UnityEngine.UI.CanvasScaler>(canvasGo);
                raycaster = Utils.AddComponentSafe<GraphicRaycaster>(canvasGo);
#else
                canvas = canvasGo.AddComponent<Canvas>();
                scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
                raycaster = canvasGo.AddComponent<GraphicRaycaster>();
#endif
            }
            if (canvas == null || scaler == null)
            {
                UnityEngine.Object.Destroy(canvasGo);
                return;
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            // Keep this browser above the handover owner while allowing the game's item tooltip
            // canvas to render over hovered PackRat slots. The old +50 order buried tooltips.
            canvas.sortingOrder = (screen.Canvas != null ? screen.Canvas.sortingOrder : 0) + 1;
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            RegisterItemUiRaycaster(raycaster);
            state.DedicatedCanvas = canvas;
            UpdateDedicatedSafeArea(state);

            var cardGo = new GameObject("PackRat_BackpackCard");
            var card = cardGo.AddComponent<RectTransform>();
            Transform cardParent = state.EditorDedicatedCanvas?.PaneHost != null
                ? state.EditorDedicatedCanvas.PaneHost
                : canvas.transform;
            card.SetParent(cardParent, worldPositionStays: false);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = BackpackCardSize;
            card.anchoredPosition = GetDedicatedHandoverBackpackPosition();
            state.DedicatedCard = card;
            state.BackpackVisualRoot = card;

            var headerGo = new GameObject("Header");
            var header = headerGo.AddComponent<RectTransform>();
            header.SetParent(card, worldPositionStays: false);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.offsetMin = new Vector2(10f, -62f);
            header.offsetMax = new Vector2(-10f, -8f);
            var headerImage = headerGo.AddComponent<Image>();
            headerImage.color = GetCurrentBackpackThemePalette().Header;
            headerImage.raycastTarget = false;

            var accentGo = new GameObject("Accent");
            var accent = accentGo.AddComponent<RectTransform>();
            accent.SetParent(header, worldPositionStays: false);
            accent.anchorMin = new Vector2(0f, 0f);
            accent.anchorMax = new Vector2(1f, 0f);
            accent.pivot = new Vector2(0.5f, 0f);
            accent.offsetMin = Vector2.zero;
            accent.offsetMax = new Vector2(0f, 3f);
            var accentImage = accentGo.AddComponent<Image>();
            accentImage.color = GetCurrentBackpackThemePalette().Accent;
            accentImage.raycastTarget = false;

            state.VisualTitleLabel = EnsureVisualLabel(header, "Title", new Vector2(0f, -16f), 18, FontStyle.Bold);
            state.VisualMetaLabel = EnsureVisualLabel(header, "Meta", new Vector2(0f, -38f), 11, FontStyle.Normal);
            // The shared browser surface now supplies the complete main-backpack header. Keep
            // these legacy labels dormant for compatibility with the fallback branch only.
            header.gameObject.SetActive(false);

            var gridGo = new GameObject("SlotGrid");
            var grid = gridGo.AddComponent<RectTransform>();
            grid.SetParent(card, worldPositionStays: false);
            grid.anchorMin = new Vector2(0.5f, 0.5f);
            grid.anchorMax = new Vector2(0.5f, 0.5f);
            grid.pivot = new Vector2(0.5f, 0.5f);
            grid.anchoredPosition = new Vector2(0f, -24f);
            grid.sizeDelta = new Vector2(306f, 306f);
            var layout = gridGo.AddComponent<GridLayoutGroup>();
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 4;
            layout.cellSize = new Vector2(72f, 72f);
            layout.spacing = new Vector2(6f, 6f);
            layout.childAlignment = TextAnchor.MiddleCenter;
            state.DedicatedGrid = grid;

            canvasGo.SetActive(false);
        }

        EnsureDedicatedBrowserHost(state);
        RebindDedicatedSlotProjection(state);

        // The dedicated canvas begins disabled so it cannot flash during scene setup. Do not
        // bind the shared browser while that owner is hidden: its presentation state is then
        // cached as "already shown" and its cloned ItemSlotUI children never receive a valid
        // active hierarchy. Open/ApplyVisibleStorageMode activates this owner before binding.
        if (state.DedicatedCanvas != null && state.DedicatedCanvas.gameObject.activeInHierarchy)
            UpdateDedicatedOverlayLayout(screen, state);
    }

    /// <summary>
    /// The handover cancel action exits through the game's private OnClose path on current
    /// builds, bypassing the public Close method. Mirror the normal cleanup there so the
    /// detached PackRat canvas cannot outlive its owner screen.
    /// </summary>
    [HarmonyPatch("OnClose")]
    [HarmonyPostfix]
    public static void OnClose(HandoverScreen __instance)
    {
        HidePanelAndRestoreVehicle(__instance);
    }

    /// <summary>
    /// Covers the escape/back route before the UI manager removes the handover owner. This is
    /// intentionally a prefix so the dedicated canvas is gone in the same frame as the exit.
    /// </summary>
    [HarmonyPatch("Exit")]
    [HarmonyPrefix]
    public static void Exit(HandoverScreen __instance)
    {
        HidePanelAndRestoreVehicle(__instance);
    }

    /// <summary>
    /// Keeps the dedicated grid as the sole owner of the handover projection. It is deliberately
    /// rebuilt from direct children only; vehicle-slot descendants must never be assigned to the
    /// shared PackRat browser.
    /// </summary>
    private static void RebindDedicatedSlotProjection(PanelState state)
    {
        if (state?.DedicatedGrid == null)
            return;

        var slots = new List<ItemSlotUI>();
        var allSlotUis = state.DedicatedGrid.GetComponentsInChildren<ItemSlotUI>(includeInactive: true);
        if (allSlotUis != null)
        {
            for (var i = 0; i < allSlotUis.Length; i++)
            {
                var slotUi = allSlotUis[i];
                if (slotUi != null && slotUi.transform.parent == state.DedicatedGrid)
                    slots.Add(slotUi);
            }
        }

        while (slots.Count < 20 && state.SlotPrefab != null)
        {
            var slotGo = UnityEngine.Object.Instantiate(state.SlotPrefab.gameObject, state.DedicatedGrid);
            slotGo.name = $"PackRat_BackpackSlot_{slots.Count + 1}";
#if !MONO
            var slotUi = Utils.GetComponentSafe<ItemSlotUI>(slotGo);
#else
            var slotUi = slotGo.GetComponent<ItemSlotUI>();
#endif
            if (slotUi == null)
            {
                UnityEngine.Object.Destroy(slotGo);
                break;
            }

            slotUi.ClearSlot();
            slotUi.gameObject.SetActive(true);
            EnsureDedicatedSlotVisualState(slotUi);
            slots.Add(slotUi);
        }

        for (var i = 0; i < slots.Count; i++)
            EnsureDedicatedSlotVisualState(slots[i]);

        state.SlotUIs = slots.ToArray();
        state.SlotsPerPage = state.SlotUIs.Length;
    }

    /// <summary>
    /// Uses Schedule I's canonical ItemSlotUI prefab for the dedicated projection. The vehicle
    /// hierarchy is only a last-resort fallback, so a car's visibility, layout, and assignments
    /// cannot propagate into the deal backpack.
    /// </summary>
    private static ItemSlotUI GetDedicatedSlotTemplate(ItemSlotUI fallback)
    {
        try
        {
            var prefab = Singleton<ItemUIManager>.Instance?.ItemSlotUIPrefab;
            if (prefab != null)
                return prefab;
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"[HandoverUI] Canonical ItemSlotUI prefab unavailable: {ex.Message}");
        }

        return fallback;
    }

    private static void UpdateDedicatedOverlayLayout(HandoverScreen screen, PanelState state)
    {
        if (state?.DedicatedCard == null)
            return;

        var browserHost = EnsureDedicatedBrowserHost(state);
        if (browserHost == null)
            return;

        var grid = state.DedicatedGrid;
        var layout = grid != null ? grid.GetComponent<GridLayoutGroup>() : null;
        if (grid == null || layout == null || state.SlotUIs == null)
            return;

        if (state.ShowingVehicle)
        {
            StorageMenuPatch.ApplyEmbeddedInventoryBrowser(browserHost, grid, layout, state.SlotUIs,
                layoutView: 3, () => GetNearbyVehicleSlots(state), "VEHICLE STORAGE", screen.GetInstanceID());
        }
        else
        {
            StorageMenuPatch.ApplyEmbeddedBackpackBrowser(browserHost, grid, layout, state.SlotUIs,
                layoutView: 3, ownerId: screen.GetInstanceID());
        }

        UpdateDedicatedDealMatchAccents(screen, state);
        if (StorageMenuPatch.TryGetEditorBackpackBrowser(browserHost, out var editorBrowser) &&
            editorBrowser.SourcePane == EditorUiPane.Handover)
            BindEditorHandoverControls(screen, state, editorBrowser);
        else
            EnsureDedicatedTransferControls(screen, state);
        var overlayScale = Mathf.Clamp(Configuration.Instance.HandoverOverlayScale, 0.5f, 1.5f);
        ApplyDedicatedCardPlacement(state, overlayScale);
        DisableDedicatedDecorativeRaycasts(state);

    }

    private static void BindEditorHandoverControls(HandoverScreen screen, PanelState state,
        EditorUiStandaloneBrowserBinding browser)
    {
        if (screen == null || state == null || browser == null || browser.ModeRow == null ||
            browser.BackpackModeButton == null || browser.VehicleModeButton == null ||
            browser.TransferRow == null || browser.AutoFillButton == null || browser.TransferStatusLabel == null)
            return;

        var firstEditorBinding = state.EditorBackpackModeButton != browser.BackpackModeButton ||
            state.EditorVehicleModeButton != browser.VehicleModeButton ||
            state.BoundTransferButton != browser.AutoFillButton;
        if (state.ShowBackpackAction == null)
            state.ShowBackpackAction = () => SelectDedicatedStorageMode(state, showVehicle: false);
        if (state.ShowVehicleAction == null)
            state.ShowVehicleAction = () => SelectDedicatedStorageMode(state, showVehicle: true);

        if (state.EditorBackpackModeButton != null && state.EditorBackpackModeButton != browser.BackpackModeButton)
            EventHelper.RemoveListener(state.ShowBackpackAction, state.EditorBackpackModeButton.onClick);
        if (state.EditorVehicleModeButton != null && state.EditorVehicleModeButton != browser.VehicleModeButton)
            EventHelper.RemoveListener(state.ShowVehicleAction, state.EditorVehicleModeButton.onClick);

        state.EditorBackpackModeButton = browser.BackpackModeButton;
        state.EditorVehicleModeButton = browser.VehicleModeButton;
        EventHelper.RemoveListener(state.ShowBackpackAction, state.EditorBackpackModeButton.onClick);
        EventHelper.AddListener(state.ShowBackpackAction, state.EditorBackpackModeButton.onClick);
        EventHelper.RemoveListener(state.ShowVehicleAction, state.EditorVehicleModeButton.onClick);
        EventHelper.AddListener(state.ShowVehicleAction, state.EditorVehicleModeButton.onClick);

        if (state.DedicatedToggleRoot != null)
        {
            if (state.DedicatedToggleButton != null && state.ToggleAction != null)
                EventHelper.RemoveListener(state.ToggleAction, state.DedicatedToggleButton.onClick);
            UnityEngine.Object.Destroy(state.DedicatedToggleRoot.gameObject);
            state.DedicatedToggleRoot = null;
            state.DedicatedToggleButton = null;
        }

        state.TransferRoot = browser.TransferRow;
        state.TransferButton = browser.AutoFillButton;
        state.TransferStatusLabel = browser.TransferStatusLabel;
        if (state.TransferAction == null)
            state.TransferAction = () => AutoFillDeal(FindOwningScreen(state), state);
        if (state.BoundTransferButton != state.TransferButton)
        {
            if (state.BoundTransferButton != null)
                EventHelper.RemoveListener(state.TransferAction, state.BoundTransferButton.onClick);
            EventHelper.AddListener(state.TransferAction, state.TransferButton.onClick);
            state.BoundTransferButton = state.TransferButton;
        }

        var hasVehicle = ResolveNearbyVehicleStorage(state, forceRefresh: false) != null;
        state.EditorVehicleModeButton.interactable = hasVehicle;
        ApplyEditorHandoverModePresentation(state, hasVehicle);

        var requirements = GetHandoverRequirements(screen, GetCustomerSlots(screen));
        state.TransferButton.interactable = requirements.Count > 0;
        if (string.IsNullOrEmpty(state.TransferStatus))
            state.TransferStatusLabel.gameObject.SetActive(false);
        else
        {
            state.TransferStatusLabel.gameObject.SetActive(true);
            state.TransferStatusLabel.text = state.TransferStatus;
        }

        if (firstEditorBinding)
            ModLogger.Info("[EditorUI] Bound the editor-authored handover mode and transfer rows.");
    }

    private static void SelectDedicatedStorageMode(PanelState state, bool showVehicle)
    {
        if (state == null)
            return;

        var hasVehicle = ResolveNearbyVehicleStorage(state, forceRefresh: true) != null;
        state.ShowingVehicle = showVehicle && hasVehicle;
        ApplyVisibleStorageMode(state, hasVehicle);
        var screen = FindOwningScreen(state);
        ApplyPrimaryHeaderForMode(screen, state, state.ShowingVehicle);
        if (!state.ShowingVehicle)
            MelonLoader.MelonCoroutines.Start(ReapplyHeaderNextFrame(screen, state));

        if (state.DedicatedCanvas != null)
            UpdateDedicatedOverlayLayout(screen, state);
        else if (!state.ShowingVehicle)
            ApplyBackpackPage(state);
        else
            UpdatePagerControls(state, GetTotalPages(state), hasVehicle);

        UpdateDedicatedVehicleToggle(screen, state, hasVehicle);
        ApplyEditorHandoverModePresentation(state, hasVehicle);
    }

    private static void ApplyEditorHandoverModePresentation(PanelState state, bool hasVehicle)
    {
        if (state?.EditorBackpackModeButton == null || state.EditorVehicleModeButton == null)
            return;

        var selected = GetCurrentBackpackThemePalette().Accent;
        var idle = GetCurrentBackpackThemePalette().ControlAlt;
        var unavailable = new Color32(54, 61, 69, 220);
        var backpackImage = state.EditorBackpackModeButton.GetComponent<Image>();
        var vehicleImage = state.EditorVehicleModeButton.GetComponent<Image>();
        if (backpackImage != null)
            backpackImage.color = state.ShowingVehicle ? idle : selected;
        if (vehicleImage != null)
            vehicleImage.color = !hasVehicle ? unavailable : state.ShowingVehicle ? selected : idle;
    }

    /// <summary>
    /// Creates an internal PackRat-only host for the shared browser. The browser implementation
    /// deliberately resets its host's local position while it lays out slots; using the card as
    /// that host made native handover refreshes snap the complete card to screen center.
    /// </summary>
    private static RectTransform EnsureDedicatedBrowserHost(PanelState state)
    {
        if (state?.DedicatedCard == null)
            return null;

        var host = state.DedicatedBrowserHost;
        if (host == null || host.parent != state.DedicatedCard)
        {
            host = state.DedicatedCard.Find("PackRat_DedicatedBrowserHost") as RectTransform;
            if (host == null)
            {
                var hostGo = new GameObject("PackRat_DedicatedBrowserHost");
                host = hostGo.AddComponent<RectTransform>();
                host.SetParent(state.DedicatedCard, worldPositionStays: false);
                hostGo.AddComponent<LayoutElement>().ignoreLayout = true;
            }

            state.DedicatedBrowserHost = host;
        }

        host.anchorMin = Vector2.zero;
        host.anchorMax = Vector2.one;
        host.pivot = new Vector2(0.5f, 0.5f);
        host.offsetMin = Vector2.zero;
        host.offsetMax = Vector2.zero;
        host.localScale = Vector3.one;

        if (state.DedicatedGrid != null && state.DedicatedGrid.parent != host)
            state.DedicatedGrid.SetParent(host, worldPositionStays: false);

        return host;
    }

    /// <summary>
    /// Positions only the PackRat-owned card against its own screen-space canvas. The hidden
    /// vehicle clone belongs to the game's handover hierarchy and may be resized during
    /// CustomerItemsChanged, so it must not participate in this calculation.
    /// </summary>
    private static void ApplyDedicatedCardPlacement(PanelState state, float overlayScale)
    {
        var card = state?.DedicatedCard;
        if (card == null)
            return;

        UpdateDedicatedSafeArea(state);
        card.anchorMin = new Vector2(0.5f, 0.5f);
        card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        var cardSize = BackpackCardSize;
        if (state.DedicatedBrowserHost != null &&
            StorageMenuPatch.TryGetEditorBackpackBrowser(state.DedicatedBrowserHost, out var browser) &&
            browser?.Root != null)
        {
            var browserSize = browser.AppliedRootSize.x > 0f && browser.AppliedRootSize.y > 0f
                ? browser.AppliedRootSize
                : browser.Root.rect.size;
            if (browserSize.x > 0f && browserSize.y > 0f)
                cardSize = browserSize;
        }
        // The dedicated card is the safe-area owner for handover. Keep its bounds synchronized
        // with the runtime-expanded authored pane so clamping measures the complete slot surface.
        card.sizeDelta = cardSize;
        card.localScale = Vector3.one * overlayScale;
        card.anchoredPosition = GetDedicatedHandoverBackpackPosition();
        ClampDedicatedCardToSafeArea(state);
    }

    private static void UpdateDedicatedSafeArea(PanelState state)
    {
        var binding = state?.EditorDedicatedCanvas;
        if (binding?.SafeAreaRoot == null || binding.Canvas == null)
            return;

        var scaleFactor = Mathf.Max(0.001f, binding.Canvas.scaleFactor);
        var safePixels = Screen.safeArea;
        var safeRoot = binding.SafeAreaRoot;
        safeRoot.anchorMin = Vector2.zero;
        safeRoot.anchorMax = Vector2.one;
        safeRoot.pivot = new Vector2(0.5f, 0.5f);
        safeRoot.offsetMin = new Vector2(safePixels.xMin / scaleFactor, safePixels.yMin / scaleFactor);
        safeRoot.offsetMax = new Vector2(
            -(Screen.width - safePixels.xMax) / scaleFactor,
            -(Screen.height - safePixels.yMax) / scaleFactor);
    }

    private static void ClampDedicatedCardToSafeArea(PanelState state)
    {
        var card = state?.DedicatedCard;
        var canvas = state?.DedicatedCanvas;
        var canvasRoot = canvas?.transform as RectTransform;
        if (card == null || canvasRoot == null)
            return;

        FloatRect safeArea;
        if (state.EditorDedicatedCanvas?.PaneHost != null && card.parent == state.EditorDedicatedCanvas.PaneHost)
        {
            var safeHost = state.EditorDedicatedCanvas.PaneHost;
            safeArea = new FloatRect(safeHost.rect.xMin, safeHost.rect.yMin,
                safeHost.rect.width, safeHost.rect.height);
        }
        else
        {
            var scaleFactor = Mathf.Max(0.001f, canvas.scaleFactor);
            var safePixels = Screen.safeArea;
            safeArea = new FloatRect(
                canvasRoot.rect.xMin + safePixels.xMin / scaleFactor,
                canvasRoot.rect.yMin + safePixels.yMin / scaleFactor,
                safePixels.width / scaleFactor,
                safePixels.height / scaleFactor);
        }
        var cardScale = Mathf.Max(0.001f, card.localScale.x);
        var desired = new FloatRect(
            card.anchoredPosition.x + card.rect.xMin * cardScale,
            card.anchoredPosition.y + card.rect.yMin * cardScale,
            card.rect.width * cardScale,
            card.rect.height * cardScale);
        var clamped = UiBoundsPolicy.Clamp(desired, safeArea);
        card.anchoredPosition += new Vector2(clamped.X - desired.X, clamped.Y - desired.Y);
    }

    private static void DisableDedicatedDecorativeRaycasts(PanelState state)
    {
        var card = state?.DedicatedCard;
        if (card == null)
            return;

        var cardImage = card.GetComponent<Image>();
        if (cardImage != null)
            cardImage.raycastTarget = false;

        var visual = card.Find("PackRat_BackpackVisual");
        var header = visual?.Find("Header");
        var accent = header?.Find("Accent");
        var headerImage = header?.GetComponent<Image>();
        var accentImage = accent?.GetComponent<Image>();
        if (headerImage != null)
            headerImage.raycastTarget = false;
        if (accentImage != null)
            accentImage.raycastTarget = false;
    }

    /// <summary>
    /// Places the inventory switch inside the shared browser header. It is deliberately part of
    /// the same dedicated canvas as the slots, so a vanilla handover layout cannot hide it.
    /// </summary>
    private static void EnsureDedicatedVehicleToggle(HandoverScreen screen, PanelState state, bool hasVehicle)
    {
        if (state?.DedicatedCard == null)
            return;

        var host = FindDedicatedBrowserHeader(state);
        if (host == null)
            return;

        if (state.DedicatedToggleRoot == null || state.DedicatedToggleRoot.parent != host)
        {
            var existing = host.Find("PackRat_HandoverVehicleToggle") as RectTransform;
            if (existing != null)
            {
                state.DedicatedToggleRoot = existing;
                state.DedicatedToggleButton = existing.GetComponentInChildren<Button>(includeInactive: true);
            }
            else
            {
                var rootGo = new GameObject("PackRat_HandoverVehicleToggle");
                var root = rootGo.AddComponent<RectTransform>();
                root.SetParent(host, worldPositionStays: false);
                root.anchorMin = new Vector2(1f, 0.5f);
                root.anchorMax = new Vector2(1f, 0.5f);
                root.pivot = new Vector2(1f, 0.5f);
                root.anchoredPosition = new Vector2(-38f, 10f);
                root.sizeDelta = new Vector2(84f, 24f);
                rootGo.AddComponent<LayoutElement>().ignoreLayout = true;
                state.DedicatedToggleRoot = root;
                state.DedicatedToggleButton = CreateToggleButton("Show Vehicle", root, Vector2.zero);
            }
        }

        if (state.DedicatedToggleButton != null && state.ToggleAction != null)
        {
            EventHelper.RemoveListener(state.ToggleAction, state.DedicatedToggleButton.onClick);
            EventHelper.AddListener(state.ToggleAction, state.DedicatedToggleButton.onClick);
        }

        UpdateDedicatedVehicleToggle(screen, state, hasVehicle);
    }

    private static void UpdateDedicatedVehicleToggle(HandoverScreen screen, PanelState state, bool hasVehicle)
    {
        if (state?.EditorBackpackModeButton != null && state.EditorVehicleModeButton != null)
        {
            if (state.DedicatedToggleRoot != null)
                state.DedicatedToggleRoot.gameObject.SetActive(false);
            state.EditorVehicleModeButton.interactable = hasVehicle;
            ApplyEditorHandoverModePresentation(state, hasVehicle);
            return;
        }

        if (state?.DedicatedToggleRoot == null)
            return;

        var header = FindDedicatedBrowserHeader(state);
        if (header != null && state.DedicatedToggleRoot.parent != header)
        {
            state.DedicatedToggleRoot.SetParent(header, worldPositionStays: false);
        }

        state.DedicatedToggleRoot.sizeDelta = new Vector2(84f, 24f);
        state.DedicatedToggleRoot.localScale = Vector3.one;
        if (!AnchorDedicatedVehicleToggleBesideStack(state, header))
        {
            // The embedded browser can be rebuilt between the handover opening and the first
            // refresh. Keep a deterministic fallback until its shared Stack control exists.
            state.DedicatedToggleRoot.anchorMin = new Vector2(1f, 0.5f);
            state.DedicatedToggleRoot.anchorMax = new Vector2(1f, 0.5f);
            state.DedicatedToggleRoot.pivot = new Vector2(1f, 0.5f);
            state.DedicatedToggleRoot.anchoredPosition = new Vector2(-38f, 10f);
        }

        // Keep the control visible even while vehicle state is unavailable. That leaves an
        // explicit, disabled receipt rather than silently making the selector disappear.
        state.DedicatedToggleRoot.gameObject.SetActive(true);
        if (state.DedicatedToggleButton == null)
            return;

        state.DedicatedToggleButton.interactable = hasVehicle;
        ConfigureToggleButton(state.DedicatedToggleButton,
            !hasVehicle ? "NO VEHICLE" : state.ShowingVehicle ? "BACKPACK" : "VEHICLE", Vector2.zero);

        var buttonRect = state.DedicatedToggleButton.transform as RectTransform;
        if (buttonRect != null)
        {
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = Vector2.zero;
            buttonRect.sizeDelta = new Vector2(84f, 24f);
        }

        var label = state.DedicatedToggleButton.GetComponentInChildren<Text>(includeInactive: true);
        if (label != null)
        {
            label.fontSize = 10;
            label.fontStyle = FontStyle.Bold;
        }

        var image = state.DedicatedToggleButton.GetComponent<Image>();
        if (image != null)
            image.color = hasVehicle ? new Color32(35, 104, 145, 255) : new Color32(54, 61, 69, 220);

        ReserveDedicatedHeaderToggleSpace(state);
        state.DedicatedToggleRoot.SetAsLastSibling();
    }

    /// <summary>
    /// Positions the handover vehicle selector against the shared browser's Stack action instead
    /// of against a fixed header edge. This preserves their relationship as the browser scales or
    /// the handover canvas is rebuilt at a different resolution.
    /// </summary>
    private static bool AnchorDedicatedVehicleToggleBesideStack(PanelState state, RectTransform header)
    {
        if (state?.DedicatedToggleRoot == null || header == null)
            return false;

        var stackRect = header.Find("PackRat_BackpackConsolidateButton") as RectTransform;
        if (stackRect == null)
            return false;

        Canvas.ForceUpdateCanvases();

        const float gap = 6f;
        var stackLeftCenterWorld = stackRect.TransformPoint(
            new Vector3(stackRect.rect.xMin, stackRect.rect.center.y, 0f));
        var desiredRightEdgeInHeader = header.InverseTransformPoint(stackLeftCenterWorld);
        desiredRightEdgeInHeader.x -= gap;

        // With a top-right anchor and right-center pivot, anchoredPosition is the selected
        // control's right edge relative to the header's top-right corner.
        var headerTopRight = new Vector3(header.rect.xMax, header.rect.yMax, 0f);
        var root = state.DedicatedToggleRoot;
        root.anchorMin = new Vector2(1f, 1f);
        root.anchorMax = new Vector2(1f, 1f);
        root.pivot = new Vector2(1f, 0.5f);
        root.anchoredPosition = (Vector2)(desiredRightEdgeInHeader - headerTopRight);
        return true;
    }

    /// <summary>
    /// Adds a compact, PackRat-owned deal action underneath the shared browser pager. The row is
    /// deliberately a sibling of the pager instead of a child of the slot grid so filtering,
    /// paging, and the game-owned handover layout cannot reposition or hide it.
    /// </summary>
    private static void EnsureDedicatedTransferControls(HandoverScreen screen, PanelState state)
    {
        if (screen == null || state?.DedicatedCard == null)
            return;

        var pager = state.DedicatedBrowserHost?.Find("PackRat_BackpackPaging") as RectTransform;
        if (pager == null)
            pager = state.DedicatedCard.Find("PackRat_BackpackPaging") as RectTransform;
        if (pager == null)
            return;

        if (state.TransferRoot == null || state.TransferRoot.parent != state.DedicatedCard)
        {
            var existing = state.DedicatedCard.Find("PackRat_HandoverTransferControls") as RectTransform;
            if (existing != null)
            {
                state.TransferRoot = existing;
                state.TransferButton = existing.Find("AutoFillButton")?.GetComponent<Button>();
                state.TransferStatusLabel = existing.Find("Status")?.GetComponent<Text>();
            }
            else
            {
                var rootGo = new GameObject("PackRat_HandoverTransferControls");
                var root = rootGo.AddComponent<RectTransform>();
                root.SetParent(state.DedicatedCard, worldPositionStays: false);
                root.anchorMin = new Vector2(0.5f, 0.5f);
                root.anchorMax = new Vector2(0.5f, 0.5f);
                root.pivot = new Vector2(0.5f, 1f);
                root.sizeDelta = new Vector2(240f, 50f);
                rootGo.AddComponent<LayoutElement>().ignoreLayout = true;

                var background = rootGo.AddComponent<Image>();
                background.color = new Color32(10, 21, 31, 220);
                background.raycastTarget = false;

                var buttonGo = new GameObject("AutoFillButton");
                var buttonRect = buttonGo.AddComponent<RectTransform>();
                buttonRect.SetParent(root, worldPositionStays: false);
                buttonRect.anchorMin = new Vector2(0.5f, 1f);
                buttonRect.anchorMax = new Vector2(0.5f, 1f);
                buttonRect.pivot = new Vector2(0.5f, 1f);
                buttonRect.anchoredPosition = new Vector2(0f, -3f);
                buttonRect.sizeDelta = new Vector2(164f, 24f);
                var buttonImage = buttonGo.AddComponent<Image>();
                buttonImage.color = new Color32(39, 112, 156, 255);
                var button = buttonGo.AddComponent<Button>();
                button.targetGraphic = buttonImage;

                var buttonLabelGo = new GameObject("Label");
                var buttonLabelRect = buttonLabelGo.AddComponent<RectTransform>();
                buttonLabelRect.SetParent(buttonRect, worldPositionStays: false);
                buttonLabelRect.anchorMin = Vector2.zero;
                buttonLabelRect.anchorMax = Vector2.one;
                buttonLabelRect.offsetMin = Vector2.zero;
                buttonLabelRect.offsetMax = Vector2.zero;
                var buttonLabel = buttonLabelGo.AddComponent<Text>();
                buttonLabel.text = "AUTO-FILL DEAL";
                buttonLabel.font = ResolveUiFont(root);
                buttonLabel.fontSize = 11;
                buttonLabel.fontStyle = FontStyle.Bold;
                buttonLabel.alignment = TextAnchor.MiddleCenter;
                buttonLabel.color = Color.white;
                buttonLabel.raycastTarget = false;

                var statusGo = new GameObject("Status");
                var statusRect = statusGo.AddComponent<RectTransform>();
                statusRect.SetParent(root, worldPositionStays: false);
                statusRect.anchorMin = new Vector2(0.5f, 0f);
                statusRect.anchorMax = new Vector2(0.5f, 0f);
                statusRect.pivot = new Vector2(0.5f, 0f);
                statusRect.anchoredPosition = new Vector2(0f, 2f);
                statusRect.sizeDelta = new Vector2(232f, 18f);
                var status = statusGo.AddComponent<Text>();
                status.font = ResolveUiFont(root);
                status.fontSize = 9;
                status.alignment = TextAnchor.MiddleCenter;
                status.color = new Color32(191, 215, 232, 255);
                status.raycastTarget = false;

                state.TransferRoot = root;
                state.TransferButton = button;
                state.TransferStatusLabel = status;
            }
        }

        var scale = pager.localScale.x <= 0f ? 1f : pager.localScale.x;
        state.TransferRoot.localScale = Vector3.one * scale;
        state.TransferRoot.anchoredPosition = new Vector2(
            pager.anchoredPosition.x,
            pager.anchoredPosition.y - pager.sizeDelta.y * scale - 8f * scale
        );
        state.TransferRoot.gameObject.SetActive(true);
        state.TransferRoot.SetAsLastSibling();

        if (state.TransferAction == null)
            state.TransferAction = () => AutoFillDeal(FindOwningScreen(state), state);

        // The handover owner refreshes this browser while it is open. Bind a button only once
        // for its lifetime; repeatedly adding the same Action makes one click replay the fill.
        if (state.TransferButton != null && state.BoundTransferButton != state.TransferButton)
        {
            if (state.BoundTransferButton != null)
                EventHelper.RemoveListener(state.TransferAction, state.BoundTransferButton.onClick);

            EventHelper.AddListener(state.TransferAction, state.TransferButton.onClick);
            state.BoundTransferButton = state.TransferButton;
        }

        var requirements = GetHandoverRequirements(screen, GetCustomerSlots(screen));
        var canFill = requirements.Count > 0;
        if (state.TransferButton != null)
            state.TransferButton.interactable = canFill;

        // The blue frame is self-explanatory; reserve this line for a result only after the
        // player explicitly triggers auto-fill.
        if (string.IsNullOrEmpty(state.TransferStatus) && state.TransferStatusLabel != null)
            state.TransferStatusLabel.gameObject.SetActive(false);
    }

    /// <summary>
    /// Fills only the remaining exact contract requirements. Existing customer-slot contents are
    /// retained, each source uses the game's ItemSlot transfer methods, and the result reports
    /// the individual sources that contributed product.
    /// </summary>
    private static void AutoFillDeal(HandoverScreen screen, PanelState state)
    {
        using var profile = UiProfiler.Measure("handover", "auto_fill");
        UiProfiler.Event("handover", "auto_fill_requested");
        if (screen == null || state == null || !state.IsOpen)
            return;

        try
        {
            var customerSlots = GetCustomerSlots(screen);
            var requirements = GetHandoverRequirements(screen, customerSlots);
            if (requirements.Count == 0)
            {
                SetTransferStatus(state, "NOTHING REMAINING FOR THIS DEAL", new Color32(220, 190, 105, 255));
                return;
            }

            var sources = new List<HandoverTransferSource>
            {
                new HandoverTransferSource { Name = "PACK", Slots = GetBackpackSlots() },
                new HandoverTransferSource { Name = "VEHICLE", Slots = GetNearbyVehicleSlots(state) },
                new HandoverTransferSource { Name = "INVENTORY", Slots = GetPlayerInventorySlots() }
            };

            var movedTotalUnits = 0;
            var oversuppliedUnits = 0;
            for (var requirementIndex = 0; requirementIndex < requirements.Count; requirementIndex++)
            {
                var requirement = requirements[requirementIndex];
                var candidates = new List<AutoFillCandidate>();
                for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
                {
                    var source = sources[sourceIndex];
                    if (source.Slots == null)
                        continue;

                    for (var slotIndex = 0; slotIndex < source.Slots.Count; slotIndex++)
                    {
                        var sourceSlot = source.Slots[slotIndex];
                        var item = sourceSlot?.ItemInstance;
                        if (!ItemMatchesRequirement(item, requirement) ||
                            !TryGetPackagedProductAmount(item, out var packageAmount))
                            continue;

                        TryGetItemQuality(item, out _, out var qualityRank);
                        candidates.Add(new AutoFillCandidate(source.Name, slotIndex, requirement.ProductId,
                            qualityRank, packageAmount, Mathf.Max(0, sourceSlot.Quantity),
                            isPackaged: true,
                            isNativeAcceptable: !sourceSlot.IsRemovalLocked &&
                                CanMoveItemToAnyCustomerSlot(item, customerSlots)));
                    }
                }

                var plan = AutoFillPlanner.Plan(
                    new AutoFillRequirement(requirement.ProductId, requirement.QualityRank,
                        requirement.Remaining),
                    candidates);
                oversuppliedUnits += plan.OversuppliedUnits;
                for (var moveIndex = 0; moveIndex < plan.Moves.Count; moveIndex++)
                {
                    var move = plan.Moves[moveIndex];
                    var source = sources.FirstOrDefault(candidate =>
                        string.Equals(candidate.Name, move.Source, StringComparison.OrdinalIgnoreCase));
                    if (source?.Slots == null || move.SourceSlotIndex < 0 ||
                        move.SourceSlotIndex >= source.Slots.Count)
                        continue;

                    var sourceSlot = source.Slots[move.SourceSlotIndex];
                    if (!ItemMatchesRequirement(sourceSlot?.ItemInstance, requirement) ||
                        !TryGetPackagedProductAmount(sourceSlot.ItemInstance, out var packageAmount))
                        continue;

                    var movedPackages = MoveMatchingItemToDeal(sourceSlot, customerSlots, move.PackageCount);
                    if (movedPackages <= 0)
                        continue;

                    var movedUnits = movedPackages * packageAmount;
                    requirement.Remaining = Mathf.Max(0, requirement.Remaining - movedUnits);
                    source.MovedUnits += movedUnits;
                    movedTotalUnits += movedUnits;
                }
            }

            if (movedTotalUnits <= 0)
            {
                LogAutoFillMatchDiagnostics(requirements, sources);
                SetTransferStatus(state, "NO MATCHING PRODUCTS FOUND", new Color32(220, 190, 105, 255));
                return;
            }

            // Only a real transfer may ask the game to refresh its handover state. A failed plan
            // leaves the entire native surface untouched; PackRat then updates only its own card.
            NotifyHandoverItemsChanged(screen);
            UpdateDedicatedOverlayLayout(screen, state);

            var sourceReceipt = new List<string>();
            for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                var source = sources[sourceIndex];
                if (source.MovedUnits > 0)
                    sourceReceipt.Add($"{source.Name} {source.MovedUnits} UNITS");
            }

            var refreshedRequirements = GetHandoverRequirements(screen, customerSlots);
            var remaining = refreshedRequirements.Sum(requirement => requirement.Remaining);

            var outcome = remaining > 0
                ? $"MOVED {movedTotalUnits} UNITS  •  {remaining} STILL NEEDED"
                : $"FILLED {movedTotalUnits} UNITS";
            if (oversuppliedUnits > 0)
                outcome += $"  •  {oversuppliedUnits} EXTRA";
            SetTransferStatus(state, $"{outcome}  •  {string.Join(" / ", sourceReceipt)}  →  DEAL",
                remaining > 0 ? new Color32(220, 190, 105, 255) : new Color32(105, 225, 142, 255));
            ModLogger.Info($"[HandoverUI] Auto-fill completed: movedUnits={movedTotalUnits}, " +
                $"remainingUnits={remaining}, oversuppliedUnits={oversuppliedUnits}, " +
                $"sources={string.Join(", ", sourceReceipt)}.");
        }
        catch (Exception ex)
        {
            SetTransferStatus(state, "AUTO-FILL FAILED — SEE LOG", new Color32(238, 125, 112, 255));
            ModLogger.Error("HandoverScreenPatch.AutoFillDeal", ex);
        }
    }

    private static List<ItemSlot> GetCustomerSlots(HandoverScreen screen)
    {
        var result = new List<ItemSlot>();
        var slots = ReflectionUtils.TryGetFieldOrProperty(screen, "CustomerSlots")
            ?? ReflectionUtils.TryGetFieldOrProperty(screen, "_customerSlots");
        var count = ReflectionUtils.TryGetListCount(slots);
        for (var index = 0; index < count; index++)
        {
            var candidate = ReflectionUtils.TryGetListItem(slots, index);
            if (Utils.Is<ItemSlot>(candidate, out var slot) && slot != null)
                result.Add(slot);
        }

        return result;
    }

    private static List<ItemSlot> GetPlayerInventorySlots()
    {
        var result = new List<ItemSlot>();
#if MONO
        var inventory = PlayerInventory.Instance;
#else
        var inventory = PlayerSingleton<PlayerInventory>.Instance;
#endif
        var slots = inventory?.GetAllInventorySlots();
        if (slots == null)
            return result;

        foreach (var slot in slots.AsEnumerable())
        {
            if (slot != null)
                result.Add(slot);
        }

        return result;
    }

    private static List<HandoverRequirement> GetHandoverRequirements(HandoverScreen screen,
        List<ItemSlot> customerSlots)
    {
        var requirements = new List<HandoverRequirement>();
        var contract = ReflectionUtils.TryGetFieldOrProperty(screen, "CurrentContract")
            ?? ReflectionUtils.TryGetFieldOrProperty(screen, "_CurrentContract");
        var productList = ReflectionUtils.TryGetFieldOrProperty(contract, "ProductList");
        var entries = ReflectionUtils.TryGetFieldOrProperty(productList, "entries")
            ?? ReflectionUtils.TryGetFieldOrProperty(productList, "Entries");
        var count = ReflectionUtils.TryGetListCount(entries);
        for (var index = 0; index < count; index++)
        {
            var entry = ReflectionUtils.TryGetListItem(entries, index);
            var productId = ReflectionUtils.TryGetFieldOrProperty(entry, "ProductID")?.ToString();
            var qualityValue = ReflectionUtils.TryGetFieldOrProperty(entry, "Quality");
            var quality = qualityValue?.ToString();
            var quantityValue = ReflectionUtils.TryGetFieldOrProperty(entry, "Quantity");
            if (string.IsNullOrWhiteSpace(productId) || !TryConvertToInt(quantityValue, out var quantity) || quantity <= 0)
                continue;

            requirements.Add(new HandoverRequirement
            {
                ProductId = productId,
                Quality = quality ?? string.Empty,
                QualityRank = TryConvertToInt(qualityValue, out var qualityRank) ? qualityRank : -1,
                Remaining = quantity
            });
        }

        if (customerSlots != null)
        {
            for (var requirementIndex = 0; requirementIndex < requirements.Count; requirementIndex++)
            {
                var requirement = requirements[requirementIndex];
                for (var slotIndex = 0; slotIndex < customerSlots.Count; slotIndex++)
                {
                    var slot = customerSlots[slotIndex];
                    if (ItemMatchesRequirement(slot?.ItemInstance, requirement) &&
                        TryGetPackagedProductAmount(slot.ItemInstance, out var packageAmount))
                    {
                        var productUnits = Mathf.Max(0, slot.Quantity) * packageAmount;
                        requirement.Remaining = Mathf.Max(0, requirement.Remaining - productUnits);
                    }
                }
            }
        }

        requirements.RemoveAll(requirement => requirement.Remaining <= 0);
        return requirements;
    }

    private static bool ItemMatchesRequirement(ItemInstance item, HandoverRequirement requirement)
    {
        if (item?.Definition == null || requirement == null)
            return false;

        var itemProductId = ReflectionUtils.TryGetFieldOrProperty(item.Definition, "ID")?.ToString()
            ?? ReflectionUtils.TryGetFieldOrProperty(item.Definition, "Id")?.ToString();
        var definitionName = ReflectionUtils.TryGetFieldOrProperty(item.Definition, "Name")?.ToString();
        var itemName = ReflectionUtils.TryGetFieldOrProperty(item, "Name")?.ToString();
        if (!AreEquivalentProductIdentifiers(requirement.ProductId, itemProductId, definitionName, itemName))
            return false;

        if (!TryGetItemQuality(item, out var itemQuality, out var itemQualityRank))
            return false;

        if (requirement.QualityRank >= 0 && itemQualityRank >= 0)
            return itemQualityRank >= requirement.QualityRank;

        return string.Equals(itemQuality, requirement.Quality, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads quality from its shared ItemFramework base type. ProductItemInstance inherits this
    /// member, but the generated IL2CPP wrapper does not expose it reliably through reflection
    /// against the concrete product type.
    /// </summary>
    private static bool TryGetItemQuality(ItemInstance item, out string quality, out int qualityRank)
    {
        quality = string.Empty;
        qualityRank = -1;
        if (item == null)
            return false;

#if MONO
        var qualityItem = item as QualityItemInstance;
#else
        var qualityItem = item.TryCast<QualityItemInstance>();
#endif
        if (qualityItem != null)
        {
            quality = qualityItem.Quality.ToString();
            qualityRank = (int)qualityItem.Quality;
            return true;
        }

        // Preserve a compatibility fallback for custom or future instances that expose a
        // quality member without deriving from QualityItemInstance.
        var qualityValue = ReflectionUtils.TryGetFieldOrProperty(item, "Quality")
            ?? ReflectionUtils.TryGetFieldOrProperty(item, "quality");
        if (qualityValue == null)
            return false;

        quality = qualityValue.ToString() ?? string.Empty;
        if (TryConvertToInt(qualityValue, out var reflectedRank))
            qualityRank = reflectedRank;

        return !string.IsNullOrEmpty(quality);
    }

    /// <summary>
    /// Adds a non-interactive blue frame to every visible source slot that can satisfy a remaining
    /// handover requirement. The slot itself stays game-owned, so its tooltip, drag/drop, and
    /// stack behavior remain untouched.
    /// </summary>
    private static void UpdateDedicatedDealMatchAccents(HandoverScreen screen, PanelState state)
    {
        if (screen == null || state?.SlotUIs == null)
            return;

        var requirements = GetHandoverRequirements(screen, GetCustomerSlots(screen));
        for (var index = 0; index < state.SlotUIs.Length; index++)
        {
            var slotUi = state.SlotUIs[index];
            if (slotUi == null)
                continue;

            var sourceSlot = GetAssignedItemSlot(slotUi);
            var isMatch = false;
            if (sourceSlot?.ItemInstance != null)
            {
                for (var requirementIndex = 0; requirementIndex < requirements.Count; requirementIndex++)
                {
                    if (!ItemMatchesRequirement(sourceSlot.ItemInstance, requirements[requirementIndex]))
                        continue;

                    isMatch = true;
                    break;
                }
            }

            SetDealMatchAccent(slotUi, isMatch);
        }
    }

    private static ItemSlot GetAssignedItemSlot(ItemSlotUI slotUi)
    {
        if (slotUi == null)
            return null;

        // This is the public game-owned binding on both current runtime wrappers. Prefer it to
        // reflection so the accent follows the same slot the native UI is currently rendering.
        if (slotUi.assignedSlot != null)
            return slotUi.assignedSlot;

        var candidate = ReflectionUtils.TryGetFieldOrProperty(slotUi, "AssignedSlot")
            ?? ReflectionUtils.TryGetFieldOrProperty(slotUi, "assignedSlot")
            ?? ReflectionUtils.TryGetFieldOrProperty(slotUi, "Slot")
            ?? ReflectionUtils.TryGetFieldOrProperty(slotUi, "slot");
        return Utils.Is<ItemSlot>(candidate, out var slot) ? slot : null;
    }

    private static void SetDealMatchAccent(ItemSlotUI slotUi, bool enabled)
    {
        var slotRect = slotUi?.Rect ?? slotUi?.transform as RectTransform;
        if (slotRect == null)
            return;

        var accent = slotRect.Find("PackRat_DealMatchAccent") as RectTransform;
        if (accent == null)
        {
            var accentGo = new GameObject("PackRat_DealMatchAccent");
            accent = accentGo.AddComponent<RectTransform>();
            accent.SetParent(slotRect, worldPositionStays: false);
            accent.anchorMin = Vector2.zero;
            accent.anchorMax = Vector2.one;
            accent.pivot = new Vector2(0.5f, 0.5f);
            // Place the border just outside the slot's native background so it remains visible
            // over icon art without covering the item or its quantity label.
            accent.offsetMin = new Vector2(-2f, -2f);
            accent.offsetMax = new Vector2(2f, 2f);
            accentGo.AddComponent<LayoutElement>().ignoreLayout = true;
            CreateDealMatchAccentEdge(accent, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(0f, 4f));
            CreateDealMatchAccentEdge(accent, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(0f, 4f));
            CreateDealMatchAccentEdge(accent, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(4f, 0f));
            CreateDealMatchAccentEdge(accent, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(4f, 0f));
        }

        accent.gameObject.SetActive(enabled);
        if (enabled)
            accent.SetAsLastSibling();
    }

    private static void CreateDealMatchAccentEdge(RectTransform parent, string name, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        var edgeGo = new GameObject(name);
        var edge = edgeGo.AddComponent<RectTransform>();
        edge.SetParent(parent, worldPositionStays: false);
        edge.anchorMin = anchorMin;
        edge.anchorMax = anchorMax;
        edge.pivot = pivot;
        edge.anchoredPosition = anchoredPosition;
        edge.sizeDelta = sizeDelta;
        var image = edgeGo.AddComponent<Image>();
        image.color = new Color32(58, 171, 232, 255);
        image.raycastTarget = false;
    }

    /// <summary>
    /// Contracts use a product identifier while inventory definitions may expose the same value
    /// as an ID, display name, or packaging-derived name. Normalize these representations before
    /// comparing so an equivalent highest-quality product is not omitted from a deal fill.
    /// </summary>
    private static bool AreEquivalentProductIdentifiers(string required, params string[] candidates)
    {
        var normalizedRequired = NormalizeProductIdentifier(required);
        if (string.IsNullOrEmpty(normalizedRequired) || candidates == null)
            return false;

        for (var index = 0; index < candidates.Length; index++)
        {
            if (string.Equals(normalizedRequired, NormalizeProductIdentifier(candidates[index]),
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string NormalizeProductIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var buffer = new char[value.Length];
        var count = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsLetterOrDigit(value[index]))
                continue;

            buffer[count++] = char.ToUpperInvariant(value[index]);
        }

        return new string(buffer, 0, count);
    }

    private static int MoveMatchingItemToDeal(ItemSlot source, List<ItemSlot> customerSlots, int requestedQuantity)
    {
        if (source?.ItemInstance == null || source.IsRemovalLocked || customerSlots == null || requestedQuantity <= 0)
            return 0;

        var item = source.ItemInstance;
        var available = Mathf.Max(0, source.Quantity);
        for (var index = 0; index < customerSlots.Count && available > 0 && requestedQuantity > 0; index++)
        {
            var destination = customerSlots[index];
            if (destination == null || destination.IsAddLocked || !destination.DoesItemMatchHardFilters(item))
                continue;
            if (destination.ItemInstance != null && !destination.ItemInstance.CanStackWith(item, checkQuantities: false))
                continue;

            var capacity = Mathf.Max(0, destination.GetCapacityForItem(item, checkPlayerFilters: false));
            var requestedMove = Mathf.Min(available, Mathf.Min(requestedQuantity, capacity));
            if (requestedMove <= 0)
                continue;

            var transfer = item.GetCopy(requestedMove);
            if (transfer == null)
                continue;

            var beforeQuantity = destination.Quantity;
            destination.AddItem(transfer);
            var moved = Mathf.Clamp(destination.Quantity - beforeQuantity, 0, requestedMove);
            if (moved <= 0)
                continue;

            source.ChangeQuantity(-moved);
            return moved;
        }

        return 0;
    }

    private static bool TryGetPackagedProductAmount(ItemInstance item, out int packageAmount)
    {
        packageAmount = 0;
        if (item == null)
            return false;

#if MONO
        var product = item as ProductItemInstance;
#else
        var product = item.TryCast<ProductItemInstance>();
#endif
        if (product?.AppliedPackaging == null)
            return false;

        packageAmount = Mathf.Max(1, product.Amount);
        return true;
    }

    private static bool CanMoveItemToAnyCustomerSlot(ItemInstance item, List<ItemSlot> customerSlots)
    {
        if (item == null || customerSlots == null)
            return false;

        for (var index = 0; index < customerSlots.Count; index++)
        {
            var destination = customerSlots[index];
            if (destination == null || destination.IsAddLocked ||
                !destination.DoesItemMatchHardFilters(item))
                continue;
            if (destination.ItemInstance != null &&
                !destination.ItemInstance.CanStackWith(item, checkQuantities: false))
                continue;
            if (destination.GetCapacityForItem(item, checkPlayerFilters: false) > 0)
                return true;
        }

        return false;
    }

    private static bool TryConvertToInt(object value, out int result)
    {
        result = 0;
        if (value == null)
            return false;

        try
        {
            result = Convert.ToInt32(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void LogAutoFillMatchDiagnostics(List<HandoverRequirement> requirements,
        List<HandoverTransferSource> sources)
    {
        var requested = new List<string>();
        if (requirements != null)
        {
            for (var index = 0; index < requirements.Count; index++)
            {
                var requirement = requirements[index];
                requested.Add($"{requirement.ProductId} q={requirement.Quality}({requirement.QualityRank}) x{requirement.Remaining}");
            }
        }

        var candidates = new List<string>();
        if (sources != null)
        {
            for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                var source = sources[sourceIndex];
                if (source?.Slots == null)
                    continue;

                for (var slotIndex = 0; slotIndex < source.Slots.Count && candidates.Count < 24; slotIndex++)
                {
                    var item = source.Slots[slotIndex]?.ItemInstance;
                    if (item?.Definition == null)
                        continue;

                    var id = ReflectionUtils.TryGetFieldOrProperty(item.Definition, "ID")?.ToString() ?? "?";
                    var name = ReflectionUtils.TryGetFieldOrProperty(item, "Name")?.ToString() ?? "?";
                    var hasQuality = TryGetItemQuality(item, out var quality, out var qualityRank);
                    candidates.Add($"{source.Name}:{id}/{name} q={(hasQuality ? quality : "?")}" +
                        $"({(hasQuality ? qualityRank.ToString() : "?")})");
                }
            }
        }

        ModLogger.Warn($"[HandoverUI] Auto-fill no-match diagnostics: requested=[{string.Join("; ", requested)}], " +
            $"candidates=[{string.Join("; ", candidates)}].");
    }

    private static void NotifyHandoverItemsChanged(HandoverScreen screen)
    {
        if (screen == null)
            return;

        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic;
        var names = new[] { "CustomerItemsChanged", "UpdateDoneButton", "UpdateSuccessChance" };
        for (var index = 0; index < names.Length; index++)
        {
            try
            {
                var method = ReflectionUtils.GetMethod(screen.GetType(), names[index], flags);
                if (method != null && method.GetParameters().Length == 0)
                    method.Invoke(screen, null);
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[HandoverUI] Could not invoke {names[index]} after auto-fill: {ex.Message}");
            }
        }
    }

    private static void SetTransferStatus(PanelState state, string text, Color color)
    {
        if (state == null)
            return;

        state.TransferStatus = text;
        if (state.TransferStatusLabel == null)
            return;

        state.TransferStatusLabel.gameObject.SetActive(!string.IsNullOrEmpty(text));
        state.TransferStatusLabel.text = text;
        state.TransferStatusLabel.color = color;
    }

    private static RectTransform FindDedicatedBrowserHeader(PanelState state)
    {
        var visualRoot = state?.DedicatedGrid?.Find("PackRat_BackpackVisual") as RectTransform;
        return visualRoot?.Find("Header") as RectTransform;
    }

    private static void ReserveDedicatedHeaderToggleSpace(PanelState state)
    {
        var header = FindDedicatedBrowserHeader(state);
        var toggle = state?.DedicatedToggleRoot;
        if (header == null || toggle == null)
            return;

        Canvas.ForceUpdateCanvases();
        var toggleLeftWorld = toggle.TransformPoint(new Vector3(toggle.rect.xMin, toggle.rect.center.y, 0f));
        var toggleLeftInHeader = header.InverseTransformPoint(toggleLeftWorld);
        var reservedRightInset = Mathf.Max(126f, header.rect.xMax - toggleLeftInHeader.x + 8f);

        var title = header.Find("Title") as RectTransform;
        if (title != null)
            title.offsetMax = new Vector2(-reservedRightInset, title.offsetMax.y);

        var meta = header.Find("Meta") as RectTransform;
        if (meta != null)
            meta.offsetMax = new Vector2(-reservedRightInset, meta.offsetMax.y);
    }

    /// <summary>
    /// Handover slot prefabs are often cloned from a hidden vehicle surface. Do not inherit a
    /// hidden CanvasGroup into PackRat's dedicated canvas; the slot itself remains a native
    /// ItemSlotUI and still owns its rendering and transfer behavior.
    /// </summary>
    private static void EnsureDedicatedSlotVisualState(ItemSlotUI slotUi)
    {
        if (slotUi == null)
            return;

        var canvasGroups = slotUi.GetComponentsInChildren<CanvasGroup>(includeInactive: true);
        if (canvasGroups == null)
            return;

        for (var i = 0; i < canvasGroups.Length; i++)
        {
            var group = canvasGroups[i];
            if (group == null)
                continue;

            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
        }
    }

    private static void RegisterItemUiRaycaster(GraphicRaycaster raycaster)
    {
        if (raycaster == null)
            return;

        try
        {
            Singleton<ItemUIManager>.Instance?.AddRaycaster(raycaster);
        }
        catch (Exception ex)
        {
            ModLogger.Error("HandoverScreenPatch.RegisterItemUiRaycaster", ex);
        }
    }

    /// <summary>
    /// VehicleContainer fills the handover canvas. Positioning its cloned outer rect does not
    /// move the grid, so compact layout is applied directly to the cloned slot container.
    /// </summary>
    private static void ConfigureCompactBackpackLayout(HandoverScreen screen, PanelState state)
    {
        var config = Configuration.Instance;
        var overlayScale = Mathf.Clamp(config.HandoverOverlayScale, 0.5f, 1.5f);
        var contentPosition = new Vector2(
            config.HandoverOverlayOffsetX,
            BackpackContentCenterY * overlayScale + config.HandoverOverlayOffsetY
        );

        var screenRoot = screen?.Container?.transform as RectTransform;
        if (screenRoot != null && state?.BackpackVisualRoot != null && state.BackpackVisualRoot.parent != screenRoot)
        {
            state.BackpackVisualRoot.SetParent(screenRoot, worldPositionStays: false);
            state.BackpackVisualRoot.SetAsLastSibling();
        }

        if (state?.BackpackVisualRoot != null)
        {
            state.BackpackVisualRoot.anchorMin = new Vector2(0.5f, 0.5f);
            state.BackpackVisualRoot.anchorMax = new Vector2(0.5f, 0.5f);
            state.BackpackVisualRoot.pivot = new Vector2(0.5f, 0.5f);
            state.BackpackVisualRoot.anchoredPosition = contentPosition;
            state.BackpackVisualRoot.localScale = Vector3.one * overlayScale;
        }

        if (state?.BackpackSlotContainer != null)
        {
            var grid = state.BackpackSlotContainer;
            if (state.BackpackVisualRoot != null && grid.parent != state.BackpackVisualRoot)
                grid.SetParent(state.BackpackVisualRoot, worldPositionStays: false);
            grid.anchorMin = new Vector2(0.5f, 0.5f);
            grid.anchorMax = new Vector2(0.5f, 0.5f);
            grid.pivot = new Vector2(0.5f, 0.5f);
            grid.anchoredPosition = new Vector2(0f, -16f);
            grid.localScale = Vector3.one * BackpackGridScale;
        }

    }

    private static void EnsureBackpackVisuals(PanelState state)
    {
        if (state?.BackpackContainer == null)
            return;

        var root = state.BackpackVisualRoot;
        if (root == null)
            root = state.BackpackContainer.Find("PackRat_BackpackVisual") as RectTransform;

        if (root == null)
        {
            var rootGo = new GameObject("PackRat_BackpackVisual");
            root = rootGo.AddComponent<RectTransform>();
            root.SetParent(state.BackpackContainer, worldPositionStays: false);
            rootGo.AddComponent<Image>();
        }

        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        var config = Configuration.Instance;
        root.anchoredPosition = new Vector2(
            config.HandoverOverlayOffsetX,
            BackpackContentCenterY * Mathf.Clamp(config.HandoverOverlayScale, 0.5f, 1.5f) + config.HandoverOverlayOffsetY
        );
        root.sizeDelta = BackpackCardSize;
        root.localScale = Vector3.one * Mathf.Clamp(config.HandoverOverlayScale, 0.5f, 1.5f);
        root.SetAsFirstSibling();

        var layout = root.GetComponent<LayoutElement>();
        if (layout == null)
            layout = root.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        var rootImage = root.GetComponent<Image>();
        if (rootImage != null)
        {
            rootImage.color = GetCurrentBackpackThemePalette().Card;
            rootImage.raycastTarget = false;
        }

        var header = root.Find("Header") as RectTransform;
        if (header == null)
        {
            var headerGo = new GameObject("Header");
            header = headerGo.AddComponent<RectTransform>();
            header.SetParent(root, worldPositionStays: false);
            headerGo.AddComponent<Image>();
        }

        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = new Vector2(1f, 1f);
        header.pivot = new Vector2(0.5f, 1f);
        header.anchoredPosition = new Vector2(0f, -8f);
        header.sizeDelta = new Vector2(0f, 58f);
        var headerImage = header.GetComponent<Image>();
        if (headerImage != null)
        {
            headerImage.color = GetCurrentBackpackThemePalette().Header;
            headerImage.raycastTarget = false;
        }

        var accent = header.Find("Accent") as RectTransform;
        if (accent == null)
        {
            var accentGo = new GameObject("Accent");
            accent = accentGo.AddComponent<RectTransform>();
            accent.SetParent(header, worldPositionStays: false);
            accentGo.AddComponent<Image>();
        }

        accent.anchorMin = new Vector2(0f, 0f);
        accent.anchorMax = new Vector2(1f, 0f);
        accent.pivot = new Vector2(0.5f, 0f);
        accent.anchoredPosition = Vector2.zero;
        accent.sizeDelta = new Vector2(0f, 3f);
        var accentImage = accent.GetComponent<Image>();
        if (accentImage != null)
        {
            accentImage.color = GetCurrentBackpackThemePalette().Accent;
            accentImage.raycastTarget = false;
        }

        state.VisualTitleLabel = EnsureVisualLabel(header, "Title", new Vector2(0f, -18f), 19, FontStyle.Bold);
        state.VisualMetaLabel = EnsureVisualLabel(header, "Meta", new Vector2(0f, -40f), 11, FontStyle.Normal);
        state.BackpackVisualRoot = root;
        UpdateBackpackVisuals(state);
    }

    private static Text EnsureVisualLabel(RectTransform parent, string name, Vector2 position, int fontSize, FontStyle fontStyle)
    {
        var labelTransform = parent.Find(name) as RectTransform;
        if (labelTransform == null)
        {
            var labelGo = new GameObject(name);
            labelTransform = labelGo.AddComponent<RectTransform>();
            labelTransform.SetParent(parent, worldPositionStays: false);
            labelGo.AddComponent<Text>();
        }

        labelTransform.anchorMin = new Vector2(0.5f, 1f);
        labelTransform.anchorMax = new Vector2(0.5f, 1f);
        labelTransform.pivot = new Vector2(0.5f, 1f);
        labelTransform.anchoredPosition = position;
        labelTransform.sizeDelta = new Vector2(360f, 24f);

        var label = labelTransform.GetComponent<Text>();
        if (label != null)
        {
            label.font = ResolveUiFont(parent);
            label.fontSize = fontSize;
            label.fontStyle = fontStyle;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
        }

        return label;
    }

    private static void UpdateBackpackVisuals(PanelState state)
    {
        if (state == null)
            return;

        if (state.VisualTitleLabel != null)
            state.VisualTitleLabel.text = GetBackpackDisplayName().ToUpperInvariant();

        if (state.VisualMetaLabel != null)
        {
            var slotCount = GetBackpackSlots().Count;
            var pageCount = Mathf.Max(1, Mathf.CeilToInt(slotCount / (float)Mathf.Max(1, state.SlotsPerPage)));
            state.VisualMetaLabel.text = $"{slotCount} SLOTS  •  PAGE {state.CurrentPage + 1}/{pageCount}";
        }
    }

    private static void SetBackpackVisualVisible(PanelState state, bool visible)
    {
        if (state?.BackpackVisualRoot != null)
            state.BackpackVisualRoot.gameObject.SetActive(visible);
    }

    private static void FitBackpackVisualToSlots(PanelState state)
    {
        if (state?.BackpackVisualRoot == null)
            return;
        if (state.DedicatedCard != null)
        {
            UpdateDedicatedOverlayLayout(FindOwningScreen(state), state);
            return;
        }
        if (!TryGetSlotBoundsInTransform(state, state.BackpackVisualRoot, out var min, out var max))
            return;

        const float sidePadding = 20f;
        const float bottomPadding = 18f;
        const float headerHeight = 76f;
        var root = state.BackpackVisualRoot;
        var bottom = min.y - bottomPadding;
        var top = max.y + headerHeight;
        root.sizeDelta = new Vector2(
            Mathf.Max(1f, max.x - min.x + sidePadding * 2f),
            Mathf.Max(1f, top - bottom)
        );
    }

    private static void EnsurePagingControls(PanelState state)
    {
        var host = state?.DedicatedCard ?? state?.BackpackContainer;
        if (host == null)
            return;

        var pagingRoot = host.Find("PackRat_Paging");
        if (pagingRoot == null)
        {
            var parent = host.parent;
            if (parent != null)
                pagingRoot = parent.Find("PackRat_Paging");
        }

        if (pagingRoot != null && pagingRoot.parent != host)
            pagingRoot.SetParent(host, worldPositionStays: false);

        if (pagingRoot == null)
        {
            var rootGo = new GameObject("PackRat_Paging");
            pagingRoot = rootGo.transform;
            pagingRoot.SetParent(host, worldPositionStays: false);

            var rootRt = rootGo.AddComponent<RectTransform>();
            rootRt.pivot = new Vector2(0.5f, 1f);
            rootRt.sizeDelta = new Vector2(176f, 58f);
            rootRt.localScale = Vector3.one;

            var layout = rootGo.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;

            var bg = rootGo.AddComponent<Image>();
            bg.color = new Color32(16, 16, 16, 185);
            bg.raycastTarget = false;
        }

        EnsurePagingBackground(pagingRoot);

        LayoutElement existingLayout = null;
        try
        {
            existingLayout = pagingRoot.GetComponent<LayoutElement>();
        }
        catch
        {
        }

        if (existingLayout == null)
        {
            try
            {
                existingLayout = pagingRoot.gameObject.AddComponent<LayoutElement>();
            }
            catch
            {
            }
        }

        if (existingLayout != null)
            existingLayout.ignoreLayout = true;

        state.PrevButton = FindPagerButton(pagingRoot, "PackRat_PrevButton");
        state.NextButton = FindPagerButton(pagingRoot, "PackRat_NextButton");
        state.ToggleButton = FindPagerButton(pagingRoot, "PackRat_ViewToggleButton");
        state.PageLabel = FindPagerLabel(pagingRoot);

        if (state.PrevButton == null)
            state.PrevButton = CreatePagerButton("<", pagingRoot, new Vector2(-70f, -1f));
        if (state.NextButton == null)
            state.NextButton = CreatePagerButton(">", pagingRoot, new Vector2(70f, -1f));
        if (state.ToggleButton == null)
            state.ToggleButton = CreateToggleButton("Show Vehicle", pagingRoot, new Vector2(0f, -30f));
        if (state.PageLabel == null)
            state.PageLabel = CreatePagerLabel(pagingRoot, new Vector2(0f, -1f));

        if (state.PageLabel != null && state.PageLabel.name != "PackRat_PageLabel")
            state.PageLabel = null;
        if (state.PageLabel == null)
            state.PageLabel = CreatePagerLabel(pagingRoot, new Vector2(0f, -1f));

        ConfigurePagerButton(state.PrevButton, "<", new Vector2(-70f, -10f));
        ConfigurePagerButton(state.NextButton, ">", new Vector2(70f, -10f));
        ConfigureToggleButton(state.ToggleButton, state.ShowingVehicle ? "Show Backpack" : "Show Vehicle", new Vector2(0f, -34f));
        ConfigurePagerLabel(state.PageLabel, new Vector2(0f, -10f));

        RectTransform pagingRt = null;
        try
        {
            pagingRt = pagingRoot.GetComponent<RectTransform>();
        }
        catch
        {
        }

        if (pagingRt == null)
        {
            try
            {
                pagingRt = pagingRoot.gameObject.AddComponent<RectTransform>();
            }
            catch
            {
            }
        }

        state.PagingRoot = pagingRt;
        UpdatePagingLayout(state);

        if (TryGetGameObject(pagingRoot, out var pagingObject)
            && TryGetGameObject(host, out var containerObject))
        {
            SetLayerRecursively(pagingObject, containerObject.layer);
            pagingRoot.SetAsLastSibling();

            Canvas parentCanvas = null;
            try
            {
                parentCanvas = host.GetComponentInParent<Canvas>();
            }
            catch
            {
            }

            Canvas pagingCanvas = null;
            try
            {
                pagingCanvas = pagingObject.GetComponent<Canvas>();
            }
            catch
            {
            }

            if (pagingCanvas == null)
            {
                try
                {
                    pagingCanvas = pagingObject.AddComponent<Canvas>();
                }
                catch
                {
                }
            }

            if (pagingCanvas == null)
                return;

            pagingCanvas.overrideSorting = true;
            if (parentCanvas != null)
            {
                pagingCanvas.sortingLayerID = parentCanvas.sortingLayerID;
                pagingCanvas.sortingOrder = parentCanvas.sortingOrder + 200;
            }
            else
            {
                pagingCanvas.sortingOrder = 5000;
            }

            GraphicRaycaster raycaster = null;
            try
            {
                raycaster = pagingObject.GetComponent<GraphicRaycaster>();
            }
            catch
            {
            }

            if (raycaster == null)
            {
                try
                {
                    pagingObject.AddComponent<GraphicRaycaster>();
                }
                catch
                {
                }
            }
        }

        if (state.PrevAction == null)
            state.PrevAction = () =>
            {
                if (state.LastPageInputFrame == Time.frameCount)
                    return;
                if (state.CurrentPage <= 0)
                    return;

                state.LastPageInputFrame = Time.frameCount;
                state.CurrentPage--;
                ApplyBackpackPage(state);
            };

        if (state.NextAction == null)
            state.NextAction = () =>
            {
                if (state.LastPageInputFrame == Time.frameCount)
                    return;
                var totalPages = GetTotalPages(state);
                if (state.CurrentPage >= totalPages - 1)
                    return;

                state.LastPageInputFrame = Time.frameCount;
                state.CurrentPage++;
                ApplyBackpackPage(state);
            };

        if (state.ToggleAction == null)
            state.ToggleAction = () =>
            {
                var hasVehicle = ResolveNearbyVehicleStorage(state, forceRefresh: true) != null;
                if (!hasVehicle)
                    state.ShowingVehicle = false;
                else
                    state.ShowingVehicle = !state.ShowingVehicle;

                ApplyVisibleStorageMode(state, hasVehicle);
                var screen = FindOwningScreen(state);
                ApplyPrimaryHeaderForMode(screen, state, state.ShowingVehicle);
                if (!state.ShowingVehicle)
                    MelonLoader.MelonCoroutines.Start(ReapplyHeaderNextFrame(screen, state));

                if (!state.ShowingVehicle)
                {
                    if (state.DedicatedCanvas != null)
                        UpdateDedicatedOverlayLayout(screen, state);
                    else
                        ApplyBackpackPage(state);
                }
                else
                    UpdatePagerControls(state, GetTotalPages(state), hasVehicle);

                UpdateDedicatedVehicleToggle(screen, state, hasVehicle);
            };

        if (state.PrevButton != null)
        {
            EventHelper.RemoveListener(state.PrevAction, state.PrevButton.onClick);
            EventHelper.AddListener(state.PrevAction, state.PrevButton.onClick);
        }
        if (state.NextButton != null)
        {
            EventHelper.RemoveListener(state.NextAction, state.NextButton.onClick);
            EventHelper.AddListener(state.NextAction, state.NextButton.onClick);
        }

        if (state.ToggleButton != null)
        {
            EventHelper.RemoveListener(state.ToggleAction, state.ToggleButton.onClick);
            EventHelper.AddListener(state.ToggleAction, state.ToggleButton.onClick);
        }
    }

    private static Button CreatePagerButton(string text, Transform parent, Vector2 anchoredPos)
    {
        var buttonGo = new GameObject("PackRat_" + (text == "<" ? "Prev" : "Next") + "Button");
        buttonGo.transform.SetParent(parent, worldPositionStays: false);

        var rt = buttonGo.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(24f, 24f);

        var image = buttonGo.AddComponent<Image>();
        image.color = new Color32(60, 60, 60, 210);

        var button = buttonGo.AddComponent<Button>();
        button.targetGraphic = image;

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(buttonGo.transform, worldPositionStays: false);
        var labelRt = labelGo.AddComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        var label = labelGo.AddComponent<Text>();
        label.text = text;
        label.fontSize = 17;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.resizeTextForBestFit = false;
        label.font = ResolveUiFont(parent);
        label.raycastTarget = false;

        return button;
    }

    private static Text CreatePagerLabel(Transform parent, Vector2 anchoredPos)
    {
        var labelGo = new GameObject("PackRat_PageLabel");
        labelGo.transform.SetParent(parent, worldPositionStays: false);

        var rt = labelGo.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(104f, 22f);

        var label = labelGo.AddComponent<Text>();
        label.text = "1/1";
        label.fontSize = 13;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color32(220, 220, 220, 255);
        label.resizeTextForBestFit = false;
        label.font = ResolveUiFont(parent);
        label.raycastTarget = false;
        return label;
    }

    private static void EnsurePagingBackground(Transform pagingRoot)
    {
        if (pagingRoot == null)
            return;

        Image rootImage = null;
        try
        {
            rootImage = pagingRoot.GetComponent<Image>();
        }
        catch
        {
        }

        if (rootImage != null)
        {
            rootImage.enabled = false;
            rootImage.raycastTarget = false;
        }

        var bgTransform = pagingRoot.Find("PackRat_PagingBackground");
        if (bgTransform == null)
        {
            var bgGo = new GameObject("PackRat_PagingBackground");
            bgTransform = bgGo.transform;
            bgTransform.SetParent(pagingRoot, worldPositionStays: false);
        }

        var bgRt = bgTransform as RectTransform;
        if (bgRt == null)
            return;

        bgRt.anchorMin = new Vector2(0.5f, 0.5f);
        bgRt.anchorMax = new Vector2(0.5f, 0.5f);
        bgRt.pivot = new Vector2(0.5f, 0.5f);
        bgRt.anchoredPosition = new Vector2(0f, -50f);
        bgRt.sizeDelta = new Vector2(176f, 58f);

        Image bgImage = null;
        try
        {
            bgImage = bgRt.GetComponent<Image>();
        }
        catch
        {
        }

        if (bgImage == null)
        {
            try
            {
                bgImage = bgRt.gameObject.AddComponent<Image>();
            }
            catch
            {
            }
        }

        if (bgImage != null)
        {
            bgImage.color = new Color32(16, 16, 16, 185);
            bgImage.raycastTarget = false;
        }

        bgRt.SetAsFirstSibling();
    }

    private static Button CreateToggleButton(string text, Transform parent, Vector2 anchoredPos)
    {
        var buttonGo = new GameObject("PackRat_ViewToggleButton");
        buttonGo.transform.SetParent(parent, worldPositionStays: false);

        var rt = buttonGo.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(138f, 22f);

        var image = buttonGo.AddComponent<Image>();
        image.color = new Color32(64, 84, 112, 240);

        var button = buttonGo.AddComponent<Button>();
        button.targetGraphic = image;

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(buttonGo.transform, worldPositionStays: false);
        var labelRt = labelGo.AddComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        var label = labelGo.AddComponent<Text>();
        label.text = text;
        label.fontSize = 12;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.resizeTextForBestFit = false;
        label.font = ResolveUiFont(parent);
        label.raycastTarget = false;

        return button;
    }

    private static Button FindPagerButton(Transform pagingRoot, string name)
    {
        if (pagingRoot == null)
            return null;

        var buttonTransform = pagingRoot.Find(name);
        if (buttonTransform == null)
            return null;

        return buttonTransform.GetComponent<Button>();
    }

    private static Text FindPagerLabel(Transform pagingRoot)
    {
        if (pagingRoot == null)
            return null;

        var labelTransform = pagingRoot.Find("PackRat_PageLabel");
        if (labelTransform != null)
        {
            var namedLabel = labelTransform.GetComponent<Text>();
            if (namedLabel != null)
                return namedLabel;
        }

        return null;
    }

    private static void ConfigurePagerButton(Button button, string text, Vector2 anchoredPos)
    {
        if (button == null)
            return;

        var rt = button.transform as RectTransform;
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(32f, 24f);
        }

        var image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = new Color32(70, 95, 130, 240);
            image.raycastTarget = true;
        }

        var label = button.GetComponentInChildren<Text>(includeInactive: true);
        if (label == null)
        {
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(button.transform, worldPositionStays: false);
            var labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            label = labelGo.AddComponent<Text>();
        }

        label.text = text;
        label.font = ResolveUiFont(button.transform);
        label.fontSize = 18;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.raycastTarget = false;
        label.resizeTextForBestFit = false;

        button.gameObject.SetActive(true);
    }

    private static void ConfigurePagerLabel(Text label, Vector2 anchoredPos)
    {
        if (label == null)
            return;

        var rt = label.transform as RectTransform;
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(104f, 22f);
        }

        label.font = ResolveUiFont(label.transform);
        label.fontSize = 13;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color32(235, 235, 235, 255);
        label.raycastTarget = false;
        label.resizeTextForBestFit = false;
        label.gameObject.SetActive(true);
    }

    private static void ConfigureToggleButton(Button button, string text, Vector2 anchoredPos)
    {
        if (button == null)
            return;

        var rt = button.transform as RectTransform;
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(138f, 22f);
        }

        var image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = new Color32(64, 84, 112, 240);
            image.raycastTarget = true;
        }

        var label = button.GetComponentInChildren<Text>(includeInactive: true);
        if (label == null)
        {
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(button.transform, worldPositionStays: false);
            var labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            label = labelGo.AddComponent<Text>();
        }

        label.text = text;
        label.font = ResolveUiFont(button.transform);
        label.fontSize = 12;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.raycastTarget = false;
        label.resizeTextForBestFit = false;

        button.gameObject.SetActive(true);
    }

    private static void ApplyBackpackPage(PanelState state)
    {
        if (state == null || state.SlotUIs == null)
            return;

        var backpackSlots = GetBackpackSlots();
        var slotsPerPage = Mathf.Max(1, state.SlotUIs.Length);
        state.SlotsPerPage = slotsPerPage;

        var totalPages = Mathf.Max(1, Mathf.CeilToInt(backpackSlots.Count / (float)slotsPerPage));
        if (state.CurrentPage < 0)
            state.CurrentPage = 0;
        if (state.CurrentPage >= totalPages)
            state.CurrentPage = totalPages - 1;

        for (var i = 0; i < state.SlotUIs.Length; i++)
        {
            var ui = state.SlotUIs[i];
            if (ui == null)
                continue;

            ui.ClearSlot();

            var slotIndex = state.CurrentPage * slotsPerPage + i;
            if (slotIndex >= 0 && slotIndex < backpackSlots.Count)
            {
                ui.AssignSlot(backpackSlots[slotIndex]);
                ui.gameObject.SetActive(true);
            }
            else
            {
                ui.gameObject.SetActive(false);
            }
        }

        if (state.PageLabel != null)
            state.PageLabel.text = $"{state.CurrentPage + 1}/{totalPages}";

        UpdateBackpackVisuals(state);
        Canvas.ForceUpdateCanvases();
        FitBackpackVisualToSlots(state);
        UpdatePagingLayout(state);

        UpdatePagerControls(state, totalPages, ResolveNearbyVehicleStorage(state) != null);
    }

    private static void UpdatePagerControls(PanelState state, int totalPages, bool hasVehicle)
    {
        var showPaging = !state.ShowingVehicle;

        if (state.PageLabel != null)
        {
            state.PageLabel.gameObject.SetActive(showPaging);
            state.PageLabel.text = $"Page {state.CurrentPage + 1}/{Mathf.Max(1, totalPages)}";
        }

        if (state.PrevButton != null)
        {
            state.PrevButton.gameObject.SetActive(showPaging);
            state.PrevButton.interactable = showPaging && totalPages > 1 && state.CurrentPage > 0;
        }

        if (state.NextButton != null)
        {
            state.NextButton.gameObject.SetActive(showPaging);
            state.NextButton.interactable = showPaging && totalPages > 1 && state.CurrentPage < totalPages - 1;
        }

        if (state.ToggleButton != null)
        {
            state.ToggleButton.gameObject.SetActive(hasVehicle);
            state.ToggleButton.interactable = hasVehicle;

            var label = state.ToggleButton.GetComponentInChildren<Text>(includeInactive: true);
            if (label != null)
                label.text = state.ShowingVehicle ? "Show Backpack" : "Show Vehicle";
        }
    }

    private static List<ItemSlot> GetBackpackSlots()
    {
        var result = new List<ItemSlot>();
        try
        {
            if (!HasBackpack())
                return result;

            var storage = Player.Local != null ? Player.Local.GetBackpackStorage() : null;
            if (storage == null || storage.ItemSlots == null)
                return result;

            foreach (var slot in storage.ItemSlots.AsEnumerable())
            {
                if (slot != null)
                    result.Add(slot);
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("HandoverScreenPatch.GetBackpackSlots", ex);
        }

        return result;
    }

    private static void ClearSlotAssignments(PanelState panel)
    {
        if (panel?.SlotUIs == null)
            return;

        for (var i = 0; i < panel.SlotUIs.Length; i++)
        {
            var ui = panel.SlotUIs[i];
            if (ui == null)
                continue;

            ui.ClearSlot();
            ui.gameObject.SetActive(true);
        }
    }

    private static void RebuildQuickMove(HandoverScreen screen, S1LandVehicle nearbyVehicle)
    {
        if (screen == null)
            return;

#if MONO
        var inventory = PlayerInventory.Instance;
#else
        var inventory = PlayerSingleton<PlayerInventory>.Instance;
#endif
        if (inventory == null)
            return;

        var allSlots = inventory.GetAllInventorySlots();
        if (allSlots == null)
            return;

        if (nearbyVehicle?.Storage?.ItemSlots != null)
        {
            foreach (var slot in nearbyVehicle.Storage.ItemSlots.AsEnumerable())
            {
                if (slot != null)
                    allSlots.Add(slot);
            }
        }

        foreach (var slot in GetBackpackSlots())
            allSlots.Add(slot);

        var customerSlots = ReflectionUtils.TryGetFieldOrProperty(screen, "CustomerSlots");
        if (customerSlots == null)
            return;

        var secondaryManaged = new List<ItemSlot>();
        var count = ReflectionUtils.TryGetListCount(customerSlots);
        for (var i = 0; i < count; i++)
        {
            var item = ReflectionUtils.TryGetListItem(customerSlots, i);
            if (item == null)
                continue;

            if (!Utils.Is<ItemSlot>(item, out var slot))
                continue;
            if (slot != null)
                secondaryManaged.Add(slot);
        }

#if !MONO
        Singleton<ItemUIManager>.Instance.EnableQuickMove(allSlots, secondaryManaged.ToIl2CppList());
#else
        Singleton<ItemUIManager>.Instance.EnableQuickMove(allSlots, secondaryManaged);
#endif
    }

    private static bool IsTextLikeComponent(Component component)
    {
        if (component == null)
            return false;

        if (component is S1TMP || component is Text)
            return true;

        var typeName = component.GetType().Name;
        if (typeName.Contains("Text", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("TMP", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var getter = component.GetType().GetMethod("get_text", Type.EmptyTypes);
        if (getter != null)
            return true;

        var value = ReflectionUtils.TryGetFieldOrProperty(component, "text");
        return value != null;
    }

    private static S1TMP GetTmpLabel(GameObject gameObject)
    {
        if (gameObject == null)
            return null;

#if !MONO
        return Utils.GetComponentSafe<S1TMP>(gameObject);
#else
        return gameObject.GetComponent<S1TMP>();
#endif
    }

    private static void SetTmpText(S1TMP label, string text)
    {
        if (label == null)
            return;

        var safeText = text ?? string.Empty;
        try
        {
            label.text = safeText;
            return;
        }
        catch
        {
        }

        try
        {
            label.SetText(safeText);
        }
        catch
        {
        }
    }

    private static string GetLabelText(Component component)
    {
        if (component == null)
            return string.Empty;

        if (component is S1TMP tmpLabel)
            return tmpLabel.text ?? string.Empty;

        if (component is Text uiText)
            return uiText.text ?? string.Empty;

        var value = ReflectionUtils.TryGetFieldOrProperty(component, "text");
        if (value != null)
            return value.ToString() ?? string.Empty;

        var getter = component.GetType().GetMethod("get_text", Type.EmptyTypes);
        if (getter != null)
        {
            try
            {
                var result = getter.Invoke(component, null);
                return result?.ToString() ?? string.Empty;
            }
            catch
            {
            }
        }

#if !MONO
        if (component.gameObject != null)
        {
            var textComp = Utils.GetComponentSafe<Text>(component.gameObject);
            if (textComp != null)
                return textComp.text ?? string.Empty;
        }
#endif

        return value as string ?? value?.ToString() ?? string.Empty;
    }

    private static string NormalizeLabelText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var result = new System.Text.StringBuilder(text.Length);
        var insideTag = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '<')
            {
                insideTag = true;
                continue;
            }

            if (ch == '>')
            {
                insideTag = false;
                continue;
            }

            if (insideTag)
                continue;

            result.Append(ch);
        }

        return result.ToString().Replace("\r", " ").Replace("\n", " ").Trim();
    }

    /// <summary>
    /// Sets the display text on a label component. Uses reflection for non-UnityEngine.UI.Text types.
    /// IL2CPP: Tries TryCast to Text, then GetComponentSafe on same GameObject, then reflection.
    /// </summary>
    private static void SetLabelText(Component component, string text)
    {
        if (component == null)
            return;

        var safeText = text ?? string.Empty;

        if (component is S1TMP tmpLabel)
        {
            SetTmpText(tmpLabel, safeText);
            return;
        }

        if (component is Text uiText)
        {
            uiText.text = safeText;
            return;
        }

#if !MONO
        // IL2CPP: component may be Il2Cpp wrapper that fails "is Text"; try Text on same GameObject
        if (component.gameObject != null)
        {
            var textOnGo = Utils.GetComponentSafe<Text>(component.gameObject);
            if (textOnGo != null)
            {
                try
                {
                    textOnGo.text = safeText;
                    return;
                }
                catch (Exception ex)
                {
                    ModLogger.Debug($"HandoverScreenPatch: GetComponentSafe<Text>.text set failed: {ex.Message}");
                }
            }
        }
#endif

        if (ReflectionUtils.TrySetFieldOrProperty(component, "text", safeText))
            return;

        if (TryInvokeTextSetter(component, "SetText", safeText))
            return;

        if (TryInvokeTextSetter(component, "set_text", safeText))
            return;

        ReflectionUtils.TrySetFieldOrProperty(component, "m_text", safeText);
    }

    private static bool TryInvokeTextSetter(Component component, string methodName, string text)
    {
        if (component == null)
            return false;

        var methods = component.GetType().GetMethods();
        for (var i = 0; i < methods.Length; i++)
        {
            var method = methods[i];
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                continue;

            var parameters = method.GetParameters();
            try
            {
                if (parameters.Length == 1)
                {
                    method.Invoke(component, new object[] { text });
                    return true;
                }

                if (parameters.Length == 2)
                {
                    method.Invoke(component, new object[] { text, true });
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool HasBackpack()
    {
        return PlayerBackpack.Instance != null && PlayerBackpack.Instance.IsUnlocked;
    }

    private static bool TryApplyHeaderPair(Component titleLabel, Component subtitleLabel, string titleText, string subtitleText)
    {
        var applied = false;

        if (titleLabel != null)
        {
            SetLabelText(titleLabel, titleText);
            applied = true;
        }

        if (subtitleLabel != null)
        {
            SetLabelText(subtitleLabel, subtitleText);
            applied = true;
        }

        return applied;
    }

    private static void SetHeaderPairActive(Component titleLabel, Component subtitleLabel, bool active)
    {
        SetComponentActive(titleLabel, active);
        SetComponentActive(subtitleLabel, active);
    }

    private static void HideOverlayHeader(PanelState panel)
    {
        if (panel?.BackpackHeaderRoot != null)
            panel.BackpackHeaderRoot.gameObject.SetActive(false);
    }

    private static void ShowOverlayHeader(PanelState panel, string titleText, string subtitleText)
    {
        if (panel?.BackpackHeaderRoot == null)
            return;

        SetHeaderPairActive(panel.SourceTitleLabel, panel.SourceSubtitleLabel, false);
        SetHeaderPairActive(panel.ClonedTitleLabel, panel.ClonedSubtitleLabel, false);
        SetLabelText(panel.OverlayTitleLabel, titleText);
        SetLabelText(panel.OverlaySubtitleLabel, subtitleText);
        panel.BackpackHeaderRoot.gameObject.SetActive(true);
    }

    private static void ApplyPrimaryHeaderForMode(HandoverScreen screen, PanelState panel, bool showingVehicle)
    {
        if (panel == null)
            return;

        var backpackTitle = PlayerBackpack.Instance?.CurrentTier?.Name ?? PlayerBackpack.StorageName;
        var backpackSubtitle = "Items from your backpack.";

        var targetTitle = showingVehicle ? VehicleHeaderTitle : backpackTitle;
        var targetSubtitle = showingVehicle ? VehicleHeaderSubtitle : backpackSubtitle;

        // The dedicated browser card is the active inventory surface for both backpack and
        // vehicle modes. The legacy header routine treats BackpackVisualRoot as backpack-only;
        // applying that rule here would deactivate the entire vehicle projection.
        if (panel.DedicatedCanvas != null)
        {
            SetHeaderPairActive(panel.SourceTitleLabel, panel.SourceSubtitleLabel, false);
            SetHeaderPairActive(panel.ClonedTitleLabel, panel.ClonedSubtitleLabel, false);
            HideOverlayHeader(panel);
            return;
        }

        UpdateBackpackHeaderTexts(panel);
        HideOverlayHeader(panel);
        SetBackpackVisualVisible(panel, !showingVehicle);

        if (showingVehicle)
        {
            var appliedVehicleHeader = TryApplyHeaderPair(panel.SourceTitleLabel, panel.SourceSubtitleLabel, targetTitle, targetSubtitle);
            SetHeaderPairActive(panel.ClonedTitleLabel, panel.ClonedSubtitleLabel, false);
            if (appliedVehicleHeader)
            {
                SetHeaderPairActive(panel.SourceTitleLabel, panel.SourceSubtitleLabel, true);
                return;
            }

            SetHeaderPairActive(panel.SourceTitleLabel, panel.SourceSubtitleLabel, false);
            ShowOverlayHeader(panel, targetTitle, targetSubtitle);
            return;
        }

        SetHeaderPairActive(panel.SourceTitleLabel, panel.SourceSubtitleLabel, false);
        SetHeaderPairActive(panel.ClonedTitleLabel, panel.ClonedSubtitleLabel, false);
        UpdateBackpackVisuals(panel);
    }

    private static HandoverScreen FindOwningScreen(PanelState state)
    {
        if (state?.BackpackContainer == null)
            return null;

        try
        {
            return state.BackpackContainer.GetComponentInParent<HandoverScreen>();
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyVisibleStorageMode(PanelState state, bool hasVehicle)
    {
        if (state == null)
            return;

        if (!hasVehicle)
            state.ShowingVehicle = false;

        var showVehicle = hasVehicle && state.ShowingVehicle;

        if (state.DedicatedCanvas != null)
        {
            // The shared browser binds and activates its native ItemSlotUI children. Do that only
            // after its owner canvas is active; otherwise Unity reports the slots inactive and
            // the deal projection can remain visually empty after the handover first initializes.
            state.DedicatedCanvas.gameObject.SetActive(true);
            if (state.DedicatedCard != null)
                state.DedicatedCard.gameObject.SetActive(true);
            if (state.DedicatedGrid != null)
                state.DedicatedGrid.gameObject.SetActive(true);
            RestoreDedicatedBrowserVisibility(state);
            var screen = FindOwningScreen(state);
            UpdateDedicatedOverlayLayout(screen, state);
            LogDedicatedProjectionState(state, "visible-bind");
            // Both inventories use the same dedicated PackRat surface. Never reactivate the
            // vanilla vehicle hierarchy here: it belongs to the animated handover owner and
            // can otherwise reflow or obscure the deal UI.
            if (state.BackpackContainer != null)
                state.BackpackContainer.gameObject.SetActive(false);
            if (state.VehicleContainer != null)
            {
                state.VehicleContainer.anchoredPosition = state.VehicleOriginalAnchoredPos;
                state.VehicleContainer.gameObject.SetActive(false);
            }
            if (state.PagingRoot != null)
                state.PagingRoot.gameObject.SetActive(false);
            UpdateDedicatedVehicleToggle(screen, state, hasVehicle);
            return;
        }

        // Force VehicleContainer off first so it never stays visible in backpack mode (game may re-enable it elsewhere).
        if (state.VehicleContainer != null)
        {
            state.VehicleContainer.anchoredPosition = state.VehicleOriginalAnchoredPos;
            state.VehicleContainer.gameObject.SetActive(showVehicle);
        }

        if (state.BackpackContainer != null)
        {
            CenterBackpackContainer(state);
            state.BackpackContainer.gameObject.SetActive(true);
        }

        ConfigureCompactBackpackLayout(FindOwningScreen(state), state);

        if (state.BackpackSlotContainer != null)
            state.BackpackSlotContainer.gameObject.SetActive(!showVehicle);

        SetClonedHeaderVisibility(state, false);

        HideOverlayHeader(state);
        UpdateBackpackHeaderLayout(state);

        // Force VehicleContainer again at end in case game re-enabled it this frame.
        if (state.VehicleContainer != null)
            state.VehicleContainer.gameObject.SetActive(showVehicle);

        ModLogger.Info($"[Handover] ApplyVisibleStorageMode: showVehicle={showVehicle}, BackpackHeaderRoot.active={state.BackpackHeaderRoot?.gameObject.activeSelf}, VehicleContainer.active={state.VehicleContainer?.gameObject.activeSelf}");

        if (state.PagingRoot != null)
            state.PagingRoot.gameObject.SetActive(true);
    }

    /// <summary>
    /// The shared backpack browser applies its open animation while the handover screen is
    /// initially hidden. A retained CanvasGroup alpha of zero would keep the later active canvas
    /// invisible, so the owner explicitly restores PackRat-only groups before rebinding slots.
    /// </summary>
    private static void RestoreDedicatedBrowserVisibility(PanelState state)
    {
        if (state?.DedicatedGrid == null)
            return;

        var groups = state.DedicatedGrid.GetComponentsInChildren<CanvasGroup>(includeInactive: true);
        if (groups == null)
            return;

        for (var index = 0; index < groups.Length; index++)
        {
            var group = groups[index];
            if (group == null)
                continue;

            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
        }
    }

    /// <summary>
    /// Emits the ownership and active-state receipt needed to distinguish a missing slot bind
    /// from a hidden dedicated canvas. This runs only when the handover surface is made visible.
    /// </summary>
    private static void LogDedicatedProjectionState(PanelState state, string phase)
    {
        if (state == null)
            return;

        var totalSlots = state.SlotUIs?.Length ?? 0;
        var activeSlots = 0;
        if (state.SlotUIs != null)
        {
            for (var index = 0; index < state.SlotUIs.Length; index++)
            {
                var slotUi = state.SlotUIs[index];
                if (slotUi != null && slotUi.gameObject.activeInHierarchy)
                    activeSlots++;
            }
        }

        ModLogger.Info(
            $"[HandoverUI] Dedicated projection {phase}: " +
            $"canvas={state.DedicatedCanvas?.gameObject.activeInHierarchy}, " +
            $"card={state.DedicatedCard?.gameObject.activeInHierarchy}, " +
            $"grid={state.DedicatedGrid?.gameObject.activeInHierarchy}, " +
            $"slots={activeSlots}/{totalSlots}."
        );
    }

    private static Vector2 GetDedicatedHandoverBackpackPosition()
    {
        var config = Configuration.Instance;
        return new Vector2(config.HandoverOverlayOffsetX, config.HandoverOverlayOffsetY);
    }

    private static Vector2 GetHandoverBackpackPosition(PanelState state)
    {
        var config = Configuration.Instance;
        var desired = new Vector2(
            config.HandoverOverlayOffsetX,
            config.HandoverOverlayOffsetY
        );

        var container = state?.BackpackContainer;
        var parent = container?.parent as RectTransform;
        if (container == null || parent == null)
            return desired;

        const float margin = 24f;
        var halfWidth = Mathf.Max(0f, parent.rect.width * 0.5f - container.rect.width * 0.5f - margin);
        var halfHeight = Mathf.Max(0f, parent.rect.height * 0.5f - container.rect.height * 0.5f - margin);
        return new Vector2(
            Mathf.Clamp(desired.x, -halfWidth, halfWidth),
            Mathf.Clamp(desired.y, -halfHeight, halfHeight)
        );
    }

    private static void UpdatePagingLayout(PanelState state)
    {
        var host = state?.DedicatedCard ?? state?.BackpackContainer;
        if (state?.PagingRoot == null || host == null)
            return;

        if (state.PagingRoot.parent != host)
            state.PagingRoot.SetParent(host, worldPositionStays: false);

        var rootRt = state.PagingRoot;
        rootRt.anchorMin = new Vector2(0.5f, 0.5f);
        rootRt.anchorMax = new Vector2(0.5f, 0.5f);
        rootRt.pivot = new Vector2(0.5f, 1f);
        rootRt.localScale = Vector3.one;
        if (state.DedicatedCard != null)
        {
            rootRt.anchoredPosition = new Vector2(0f, -state.DedicatedCard.rect.height * 0.5f - 92f);
            return;
        }

        var bottomOfContainer = -(state.BackpackContainer.rect.height * state.BackpackContainer.pivot.y);
        if (TryGetBottomSlotYInContainer(state, out var bottomSlotY))
            bottomOfContainer = bottomSlotY;

        // Keep the controls close to the visible slots. The old fixed 150px gap pushed the
        // pager below the screen when the game or player used a reduced UI scale.
        rootRt.anchoredPosition = new Vector2(0f, bottomOfContainer - 20f);
    }

    private static bool TryGetBottomSlotYInContainer(PanelState state, out float y)
    {
        y = 0f;
        if (state?.SlotUIs == null || state.BackpackContainer == null)
            return false;

        var found = false;
        var minY = float.MaxValue;
        for (var i = 0; i < state.SlotUIs.Length; i++)
        {
            var slotUi = state.SlotUIs[i];
            if (slotUi == null)
                continue;

            var slotRt = slotUi.transform as RectTransform;
            if (slotRt == null)
                continue;

            var worldBottom = slotRt.TransformPoint(new Vector3(0f, slotRt.rect.yMin, 0f));
            var localBottom = state.BackpackContainer.InverseTransformPoint(worldBottom);
            if (localBottom.y < minY)
            {
                minY = localBottom.y;
                found = true;
            }
        }

        if (!found)
            return false;

        y = minY;
        return true;
    }

    private static bool TryGetSlotBoundsInTransform(PanelState state, RectTransform target, out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);
        if (state?.SlotUIs == null || target == null)
            return false;

        var found = false;
        for (var i = 0; i < state.SlotUIs.Length; i++)
        {
            var slotUi = state.SlotUIs[i];
            if (slotUi == null || !slotUi.gameObject.activeInHierarchy)
                continue;

            var slot = slotUi.transform as RectTransform;
            if (slot == null)
                continue;

            var corners = new[]
            {
                new Vector3(slot.rect.xMin, slot.rect.yMin, 0f),
                new Vector3(slot.rect.xMax, slot.rect.yMax, 0f)
            };
            for (var cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
            {
                var local = target.InverseTransformPoint(slot.TransformPoint(corners[cornerIndex]));
                min.x = Mathf.Min(min.x, local.x);
                min.y = Mathf.Min(min.y, local.y);
                max.x = Mathf.Max(max.x, local.x);
                max.y = Mathf.Max(max.y, local.y);
                found = true;
            }
        }

        return found;
    }

    private static bool IsComponentAlive(Component component)
    {
        if (component == null)
            return false;

        try
        {
            return component.gameObject != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetGameObject(Component component, out GameObject gameObject)
    {
        gameObject = null;
        if (component == null)
            return false;

        try
        {
            gameObject = component.gameObject;
            return gameObject != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetRectTransform(Component component, out RectTransform rectTransform)
    {
        rectTransform = null;
        if (component == null)
            return false;

        try
        {
            rectTransform = component.transform as RectTransform;
            return rectTransform != null;
        }
        catch
        {
            return false;
        }
    }

    private static void SetComponentActive(Component component, bool active)
    {
        if (component == null)
            return;

        try
        {
            if (component.gameObject != null)
                component.gameObject.SetActive(active);
        }
        catch
        {
        }
    }

    private static void SetClonedHeaderVisibility(PanelState state, bool visible)
    {
        if (state?.BackpackContainer == null)
            return;

        var textLike = state.BackpackContainer.GetComponentsInChildren<Component>(true)
            .Where(IsTextLikeComponent)
            .ToArray();

        for (var i = 0; i < textLike.Length; i++)
        {
            var label = textLike[i];
            if (label == null)
                continue;

            if (IsUnderTransform(label, state.BackpackSlotContainer))
                continue;
            if (IsUnderTransform(label, state.PagingRoot))
                continue;
            if (IsUnderTransform(label, state.BackpackHeaderRoot))
                continue;

            SetComponentActive(label, visible);
        }
    }

    private static bool IsUnderTransform(Component component, Transform parent)
    {
        if (component == null || parent == null)
            return false;

        try
        {
            return component.transform != null && component.transform.IsChildOf(parent);
        }
        catch
        {
            return false;
        }
    }

    private static Font ResolveUiFont(Transform context)
    {
        if (context != null)
        {
            var text = context.GetComponentsInParent<Text>(true).FirstOrDefault(t => t != null && t.font != null);
            if (text != null)
                return text.font;
        }

        var arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (arial != null)
            return arial;

        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static void SetLayerRecursively(GameObject gameObject, int layer)
    {
        if (gameObject == null)
            return;

        gameObject.layer = layer;
        for (var i = 0; i < gameObject.transform.childCount; i++)
        {
            var child = gameObject.transform.GetChild(i);
            if (child != null)
                SetLayerRecursively(child.gameObject, layer);
        }
    }

    private static RectTransform FindMatchingRectTransform(RectTransform clonedRoot, RectTransform source)
    {
        if (clonedRoot == null || source == null)
            return null;

        var candidates = clonedRoot.GetComponentsInChildren<RectTransform>(includeInactive: true);
        for (var i = 0; i < candidates.Length; i++)
        {
            if (candidates[i] == null)
                continue;
            if (string.Equals(candidates[i].name, source.name, StringComparison.Ordinal))
                return candidates[i];
        }

        return null;
    }

    /// <summary>
    /// Resolves a usable vehicle for the handover selector. The last driven vehicle is preferred,
    /// then nearby player-owned vehicles are considered as a recovery path when game state has not
    /// refreshed LastDrivenVehicle yet.
    /// </summary>
    private static S1LandVehicle ResolveNearbyVehicleStorage(PanelState state, bool forceRefresh = false)
    {
        if (state != null && IsUsableNearbyVehicle(state.NearbyVehicle, out _))
            return state.NearbyVehicle;

        if (!forceRefresh && state != null && Time.frameCount < state.NextVehicleProbeFrame)
            return null;

        if (state != null)
            state.NextVehicleProbeFrame = Time.frameCount + 30;

        var player = Player.Local;
        if (player == null)
            return null;

        var lastDriven = player.LastDrivenVehicle;
        if (IsUsableNearbyVehicle(lastDriven, out _))
        {
            if (state != null)
                state.NearbyVehicle = lastDriven;
            return lastDriven;
        }

        S1LandVehicle closest = null;
        var closestDistance = float.MaxValue;
        var vehicles = Utils.FindObjectsOfTypeSafe<S1LandVehicle>();
        for (var i = 0; i < vehicles.Length; i++)
        {
            var vehicle = vehicles[i];
            if (!IsUsableNearbyVehicle(vehicle, out var distance) || distance >= closestDistance)
                continue;

            closest = vehicle;
            closestDistance = distance;
        }

        if (state != null)
            state.NearbyVehicle = closest;
        return closest;
    }

    private static List<ItemSlot> GetNearbyVehicleSlots(PanelState state)
    {
        var result = new List<ItemSlot>();
        var vehicle = ResolveNearbyVehicleStorage(state, forceRefresh: true);
        if (vehicle?.Storage?.ItemSlots == null)
            return result;

        foreach (var slot in vehicle.Storage.ItemSlots.AsEnumerable())
        {
            if (slot != null)
                result.Add(slot);
        }

        return result;
    }

    private static bool IsUsableNearbyVehicle(S1LandVehicle vehicle, out float distance)
    {
        distance = float.MaxValue;
        var player = Player.Local;
        // The owner flag can arrive a frame after the client receives LastDrivenVehicle.
        // The player-selected vehicle is therefore valid immediately; fallback discoveries
        // must still be explicitly player-owned.
        if (player == null || vehicle == null || vehicle.Storage == null ||
            (!vehicle.IsPlayerOwned && vehicle != player.LastDrivenVehicle))
            return false;

        distance = Vector3.Distance(vehicle.transform.position, player.transform.position);
        return distance <= VehicleMaxDistance;
    }

    private static void LogVehicleSelector(S1LandVehicle vehicle)
    {
        var player = Player.Local;
        var lastDriven = player != null ? player.LastDrivenVehicle : null;
        var lastDrivenDistance = IsUsableNearbyVehicle(lastDriven, out var distance) ? distance : -1f;
        var selectedName = vehicle != null ? vehicle.gameObject.name : "none";
        var selectedDistance = IsUsableNearbyVehicle(vehicle, out distance) ? distance : -1f;
        ModLogger.Info($"[HandoverUI] Vehicle selector: lastDriven={(lastDriven != null ? lastDriven.gameObject.name : "none")}, " +
            $"lastDrivenDistance={lastDrivenDistance:0.0}, selected={selectedName}, selectedDistance={selectedDistance:0.0}.");
    }

    private static int GetTotalPages(PanelState state)
    {
        var slotCount = GetBackpackSlots().Count;
        var perPage = Mathf.Max(1, state.SlotsPerPage);
        return Mathf.Max(1, Mathf.CeilToInt(slotCount / (float)perPage));
    }

    private static void HidePanelAndRestoreVehicle(HandoverScreen screen)
    {
        if (screen == null)
            return;

        if (!States.TryGetValue(screen.GetInstanceID(), out var state))
            return;

        state.IsOpen = false;

        if (state.BackpackContainer != null)
            state.BackpackContainer.gameObject.SetActive(false);
        if (state.PagingRoot != null)
            state.PagingRoot.gameObject.SetActive(false);
        if (state.DedicatedToggleRoot != null)
            state.DedicatedToggleRoot.gameObject.SetActive(false);
        if (state.DedicatedCanvas != null)
            state.DedicatedCanvas.gameObject.SetActive(false);
        SetBackpackVisualVisible(state, false);
        HideOverlayHeader(state);
        SetHeaderPairActive(state.SourceTitleLabel, state.SourceSubtitleLabel, true);
        if (state.VehicleContainer != null)
            state.VehicleContainer.anchoredPosition = state.VehicleOriginalAnchoredPos;
    }
}
