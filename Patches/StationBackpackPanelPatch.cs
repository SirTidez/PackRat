using HarmonyLib;
using PackRat.Config;
using PackRat.Extensions;
using PackRat.Helpers;
using UnityEngine;
using UnityEngine.UI;

#if MONO
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.Money;
using ScheduleOne.ObjectScripts;
using ScheduleOne.PlayerScripts;
using ScheduleOne.StationFramework;
using ScheduleOne.UI;
using ScheduleOne.UI.Items;
using ScheduleOne.UI.Stations;
#else
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.StationFramework;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Items;
using Il2CppScheduleOne.UI.Stations;
#endif

namespace PackRat.Patches;

/// <summary>
/// Adds the paged backpack panel to station interfaces that expose item slots.
/// </summary>
public static class StationBackpackPanelPatch
{
    private sealed class PanelState
    {
        public RectTransform Root;
        public RectTransform SlotContainer;
        public GridLayoutGroup Grid;
        public ItemSlotUI[] SlotUIs;
        public Text TitleLabel;
        public Text SubtitleLabel;
        public RectTransform PagingRoot;
        public Button PrevButton;
        public Button NextButton;
        public Text PageLabel;
        public Action PrevAction;
        public Action NextAction;
        public int CurrentPage;
        public int LastPageInputFrame;
        public bool Initialized;
    }

    private const int SlotsPerPage = 9;
    private const int GridRows = 3;
    private const float LeftOffset = 520f;
    private static readonly Vector2 PanelSize = new Vector2(380f, 520f);
    private static readonly Dictionary<int, PanelState> Panels = new Dictionary<int, PanelState>();
    private static readonly List<ItemSlot> ActiveInventorySlots = new List<ItemSlot>();
    private static readonly List<ItemSlot> ActiveStationSlots = new List<ItemSlot>();
    private static readonly List<ItemSlot> ActiveBackpackSlots = new List<ItemSlot>();
    private static bool _quickMoveActive;

    [HarmonyPatch(typeof(ItemUIManager), "GetQuickMoveSlots")]
    [HarmonyPostfix]
#if !MONO
    public static void GetQuickMoveSlots(ItemSlot sourceSlot, ref Il2CppSystem.Collections.Generic.List<ItemSlot> __result)
#else
    public static void GetQuickMoveSlots(ItemSlot sourceSlot, ref List<ItemSlot> __result)
#endif
    {
        if (!_quickMoveActive || sourceSlot == null || sourceSlot.ItemInstance == null)
            return;

        var targets = new List<ItemSlot>();
        if (ActiveInventorySlots.Contains(sourceSlot))
        {
            AddQuickMoveTargets(sourceSlot, ActiveStationSlots, targets);
            AddQuickMoveTargets(sourceSlot, ActiveBackpackSlots, targets);
        }
        else if (ActiveStationSlots.Contains(sourceSlot))
        {
            AddQuickMoveTargets(sourceSlot, ActiveInventorySlots, targets);
            AddQuickMoveTargets(sourceSlot, ActiveBackpackSlots, targets);
        }
        else if (ActiveBackpackSlots.Contains(sourceSlot))
        {
            AddQuickMoveTargets(sourceSlot, ActiveStationSlots, targets);
            AddQuickMoveTargets(sourceSlot, ActiveInventorySlots, targets);
        }
        else
        {
            return;
        }

#if !MONO
        __result = targets.ToIl2CppList();
#else
        __result = targets;
#endif
    }

