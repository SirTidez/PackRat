using HarmonyLib;
using PackRat.Extensions;
using PackRat.Helpers;
using UnityEngine;
using UnityEngine.UI;

#if MONO
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Storage;
using ScheduleOne.UI;
using ScheduleOne.UI.Handover;
using ScheduleOne.UI.Items;
#else
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Handover;
using Il2CppScheduleOne.UI.Items;
#endif

namespace PackRat.Patches;

/// <summary>
/// Adds backpack storage support to the handover screen with paging.
/// </summary>
[HarmonyPatch(typeof(HandoverScreen))]
public static class HandoverScreenPatch
{
    private const float VehicleMaxDistance = 20f;
    private const string VehicleHeaderTitle = "Vehicle";
    private const string VehicleHeaderSubtitle = "This is the vehicle you last drove.\nMust be within 20 meters.";

    private sealed class PanelState
    {
        public RectTransform BackpackContainer;
        public RectTransform BackpackSlotContainer;
        public RectTransform BackpackHeaderRoot;
        public RectTransform PagingRoot;
        public RectTransform VehicleContainer;
        public Component TitleLabel;
        public Component SubtitleLabel;
        public Text BackpackTitleText;
        public Text BackpackSubtitleText;
        public ItemSlotUI[] SlotUIs;
        public Button PrevButton;
        public Button NextButton;
        public Button ToggleButton;
        public Text PageLabel;
        public Action PrevAction;
        public Action NextAction;
        public Action ToggleAction;
        public Vector2 VehicleOriginalAnchoredPos;
        public int CurrentPage;
        public int SlotsPerPage;
        public bool ShowingVehicle;
        public int LastPageInputFrame;
        public bool Initialized;
    }

