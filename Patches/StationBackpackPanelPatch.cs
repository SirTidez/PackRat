using HarmonyLib;
using PackRat.Config;
using PackRat.Extensions;
using PackRat.Helpers;
using PackRat.Logic;
using PackRat.Profiling;
using System.Reflection;
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
        public RectTransform StationContainer;
        public RectTransform Root;
        public RectTransform HeaderRoot;
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
        public int OwnerId;
    }

    private const int SlotsPerPage = 16;
    private const int GridRows = 4;
    private const float PanelMargin = 24f;
    private static readonly Vector2 PanelSize = new Vector2(360f, 410f);
    private static readonly Vector2 SlotContainerSize = new Vector2(306f, 306f);
    private static readonly Vector2 SlotSize = new Vector2(72f, 72f);
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

        // Preserve the game's cash/employee-locker routing. Station panels only extend item
        // transfers; money must remain on the native CashSlot path and never spill into a
        // backpack merely because one is open beside the station.
        if (sourceSlot.ItemInstance is CashInstance)
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
        using var profile = UiProfiler.Measure("station", "show",
            $"type={stationCanvas?.GetType().FullName};id={stationCanvas?.GetInstanceID()}");
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

            panel.Root.gameObject.SetActive(true);
            panel.OwnerId = stationCanvas.GetInstanceID();
            if (panel.PagingRoot != null)
                panel.PagingRoot.gameObject.SetActive(false);
            StorageMenuPatch.ApplyEmbeddedBackpackBrowser(panel.Root, panel.SlotContainer, panel.Grid,
                panel.SlotUIs, layoutView: 2, ownerId: panel.OwnerId);
            var backpackSlots = GetBackpackSlots();
            RebuildQuickMove(stationSlots, backpackSlots);
        }
        catch (Exception ex)
        {
            ModLogger.Error("StationBackpackPanelPatch.ShowForStation", ex);
        }
    }

    private static void HideForStation(Component stationCanvas)
    {
        using var profile = UiProfiler.Measure("station", "hide",
            $"type={stationCanvas?.GetType().FullName};id={stationCanvas?.GetInstanceID()}");
        if (stationCanvas == null)
            return;

        if (!Panels.TryGetValue(stationCanvas.GetInstanceID(), out var panel))
            return;

        // The shared browser owns a separate binding cache. Clear it before the station tears
        // down its cloned ItemSlotUIs so the next Open always performs a real rebind.
        StorageMenuPatch.ResetEmbeddedBackpackBrowser(panel.Root);

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

    /// <summary>
    /// Reapplies the station panel's position and scale after a live preference change.
    /// </summary>
    public static void RefreshActiveLayouts()
    {
        try
        {
            foreach (var panel in Panels.Values)
            {
                if (panel?.Root == null || panel.StationContainer == null || !panel.Root.gameObject.activeSelf)
                    continue;

                StorageMenuPatch.ApplyEmbeddedBackpackBrowser(panel.Root, panel.SlotContainer, panel.Grid,
                    panel.SlotUIs, layoutView: 2, ownerId: panel.OwnerId);
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("StationBackpackPanelPatch.RefreshActiveLayouts", ex);
        }
    }

    private static PanelState EnsurePanel(Component stationCanvas, RectTransform stationContainer, ItemSlotUI slotTemplate)
    {
        var id = stationCanvas.GetInstanceID();
        if (Panels.TryGetValue(id, out var existing)
            && existing.Initialized
            && existing.Root != null
            && existing.SlotUIs != null)
        {
            existing.StationContainer = stationContainer;
            ConfigureRoot(stationContainer, existing.Root);
            EnsurePager(existing);
            return existing;
        }

        var panel = existing ?? new PanelState();
        panel.StationContainer = stationContainer;
        var rootObject = new GameObject("PackRat_StationBackpackPanel");
        var root = rootObject.AddComponent<RectTransform>();
        ConfigureRoot(stationContainer, root);
        root.gameObject.SetActive(false);

        panel.Root = root;
        panel.HeaderRoot = CreatePanelHeader(root);
        panel.HeaderRoot.gameObject.SetActive(false);
        panel.TitleLabel = CreateText("PackRat_StationBackpackTitle", panel.HeaderRoot, new Vector2(0f, 10f), 16, "BACKPACK");
        panel.SubtitleLabel = CreateText("PackRat_StationBackpackSubtitle", panel.HeaderRoot, new Vector2(0f, -11f), 11, string.Empty);
        panel.TitleLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(306f, 24f);
        panel.SubtitleLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(306f, 18f);

        var slotContainerObject = new GameObject("PackRat_StationBackpackSlotContainer");
        var slotContainer = slotContainerObject.AddComponent<RectTransform>();
        slotContainer.SetParent(root, worldPositionStays: false);
        slotContainer.anchorMin = new Vector2(0.5f, 0.5f);
        slotContainer.anchorMax = new Vector2(0.5f, 0.5f);
        slotContainer.pivot = new Vector2(0.5f, 0.5f);
        slotContainer.anchoredPosition = new Vector2(0f, -24f);
        slotContainer.sizeDelta = SlotContainerSize;
        panel.SlotContainer = slotContainer;

        var grid = slotContainerObject.AddComponent<GridLayoutGroup>();
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = GridRows;
        grid.cellSize = SlotSize;
        grid.spacing = new Vector2(6f, 6f);
        grid.childAlignment = TextAnchor.UpperCenter;
        panel.Grid = grid;

        panel.SlotUIs = new ItemSlotUI[20];
        for (var i = 0; i < panel.SlotUIs.Length; i++)
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
        var scale = Mathf.Clamp(config.StationOverlayScale, 0.5f, 1.5f);
        var desired = new Vector2(
            stationContainer.anchoredPosition.x - stationContainer.rect.width * 0.5f - panel.Root.rect.width * scale * 0.5f - PanelMargin
                + config.StationOverlayOffsetX,
            stationContainer.anchoredPosition.y + config.StationOverlayOffsetY
        );
        panel.Root.anchoredPosition = ClampToParentBounds(panel.Root, desired, PanelMargin);
    }

    private static void ConfigureRoot(RectTransform stationContainer, RectTransform root)
    {
        if (root == null)
            return;

        var overlayParent = FindOverlayParent(stationContainer);
        if (overlayParent != null && root.parent != overlayParent)
            root.SetParent(overlayParent, worldPositionStays: false);

        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.pivot = new Vector2(0.5f, 0.5f);
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one;
        EnsureIgnoredByLayout(root);
        EnsureOverlaySorting(root, stationContainer);
    }

    private static RectTransform CreatePanelHeader(RectTransform root)
    {
        var headerGo = new GameObject("PackRat_StationBackpackHeader");
        var header = headerGo.AddComponent<RectTransform>();
        header.SetParent(root, worldPositionStays: false);
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = new Vector2(1f, 1f);
        header.pivot = new Vector2(0.5f, 1f);
        header.offsetMin = new Vector2(10f, -62f);
        header.offsetMax = new Vector2(-10f, -8f);
        var image = headerGo.AddComponent<Image>();
        image.color = new Color32(35, 61, 86, 248);
        image.raycastTarget = false;

        var accentGo = new GameObject("Accent");
        var accent = accentGo.AddComponent<RectTransform>();
        accent.SetParent(header, worldPositionStays: false);
        accent.anchorMin = new Vector2(0f, 0f);
        accent.anchorMax = new Vector2(1f, 0f);
        accent.pivot = new Vector2(0.5f, 0f);
        accent.offsetMin = Vector2.zero;
        accent.offsetMax = new Vector2(0f, 3f);
        var accentImage = accentGo.AddComponent<Image>();
        accentImage.color = new Color32(76, 173, 229, 255);
        accentImage.raycastTarget = false;
        header.SetAsFirstSibling();
        return header;
    }

    private static void EnsurePanelBackground(RectTransform root)
    {
        if (root == null)
            return;

#if !MONO
        var image = Utils.GetOrAddComponentSafe<Image>(root.gameObject);
#else
        var image = root.GetComponent<Image>();
        if (image == null)
            image = root.gameObject.AddComponent<Image>();
#endif
        if (image != null)
        {
            image.color = new Color32(18, 20, 23, 220);
            image.raycastTarget = false;
        }
    }

    private static Vector2 ClampToParentBounds(RectTransform rectTransform, Vector2 desired, float margin)
    {
        var parent = rectTransform?.parent as RectTransform;
        if (rectTransform == null || parent == null)
            return desired;

        var halfWidth = Mathf.Max(0f, parent.rect.width * 0.5f - rectTransform.rect.width * rectTransform.localScale.x * 0.5f - margin);
        var halfHeight = Mathf.Max(0f, parent.rect.height * 0.5f - rectTransform.rect.height * rectTransform.localScale.y * 0.5f - margin);
        return new Vector2(
            Mathf.Clamp(desired.x, -halfWidth, halfWidth),
            Mathf.Clamp(desired.y, -halfHeight, halfHeight)
        );
    }

    private static Transform FindOverlayParent(RectTransform stationContainer)
    {
        if (stationContainer == null)
            return null;

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

    private static void EnsureOverlaySorting(RectTransform root, RectTransform sourceContainer)
    {
        if (root == null)
            return;

#if !MONO
        var rootCanvas = Utils.GetOrAddComponentSafe<Canvas>(root.gameObject);
#else
        var rootCanvas = root.GetComponent<Canvas>();
        if (rootCanvas == null)
            rootCanvas = root.gameObject.AddComponent<Canvas>();
#endif
        if (rootCanvas != null)
        {
            rootCanvas.overrideSorting = true;
            var parentCanvas = sourceContainer != null
                ? sourceContainer.GetComponentInParent<Canvas>()
                : null;
            Canvas hudCanvas = null;
            try
            {
                hudCanvas = Singleton<HUD>.Instance?.canvas;
            }
            catch
            {
            }

            if (hudCanvas != null)
            {
                // ItemUIManager creates the temporary dragged icon under HUD.transform. Keep the
                // station browser immediately below that owning canvas instead of deriving its
                // order from station canvases, whose orders vary between station interfaces.
                rootCanvas.sortingLayerID = hudCanvas.sortingLayerID;
                rootCanvas.sortingOrder = hudCanvas.sortingOrder - 1;
            }
            else if (parentCanvas != null)
            {
                rootCanvas.sortingLayerID = parentCanvas.sortingLayerID;
                rootCanvas.sortingOrder = parentCanvas.sortingOrder + 1;
            }
            else
            {
                rootCanvas.sortingOrder = 5000;
            }

            if (UiProfiler.IsEnabled)
                UiProfiler.Event("station", "canvas_configured",
                    $"ownerOrder={parentCanvas?.sortingOrder};hudOrder={hudCanvas?.sortingOrder};" +
                    $"panelOrder={rootCanvas.sortingOrder};panelLayer={rootCanvas.sortingLayerID}");
        }

#if !MONO
        var raycaster = Utils.GetOrAddComponentSafe<GraphicRaycaster>(root.gameObject);
#else
        var raycaster = root.GetComponent<GraphicRaycaster>();
        if (raycaster == null)
            raycaster = root.gameObject.AddComponent<GraphicRaycaster>();
#endif
        RegisterItemUiRaycaster(raycaster);
        root.SetAsLastSibling();
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

    private static void UpdatePanelHeader(PanelState panel, List<ItemSlot> backpackSlots)
    {
        if (panel == null)
            return;

        var usedSlots = 0;
        for (var i = 0; i < backpackSlots.Count; i++)
        {
            if (backpackSlots[i]?.ItemInstance != null)
                usedSlots++;
        }

        if (panel.TitleLabel != null)
            panel.TitleLabel.text = "BACKPACK";
        if (panel.SubtitleLabel != null)
            panel.SubtitleLabel.text = $"{usedSlots} / {backpackSlots.Count} slots";
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
            pagingRoot.anchoredPosition = new Vector2(0f, -226f);
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

    private static IEnumerable<MethodBase> ResolveStationMethods(string methodName, params string[] typeNames)
    {
        var seen = new HashSet<MethodBase>();
        foreach (var typeName in RuntimeCompatibility.ExpandScheduleOneTypeNames(typeNames))
        {
            var type = ReflectionUtils.GetTypeByName(typeName);
            var method = ReflectionUtils.GetMethod(
                type,
                methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null && seen.Add(method))
                yield return method;
        }
    }

    private static bool GetOpenArg(object[] args)
    {
        if (args == null)
            return false;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is bool open)
                return open;
        }

        return false;
    }

    [HarmonyPatch]
    private static class ChemistryOpenPatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "Open",
            "ScheduleOne.UI.Stations.ChemistryStationInterface",
            "ScheduleOne.UI.Stations.ChemistryStationCanvas");

        [HarmonyPostfix]
        public static void Postfix(Component __instance) => ShowForStation(__instance);
    }

    [HarmonyPatch]
    private static class ChemistryClosePatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "Close",
            "ScheduleOne.UI.Stations.ChemistryStationInterface",
            "ScheduleOne.UI.Stations.ChemistryStationCanvas");

        [HarmonyPrefix]
        public static void Prefix(Component __instance) => HideForStation(__instance);
    }

    [HarmonyPatch]
    private static class MixingOpenPatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "Open",
            "ScheduleOne.UI.Stations.MixingStationInterface",
            "ScheduleOne.UI.Stations.MixingStationCanvas");

        [HarmonyPostfix]
        public static void Postfix(Component __instance) => ShowForStation(__instance);
    }

    [HarmonyPatch]
    private static class MixingClosePatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "Close",
            "ScheduleOne.UI.Stations.MixingStationInterface",
            "ScheduleOne.UI.Stations.MixingStationCanvas");

        [HarmonyPrefix]
        public static void Prefix(Component __instance) => HideForStation(__instance);
    }

    [HarmonyPatch]
    private static class PackagingOpenPatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "Open",
            "ScheduleOne.UI.Stations.PackagingStationCanvas");

        [HarmonyPostfix]
        public static void Postfix(Component __instance) => ShowForStation(__instance);
    }

    [HarmonyPatch]
    private static class PackagingClosePatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "Close",
            "ScheduleOne.UI.Stations.PackagingStationCanvas");

        [HarmonyPrefix]
        public static void Prefix(Component __instance) => HideForStation(__instance);
    }

    [HarmonyPatch]
    private static class PackagingSetOpenPatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "SetIsOpen",
            "ScheduleOne.UI.Stations.PackagingStationCanvas");

        [HarmonyPostfix]
        public static void Postfix(Component __instance, object[] __args)
        {
            if (GetOpenArg(__args))
                ShowForStation(__instance);
            else
                HideForStation(__instance);
        }
    }

    [HarmonyPatch]
    private static class BrickPressOpenPatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "Open",
            "ScheduleOne.UI.Stations.BrickPressCanvas");

        [HarmonyPostfix]
        public static void Postfix(Component __instance) => ShowForStation(__instance);
    }

    [HarmonyPatch]
    private static class BrickPressClosePatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "Close",
            "ScheduleOne.UI.Stations.BrickPressCanvas");

        [HarmonyPrefix]
        public static void Prefix(Component __instance) => HideForStation(__instance);
    }

    [HarmonyPatch]
    private static class BrickPressSetOpenPatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "SetIsOpen",
            "ScheduleOne.UI.Stations.BrickPressCanvas");

        [HarmonyPostfix]
        public static void Postfix(Component __instance, object[] __args)
        {
            if (GetOpenArg(__args))
                ShowForStation(__instance);
            else
                HideForStation(__instance);
        }
    }

    [HarmonyPatch]
    private static class CauldronOpenPatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "Open",
            "ScheduleOne.UI.Stations.CauldronInterface",
            "ScheduleOne.UI.Stations.CauldronCanvas");

        [HarmonyPostfix]
        public static void Postfix(Component __instance) => ShowForStation(__instance);
    }

    [HarmonyPatch]
    private static class CauldronClosePatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "Close",
            "ScheduleOne.UI.Stations.CauldronInterface",
            "ScheduleOne.UI.Stations.CauldronCanvas");

        [HarmonyPrefix]
        public static void Prefix(Component __instance) => HideForStation(__instance);
    }

    [HarmonyPatch]
    private static class CauldronSetOpenPatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "SetIsOpen",
            "ScheduleOne.UI.Stations.CauldronCanvas");

        [HarmonyPostfix]
        public static void Postfix(Component __instance, object[] __args)
        {
            if (GetOpenArg(__args))
                ShowForStation(__instance);
            else
                HideForStation(__instance);
        }
    }

    [HarmonyPatch]
    private static class LabOvenOpenPatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "Open",
            "ScheduleOne.UI.Stations.LabOvenCanvas");

        [HarmonyPostfix]
        public static void Postfix(Component __instance) => ShowForStation(__instance);
    }

    [HarmonyPatch]
    private static class LabOvenClosePatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "Close",
            "ScheduleOne.UI.Stations.LabOvenCanvas");

        [HarmonyPrefix]
        public static void Prefix(Component __instance) => HideForStation(__instance);
    }

    [HarmonyPatch]
    private static class LabOvenSetOpenPatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "SetIsOpen",
            "ScheduleOne.UI.Stations.LabOvenCanvas");

        [HarmonyPostfix]
        public static void Postfix(Component __instance, object[] __args)
        {
            if (GetOpenArg(__args))
                ShowForStation(__instance);
            else
                HideForStation(__instance);
        }
    }

    [HarmonyPatch]
    private static class DryingRackOpenPatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "Open",
            "ScheduleOne.UI.Stations.DryingRackInterface",
            "ScheduleOne.UI.Stations.DryingRackCanvas");

        [HarmonyPostfix]
        public static void Postfix(Component __instance) => ShowForStation(__instance);
    }

    [HarmonyPatch]
    private static class DryingRackClosePatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "Close",
            "ScheduleOne.UI.Stations.DryingRackInterface",
            "ScheduleOne.UI.Stations.DryingRackCanvas");

        [HarmonyPrefix]
        public static void Prefix(Component __instance) => HideForStation(__instance);
    }

    [HarmonyPatch]
    private static class DryingRackSetOpenPatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "SetIsOpen",
            "ScheduleOne.UI.Stations.DryingRackCanvas");

        [HarmonyPostfix]
        public static void Postfix(Component __instance, object[] __args)
        {
            if (GetOpenArg(__args))
                ShowForStation(__instance);
            else
                HideForStation(__instance);
        }
    }

    [HarmonyPatch]
    private static class MushroomSpawnOpenPatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "Open",
            "ScheduleOne.UI.Stations.MushroomSpawnStationInterface");

        [HarmonyPostfix]
        public static void Postfix(Component __instance) => ShowForStation(__instance);
    }

    [HarmonyPatch]
    private static class MushroomSpawnClosePatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => RuntimeCompatibility.HasResolvedTargets(TargetMethods());

        private static IEnumerable<MethodBase> TargetMethods() => ResolveStationMethods(
            "Close",
            "ScheduleOne.UI.Stations.MushroomSpawnStationInterface");

        [HarmonyPrefix]
        public static void Prefix(Component __instance) => HideForStation(__instance);
    }
}