    private static void ShowForStation(Component stationCanvas)
    {
        try
        {
            HideForStation(stationCanvas);
            if (stationCanvas == null || !HasBackpack())
                return;

            var stationSlots = GetStationSlots(stationCanvas);
            if (stationSlots.Count == 0)
                return;

            var slotTemplate = FindBackpackSlotTemplate(stationCanvas);
            var container = FindContainer(stationCanvas);
            if (slotTemplate == null || container == null)
                return;

            var panel = EnsurePanel(stationCanvas, container, slotTemplate);
            if (panel == null)
                return;

            panel.CurrentPage = 0;
            PositionPanel(container, panel);
            AssignBackpackPage(panel, GetBackpackSlots());
            panel.Root.gameObject.SetActive(true);
            RebuildQuickMove(stationSlots, GetBackpackSlots());
        }
        catch (Exception ex)
        {
            ModLogger.Error("StationBackpackPanelPatch.ShowForStation", ex);
        }
    }

    private static void HideForStation(Component stationCanvas)
    {
        if (stationCanvas == null)
            return;

        if (!Panels.TryGetValue(stationCanvas.GetInstanceID(), out var panel))
            return;

        if (panel.SlotUIs != null)
        {
            for (var i = 0; i < panel.SlotUIs.Length; i++)
            {
                var slotUi = panel.SlotUIs[i];
                if (slotUi == null)
                    continue;

                ResetSlotUi(slotUi);
                slotUi.ClearSlot();
            }
        }

        if (panel.Root != null)
            panel.Root.gameObject.SetActive(false);

        _quickMoveActive = false;
        ActiveInventorySlots.Clear();
        ActiveStationSlots.Clear();
        ActiveBackpackSlots.Clear();
    }

    private static PanelState EnsurePanel(Component stationCanvas, RectTransform stationContainer, ItemSlotUI slotTemplate)
    {
        var id = stationCanvas.GetInstanceID();
        if (Panels.TryGetValue(id, out var existing)
            && existing.Initialized
            && existing.Root != null
            && existing.SlotUIs != null)
        {
            ConfigureRoot(stationContainer, existing.Root);
            EnsurePager(existing);
            return existing;
        }

        var panel = existing ?? new PanelState();
        var rootObject = new GameObject("PackRat_StationBackpackPanel");
        var root = rootObject.AddComponent<RectTransform>();
        ConfigureRoot(stationContainer, root);
        root.gameObject.SetActive(false);

        panel.Root = root;
        panel.TitleLabel = CreateText("PackRat_StationBackpackTitle", root, new Vector2(0f, 208f), 24, PlayerBackpack.Instance?.CurrentTier?.Name ?? PlayerBackpack.StorageName);
        panel.SubtitleLabel = CreateText("PackRat_StationBackpackSubtitle", root, new Vector2(0f, 168f), 18, "Items from your backpack.");

        var slotContainerObject = new GameObject("PackRat_StationBackpackSlotContainer");
        var slotContainer = slotContainerObject.AddComponent<RectTransform>();
        slotContainer.SetParent(root, worldPositionStays: false);
        slotContainer.anchorMin = new Vector2(0.5f, 0.5f);
        slotContainer.anchorMax = new Vector2(0.5f, 0.5f);
        slotContainer.pivot = new Vector2(0.5f, 0.5f);
        slotContainer.anchoredPosition = new Vector2(0f, -10f);
        slotContainer.sizeDelta = new Vector2(340f, 340f);
        panel.SlotContainer = slotContainer;

        var templateRect = slotTemplate.GetComponent<RectTransform>();
        var cellSize = templateRect != null && templateRect.sizeDelta.x > 0f
            ? templateRect.sizeDelta
            : new Vector2(96f, 96f);

        var grid = slotContainerObject.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
        grid.constraintCount = GridRows;
        grid.cellSize = cellSize;
        grid.spacing = new Vector2(12f, 12f);
        grid.childAlignment = TextAnchor.UpperCenter;
        panel.Grid = grid;

        panel.SlotUIs = new ItemSlotUI[SlotsPerPage];
        for (var i = 0; i < SlotsPerPage; i++)
        {
            var slotObject = UnityEngine.Object.Instantiate(slotTemplate.gameObject, slotContainer);
            slotObject.name = $"PackRat_StationBackpackSlot ({i})";
            var slotUi = slotObject.GetComponent<ItemSlotUI>();
            if (slotUi != null)
            {
                StripTemplateText(slotObject, slotUi);
                ResetSlotUi(slotUi);
                slotUi.ClearSlot();
            }
            panel.SlotUIs[i] = slotUi;
        }

        panel.Initialized = true;
        Panels[id] = panel;
        EnsurePager(panel);
        return panel;
    }