    private static readonly Dictionary<int, PanelState> States = new Dictionary<int, PanelState>();

    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    public static void Start(HandoverScreen __instance)
    {
        try
        {
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
        try
        {
            if (!HasBackpack())
            {
                HidePanelAndRestoreVehicle(__instance);
                return;
            }

            var panel = EnsurePanel(__instance);
            if (panel == null || panel.BackpackContainer == null)
                return;

            panel.CurrentPage = 0;
            panel.SlotsPerPage = panel.SlotUIs != null ? panel.SlotUIs.Length : 0;
            panel.ShowingVehicle = false;
            panel.BackpackContainer.gameObject.SetActive(true);
            if (panel.PagingRoot != null)
                panel.PagingRoot.gameObject.SetActive(true);

            UpdateBackpackHeaderTexts(panel);

            var hasVehicle = HasNearbyVehicleStorage();
            if (__instance.NoVehicle != null)
                __instance.NoVehicle.SetActive(!hasVehicle && !panel.BackpackContainer.gameObject.activeSelf);

            ApplyVisibleStorageMode(panel, hasVehicle);
            if (__instance.NoVehicle != null)
                __instance.NoVehicle.SetActive(false);

            if (panel.BackpackContainer != null)
                panel.BackpackContainer.gameObject.SetActive(true);
            if (panel.BackpackSlotContainer != null)
                panel.BackpackSlotContainer.gameObject.SetActive(true);
            if (panel.BackpackHeaderRoot != null)
                panel.BackpackHeaderRoot.gameObject.SetActive(false);
            if (panel.VehicleContainer != null)
                panel.VehicleContainer.gameObject.SetActive(false);

            ApplyPrimaryHeaderForMode(__instance, panel, panel.ShowingVehicle);

            ApplyBackpackPage(panel);

            if (panel.PagingRoot != null)
            {
                var slotCount = GetBackpackSlots().Count;
                var slotsPerPage = panel.SlotUIs != null ? panel.SlotUIs.Length : 0;
                ModLogger.Debug($"Handover pager root='{panel.PagingRoot.name}' active={panel.PagingRoot.gameObject.activeInHierarchy} anchored={panel.PagingRoot.anchoredPosition} slots={slotCount} perPage={slotsPerPage}");
            }

            RebuildQuickMove(__instance, hasVehicle);
        }
        catch (Exception ex)
        {
            ModLogger.Error("HandoverScreenPatch.Open", ex);
        }
    }

    [HarmonyPatch("Close")]
    [HarmonyPostfix]
    public static void Close(HandoverScreen __instance)
    {
        try
        {
            if (!States.TryGetValue(__instance.GetInstanceID(), out var panel))
                return;

            ClearSlotAssignments(panel);
            if (panel.BackpackContainer != null)
                panel.BackpackContainer.gameObject.SetActive(false);
            if (panel.PagingRoot != null)
                panel.PagingRoot.gameObject.SetActive(false);
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
        if (screen == null)
            return null;

        var id = screen.GetInstanceID();
        if (States.TryGetValue(id, out var existing)
            && existing.Initialized
            && IsComponentAlive(existing.BackpackContainer)
            && IsComponentAlive(existing.VehicleContainer)
            && IsComponentAlive(existing.BackpackHeaderRoot)
            && IsComponentAlive(existing.PagingRoot)
            && IsComponentAlive(existing.PrevButton)
            && IsComponentAlive(existing.NextButton)
            && IsComponentAlive(existing.ToggleButton)
            && IsComponentAlive(existing.PageLabel))
        {
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
            clone.anchoredPosition = state.VehicleOriginalAnchoredPos;
            clone.localScale = state.VehicleContainer.localScale;
            clone.gameObject.SetActive(false);
            state.BackpackContainer = clone;
        }

        state.BackpackSlotContainer = FindMatchingRectTransform(state.BackpackContainer, screen.VehicleSlotContainer);
        var slotSearchRoot = state.BackpackSlotContainer != null ? state.BackpackSlotContainer : state.BackpackContainer;
        state.SlotUIs = slotSearchRoot.GetComponentsInChildren<ItemSlotUI>(includeInactive: false);
        if (state.SlotUIs == null || state.SlotUIs.Length == 0)
            state.SlotUIs = slotSearchRoot.GetComponentsInChildren<ItemSlotUI>(includeInactive: true);
        ResolveLabels(state, screen);
        EnsureBackpackHeader(state);
        EnsurePagingControls(state);
        state.Initialized = true;
        States[id] = state;
        return state;
    }

    private static void ResolveLabels(PanelState state, HandoverScreen screen)
    {
        state.TitleLabel = null;
        state.SubtitleLabel = null;

        if (screen != null && screen.VehicleContainer != null)
        {
            var sourceLabels = screen.VehicleContainer.GetComponentsInChildren<Component>(true)
                .Where(IsTextLikeComponent)
                .ToArray();

            Component sourceTitle = null;
            Component sourceSubtitle = null;

            for (var i = 0; i < sourceLabels.Length; i++)
            {
                var label = sourceLabels[i];
                if (label == null)
                    continue;

                var text = (GetLabelText(label) ?? string.Empty).Trim();
                if (sourceTitle == null && text.Equals(VehicleHeaderTitle, StringComparison.OrdinalIgnoreCase))
                {
                    sourceTitle = label;
                    continue;
                }

                if (sourceSubtitle == null && text.Contains("vehicle you last drove", StringComparison.OrdinalIgnoreCase))
                    sourceSubtitle = label;
            }

            state.TitleLabel = FindMatchingLabelComponent(state.BackpackContainer, screen.VehicleContainer, sourceTitle);
            state.SubtitleLabel = FindMatchingLabelComponent(state.BackpackContainer, screen.VehicleContainer, sourceSubtitle);
        }

        var labels = state.BackpackContainer.GetComponentsInChildren<Component>(true)
            .Where(IsTextLikeComponent)
            .ToArray();

        for (var i = 0; i < labels.Length; i++)
        {
            var label = labels[i];
            if (label == null)
                continue;

            var text = (GetLabelText(label) ?? string.Empty).Trim();
            if (state.TitleLabel == null && text.Equals("Vehicle", StringComparison.OrdinalIgnoreCase))
            {
                state.TitleLabel = label;
                continue;
            }

            if (state.SubtitleLabel == null && text.Contains("vehicle you last drove", StringComparison.OrdinalIgnoreCase))
            {
                state.SubtitleLabel = label;
            }
        }

        if (state.TitleLabel == null && labels.Length > 0)
            state.TitleLabel = labels[0];
        if (state.SubtitleLabel == null && labels.Length > 1)
            state.SubtitleLabel = labels[1];
    }

    private static Component FindMatchingLabelComponent(RectTransform clonedRoot, RectTransform sourceRoot, Component sourceLabel)
    {
        if (clonedRoot == null || sourceRoot == null || sourceLabel == null)
            return null;

        RectTransform sourceRt = null;
        try
        {
            sourceRt = sourceLabel.transform as RectTransform;
        }
        catch
        {
        }

        if (sourceRt == null)
            return null;

        var matchingRt = default(RectTransform);

        var relativePath = BuildRelativePath(sourceRoot, sourceRt);
        if (!string.IsNullOrEmpty(relativePath))
        {
            var matchedByPath = clonedRoot.Find(relativePath);
            matchingRt = matchedByPath as RectTransform;
        }

        if (matchingRt == null)
            matchingRt = FindMatchingRectTransform(clonedRoot, sourceRt);

        if (matchingRt == null)
            return null;

        var sourceType = sourceLabel.GetType();
        var components = matchingRt.GetComponents<Component>();
        for (var i = 0; i < components.Length; i++)
        {
            var component = components[i];
            if (component == null)
                continue;
            if (component.GetType() == sourceType)
                return component;
        }

        for (var i = 0; i < components.Length; i++)
        {
            var component = components[i];
            if (component != null && IsTextLikeComponent(component))
                return component;
        }

        return null;
    }

    private static string BuildRelativePath(Transform root, Transform target)
    {
        if (root == null || target == null)
            return null;

        try
        {
            if (!target.IsChildOf(root))
                return null;
        }
        catch
        {
            return null;
        }

        var segments = new List<string>();
        var current = target;
        while (current != null && current != root)
        {
            segments.Add(current.name);
            current = current.parent;
        }

        if (segments.Count == 0)
            return string.Empty;

        segments.Reverse();
        return string.Join("/", segments);
    }

    private static void EnsureBackpackHeader(PanelState state)
    {
        if (state?.BackpackContainer == null)
            return;

        var headerRoot = state.BackpackContainer.Find("PackRat_BackpackHeader") as RectTransform;
        if (headerRoot == null)
        {
            var rootGo = new GameObject("PackRat_BackpackHeader");
            headerRoot = rootGo.AddComponent<RectTransform>();
            headerRoot.SetParent(state.BackpackContainer, worldPositionStays: false);
            headerRoot.anchorMin = new Vector2(0.5f, 1f);
            headerRoot.anchorMax = new Vector2(0.5f, 1f);
            headerRoot.pivot = new Vector2(0.5f, 1f);
            headerRoot.anchoredPosition = new Vector2(0f, -8f);
            headerRoot.sizeDelta = new Vector2(380f, 92f);
        }

        state.BackpackHeaderRoot = headerRoot;
        state.BackpackTitleText = EnsureHeaderText(headerRoot, "PackRat_BackpackTitle", new Vector2(0f, -18f), new Vector2(360f, 40f), 16, FontStyle.Bold, Color.white);
        state.BackpackSubtitleText = EnsureHeaderText(headerRoot, "PackRat_BackpackSubtitle", new Vector2(0f, -50f), new Vector2(360f, 24f), 8, FontStyle.Normal, new Color32(218, 218, 218, 255));
        UpdateBackpackHeaderLayout(state);

        if (TryGetGameObject(headerRoot, out var headerObject)
            && TryGetGameObject(state.BackpackContainer, out var containerObject))
        {
            SetLayerRecursively(headerObject, containerObject.layer);
            headerRoot.SetAsLastSibling();

            var parentCanvas = state.BackpackContainer.GetComponentInParent<Canvas>();
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
        headerRoot.anchorMin = new Vector2(0.5f, 0.5f);
        headerRoot.anchorMax = new Vector2(0.5f, 0.5f);
        headerRoot.pivot = new Vector2(0.5f, 1f);
        headerRoot.localScale = Vector3.one;

        var topOfContainer = state.BackpackContainer.rect.height * (1f - state.BackpackContainer.pivot.y);
        headerRoot.anchoredPosition = new Vector2(0f, topOfContainer - 8f);
    }

    private static Text EnsureHeaderText(RectTransform parent, string name, Vector2 anchoredPosition, Vector2 size, int fontSize, FontStyle fontStyle, Color color)
    {
        if (parent == null)
            return null;

        var textTransform = parent.Find(name);
        Text text = null;
        if (textTransform != null)
            text = textTransform.GetComponent<Text>();

        if (text == null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = size;

            text = go.AddComponent<Text>();
        }

        var textRt = text.transform as RectTransform;
        if (textRt != null)
        {
            textRt.anchorMin = new Vector2(0.5f, 1f);
            textRt.anchorMax = new Vector2(0.5f, 1f);
            textRt.pivot = new Vector2(0.5f, 0.5f);
            textRt.anchoredPosition = anchoredPosition;
            textRt.sizeDelta = size;
        }

        text.font = ResolveUiFont(parent);
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.resizeTextForBestFit = false;
        text.raycastTarget = false;
        return text;
    }

    private static void UpdateBackpackHeaderTexts(PanelState state)
    {
        if (state == null)
            return;

        if (state.BackpackTitleText != null)
            state.BackpackTitleText.text = PlayerBackpack.Instance?.CurrentTier?.Name ?? PlayerBackpack.StorageName;

        if (state.BackpackSubtitleText != null)
            state.BackpackSubtitleText.text = "Items from your backpack.";
    }

    private static void EnsurePagingControls(PanelState state)
    {
        if (state.BackpackContainer == null)
            return;

        var pagingRoot = state.BackpackContainer.Find("PackRat_Paging");
        if (pagingRoot == null)
        {
            var parent = state.BackpackContainer.parent;
            if (parent != null)
                pagingRoot = parent.Find("PackRat_Paging");
        }

        if (pagingRoot != null && pagingRoot.parent != state.BackpackContainer)
            pagingRoot.SetParent(state.BackpackContainer, worldPositionStays: false);

        if (pagingRoot == null)
        {
            var rootGo = new GameObject("PackRat_Paging");
            pagingRoot = rootGo.transform;
            pagingRoot.SetParent(state.BackpackContainer, worldPositionStays: false);

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
            && TryGetGameObject(state.BackpackContainer, out var containerObject))
        {
            SetLayerRecursively(pagingObject, containerObject.layer);
            pagingRoot.SetAsLastSibling();

            Canvas parentCanvas = null;
            try
            {
                parentCanvas = state.BackpackContainer.GetComponentInParent<Canvas>();
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
                var hasVehicle = HasNearbyVehicleStorage();
                if (!hasVehicle)
                    state.ShowingVehicle = false;
                else
                    state.ShowingVehicle = !state.ShowingVehicle;

                ApplyVisibleStorageMode(state, hasVehicle);
                ApplyPrimaryHeaderForMode(FindOwningScreen(state), state, state.ShowingVehicle);

                if (!state.ShowingVehicle)
                    ApplyBackpackPage(state);
                else
                    UpdatePagerControls(state, GetTotalPages(state), hasVehicle);
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

        Canvas.ForceUpdateCanvases();
        UpdatePagingLayout(state);

        UpdatePagerControls(state, totalPages, HasNearbyVehicleStorage());
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

    private static void RebuildQuickMove(HandoverScreen screen, bool hasVehicle)
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

        if (hasVehicle && Player.Local?.LastDrivenVehicle?.Storage?.ItemSlots != null)
        {
            foreach (var slot in Player.Local.LastDrivenVehicle.Storage.ItemSlots.AsEnumerable())
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
        var typeName = component.GetType().Name;
        return typeName.Contains("Text", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetLabelText(Component component)
    {
        if (component == null)
            return string.Empty;

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

        return value as string ?? value?.ToString() ?? string.Empty;
    }

    private static void SetLabelText(Component component, string text)
    {
        if (component == null)
            return;

        var safeText = text ?? string.Empty;

        if (component is Text uiText)
        {
            uiText.text = safeText;
            return;
        }

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

    private static void ApplyPrimaryHeaderForMode(HandoverScreen screen, PanelState panel, bool showingVehicle)
    {
        if (panel == null)
            return;

        var backpackTitle = PlayerBackpack.Instance?.CurrentTier?.Name ?? PlayerBackpack.StorageName;
        var backpackSubtitle = "Items from your backpack.";

        var targetTitle = showingVehicle ? VehicleHeaderTitle : backpackTitle;
        var targetSubtitle = showingVehicle ? VehicleHeaderSubtitle : backpackSubtitle;

        if (panel.BackpackHeaderRoot != null)
            panel.BackpackHeaderRoot.gameObject.SetActive(false);

        if (panel.TitleLabel != null)
        {
            SetLabelText(panel.TitleLabel, targetTitle);
            SetComponentActive(panel.TitleLabel, true);
        }

        if (panel.SubtitleLabel != null)
        {
            SetLabelText(panel.SubtitleLabel, targetSubtitle);
            SetComponentActive(panel.SubtitleLabel, true);
        }
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

        if (state.BackpackContainer != null)
        {
            state.BackpackContainer.anchoredPosition = state.VehicleOriginalAnchoredPos;
            state.BackpackContainer.gameObject.SetActive(true);
        }

        if (state.BackpackSlotContainer != null)
            state.BackpackSlotContainer.gameObject.SetActive(!showVehicle);

        SetClonedHeaderVisibility(state, showVehicle);

        if (state.BackpackHeaderRoot != null)
            state.BackpackHeaderRoot.gameObject.SetActive(false);
        UpdateBackpackHeaderLayout(state);

        if (state.VehicleContainer != null)
        {
            state.VehicleContainer.anchoredPosition = state.VehicleOriginalAnchoredPos;
            state.VehicleContainer.gameObject.SetActive(showVehicle);
        }

        if (state.PagingRoot != null)
            state.PagingRoot.gameObject.SetActive(true);
    }

    private static void UpdatePagingLayout(PanelState state)
    {
        if (state?.PagingRoot == null || state.BackpackContainer == null)
            return;

        if (state.PagingRoot.parent != state.BackpackContainer)
            state.PagingRoot.SetParent(state.BackpackContainer, worldPositionStays: false);

        var rootRt = state.PagingRoot;
        rootRt.anchorMin = new Vector2(0.5f, 0.5f);
        rootRt.anchorMax = new Vector2(0.5f, 0.5f);
        rootRt.pivot = new Vector2(0.5f, 1f);
        rootRt.localScale = Vector3.one;
        const float marginBelowContainer = 150f;
        var bottomOfContainer = -(state.BackpackContainer.rect.height * state.BackpackContainer.pivot.y);
        rootRt.anchoredPosition = new Vector2(0f, bottomOfContainer - marginBelowContainer);
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

    private static bool HasNearbyVehicleStorage()
    {
        var player = Player.Local;
        if (player == null || player.LastDrivenVehicle == null)
            return false;

        var storage = player.LastDrivenVehicle.Storage;
        if (storage == null)
            return false;

        return Vector3.Distance(player.LastDrivenVehicle.transform.position, player.transform.position) < VehicleMaxDistance;
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

        if (state.BackpackContainer != null)
            state.BackpackContainer.gameObject.SetActive(false);
        if (state.PagingRoot != null)
            state.PagingRoot.gameObject.SetActive(false);
        if (state.VehicleContainer != null)
            state.VehicleContainer.anchoredPosition = state.VehicleOriginalAnchoredPos;
    }
}
