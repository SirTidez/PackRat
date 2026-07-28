using HarmonyLib;
using PackRat.Config;
using PackRat.Extensions;
using PackRat.Helpers;
using PackRat.Networking;
using UnityEngine;
using UnityEngine.UI;

#if MONO
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.Money;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Storage;
using ScheduleOne.UI;
using ScheduleOne.UI.Items;
using S1TMP = TMPro.TextMeshProUGUI;
using S1Action = System.Action;
#else
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Items;
using S1TMP = Il2CppTMPro.TextMeshProUGUI;
using S1Action = Il2CppSystem.Action;
#endif

namespace PackRat.Patches;

/// <summary>
/// Harmony patches for <see cref="StorageMenu"/>.
/// Expands the slot UI array to support up to <see cref="PlayerBackpack.MaxStorageSlots"/> slots
/// and adjusts the menu layout for large slot counts.
/// </summary>
[HarmonyPatch(typeof(StorageMenu))]
public static class StorageMenuPatch
{
    private sealed class BackpackPanelState
    {
        public RectTransform Container;
        public RectTransform SlotContainer;
        public GridLayoutGroup SlotGridLayout;
        public ItemSlotUI[] SlotUIs;
        public S1TMP TitleLabel;
        public S1TMP SubtitleLabel;
        public RectTransform PagingRoot;
        public Button PrevButton;
        public Button NextButton;
        public Text PageLabel;
        public Action PrevAction;
        public Action NextAction;
        public int CurrentPage;
        public int SlotsPerPage;
        public int LastPageInputFrame;
        public bool Initialized;
    }

    private sealed class StandaloneBackpackState
    {
        public RectTransform VisualRoot;
        public Text VisualTitleLabel;
        public Text VisualMetaLabel;
        public InputField SearchInput;
        public Text SearchText;
        public Text SearchPlaceholder;
        public Action<string> SearchAction;
        public RectTransform PagingRoot;
        public Button PrevButton;
        public Button NextButton;
        public Text PageLabel;
        public Action PrevAction;
        public Action NextAction;
        public int CurrentPage;
        public int LastPageInputFrame;
        public bool IsOpen;
        public string SearchTerm;
    }

    private const int StandaloneBackpackSlotsPerPage = 20;
    private const int StandaloneBackpackGridRows = 4;
    private const int SearchMetadataMinimumTermLength = 2;
    private const float StandaloneGridVerticalOffset = 118f;
    private const float StandaloneCardPadding = 14f;
    private const float StandaloneHeaderHeight = 88f;
    private const float StandaloneCloseGap = 24f;
    private const int StorageBackpackSlotsPerPage = 4;
    private const int StorageBackpackGridRows = 4;
    private const float CompactPanelMargin = 24f;
    private static readonly Vector2 CompactPanelSize = new Vector2(184f, 472f);
    private static readonly Vector2 CompactSlotContainerSize = new Vector2(152f, 332f);
    private static readonly Vector2 CompactSlotSize = new Vector2(72f, 72f);

    private static readonly Dictionary<int, BackpackPanelState> BackpackPanels = new Dictionary<int, BackpackPanelState>();
    private static readonly Dictionary<int, StandaloneBackpackState> StandaloneBackpackPanels = new Dictionary<int, StandaloneBackpackState>();
    private static readonly List<ItemSlot> ActiveInventorySlots = new List<ItemSlot>();
    private static readonly List<ItemSlot> ActiveStorageSlots = new List<ItemSlot>();
    private static readonly List<ItemSlot> ActiveBackpackSlots = new List<ItemSlot>();
    private static bool _quickMoveActive;

    [HarmonyPatch("Awake")]
    [HarmonyPrefix]
    public static void Awake(StorageMenu __instance)
    {
        if (__instance.SlotsUIs.Length >= PlayerBackpack.MaxStorageSlots)
            return;

        var container = __instance.SlotContainer;
        var prefab = __instance.SlotsUIs[0]?.gameObject;
        if (prefab == null)
        {
            ModLogger.Error("StorageMenu prefab is null. Cannot create additional slots.");
            return;
        }

        var slots = new ItemSlotUI[PlayerBackpack.MaxStorageSlots];
        for (var i = 0; i < PlayerBackpack.MaxStorageSlots; i++)
        {
            if (i < __instance.SlotsUIs.Length)
            {
                slots[i] = __instance.SlotsUIs[i];
                continue;
            }

            var slot = UnityEngine.Object.Instantiate(prefab, container);
            slot.name = $"{prefab.name} ({i})";
            var slotUi = slot.GetComponent<ItemSlotUI>();
            if (slotUi != null)
                ResetSlotUi(slotUi);
            slot.gameObject.SetActive(false);
            slots[i] = slotUi;
        }

        __instance.SlotsUIs = slots;
    }

    [HarmonyPatch("Open", [typeof(IItemSlotOwner), typeof(string), typeof(string), typeof(S1Action)])]
    [HarmonyPostfix]
    public static void Open(StorageMenu __instance, IItemSlotOwner owner, string title, string subtitle, S1Action onClosedCallback)
    {
        if (IsBackpackOwner(owner))
        {
            ModLogger.Info($"[BackpackUI] StorageMenu standalone branch: title='{title}', container='{__instance.Container?.name}', slots={owner.ItemSlots.Count}.");
            ResetStandaloneBackpackPage(__instance);
            ApplyStandaloneBackpackMenu(__instance);
            return;
        }

        RestoreStandaloneBackpackLabels(__instance);

        if (owner != null)
        {
            for (var i = 0; i < __instance.SlotsUIs.Length; i++)
            {
                var slotUi = __instance.SlotsUIs[i];
                if (slotUi == null)
                    continue;

                ResetSlotUi(slotUi);
                slotUi.ClearSlot();
                if (owner.ItemSlots.Count > i)
                {
                    slotUi.gameObject.SetActive(true);
                    slotUi.AssignSlot(owner.ItemSlots[i]);
                }
                else
                {
                    slotUi.gameObject.SetActive(false);
                }
            }
        }

        var spacing = __instance.SlotGridLayout.cellSize.y + __instance.SlotGridLayout.spacing.y;
        __instance.CloseButtonContainer.anchoredPosition = new Vector2(
            0f,
            __instance.SlotGridLayout.constraintCount * -spacing - __instance.CloseButtonContainer.sizeDelta.y
        );

        if (__instance.SlotGridLayout.constraintCount <= 4)
        {
            __instance.Container.localPosition = Vector3.zero;
        }
        else
        {
            __instance.Container.localPosition = new Vector3(
                0f,
                (__instance.SlotGridLayout.constraintCount - 4) * spacing,
                0f
            );
        }

        ApplyBackpackSidePanel(__instance, owner);
    }