    private static void PositionPanel(RectTransform stationContainer, PanelState panel)
    {
        if (panel?.Root == null)
            return;

        ConfigureRoot(stationContainer, panel.Root);
        var config = Configuration.Instance;
        panel.Root.anchoredPosition = new Vector2(
            -LeftOffset + config.StationOverlayOffsetX,
            config.StationOverlayOffsetY
        );
        panel.Root.localPosition = new Vector3(panel.Root.localPosition.x, panel.Root.localPosition.y, 0f);
    }

    private static void ConfigureRoot(RectTransform stationContainer, RectTransform root)
    {
        if (root == null)
            return;

        var overlayParent = FindOverlayParent(stationContainer);
        if (overlayParent != null && root.parent != overlayParent)
            root.SetParent(overlayParent, worldPositionStays: false);

        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = PanelSize;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one;
        EnsureIgnoredByLayout(root);
        EnsureOverlaySorting(root, stationContainer);
    }

    private static Transform FindOverlayParent(RectTransform stationContainer)
    {
        try
        {
            var itemUiCanvas = Singleton<ItemUIManager>.Instance?.Canvas;
            if (itemUiCanvas != null)
                return itemUiCanvas.transform;
        }
        catch
        {
        }

        if (stationContainer == null)
            return null;

        var canvas = stationContainer.GetComponentInParent<Canvas>();
        if (canvas != null)
            return canvas.transform;

        return stationContainer.parent;
    }

    private static void EnsureIgnoredByLayout(RectTransform rectTransform)
    {
        if (rectTransform == null)
            return;

#if !MONO
        var layout = Utils.GetOrAddComponentSafe<LayoutElement>(rectTransform.gameObject);
#else
        var layout = rectTransform.GetComponent<LayoutElement>();
        if (layout == null)
            layout = rectTransform.gameObject.AddComponent<LayoutElement>();
#endif
        if (layout != null)
            layout.ignoreLayout = true;
    }

    private static void EnsureOverlaySorting(RectTransform root, RectTransform stationContainer)
    {
        if (root == null)
            return;

        var rootCanvas = root.GetComponent<Canvas>();
        if (rootCanvas == null)
            rootCanvas = root.gameObject.AddComponent<Canvas>();

        rootCanvas.overrideSorting = true;

        var parentCanvas = root.parent != null
            ? root.parent.GetComponentInParent<Canvas>()
            : null;
        if (parentCanvas != null)
        {
            rootCanvas.sortingLayerID = parentCanvas.sortingLayerID;
            rootCanvas.sortingOrder = parentCanvas.sortingOrder + 200;
        }
        else
        {
            rootCanvas.sortingOrder = 5000;
        }

        root.SetAsLastSibling();

        var raycaster = root.GetComponent<GraphicRaycaster>();
        if (raycaster == null)
            raycaster = root.gameObject.AddComponent<GraphicRaycaster>();

        RegisterItemUiRaycaster(raycaster);
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
            ModLogger.Error("StationBackpackPanelPatch.RegisterItemUiRaycaster", ex);
        }
    }

    private static void AssignBackpackPage(PanelState panel, List<ItemSlot> backpackSlots)
    {
        var totalPages = Mathf.Max(1, Mathf.CeilToInt(backpackSlots.Count / (float)SlotsPerPage));
        if (panel.CurrentPage < 0)
            panel.CurrentPage = 0;
        if (panel.CurrentPage >= totalPages)
            panel.CurrentPage = totalPages - 1;

        for (var i = 0; i < panel.SlotUIs.Length; i++)
        {
            var ui = panel.SlotUIs[i];
            if (ui == null)
                continue;

            ResetSlotUi(ui);
            ui.ClearSlot();
            var slotIndex = panel.CurrentPage * SlotsPerPage + i;
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

        UpdatePager(panel, totalPages);
    }

    private static void EnsurePager(PanelState panel)
    {
        if (panel.PagingRoot == null)
        {
            var pagingObject = new GameObject("PackRat_StationBackpackPaging");
            var pagingRoot = pagingObject.AddComponent<RectTransform>();
            pagingRoot.SetParent(panel.Root, worldPositionStays: false);
            pagingRoot.anchorMin = new Vector2(0.5f, 0.5f);
            pagingRoot.anchorMax = new Vector2(0.5f, 0.5f);
            pagingRoot.pivot = new Vector2(0.5f, 1f);
            pagingRoot.anchoredPosition = new Vector2(0f, -205f);
            pagingRoot.sizeDelta = new Vector2(180f, 40f);
            panel.PagingRoot = pagingRoot;
        }

        if (panel.PrevButton == null)
            panel.PrevButton = CreatePagerButton("<", panel.PagingRoot, new Vector2(-70f, -10f));
        if (panel.NextButton == null)
            panel.NextButton = CreatePagerButton(">", panel.PagingRoot, new Vector2(70f, -10f));
        if (panel.PageLabel == null)
            panel.PageLabel = CreatePagerLabel(panel.PagingRoot, new Vector2(0f, -10f));

        if (panel.PrevAction == null)
            panel.PrevAction = () =>
            {
                if (panel.LastPageInputFrame == Time.frameCount || panel.CurrentPage <= 0)
                    return;

                panel.LastPageInputFrame = Time.frameCount;
                panel.CurrentPage--;
                AssignBackpackPage(panel, GetBackpackSlots());
            };

        if (panel.NextAction == null)
            panel.NextAction = () =>
            {
                if (panel.LastPageInputFrame == Time.frameCount)
                    return;

                var totalPages = Mathf.Max(1, Mathf.CeilToInt(GetBackpackSlots().Count / (float)SlotsPerPage));
                if (panel.CurrentPage >= totalPages - 1)
                    return;

                panel.LastPageInputFrame = Time.frameCount;
                panel.CurrentPage++;
                AssignBackpackPage(panel, GetBackpackSlots());
            };

        EventHelper.RemoveListener(panel.PrevAction, panel.PrevButton.onClick);
        EventHelper.AddListener(panel.PrevAction, panel.PrevButton.onClick);
        EventHelper.RemoveListener(panel.NextAction, panel.NextButton.onClick);
        EventHelper.AddListener(panel.NextAction, panel.NextButton.onClick);
    }

    private static void UpdatePager(PanelState panel, int totalPages)
    {
        var showPaging = totalPages > 1;
        if (panel.PageLabel != null)
        {
            panel.PageLabel.gameObject.SetActive(true);
            panel.PageLabel.text = $"Page {panel.CurrentPage + 1}/{Mathf.Max(1, totalPages)}";
        }

        if (panel.PrevButton != null)
        {
            panel.PrevButton.gameObject.SetActive(showPaging);
            panel.PrevButton.interactable = showPaging && panel.CurrentPage > 0;
        }

        if (panel.NextButton != null)
        {
            panel.NextButton.gameObject.SetActive(showPaging);
            panel.NextButton.interactable = showPaging && panel.CurrentPage < totalPages - 1;
        }
    }

    private static void RebuildQuickMove(List<ItemSlot> stationSlots, List<ItemSlot> backpackSlots)
    {
        _quickMoveActive = false;
        ActiveInventorySlots.Clear();
        ActiveStationSlots.Clear();
        ActiveBackpackSlots.Clear();

#if MONO
        var inventory = PlayerInventory.Instance;
#else
        var inventory = PlayerSingleton<PlayerInventory>.Instance;
#endif
        if (inventory == null)
            return;

        foreach (var slot in inventory.GetAllInventorySlots().AsEnumerable())
        {
            if (slot != null)
                ActiveInventorySlots.Add(slot);
        }

        foreach (var slot in stationSlots)
        {
            if (slot != null && !ActiveStationSlots.Contains(slot))
                ActiveStationSlots.Add(slot);
        }

        foreach (var slot in backpackSlots)
        {
            if (slot != null)
                ActiveBackpackSlots.Add(slot);
        }

        var secondarySlots = new List<ItemSlot>(ActiveStationSlots);
        secondarySlots.AddRange(ActiveBackpackSlots);

#if !MONO
        Singleton<ItemUIManager>.Instance.EnableQuickMove(ActiveInventorySlots.ToIl2CppList(), secondarySlots.ToIl2CppList());
#else
        Singleton<ItemUIManager>.Instance.EnableQuickMove(ActiveInventorySlots, secondarySlots);
#endif
        _quickMoveActive = ActiveInventorySlots.Count > 0 && (ActiveStationSlots.Count > 0 || ActiveBackpackSlots.Count > 0);
    }

    private static List<ItemSlot> GetStationSlots(Component stationCanvas)
    {
        var result = new List<ItemSlot>();
        var slotUis = stationCanvas.GetComponentsInChildren<ItemSlotUI>(includeInactive: true);
        for (var i = 0; i < slotUis.Length; i++)
        {
            var slot = slotUis[i]?.assignedSlot;
            if (slot != null && !result.Contains(slot))
                result.Add(slot);
        }

        return result;
    }

    private static ItemSlotUI FindBackpackSlotTemplate(Component stationCanvas)
    {
        try
        {
            var prefab = Singleton<ItemUIManager>.Instance?.ItemSlotUIPrefab;
            if (prefab != null)
                return prefab;
        }
        catch
        {
        }

        return FindStationSlotTemplate(stationCanvas);
    }

    private static ItemSlotUI FindStationSlotTemplate(Component stationCanvas)
    {
        var slotUis = stationCanvas.GetComponentsInChildren<ItemSlotUI>(includeInactive: true);
        for (var i = 0; i < slotUis.Length; i++)
        {
            if (slotUis[i] != null)
                return slotUis[i];
        }

        return null;
    }

    private static RectTransform FindContainer(Component stationCanvas)
    {
        var container = ReflectionUtils.TryGetFieldOrProperty(stationCanvas, "Container")
            ?? ReflectionUtils.TryGetFieldOrProperty(stationCanvas, "_container");

        if (container is RectTransform rect)
            return rect;

        if (container is GameObject gameObject)
            return gameObject.GetComponent<RectTransform>();

        return stationCanvas.GetComponent<RectTransform>();
    }

    private static bool HasBackpack()
    {
        return PlayerBackpack.Instance != null && PlayerBackpack.Instance.IsUnlocked && Player.Local != null;
    }

    private static List<ItemSlot> GetBackpackSlots()
    {
        var result = new List<ItemSlot>();
        try
        {
            if (!HasBackpack())
                return result;

            var storage = Player.Local.GetBackpackStorage();
            if (storage?.ItemSlots == null)
                return result;

            foreach (var slot in storage.ItemSlots.AsEnumerable())
            {
                if (slot != null)
                    result.Add(slot);
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("StationBackpackPanelPatch.GetBackpackSlots", ex);
        }

        return result;
    }

    private static void AddQuickMoveTargets(ItemSlot sourceSlot, List<ItemSlot> candidates, List<ItemSlot> targets)
    {
        if (sourceSlot?.ItemInstance == null || candidates == null)
            return;

        if (sourceSlot.ItemInstance is CashInstance)
        {
            AddCashQuickMoveTargets(sourceSlot, candidates, targets);
            return;
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!CanQuickMoveToSlot(sourceSlot, candidate, targets))
                continue;
            if (candidate.GetCapacityForItem(sourceSlot.ItemInstance, false) <= 0)
                continue;

            targets.Add(candidate);
        }
    }

    private static void AddCashQuickMoveTargets(ItemSlot sourceSlot, List<ItemSlot> candidates, List<ItemSlot> targets)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!CanQuickMoveToSlot(sourceSlot, candidate, targets))
                continue;
            if (!(candidate.ItemInstance is CashInstance cash) || GetCashCapacity(candidate, cash) <= 0f)
                continue;

            targets.Add(candidate);
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!CanQuickMoveToSlot(sourceSlot, candidate, targets))
                continue;
            if (candidate.ItemInstance != null)
                continue;

            targets.Add(candidate);
        }
    }

    private static bool CanQuickMoveToSlot(ItemSlot sourceSlot, ItemSlot candidate, List<ItemSlot> targets)
    {
        if (sourceSlot?.ItemInstance == null || candidate == null || candidate == sourceSlot || targets.Contains(candidate))
            return false;
        if (candidate.IsLocked || candidate.IsAddLocked || candidate.IsRemovalLocked)
            return false;
        return candidate.DoesItemMatchHardFilters(sourceSlot.ItemInstance);
    }

    private static float GetCashCapacity(ItemSlot slot, CashInstance cash)
    {
        if (slot == null || cash == null)
            return 0f;

        var maxBalance = slot is CashSlot ? float.MaxValue : 1000f;
        return Math.Max(0f, maxBalance - cash.Balance);
    }

    private static Text CreateText(string name, Transform parent, Vector2 position, int fontSize, string text)
    {
        var labelObject = new GameObject(name);
        labelObject.transform.SetParent(parent, worldPositionStays: false);
        var rect = labelObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(360f, 34f);

        var label = labelObject.AddComponent<Text>();
        label.text = text ?? string.Empty;
        label.fontSize = fontSize;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.font = ResolveUiFont(parent);
        label.raycastTarget = false;
        return label;
    }

    private static Button CreatePagerButton(string text, Transform parent, Vector2 anchoredPos)
    {
        var buttonGo = new GameObject("PackRat_StationBackpack" + (text == "<" ? "Prev" : "Next") + "Button");
        buttonGo.transform.SetParent(parent, worldPositionStays: false);
        var rect = buttonGo.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(24f, 24f);

        var image = buttonGo.AddComponent<Image>();
        image.color = new Color32(60, 60, 60, 210);
        var button = buttonGo.AddComponent<Button>();
        button.targetGraphic = image;

        var label = CreateText("Label", buttonGo.transform, Vector2.zero, 17, text);
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        return button;
    }

    private static Text CreatePagerLabel(Transform parent, Vector2 anchoredPos)
    {
        var label = CreateText("PackRat_StationBackpackPageLabel", parent, anchoredPos, 13, "Page 1/1");
        label.fontStyle = FontStyle.Normal;
        label.color = new Color32(220, 220, 220, 255);
        label.GetComponent<RectTransform>().sizeDelta = new Vector2(104f, 22f);
        return label;
    }

    private static Font ResolveUiFont(Transform context)
    {
        if (context != null)
        {
            var labels = context.GetComponentsInParent<Text>(includeInactive: true);
            for (var i = 0; i < labels.Length; i++)
            {
                if (labels[i]?.font != null)
                    return labels[i].font;
            }
        }

        return Resources.GetBuiltinResource<Font>("Arial.ttf")
            ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static void ResetSlotUi(ItemSlotUI slotUi)
    {
        if (slotUi == null)
            return;

        var itemUi = slotUi.ItemUI;
        if (itemUi != null)
        {
            itemUi.Destroy();
            slotUi.ItemUI = null;
        }

        if (slotUi.ItemContainer != null)
        {
            for (var i = slotUi.ItemContainer.childCount - 1; i >= 0; i--)
            {
                var child = slotUi.ItemContainer.GetChild(i);
                if (child != null)
                    UnityEngine.Object.Destroy(child.gameObject);
            }
        }
    }

    private static void StripTemplateText(GameObject slotObject, ItemSlotUI slotUi)
    {
        if (slotObject == null)
            return;

        var components = slotObject.GetComponentsInChildren<Component>(includeInactive: true);
        for (var i = 0; i < components.Length; i++)
        {
            var component = components[i];
            if (component == null || component.gameObject == slotObject)
                continue;

            if (slotUi != null
                && slotUi.ItemContainer != null
                && component.transform.IsChildOf(slotUi.ItemContainer))
            {
                continue;
            }

            var typeName = component.GetType().Name;
            var fullName = component.GetType().FullName ?? string.Empty;
            if (!typeName.Contains("Text", StringComparison.OrdinalIgnoreCase)
                && !fullName.Contains("TMPro", StringComparison.OrdinalIgnoreCase)
                && !fullName.Contains("TextMeshPro", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            UnityEngine.Object.Destroy(component.gameObject);
        }
    }

    [HarmonyPatch(typeof(ChemistryStationCanvas), "Open")]
    private static class ChemistryOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ChemistryStationCanvas __instance) => ShowForStation(__instance);
    }

    [HarmonyPatch(typeof(ChemistryStationCanvas), "Close")]
    private static class ChemistryClosePatch
    {
        [HarmonyPrefix]
        public static void Prefix(ChemistryStationCanvas __instance) => HideForStation(__instance);
    }

    [HarmonyPatch(typeof(MixingStationCanvas), "Open")]
    private static class MixingOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MixingStationCanvas __instance) => ShowForStation(__instance);
    }

    [HarmonyPatch(typeof(MixingStationCanvas), "Close")]
    private static class MixingClosePatch
    {
        [HarmonyPrefix]
        public static void Prefix(MixingStationCanvas __instance) => HideForStation(__instance);
    }

    [HarmonyPatch(typeof(PackagingStationCanvas), "SetIsOpen")]
    private static class PackagingSetOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PackagingStationCanvas __instance, bool open)
        {
            if (open)
                ShowForStation(__instance);
            else
                HideForStation(__instance);
        }
    }

    [HarmonyPatch(typeof(BrickPressCanvas), "SetIsOpen")]
    private static class BrickPressSetOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BrickPressCanvas __instance, bool open)
        {
            if (open)
                ShowForStation(__instance);
            else
                HideForStation(__instance);
        }
    }

    [HarmonyPatch(typeof(CauldronCanvas), "SetIsOpen")]
    private static class CauldronSetOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(CauldronCanvas __instance, bool open)
        {
            if (open)
                ShowForStation(__instance);
            else
                HideForStation(__instance);
        }
    }

    [HarmonyPatch(typeof(LabOvenCanvas), "SetIsOpen")]
    private static class LabOvenSetOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LabOvenCanvas __instance, bool open)
        {
            if (open)
                ShowForStation(__instance);
            else
                HideForStation(__instance);
        }
    }

    [HarmonyPatch(typeof(DryingRackCanvas), "SetIsOpen")]
    private static class DryingRackSetOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(DryingRackCanvas __instance, bool open)
        {
            if (open)
                ShowForStation(__instance);
            else
                HideForStation(__instance);
        }
    }

    [HarmonyPatch(typeof(MushroomSpawnStationInterface), "Open")]
    private static class MushroomSpawnOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MushroomSpawnStationInterface __instance) => ShowForStation(__instance);
    }

    [HarmonyPatch(typeof(MushroomSpawnStationInterface), "Close")]
    private static class MushroomSpawnClosePatch
    {
        [HarmonyPrefix]
        public static void Prefix(MushroomSpawnStationInterface __instance) => HideForStation(__instance);
    }
}