    [HarmonyPatch("CloseMenu")]
    [HarmonyPrefix]
    public static void CloseMenu(StorageMenu __instance)
    {
        if (IsStandaloneBackpackOpen(__instance))
            BackpackStateSyncManager.CompleteLocalBackpackEdit();

        HideBackpackSidePanel(__instance);
        HideStandaloneBackpackPaging(__instance);
        RestoreStandaloneBackpackLabels(__instance);
        __instance.Container.localPosition = Vector3.zero;
        _quickMoveActive = false;
        ActiveInventorySlots.Clear();
        ActiveStorageSlots.Clear();
        ActiveBackpackSlots.Clear();
    }

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
            AddQuickMoveTargets(sourceSlot, ActiveStorageSlots, targets);
            AddQuickMoveTargets(sourceSlot, ActiveBackpackSlots, targets);
        }
        else if (ActiveStorageSlots.Contains(sourceSlot))
        {
            AddQuickMoveTargets(sourceSlot, ActiveInventorySlots, targets);
            AddQuickMoveTargets(sourceSlot, ActiveBackpackSlots, targets);
        }
        else if (ActiveBackpackSlots.Contains(sourceSlot))
        {
            AddQuickMoveTargets(sourceSlot, ActiveStorageSlots, targets);
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

    /// <summary>
    /// Keeps the standalone backpack menu at a fixed, readable grid size. Large bags are paged
    /// instead of increasing the grid row count, which previously pushed the close button and
    /// content beyond smaller or scaled displays.
    /// </summary>
    private static void ApplyStandaloneBackpackMenu(StorageMenu menu)
    {
        if (menu == null || menu.SlotsUIs == null || menu.SlotGridLayout == null)
            return;

        var backpackSlots = GetBackpackSlots();
        var state = EnsureStandaloneBackpackPaging(menu);
        if (state == null)
            return;

        state.IsOpen = true;
        var displaySlots = GetFilteredBackpackSlots(backpackSlots, state.SearchTerm);
        var totalPages = Mathf.Max(1, Mathf.CeilToInt(displaySlots.Count / (float)StandaloneBackpackSlotsPerPage));
        state.CurrentPage = Mathf.Clamp(state.CurrentPage, 0, totalPages - 1);
        var firstSlotIndex = state.CurrentPage * StandaloneBackpackSlotsPerPage;
        var visibleSlotCount = Mathf.Clamp(displaySlots.Count - firstSlotIndex, 1, StandaloneBackpackSlotsPerPage);
        // The card represents the backpack's capacity, not the number of current search hits.
        // Keep its geometry fixed while a filter only changes the slots populated within it.
        var gridSlotCount = Mathf.Clamp(backpackSlots.Count, 1, StandaloneBackpackSlotsPerPage);
        var gridSize = ConfigureStandaloneBackpackGrid(menu, gridSlotCount);
        EnsureStandaloneBackpackVisuals(menu, state, backpackSlots.Count, displaySlots.Count, totalPages);

        for (var i = 0; i < menu.SlotsUIs.Length; i++)
        {
            var slotUi = menu.SlotsUIs[i];
            if (slotUi == null)
                continue;

            ResetSlotUi(slotUi);
            slotUi.ClearSlot();
            var slotIndex = state.CurrentPage * StandaloneBackpackSlotsPerPage + i;
            if (i < StandaloneBackpackSlotsPerPage && slotIndex < displaySlots.Count)
            {
                slotUi.AssignSlot(displaySlots[slotIndex]);
                slotUi.gameObject.SetActive(true);
            }
            else
            {
                slotUi.gameObject.SetActive(false);
            }
        }

        menu.Container.localPosition = Vector3.zero;
        menu.CloseButtonContainer.anchoredPosition = new Vector2(
            0f,
            StandaloneGridVerticalOffset - (gridSize.y * 0.5f) - menu.CloseButtonContainer.sizeDelta.y - StandaloneCloseGap
        );
        PositionStandalonePaging(menu, state);
        UpdateStandalonePager(state, totalPages);

        ModLogger.Info(
            $"[BackpackUI] Standalone layout applied: capacitySlots={gridSlotCount}, visibleSlots={visibleSlotCount}, gridSize={gridSize}, " +
            $"gridPosition={menu.SlotContainer.anchoredPosition}, closePosition={menu.CloseButtonContainer.anchoredPosition}."
        );
    }

    /// <summary>
    /// Gives the hotkey-opened backpack a fixed four-row grid with a known center anchor.
    /// The grid owns its cell geometry; the surrounding card is sized from these same bounds.
    /// </summary>
    private static Vector2 ConfigureStandaloneBackpackGrid(StorageMenu menu, int visibleSlotCount)
    {
        var grid = menu.SlotGridLayout;
        var slotContainer = menu.SlotContainer;
        if (grid == null || slotContainer == null)
            return Vector2.zero;

        grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
        grid.constraintCount = StandaloneBackpackGridRows;
        grid.childAlignment = TextAnchor.MiddleCenter;

        var rowCount = Mathf.Min(StandaloneBackpackGridRows, Mathf.Max(1, visibleSlotCount));
        var columnCount = Mathf.Max(1, Mathf.CeilToInt(visibleSlotCount / (float)StandaloneBackpackGridRows));
        var padding = grid.padding;
        var width = (columnCount * grid.cellSize.x) + ((columnCount - 1) * grid.spacing.x) + padding.left + padding.right;
        var height = (rowCount * grid.cellSize.y) + ((rowCount - 1) * grid.spacing.y) + padding.top + padding.bottom;
        var gridSize = new Vector2(width, height);

        slotContainer.anchorMin = new Vector2(0.5f, 0.5f);
        slotContainer.anchorMax = new Vector2(0.5f, 0.5f);
        slotContainer.pivot = new Vector2(0.5f, 0.5f);
        slotContainer.sizeDelta = gridSize;
        slotContainer.anchoredPosition = new Vector2(0f, StandaloneGridVerticalOffset);
        return gridSize;
    }

    private static void EnsureStandaloneBackpackVisuals(StorageMenu menu, StandaloneBackpackState state, int slotCount,
        int filteredSlotCount, int totalPages)
    {
        if (menu?.SlotContainer == null || state == null)
            return;

        if (state.VisualRoot == null)
        {
            var visualGo = new GameObject("PackRat_BackpackVisual");
            var visualRoot = visualGo.AddComponent<RectTransform>();
            visualRoot.SetParent(menu.SlotContainer, worldPositionStays: false);
            var background = visualGo.AddComponent<Image>();
            background.color = new Color32(15, 21, 28, 238);
            background.raycastTarget = false;
            var layoutElement = visualGo.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;
            visualRoot.SetAsFirstSibling();
            state.VisualRoot = visualRoot;

            var headerGo = new GameObject("Header");
            var header = headerGo.AddComponent<RectTransform>();
            header.SetParent(visualRoot, worldPositionStays: false);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.offsetMin = new Vector2(10f, -60f);
            header.offsetMax = new Vector2(-10f, -8f);
            var headerImage = headerGo.AddComponent<Image>();
            headerImage.color = new Color32(35, 61, 86, 248);
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
            accentImage.color = new Color32(76, 173, 229, 255);
            accentImage.raycastTarget = false;

            state.VisualTitleLabel = CreateBackpackVisualLabel(header, "Title", 18, FontStyle.Bold,
                TextAnchor.MiddleLeft, new Color32(244, 247, 250, 255), new Vector2(12f, 12f), new Vector2(-12f, -10f));
            state.VisualMetaLabel = CreateBackpackVisualLabel(header, "Meta", 11, FontStyle.Bold,
                TextAnchor.LowerLeft, new Color32(166, 205, 229, 255), new Vector2(12f, 5f), new Vector2(-12f, 6f));
            CreateStandaloneSearchInput(header, state, menu);
        }

        if (state.VisualRoot.parent != menu.SlotContainer)
            state.VisualRoot.SetParent(menu.SlotContainer, worldPositionStays: false);

        var visualLayoutElement = state.VisualRoot.GetComponent<LayoutElement>();
        if (visualLayoutElement == null)
            visualLayoutElement = state.VisualRoot.gameObject.AddComponent<LayoutElement>();
        visualLayoutElement.ignoreLayout = true;

        state.VisualRoot.anchorMin = Vector2.zero;
        state.VisualRoot.anchorMax = Vector2.one;
        state.VisualRoot.offsetMin = new Vector2(-StandaloneCardPadding, -StandaloneCardPadding);
        state.VisualRoot.offsetMax = new Vector2(StandaloneCardPadding, StandaloneHeaderHeight + StandaloneCardPadding);
        state.VisualRoot.SetAsFirstSibling();

        var headerRect = state.VisualRoot.Find("Header") as RectTransform;
        if (headerRect != null)
        {
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(8f, -StandaloneHeaderHeight - 4f);
            headerRect.offsetMax = new Vector2(-8f, -8f);
        }

        ConfigureStandaloneHeaderLabels(state);
        BindStandaloneSearchInput(state, menu);

        if (menu.TitleLabel != null)
            menu.TitleLabel.gameObject.SetActive(false);
        if (menu.SubtitleLabel != null)
            menu.SubtitleLabel.gameObject.SetActive(false);

        state.VisualRoot.gameObject.SetActive(true);
        var title = PlayerBackpack.Instance?.CurrentTier?.Name ?? PlayerBackpack.StorageName;
        if (state.VisualTitleLabel != null)
            state.VisualTitleLabel.text = title.ToUpperInvariant();
        if (state.VisualMetaLabel != null)
        {
            var filterActive = !string.IsNullOrWhiteSpace(state.SearchTerm);
            var filterSummary = filterActive ? $" • {filteredSlotCount} MATCHES" : string.Empty;
            state.VisualMetaLabel.text =
                $"{slotCount} SLOTS{filterSummary}  •  PAGE {state.CurrentPage + 1}/{Mathf.Max(1, totalPages)}";
        }
    }

    private static void ConfigureStandaloneHeaderLabels(StandaloneBackpackState state)
    {
        if (state == null)
            return;

        ConfigureHeaderLabel(state.VisualTitleLabel, new Vector2(12f, -31f), new Vector2(-12f, -8f),
            TextAnchor.MiddleLeft);
        ConfigureHeaderLabel(state.VisualMetaLabel, new Vector2(12f, -50f), new Vector2(-12f, -31f),
            TextAnchor.MiddleLeft);
    }

    private static void ConfigureHeaderLabel(Text label, Vector2 offsetMin, Vector2 offsetMax, TextAnchor alignment)
    {
        if (label == null)
            return;

        var labelRect = label.GetComponent<RectTransform>();
        if (labelRect == null)
            return;

        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.offsetMin = offsetMin;
        labelRect.offsetMax = offsetMax;
        label.alignment = alignment;
    }

    private static void CreateStandaloneSearchInput(RectTransform header, StandaloneBackpackState state, StorageMenu menu)
    {
        if (header == null || state == null || state.SearchInput != null)
            return;

        var rootGo = new GameObject("SearchInput");
        var root = rootGo.AddComponent<RectTransform>();
        root.SetParent(header, worldPositionStays: false);
        root.anchorMin = new Vector2(0f, 0f);
        root.anchorMax = new Vector2(1f, 0f);
        root.pivot = new Vector2(0.5f, 0f);
        root.offsetMin = new Vector2(12f, 7f);
        root.offsetMax = new Vector2(-12f, 32f);

        var background = rootGo.AddComponent<Image>();
        background.color = new Color32(10, 15, 20, 245);
        background.raycastTarget = true;

        var input = rootGo.AddComponent<InputField>();
        input.targetGraphic = background;
        input.lineType = InputField.LineType.SingleLine;
        input.contentType = InputField.ContentType.Standard;
        input.characterLimit = 64;
        input.caretColor = new Color32(244, 247, 250, 255);

        var text = CreateSearchText(root, "Text", new Color32(244, 247, 250, 255));
        var placeholder = CreateSearchText(root, "Placeholder", new Color32(144, 167, 181, 255));
        placeholder.text = "Search name, quality, or type";
        placeholder.fontStyle = FontStyle.Italic;

        input.textComponent = text;
        input.placeholder = placeholder;
        state.SearchInput = input;
        state.SearchText = text;
        state.SearchPlaceholder = placeholder;
        state.SearchTerm = string.Empty;
        input.SetTextWithoutNotify(string.Empty);
    }

    private static Text CreateSearchText(RectTransform parent, string name, Color color)
    {
        var textGo = new GameObject(name);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.SetParent(parent, worldPositionStays: false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 1f);
        textRect.offsetMax = new Vector2(-8f, -1f);

        var text = textGo.AddComponent<Text>();
        text.font = ResolveUiFont(parent);
        text.fontSize = 12;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private static void BindStandaloneSearchInput(StandaloneBackpackState state, StorageMenu menu)
    {
        if (state?.SearchInput == null || menu == null)
            return;

        if (state.SearchAction == null)
        {
            state.SearchAction = value =>
            {
                state.SearchTerm = value ?? string.Empty;
                state.CurrentPage = 0;
                if (state.IsOpen)
                    ApplyStandaloneBackpackMenu(menu);
            };
        }

        EventHelper.RemoveListener<string>(state.SearchAction, state.SearchInput.onValueChanged);
        EventHelper.AddListener<string>(state.SearchAction, state.SearchInput.onValueChanged);
        state.SearchInput.SetTextWithoutNotify(state.SearchTerm ?? string.Empty);
    }

    private static Text CreateBackpackVisualLabel(RectTransform parent, string name, int fontSize, FontStyle style,
        TextAnchor alignment, Color color, Vector2 offsetMin, Vector2 offsetMax)
    {
        var labelGo = new GameObject(name);
        var labelRt = labelGo.AddComponent<RectTransform>();
        labelRt.SetParent(parent, worldPositionStays: false);
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = offsetMin;
        labelRt.offsetMax = offsetMax;
        var label = labelGo.AddComponent<Text>();
        label.font = ResolveUiFont(parent);
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = alignment;
        label.color = color;
        label.raycastTarget = false;
        label.resizeTextForBestFit = false;
        return label;
    }

    private static void RestoreStandaloneBackpackLabels(StorageMenu menu)
    {
        if (menu == null)
            return;

        if (menu.TitleLabel != null)
            menu.TitleLabel.gameObject.SetActive(true);
        if (menu.SubtitleLabel != null)
            menu.SubtitleLabel.gameObject.SetActive(true);

        if (StandaloneBackpackPanels.TryGetValue(menu.GetInstanceID(), out var state))
        {
            state.IsOpen = false;
            if (state.VisualRoot != null)
                state.VisualRoot.gameObject.SetActive(false);
        }
    }

    private static StandaloneBackpackState EnsureStandaloneBackpackPaging(StorageMenu menu)
    {
        if (menu == null || menu.Container == null)
            return null;

        var id = menu.GetInstanceID();
        if (!StandaloneBackpackPanels.TryGetValue(id, out var state))
        {
            state = new StandaloneBackpackState();
            StandaloneBackpackPanels[id] = state;
        }

        if (state.PagingRoot == null)
        {
            var pagingGo = new GameObject("PackRat_BackpackPaging");
            var pagingRt = pagingGo.AddComponent<RectTransform>();
            pagingRt.SetParent(menu.Container, worldPositionStays: false);
            pagingRt.anchorMin = new Vector2(0.5f, 0.5f);
            pagingRt.anchorMax = new Vector2(0.5f, 0.5f);
            pagingRt.pivot = new Vector2(0.5f, 1f);
            pagingRt.sizeDelta = new Vector2(180f, 32f);
            state.PagingRoot = pagingRt;
        }

        if (state.PrevButton == null)
            state.PrevButton = CreatePagerButton("<", state.PagingRoot, new Vector2(-70f, -10f));
        if (state.NextButton == null)
            state.NextButton = CreatePagerButton(">", state.PagingRoot, new Vector2(70f, -10f));
        if (state.PageLabel == null)
            state.PageLabel = CreatePagerLabel(state.PagingRoot, new Vector2(0f, -10f));

        if (state.PrevAction == null)
            state.PrevAction = () =>
            {
                if (state.LastPageInputFrame == Time.frameCount || state.CurrentPage <= 0)
                    return;

                state.LastPageInputFrame = Time.frameCount;
                state.CurrentPage--;
                ApplyStandaloneBackpackMenu(menu);
            };

        if (state.NextAction == null)
            state.NextAction = () =>
            {
                var filteredSlots = GetFilteredBackpackSlots(GetBackpackSlots(), state.SearchTerm);
                var totalPages = Mathf.Max(1, Mathf.CeilToInt(filteredSlots.Count / (float)StandaloneBackpackSlotsPerPage));
                if (state.LastPageInputFrame == Time.frameCount || state.CurrentPage >= totalPages - 1)
                    return;

                state.LastPageInputFrame = Time.frameCount;
                state.CurrentPage++;
                ApplyStandaloneBackpackMenu(menu);
            };

        EventHelper.RemoveListener(state.PrevAction, state.PrevButton.onClick);
        EventHelper.AddListener(state.PrevAction, state.PrevButton.onClick);
        EventHelper.RemoveListener(state.NextAction, state.NextButton.onClick);
        EventHelper.AddListener(state.NextAction, state.NextButton.onClick);
        return state;
    }

    private static void PositionStandalonePaging(StorageMenu menu, StandaloneBackpackState state)
    {
        if (menu?.CloseButtonContainer == null || state?.PagingRoot == null)
            return;

        state.PagingRoot.anchoredPosition = menu.CloseButtonContainer.anchoredPosition + new Vector2(0f, -32f);
        state.PagingRoot.gameObject.SetActive(true);
    }

    private static void UpdateStandalonePager(StandaloneBackpackState state, int totalPages)
    {
        if (state == null)
            return;

        var showPaging = totalPages > 1;
        if (state.PageLabel != null)
        {
            state.PageLabel.gameObject.SetActive(true);
            state.PageLabel.text = $"Page {state.CurrentPage + 1}/{Mathf.Max(1, totalPages)}";
        }

        if (state.PrevButton != null)
        {
            state.PrevButton.gameObject.SetActive(showPaging);
            state.PrevButton.interactable = showPaging && state.CurrentPage > 0;
        }

        if (state.NextButton != null)
        {
            state.NextButton.gameObject.SetActive(showPaging);
            state.NextButton.interactable = showPaging && state.CurrentPage < totalPages - 1;
        }
    }

    private static void HideStandaloneBackpackPaging(StorageMenu menu)
    {
        if (menu == null || !StandaloneBackpackPanels.TryGetValue(menu.GetInstanceID(), out var state))
            return;

        if (state.PagingRoot != null)
            state.PagingRoot.gameObject.SetActive(false);
    }

    private static void ResetStandaloneBackpackPage(StorageMenu menu)
    {
        if (menu != null && StandaloneBackpackPanels.TryGetValue(menu.GetInstanceID(), out var state))
        {
            state.CurrentPage = 0;
            state.SearchTerm = string.Empty;
            if (state.SearchInput != null)
                state.SearchInput.SetTextWithoutNotify(string.Empty);
        }
    }

    private static bool IsStandaloneBackpackOpen(StorageMenu menu)
    {
        return menu != null
            && StandaloneBackpackPanels.TryGetValue(menu.GetInstanceID(), out var state)
            && state.IsOpen;
    }

    /// <summary>
    /// Returns whether the standalone backpack search box owns keyboard input. The backpack
    /// hotkey must not close the menu while it is being typed into.
    /// </summary>
    public static bool IsStandaloneBackpackSearchFocused()
    {
        try
        {
            var menu = Singleton<StorageMenu>.Instance;
            return menu != null
                && StandaloneBackpackPanels.TryGetValue(menu.GetInstanceID(), out var state)
                && state.IsOpen
                && state.SearchInput != null
                && state.SearchInput.gameObject.activeInHierarchy
                && state.SearchInput.isFocused;
        }
        catch (Exception ex)
        {
            ModLogger.Error("StorageMenuPatch.IsStandaloneBackpackSearchFocused", ex);
            return false;
        }
    }

    private static void ApplyBackpackSidePanel(StorageMenu menu, IItemSlotOwner openedOwner)
    {
        try
        {
            HideBackpackSidePanel(menu);

            if (menu == null || openedOwner == null || IsBackpackOwner(openedOwner))
                return;

            var backpackSlots = GetBackpackSlots();
            if (backpackSlots.Count == 0)
                return;

            var panel = EnsureBackpackPanel(menu);
            if (panel?.Container == null || panel.SlotUIs == null)
                return;

            panel.CurrentPage = 0;
            SetPanelHeader(menu, panel.Container);
            PositionSideBySide(menu, panel);
            AssignBackpackSlots(panel, backpackSlots);
            panel.Container.gameObject.SetActive(true);
            RebuildStorageQuickMove(openedOwner, backpackSlots);
        }
        catch (Exception ex)
        {
            ModLogger.Error("StorageMenuPatch.ApplyBackpackSidePanel", ex);
        }
    }

    private static BackpackPanelState EnsureBackpackPanel(StorageMenu menu)
    {
        if (menu == null || menu.Container == null)
            return null;

        var id = menu.GetInstanceID();
        if (BackpackPanels.TryGetValue(id, out var existing)
            && existing.Initialized
            && existing.Container != null
            && existing.SlotContainer != null
            && existing.SlotGridLayout != null
            && existing.SlotUIs != null)
        {
            EnsureOverlaySorting(existing.Container, menu.Container);
            EnsurePagingControls(existing);
            return existing;
        }

        var panel = existing ?? new BackpackPanelState();

        var rootObject = new GameObject("PackRat_BackpackStoragePanel");
        var root = rootObject.AddComponent<RectTransform>();
        root.SetParent(menu.Container.parent, worldPositionStays: false);
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = CompactPanelSize;
        root.localScale = Vector3.one;
        root.gameObject.SetActive(false);
        EnsureOverlaySorting(root, menu.Container);

        panel.Container = root;
        panel.TitleLabel = CloneLabel(menu.TitleLabel, menu.Container, root);
        panel.SubtitleLabel = CloneLabel(menu.SubtitleLabel, menu.Container, root);

        var slotContainer = UnityEngine.Object.Instantiate(menu.SlotContainer, root);
        slotContainer.name = "PackRat_BackpackSlotContainer";
        CopyRectTransform(menu.Container, menu.SlotContainer, root, slotContainer);
        panel.SlotContainer = slotContainer;
        panel.SlotGridLayout = slotContainer.GetComponent<GridLayoutGroup>();
        panel.SlotUIs = slotContainer.GetComponentsInChildren<ItemSlotUI>(includeInactive: true);
        panel.SlotsPerPage = StorageBackpackSlotsPerPage;
        ConfigureCompactSidePanel(menu, panel);
        panel.Initialized = true;
        BackpackPanels[id] = panel;

        SetPanelHeader(menu, root);
        EnsurePagingControls(panel);
        return panel;
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
            if (parentCanvas != null)
            {
                rootCanvas.sortingLayerID = parentCanvas.sortingLayerID;
                rootCanvas.sortingOrder = parentCanvas.sortingOrder + 200;
            }
            else
            {
                rootCanvas.sortingOrder = 5000;
            }
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
            ModLogger.Error("StorageMenuPatch.RegisterItemUiRaycaster", ex);
        }
    }

    private static void PositionSideBySide(StorageMenu menu, BackpackPanelState panel)
    {
        var clone = panel.Container;
        if (menu?.Container == null || clone == null)
            return;

        var original = menu.Container;
        var config = Configuration.Instance;
        var desired = new Vector2(
            original.anchoredPosition.x - original.rect.width * 0.5f - clone.rect.width * 0.5f - CompactPanelMargin
                + config.StorageOverlayOffsetX,
            original.anchoredPosition.y + config.StorageOverlayOffsetY
        );
        clone.anchoredPosition = ClampToParentBounds(clone, desired, CompactPanelMargin);
    }

    private static void ConfigureCompactSidePanel(StorageMenu menu, BackpackPanelState panel)
    {
        if (panel?.Container == null || panel.SlotContainer == null)
            return;

        panel.Container.sizeDelta = CompactPanelSize;
        EnsureCompactPanelBackground(panel.Container);

        panel.SlotContainer.anchorMin = new Vector2(0.5f, 0.5f);
        panel.SlotContainer.anchorMax = new Vector2(0.5f, 0.5f);
        panel.SlotContainer.pivot = new Vector2(0.5f, 0.5f);
        panel.SlotContainer.anchoredPosition = new Vector2(0f, -14f);
        panel.SlotContainer.sizeDelta = CompactSlotContainerSize;

        if (panel.SlotGridLayout != null)
        {
            panel.SlotGridLayout.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            panel.SlotGridLayout.constraintCount = StorageBackpackGridRows;
            panel.SlotGridLayout.cellSize = CompactSlotSize;
            panel.SlotGridLayout.spacing = new Vector2(8f, 8f);
            panel.SlotGridLayout.childAlignment = TextAnchor.UpperCenter;
        }

        PositionCompactLabel(panel.TitleLabel, new Vector2(0f, 206f), new Vector2(160f, 30f), 18f);
        PositionCompactLabel(panel.SubtitleLabel, new Vector2(0f, 176f), new Vector2(160f, 24f), 12f);
    }

    private static void EnsureCompactPanelBackground(RectTransform container)
    {
        if (container == null)
            return;

#if !MONO
        var image = Utils.GetOrAddComponentSafe<Image>(container.gameObject);
#else
        var image = container.GetComponent<Image>();
        if (image == null)
            image = container.gameObject.AddComponent<Image>();
#endif
        if (image != null)
        {
            image.color = new Color32(18, 20, 23, 220);
            image.raycastTarget = false;
        }
    }

    private static void PositionCompactLabel(S1TMP label, Vector2 position, Vector2 size, float fontSize)
    {
        if (label == null)
            return;

        var rect = label.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
        label.fontSize = fontSize;
    }

    private static Vector2 ClampToParentBounds(RectTransform rectTransform, Vector2 desired, float margin)
    {
        var parent = rectTransform?.parent as RectTransform;
        if (parent == null || rectTransform == null)
            return desired;

        var halfWidth = Mathf.Max(0f, parent.rect.width * 0.5f - rectTransform.rect.width * 0.5f - margin);
        var halfHeight = Mathf.Max(0f, parent.rect.height * 0.5f - rectTransform.rect.height * 0.5f - margin);
        return new Vector2(
            Mathf.Clamp(desired.x, -halfWidth, halfWidth),
            Mathf.Clamp(desired.y, -halfHeight, halfHeight)
        );
    }

    private static void AssignBackpackSlots(BackpackPanelState panel, List<ItemSlot> backpackSlots)
    {
        if (panel.SlotGridLayout != null)
            panel.SlotGridLayout.constraintCount = StorageBackpackGridRows;

        var slotsPerPage = Mathf.Max(1, panel.SlotsPerPage > 0 ? panel.SlotsPerPage : StorageBackpackSlotsPerPage);
        var totalPages = Mathf.Max(1, Mathf.CeilToInt(backpackSlots.Count / (float)slotsPerPage));
        if (panel.CurrentPage < 0)
            panel.CurrentPage = 0;
        if (panel.CurrentPage >= totalPages)
            panel.CurrentPage = totalPages - 1;

        for (var i = 0; i < panel.SlotUIs.Length; i++)
        {
            var slotUi = panel.SlotUIs[i];
            if (slotUi == null)
                continue;

            ResetSlotUi(slotUi);
            slotUi.ClearSlot();
            if (i >= slotsPerPage)
            {
                slotUi.gameObject.SetActive(false);
                continue;
            }

            var slotIndex = panel.CurrentPage * slotsPerPage + i;
            if (slotIndex >= 0 && slotIndex < backpackSlots.Count)
            {
                slotUi.gameObject.SetActive(true);
                slotUi.AssignSlot(backpackSlots[slotIndex]);
            }
            else
            {
                slotUi.gameObject.SetActive(false);
            }
        }

        UpdatePagerControls(panel, totalPages);
    }

    private static void EnsurePagingControls(BackpackPanelState panel)
    {
        if (panel == null || panel.Container == null)
            return;

        if (panel.PagingRoot == null)
        {
            var pagingGo = new GameObject("PackRat_StorageBackpackPaging");
            var pagingRt = pagingGo.AddComponent<RectTransform>();
            pagingRt.SetParent(panel.Container, worldPositionStays: false);
            pagingRt.anchorMin = new Vector2(0.5f, 0.5f);
            pagingRt.anchorMax = new Vector2(0.5f, 0.5f);
            pagingRt.pivot = new Vector2(0.5f, 1f);
            pagingRt.anchoredPosition = new Vector2(0f, -226f);
            pagingRt.sizeDelta = new Vector2(180f, 40f);
            panel.PagingRoot = pagingRt;
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
                AssignBackpackSlots(panel, GetBackpackSlots());
            };

        if (panel.NextAction == null)
            panel.NextAction = () =>
            {
                if (panel.LastPageInputFrame == Time.frameCount)
                    return;

                var totalPages = Mathf.Max(1, Mathf.CeilToInt(GetBackpackSlots().Count / (float)StorageBackpackSlotsPerPage));
                if (panel.CurrentPage >= totalPages - 1)
                    return;

                panel.LastPageInputFrame = Time.frameCount;
                panel.CurrentPage++;
                AssignBackpackSlots(panel, GetBackpackSlots());
            };

        if (panel.PrevButton != null)
        {
            EventHelper.RemoveListener(panel.PrevAction, panel.PrevButton.onClick);
            EventHelper.AddListener(panel.PrevAction, panel.PrevButton.onClick);
        }

        if (panel.NextButton != null)
        {
            EventHelper.RemoveListener(panel.NextAction, panel.NextButton.onClick);
            EventHelper.AddListener(panel.NextAction, panel.NextButton.onClick);
        }
    }

    private static Button CreatePagerButton(string text, Transform parent, Vector2 anchoredPos)
    {
        var buttonGo = new GameObject("PackRat_StorageBackpack" + (text == "<" ? "Prev" : "Next") + "Button");
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
        var labelGo = new GameObject("PackRat_StorageBackpackPageLabel");
        labelGo.transform.SetParent(parent, worldPositionStays: false);

        var rt = labelGo.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(104f, 22f);

        var label = labelGo.AddComponent<Text>();
        label.text = "Page 1/1";
        label.fontSize = 13;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color32(220, 220, 220, 255);
        label.resizeTextForBestFit = false;
        label.font = ResolveUiFont(parent);
        label.raycastTarget = false;
        return label;
    }

    private static Font ResolveUiFont(Transform context)
    {
        if (context != null)
        {
            var textLabels = context.GetComponentsInParent<Text>(true);
            for (var i = 0; i < textLabels.Length; i++)
            {
                var text = textLabels[i];
                if (text != null && text.font != null)
                    return text.font;
            }
        }

        var arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (arial != null)
            return arial;

        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static void UpdatePagerControls(BackpackPanelState panel, int totalPages)
    {
        var showPaging = panel != null && totalPages > 1;

        if (panel?.PagingRoot != null)
            panel.PagingRoot.gameObject.SetActive(true);

        if (panel?.PageLabel != null)
        {
            panel.PageLabel.gameObject.SetActive(true);
            panel.PageLabel.text = $"Page {panel.CurrentPage + 1}/{Mathf.Max(1, totalPages)}";
        }

        if (panel?.PrevButton != null)
        {
            panel.PrevButton.gameObject.SetActive(showPaging);
            panel.PrevButton.interactable = showPaging && panel.CurrentPage > 0;
        }

        if (panel?.NextButton != null)
        {
            panel.NextButton.gameObject.SetActive(showPaging);
            panel.NextButton.interactable = showPaging && panel.CurrentPage < totalPages - 1;
        }
    }

    private static void HideBackpackSidePanel(StorageMenu menu)
    {
        if (menu == null)
            return;

        if (!BackpackPanels.TryGetValue(menu.GetInstanceID(), out var panel))
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

        if (panel.Container != null)
            panel.Container.gameObject.SetActive(false);
    }

    private static void RebuildStorageQuickMove(IItemSlotOwner openedOwner, List<ItemSlot> backpackSlots)
    {
        _quickMoveActive = false;
        ActiveInventorySlots.Clear();
        ActiveStorageSlots.Clear();
        ActiveBackpackSlots.Clear();

#if MONO
        var inventory = PlayerInventory.Instance;
#else
        var inventory = PlayerSingleton<PlayerInventory>.Instance;
#endif
        if (inventory == null || openedOwner?.ItemSlots == null)
            return;

        foreach (var slot in inventory.GetAllInventorySlots().AsEnumerable())
        {
            if (slot != null)
                ActiveInventorySlots.Add(slot);
        }

        foreach (var slot in openedOwner.ItemSlots.AsEnumerable())
        {
            if (slot != null)
                ActiveStorageSlots.Add(slot);
        }

        foreach (var slot in backpackSlots)
        {
            if (slot != null)
                ActiveBackpackSlots.Add(slot);
        }

        var secondarySlots = new List<ItemSlot>(ActiveStorageSlots);
        secondarySlots.AddRange(ActiveBackpackSlots);

#if !MONO
        Singleton<ItemUIManager>.Instance.EnableQuickMove(ActiveInventorySlots.ToIl2CppList(), secondarySlots.ToIl2CppList());
#else
        Singleton<ItemUIManager>.Instance.EnableQuickMove(ActiveInventorySlots, secondarySlots);
#endif
        _quickMoveActive = ActiveInventorySlots.Count > 0 && (ActiveStorageSlots.Count > 0 || ActiveBackpackSlots.Count > 0);
    }

    private static List<ItemSlot> GetBackpackSlots()
    {
        var result = new List<ItemSlot>();
        try
        {
            var backpack = PlayerBackpack.Instance;
            if (backpack == null || !backpack.IsUnlocked || Player.Local == null)
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
            ModLogger.Error("StorageMenuPatch.GetBackpackSlots", ex);
        }

        return result;
    }

    /// <summary>
    /// Builds a display-only projection of backpack slots for the live search field. Empty slots
    /// remain visible in the normal view but are omitted while a filter is active.
    /// </summary>
    private static List<ItemSlot> GetFilteredBackpackSlots(List<ItemSlot> backpackSlots, string searchTerm)
    {
        if (backpackSlots == null || backpackSlots.Count == 0)
            return new List<ItemSlot>();

        if (string.IsNullOrWhiteSpace(searchTerm))
            return backpackSlots;

        var results = new List<ItemSlot>();
        for (var i = 0; i < backpackSlots.Count; i++)
        {
            var slot = backpackSlots[i];
            if (SlotMatchesSearch(slot, searchTerm))
                results.Add(slot);
        }

        return results;
    }

    private static bool SlotMatchesSearch(ItemSlot slot, string searchTerm)
    {
        if (slot?.ItemInstance == null || string.IsNullOrWhiteSpace(searchTerm))
            return false;

        try
        {
            var item = slot.ItemInstance;
            var definition = item.Definition;
            var quality = ReflectionUtils.TryGetFieldOrProperty(item, "Quality")
                ?? ReflectionUtils.TryGetFieldOrProperty(item, "quality");
            var nameAndId = new[]
            {
                definition?.Name ?? string.Empty,
                definition?.ID ?? string.Empty
            };
            var metadata = new[]
            {
                definition != null ? definition.Category.ToString() : string.Empty,
                definition?.GetType().Name ?? string.Empty,
                item.GetType().Name ?? string.Empty,
                quality?.ToString() ?? string.Empty
            };

            var terms = searchTerm.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < terms.Length; i++)
            {
                var term = terms[i];
                var matchesNameOrId = MatchesSearchPrefix(nameAndId, term);
                var matchesMetadata = term.Length >= SearchMetadataMinimumTermLength &&
                                      MatchesSearchPrefix(metadata, term);
                if (!matchesNameOrId && !matchesMetadata)
                    return false;
            }

            return terms.Length > 0;
        }
        catch (Exception ex)
        {
            ModLogger.Error("StorageMenuPatch.SlotMatchesSearch", ex);
            return false;
        }
    }

    /// <summary>
    /// Matches a term at the beginning of a name or metadata word. This keeps short input such as
    /// "c" useful for Cocaine without showing every item whose hidden category happens to contain c.
    /// </summary>
    private static bool MatchesSearchPrefix(IEnumerable<string> fields, string term)
    {
        if (fields == null || string.IsNullOrWhiteSpace(term))
            return false;

        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field))
                continue;

            var words = field.Split(new[] { ' ', '-', '_', '/', '\\', '(', ')', '[', ']' },
                StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < words.Length; i++)
            {
                if (words[i].StartsWith(term, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static bool IsBackpackOwner(IItemSlotOwner owner)
    {
        if (owner == null || Player.Local == null)
            return false;

        try
        {
            var backpackStorage = Player.Local.GetBackpackStorage();
            if (backpackStorage == null)
                return false;

#if !MONO
            var ownerStorage = owner.TryCast<StorageEntity>();
            return ownerStorage != null && ownerStorage.Pointer == backpackStorage.Pointer;
#else
            return ReferenceEquals(owner, backpackStorage);
#endif
        }
        catch
        {
            return false;
        }
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

    private static void SetPanelHeader(StorageMenu menu, RectTransform container)
    {
        if (menu == null || container == null)
            return;

        if (!BackpackPanels.TryGetValue(menu.GetInstanceID(), out var panel))
            return;

        var backpackSlots = GetBackpackSlots();
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

    private static S1TMP CloneLabel(S1TMP sourceLabel, RectTransform sourceRoot, RectTransform targetRoot)
    {
        if (sourceLabel == null || sourceRoot == null || targetRoot == null)
            return null;

        var clone = UnityEngine.Object.Instantiate(sourceLabel.gameObject, targetRoot);
        clone.name = $"PackRat_{sourceLabel.gameObject.name}";
        var cloneRect = clone.GetComponent<RectTransform>();
        CopyRectTransform(sourceRoot, sourceLabel.GetComponent<RectTransform>(), targetRoot, cloneRect);
        return clone.GetComponent<S1TMP>();
    }

    private static void CopyRectTransform(RectTransform sourceRoot, RectTransform source, RectTransform targetRoot, RectTransform target)
    {
        if (sourceRoot == null || source == null || targetRoot == null || target == null)
            return;

        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.sizeDelta = source.sizeDelta;
        target.anchoredPosition = source.anchoredPosition;
        target.localScale = source.localScale;
        target.localRotation = source.localRotation;
    }

}
