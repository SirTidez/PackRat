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
using ScheduleOne.Levelling;
using ScheduleOne.Money;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Product;
using ScheduleOne.Storage;
using ScheduleOne.UI;
using ScheduleOne.UI.Items;
using S1TMP = TMPro.TextMeshProUGUI;
using S1Action = System.Action;
#else
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Product;
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
    private enum StandaloneBackpackSortMode
    {
        SlotOrder,
        Name,
        Quantity,
        Quality,
        Type
    }

    private enum StandaloneBackpackDropdown
    {
        None,
        Type,
        Quality,
        Sort
    }

    private enum StandaloneBackpackSettingsPage
    {
        General,
        Tiers,
        Layout
    }

    private sealed class StandaloneBackpackDropdownOption
    {
        public string Label;
        public Action SelectAction;
        public bool ShowQualityStar;
        public Color QualityStarColor;
    }

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
        public RectTransform HeaderRoot;
        public RectTransform DropdownRoot;
        public RectTransform SettingsRoot;
        public RectTransform SettingsCard;
        public RectTransform SettingsContentRoot;
        public RectTransform SettingsGeneralPage;
        public RectTransform SettingsTiersPage;
        public RectTransform SettingsLayoutPage;
        public Text SettingsSessionStatusValue;
        public Text VisualTitleLabel;
        public Text VisualMetaLabel;
        public InputField SearchInput;
        public Text SearchText;
        public Text SearchPlaceholder;
        public Action<string> SearchAction;
        public Button TypeFilterButton;
        public Button QualityFilterButton;
        public Button SortButton;
        public Button ClearFiltersButton;
        public Text TypeFilterLabel;
        public Text QualityFilterLabel;
        public Text SortLabel;
        public Text ClearFiltersLabel;
        public Action TypeFilterAction;
        public Action QualityFilterAction;
        public Action SortAction;
        public Action ClearFiltersAction;
        public Button SettingsButton;
        public Text SettingsLabel;
        public Button SettingsGeneralButton;
        public Button SettingsTiersButton;
        public Button SettingsLayoutButton;
        public readonly List<GameObject> SettingsRows = new List<GameObject>();
        public bool SettingsOpen;
        public bool AwaitingToggleKey;
        public int SettingsTierIndex;
        public StandaloneBackpackSettingsPage SettingsPage;
        public bool SearchListenerBound;
        public StandaloneBackpackDropdown ActiveDropdown;
        public readonly List<Button> DropdownOptionButtons = new List<Button>();
        public readonly List<Text> DropdownOptionLabels = new List<Text>();
        public readonly List<Image> DropdownOptionQualityStars = new List<Image>();
        public readonly List<Action> DropdownOptionActions = new List<Action>();
        public readonly List<StandaloneBackpackDropdownOption> DropdownOptions = new List<StandaloneBackpackDropdownOption>();
        public Sprite QualityStarSprite;
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
        public string TypeFilter;
        public string QualityFilter;
        public StandaloneBackpackSortMode SortMode;
    }

    private const int StandaloneBackpackSlotsPerPage = 20;
    private const int StandaloneBackpackGridRows = 4;
    private const int SearchMetadataMinimumTermLength = 2;
    private const float StandaloneGridVerticalOffset = 118f;
    private const float StandaloneCardPadding = 14f;
    private const float StandaloneHeaderHeight = 116f;
    private const float StandaloneCloseGap = 24f;
    private const float StandaloneHeaderControlInset = 3f;
    private const float StandaloneHeaderSearchBottom = 6f;
    private const float StandaloneHeaderSearchTop = 32f;
    private const int PillSpriteSize = 32;
    private const int PillSpriteBorder = 8;
    private const float PillSpriteCornerRadius = 7f;
    private const int DesktopTabSpriteSize = 32;
    private const int DesktopTabCornerRadius = 10;
    private const string SettingsCogResourceName = "PackRat.assets.settings-cog-ui.png";
    private const int StorageBackpackSlotsPerPage = 4;
    private const int StorageBackpackGridRows = 4;
    private const float CompactPanelMargin = 24f;
    private static readonly Vector2 CompactPanelSize = new Vector2(184f, 472f);
    private static readonly Vector2 CompactSlotContainerSize = new Vector2(152f, 332f);
    private static readonly Vector2 CompactSlotSize = new Vector2(72f, 72f);

    private static readonly Dictionary<int, BackpackPanelState> BackpackPanels = new Dictionary<int, BackpackPanelState>();
    private static readonly Dictionary<int, StandaloneBackpackState> StandaloneBackpackPanels = new Dictionary<int, StandaloneBackpackState>();
    private static Sprite _settingsCogSprite;
    private static Texture2D _settingsCogTexture;
    private static bool _settingsCogLoadAttempted;
    private static Sprite _pillButtonSprite;
    private static Texture2D _pillButtonTexture;
    private static Sprite _desktopTabSprite;
    private static Texture2D _desktopTabTexture;
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

    private static void CaptureStandaloneQualityStarSprite(StorageMenu menu, StandaloneBackpackState state)
    {
        if (menu?.SlotsUIs == null || state == null || state.QualityStarSprite != null)
            return;

        for (var i = 0; i < menu.SlotsUIs.Length; i++)
        {
            var itemUi = menu.SlotsUIs[i]?.ItemUI;
            if (itemUi == null)
                continue;

#if MONO
            var qualityItemUi = itemUi as QualityItemUI;
#else
            var qualityItemUi = itemUi.TryCast<QualityItemUI>();
#endif
            if (qualityItemUi?.QualityIcon?.sprite == null)
                continue;

            state.QualityStarSprite = qualityItemUi.QualityIcon.sprite;
            return;
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
        var displaySlots = GetDisplayBackpackSlots(backpackSlots, state);
        var totalPages = Mathf.Max(1, Mathf.CeilToInt(displaySlots.Count / (float)StandaloneBackpackSlotsPerPage));
        state.CurrentPage = Mathf.Clamp(state.CurrentPage, 0, totalPages - 1);
        var firstSlotIndex = state.CurrentPage * StandaloneBackpackSlotsPerPage;
        var visibleSlotCount = Mathf.Clamp(displaySlots.Count - firstSlotIndex, 1, StandaloneBackpackSlotsPerPage);
        // The card represents the backpack's capacity, not the number of current search hits.
        // Keep its geometry fixed while a filter only changes the slots populated within it.
        var gridSlotCount = Mathf.Clamp(backpackSlots.Count, 1, StandaloneBackpackSlotsPerPage);
        var gridSize = ConfigureStandaloneBackpackGrid(menu, gridSlotCount);
        EnsureStandaloneBackpackVisuals(menu, state, backpackSlots.Count, displaySlots.Count, totalPages);

        // First remove every previous layout child, then populate the compact projection in a
        // second pass. Updating active and inactive GridLayoutGroup children together leaves
        // stale positions behind for type/quality/sort projections on some game UI prefabs.
        for (var i = 0; i < menu.SlotsUIs.Length; i++)
        {
            var slotUi = menu.SlotsUIs[i];
            if (slotUi == null)
                continue;

            ResetSlotUi(slotUi);
            slotUi.ClearSlot();
            slotUi.gameObject.SetActive(false);
        }

        Canvas.ForceUpdateCanvases();

        for (var i = 0; i < menu.SlotsUIs.Length; i++)
        {
            var slotUi = menu.SlotsUIs[i];
            if (slotUi == null)
                continue;

            var slotIndex = firstSlotIndex + i;
            if (i < StandaloneBackpackSlotsPerPage && slotIndex < displaySlots.Count)
            {
                slotUi.AssignSlot(displaySlots[slotIndex]);
                slotUi.gameObject.SetActive(true);
            }
        }

        CaptureStandaloneQualityStarSprite(menu, state);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(menu.SlotContainer);

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

        var rowCount = Mathf.Min(StandaloneBackpackGridRows, Mathf.Max(1, visibleSlotCount));
        var columnCount = Mathf.Max(1, Mathf.CeilToInt(visibleSlotCount / (float)StandaloneBackpackGridRows));
        // The source StorageMenu prefab uses a horizontal start axis. Pair it explicitly with a
        // fixed column count; a fixed-row constraint with that axis is ignored by uGUI and stacks
        // filtered children into one column.
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columnCount;
        grid.childAlignment = TextAnchor.UpperCenter;

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
            state.HeaderRoot = header;
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
            CreateStandaloneFilterControls(header, state, menu);
            CreateStandaloneSettingsButton(header, state, menu);
            CreateStandaloneDropdown(header, state);
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
            state.HeaderRoot = headerRect;
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(8f, -StandaloneHeaderHeight - 4f);
            headerRect.offsetMax = new Vector2(-8f, -8f);
        }

        CreateStandaloneDropdown(state.HeaderRoot, state);
        CreateStandaloneSettingsButton(state.HeaderRoot, state, menu);
        EnsureStandaloneSettingsPanel(menu, state);

        ConfigureStandaloneHeaderLabels(state);
        BindStandaloneSearchInput(state, menu);
        BindStandaloneFilterControls(state, menu);

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
            var filterActive = HasStandaloneFilters(state);
            var filterSummary = filterActive ? $" • {filteredSlotCount} MATCHES" : string.Empty;
            state.VisualMetaLabel.text =
                $"{slotCount} SLOTS{filterSummary}  •  PAGE {state.CurrentPage + 1}/{Mathf.Max(1, totalPages)}";
        }
    }

    private static void ConfigureStandaloneHeaderLabels(StandaloneBackpackState state)
    {
        if (state == null)
            return;

        ConfigureHeaderLabel(state.VisualTitleLabel, new Vector2(12f, -31f), new Vector2(-42f, -8f),
            TextAnchor.MiddleLeft);
        ConfigureHeaderLabel(state.VisualMetaLabel, new Vector2(12f, -50f), new Vector2(-42f, -31f),
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
        // Match the filter rail's responsive width. Both rows are anchored to the same header
        // edges and share the same inset, so they continue to align as the card scales.
        root.offsetMin = new Vector2(StandaloneHeaderControlInset, StandaloneHeaderSearchBottom);
        root.offsetMax = new Vector2(-StandaloneHeaderControlInset, StandaloneHeaderSearchTop);

        var background = rootGo.AddComponent<Image>();
        background.color = new Color32(10, 15, 20, 245);
        background.raycastTarget = true;
        ApplyPillButtonPresentation(background);

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
        state.SearchListenerBound = false;
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

        if (!state.SearchListenerBound)
        {
            EventHelper.AddListener<string>(state.SearchAction, state.SearchInput.onValueChanged);
            state.SearchListenerBound = true;
        }
        state.SearchInput.SetTextWithoutNotify(state.SearchTerm ?? string.Empty);
    }

    private static void CreateStandaloneFilterControls(RectTransform header, StandaloneBackpackState state, StorageMenu menu)
    {
        if (header == null || state == null || state.TypeFilterButton != null)
            return;

        state.TypeFilterButton = CreateStandaloneHeaderButton(header, "TypeFilter", 0f, 0.25f, out state.TypeFilterLabel);
        state.QualityFilterButton = CreateStandaloneHeaderButton(header, "QualityFilter", 0.25f, 0.5f, out state.QualityFilterLabel);
        state.SortButton = CreateStandaloneHeaderButton(header, "Sort", 0.5f, 0.75f, out state.SortLabel);
        state.ClearFiltersButton = CreateStandaloneHeaderButton(header, "Clear", 0.75f, 1f, out state.ClearFiltersLabel);
    }

    private static Button CreateStandaloneHeaderButton(RectTransform parent, string name, float minX, float maxX, out Text label)
    {
        var buttonGo = new GameObject("PackRat_Backpack" + name + "Button");
        var rect = buttonGo.AddComponent<RectTransform>();
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = new Vector2(minX, 0f);
        rect.anchorMax = new Vector2(maxX, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.offsetMin = new Vector2(StandaloneHeaderControlInset, 37f);
        rect.offsetMax = new Vector2(-StandaloneHeaderControlInset, 58f);

        var image = buttonGo.AddComponent<Image>();
        image.color = new Color32(18, 30, 40, 245);
        ApplyPillButtonPresentation(image);
        var button = buttonGo.AddComponent<Button>();
        button.targetGraphic = image;

        label = CreateSearchText(rect, "Label", new Color32(190, 221, 241, 255));
        label.fontSize = 9;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        return button;
    }

    private static void BindStandaloneFilterControls(StandaloneBackpackState state, StorageMenu menu)
    {
        if (state == null || menu == null)
            return;

        if (state.TypeFilterAction == null)
            state.TypeFilterAction = () =>
            {
                ShowStandaloneDropdown(menu, state, StandaloneBackpackDropdown.Type);
            };
        if (state.QualityFilterAction == null)
            state.QualityFilterAction = () =>
            {
                ShowStandaloneDropdown(menu, state, StandaloneBackpackDropdown.Quality);
            };
        if (state.SortAction == null)
            state.SortAction = () =>
            {
                ShowStandaloneDropdown(menu, state, StandaloneBackpackDropdown.Sort);
            };
        if (state.ClearFiltersAction == null)
            state.ClearFiltersAction = () =>
            {
                state.SearchTerm = string.Empty;
                state.TypeFilter = string.Empty;
                state.QualityFilter = string.Empty;
                state.SortMode = StandaloneBackpackSortMode.SlotOrder;
                state.SearchInput?.SetTextWithoutNotify(string.Empty);
                HideStandaloneDropdown(state);
                RefreshStandaloneFilterView(menu, state);
            };

        // The game may rebuild or reactivate this owner panel while the backpack remains open.
        // Rebind the permanent actions each refresh so controls cannot retain a stale listener;
        // RemoveListener keeps this idempotent rather than stacking callbacks on each layout pass.
        RebindHeaderButton(state.TypeFilterButton, state.TypeFilterAction);
        RebindHeaderButton(state.QualityFilterButton, state.QualityFilterAction);
        RebindHeaderButton(state.SortButton, state.SortAction);
        RebindHeaderButton(state.ClearFiltersButton, state.ClearFiltersAction);
        UpdateStandaloneFilterLabels(state);
    }

    private static void RebindHeaderButton(Button button, Action action)
    {
        if (button == null || action == null)
            return;

        EventHelper.RemoveListener(action, button.onClick);
        EventHelper.AddListener(action, button.onClick);
    }

    private static void RefreshStandaloneFilterView(StorageMenu menu, StandaloneBackpackState state)
    {
        if (state == null)
            return;

        state.CurrentPage = 0;
        if (state.IsOpen)
            ApplyStandaloneBackpackMenu(menu);
    }

    private static void UpdateStandaloneFilterLabels(StandaloneBackpackState state)
    {
        if (state == null)
            return;

        var backpackSlots = GetBackpackSlots();
        var typeOptions = GetAvailableStandaloneFilterOptions(backpackSlots, state, GetSlotType,
            ignoreTypeFilter: true, ignoreQualityFilter: false);
        var qualityOptions = GetAvailableStandaloneFilterOptions(backpackSlots, state, GetSlotQuality,
            ignoreTypeFilter: false, ignoreQualityFilter: true);

        if (state.TypeFilterLabel != null)
            state.TypeFilterLabel.text = typeOptions.Count == 0 ? "TYPE: --" :
                (string.IsNullOrEmpty(state.TypeFilter) ? "TYPE: ALL" : "TYPE: " + state.TypeFilter.ToUpperInvariant());
        if (state.QualityFilterLabel != null)
            state.QualityFilterLabel.text = qualityOptions.Count == 0 ? "QUALITY: --" :
                (string.IsNullOrEmpty(state.QualityFilter) ? "QUALITY: ALL" : "QUALITY: " + state.QualityFilter.ToUpperInvariant());
        if (state.SortLabel != null)
            state.SortLabel.text = "SORT: " + GetSortModeLabel(state.SortMode);
        if (state.ClearFiltersLabel != null)
            state.ClearFiltersLabel.text = "CLEAR";

        if (state.TypeFilterButton != null)
            state.TypeFilterButton.interactable = typeOptions.Count > 0;
        if (state.QualityFilterButton != null)
            state.QualityFilterButton.interactable = qualityOptions.Count > 0;
    }

    private static void CreateStandaloneSettingsButton(RectTransform header, StandaloneBackpackState state, StorageMenu menu)
    {
        if (header == null || state == null || state.SettingsButton != null)
            return;

        state.SettingsButton = CreateStandaloneActionButton(header, "SettingsCog",
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-31f, -31f), new Vector2(-8f, -8f),
            string.Empty, 15, out state.SettingsLabel);
        CreateStandaloneCogIcon(state.SettingsButton.GetComponent<RectTransform>());
        EventHelper.AddListener(() => ToggleStandaloneSettings(menu, state), state.SettingsButton.onClick);
    }

    private static Button CreateStandaloneActionButton(RectTransform parent, string name, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, string caption, int fontSize, out Text label)
    {
        var buttonGo = new GameObject("PackRat_Backpack" + name + "Button");
        var rect = buttonGo.AddComponent<RectTransform>();
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        var image = buttonGo.AddComponent<Image>();
        image.color = new Color32(18, 30, 40, 245);
        ApplyPillButtonPresentation(image);
        var button = buttonGo.AddComponent<Button>();
        button.targetGraphic = image;
        label = CreateSearchText(rect, "Label", new Color32(211, 232, 246, 255));
        label.text = caption;
        label.fontSize = fontSize;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        return button;
    }

    /// <summary>
    /// Adds the shipped PNG cog instead of relying on a font glyph or improvised UI geometry.
    /// </summary>
    private static void CreateStandaloneCogIcon(RectTransform parent)
    {
        if (parent == null)
            return;

        var iconGo = new GameObject("CogIcon");
        var icon = iconGo.AddComponent<RectTransform>();
        icon.SetParent(parent, worldPositionStays: false);
        icon.anchorMin = new Vector2(0.5f, 0.5f);
        icon.anchorMax = new Vector2(0.5f, 0.5f);
        icon.pivot = new Vector2(0.5f, 0.5f);
        icon.sizeDelta = new Vector2(16f, 16f);

        var image = iconGo.AddComponent<Image>();
        image.sprite = GetStandaloneSettingsCogSprite();
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private static Sprite GetStandaloneSettingsCogSprite()
    {
        if (_settingsCogSprite != null)
            return _settingsCogSprite;
        if (_settingsCogLoadAttempted)
            return null;

        _settingsCogLoadAttempted = true;
        try
        {
            using var stream = typeof(StorageMenuPatch).Assembly.GetManifestResourceStream(SettingsCogResourceName);
            if (stream == null)
            {
                ModLogger.Warn("[BackpackUI] Settings cog PNG resource was not found.");
                return null;
            }

            var bytes = new byte[stream.Length];
            stream.Read(bytes, 0, bytes.Length);
            _settingsCogTexture = new Texture2D(2, 2);
            _settingsCogTexture.filterMode = FilterMode.Bilinear;
            if (!_settingsCogTexture.LoadImage(bytes))
            {
                ModLogger.Warn("[BackpackUI] Settings cog PNG could not be decoded.");
                return null;
            }

            _settingsCogSprite = Sprite.Create(_settingsCogTexture,
                new Rect(0f, 0f, _settingsCogTexture.width, _settingsCogTexture.height), new Vector2(0.5f, 0.5f));
            return _settingsCogSprite;
        }
        catch (Exception ex)
        {
            ModLogger.Error("StorageMenuPatch.GetStandaloneSettingsCogSprite", ex);
            return null;
        }
    }

    /// <summary>
    /// Applies Unity's built-in nine-sliced UI sprite so settings controls retain rounded corners
    /// while their layout group changes width or height.
    /// </summary>
    private static void ApplyRoundedButtonPresentation(Image image)
    {
        if (image == null)
            return;

        try
        {
            var roundedSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            if (roundedSprite == null)
                return;

            image.sprite = roundedSprite;
            image.type = Image.Type.Sliced;
        }
        catch (Exception ex)
        {
            ModLogger.Warn("[BackpackUI] Rounded settings button sprite was unavailable: " + ex.Message);
        }
    }

    /// <summary>
    /// Applies a shared nine-sliced, gently rounded surface to PackRat-owned interactive controls.
    /// The low-radius corners keep compact filter, settings, dropdown, and pager controls smooth
    /// instead of stretching circular caps into diamond-like shapes.
    /// </summary>
    private static void ApplyPillButtonPresentation(Image image)
    {
        if (image == null)
            return;

        var pillSprite = GetPillButtonSprite();
        if (pillSprite == null)
            return;

        image.sprite = pillSprite;
        image.type = Image.Type.Sliced;
    }

    private static Sprite GetPillButtonSprite()
    {
        if (_pillButtonSprite != null)
            return _pillButtonSprite;

        try
        {
            _pillButtonTexture = new Texture2D(PillSpriteSize, PillSpriteSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[PillSpriteSize * PillSpriteSize];
            for (var y = 0; y < PillSpriteSize; y++)
            {
                for (var x = 0; x < PillSpriteSize; x++)
                {
                    var point = new Vector2(x + 0.5f, y + 0.5f);
                    var innerMin = new Vector2(PillSpriteCornerRadius, PillSpriteCornerRadius);
                    var innerMax = new Vector2(PillSpriteSize - PillSpriteCornerRadius,
                        PillSpriteSize - PillSpriteCornerRadius);
                    var nearest = new Vector2(Mathf.Clamp(point.x, innerMin.x, innerMax.x),
                        Mathf.Clamp(point.y, innerMin.y, innerMax.y));
                    var distance = Vector2.Distance(point, nearest);
                    var alpha = Mathf.Clamp01(PillSpriteCornerRadius - distance + 0.5f);
                    pixels[(y * PillSpriteSize) + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            _pillButtonTexture.SetPixels32(pixels);
            _pillButtonTexture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            _pillButtonSprite = Sprite.Create(_pillButtonTexture,
                new Rect(0f, 0f, PillSpriteSize, PillSpriteSize), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect,
                new Vector4(PillSpriteBorder, PillSpriteBorder, PillSpriteBorder, PillSpriteBorder));
            return _pillButtonSprite;
        }
        catch (Exception ex)
        {
            ModLogger.Warn("[BackpackUI] Pill button sprite was unavailable: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Gives the settings modal its own desktop-tab silhouette: rounded top corners, a flat
    /// bottom edge, and a sliced centre that can overlap the content panel without becoming a
    /// capsule. The runtime sprite keeps this presentation resolution-independent.
    /// </summary>
    private static void ApplyDesktopTabPresentation(Image image)
    {
        if (image == null)
            return;

        var tabSprite = GetDesktopTabSprite();
        if (tabSprite == null)
            return;

        image.sprite = tabSprite;
        image.type = Image.Type.Sliced;
    }

    private static Sprite GetDesktopTabSprite()
    {
        if (_desktopTabSprite != null)
            return _desktopTabSprite;

        try
        {
            _desktopTabTexture = new Texture2D(DesktopTabSpriteSize, DesktopTabSpriteSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[DesktopTabSpriteSize * DesktopTabSpriteSize];
            var cornerCenter = DesktopTabSpriteSize - DesktopTabCornerRadius - 0.5f;
            for (var y = 0; y < DesktopTabSpriteSize; y++)
            {
                for (var x = 0; x < DesktopTabSpriteSize; x++)
                {
                    var alpha = 1f;
                    if (y >= DesktopTabSpriteSize - DesktopTabCornerRadius)
                    {
                        var centerX = x < DesktopTabCornerRadius
                            ? DesktopTabCornerRadius - 0.5f
                            : (x >= DesktopTabSpriteSize - DesktopTabCornerRadius
                                ? DesktopTabSpriteSize - DesktopTabCornerRadius - 0.5f
                                : x);
                        var distance = Vector2.Distance(new Vector2(x, y), new Vector2(centerX, cornerCenter));
                        alpha = Mathf.Clamp01(DesktopTabCornerRadius - distance + 0.5f);
                    }

                    pixels[(y * DesktopTabSpriteSize) + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            _desktopTabTexture.SetPixels32(pixels);
            _desktopTabTexture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            _desktopTabSprite = Sprite.Create(_desktopTabTexture,
                new Rect(0f, 0f, DesktopTabSpriteSize, DesktopTabSpriteSize), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect,
                new Vector4(DesktopTabCornerRadius, 0f, DesktopTabCornerRadius, DesktopTabCornerRadius));
            return _desktopTabSprite;
        }
        catch (Exception ex)
        {
            ModLogger.Warn("[BackpackUI] Desktop settings tab sprite was unavailable: " + ex.Message);
            return null;
        }
    }

    private static void ConfigureStandaloneDesktopTab(Button button)
    {
        if (button == null)
            return;

        ApplyDesktopTabPresentation(button.targetGraphic as Image);
        var rect = button.GetComponent<RectTransform>();
        if (rect != null)
        {
            var index = Mathf.Clamp(button.transform.GetSiblingIndex(), 0, 2);
            var minX = index / 3f;
            var maxX = (index + 1) / 3f;
            rect.anchorMin = new Vector2(minX, 0f);
            rect.anchorMax = new Vector2(maxX, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            // Preserve a compact desktop-tab group, but leave a visible six-pixel gutter between
            // faces so their labels and rounded upper corners do not read as one control.
            rect.offsetMin = new Vector2(index == 0 ? 0f : 3f, 0f);
            rect.offsetMax = new Vector2(index == 2 ? 0f : -3f, 31f);
        }

        var label = button.GetComponentInChildren<Text>();
        if (label != null)
        {
            label.fontSize = 10;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
        }
    }

    private static void EnsureStandaloneSettingsPanel(StorageMenu menu, StandaloneBackpackState state)
    {
        if (menu == null || state?.VisualRoot == null)
            return;

        if (state.SettingsRoot == null)
        {
            var settingsGo = new GameObject("PackRat_BackpackSettings");
            var root = settingsGo.AddComponent<RectTransform>();
            root.SetParent(state.VisualRoot, worldPositionStays: false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            var background = settingsGo.AddComponent<Image>();
            background.color = new Color32(4, 9, 13, 112);

            var canvas = Utils.AddComponentSafe<Canvas>(settingsGo);
            if (canvas != null)
            {
                canvas.overrideSorting = true;
                canvas.sortingOrder = 125;
            }
            Utils.AddComponentSafe<GraphicRaycaster>(settingsGo);
            state.SettingsRoot = root;

            var cardGo = new GameObject("SettingsCard");
            var card = cardGo.AddComponent<RectTransform>();
            card.SetParent(root, worldPositionStays: false);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(350f, 360f);
            var cardImage = cardGo.AddComponent<Image>();
            cardImage.color = new Color32(10, 23, 31, 252);
            ApplyRoundedButtonPresentation(cardImage);
            state.SettingsCard = card;

            var header = CreateStandaloneSettingsRegion(card, "Header", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(10f, -44f), new Vector2(-10f, -10f));
            var headerLayout = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            headerLayout.padding = new RectOffset(10, 10, 4, 4);
            headerLayout.spacing = 8f;
            headerLayout.childAlignment = TextAnchor.MiddleLeft;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = false;
            headerLayout.childForceExpandHeight = false;

            var title = CreateSearchText(header, "Title", new Color32(242, 247, 251, 255));
            title.text = "BACKPACK SETTINGS";
            title.fontSize = 14;
            title.fontStyle = FontStyle.Bold;
            title.alignment = TextAnchor.MiddleLeft;
            AddStandaloneLayoutElement(title.gameObject, minWidth: 120f, flexibleWidth: 1f);

            var closeButton = CreateStandaloneActionButton(header, "SettingsClose",
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero,
                "CLOSE", 8, out _);
            AddStandaloneLayoutElement(closeButton.gameObject, preferredWidth: 58f, preferredHeight: 25f);
            EventHelper.AddListener(() => ToggleStandaloneSettings(menu, state), closeButton.onClick);

            var sessionStatus = CreateStandaloneSettingsRegion(card, "SessionStatus", new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(10f, -70f), new Vector2(-10f, -48f));
            var sessionStatusImage = sessionStatus.gameObject.AddComponent<Image>();
            sessionStatusImage.color = new Color32(18, 36, 49, 238);
            ApplyRoundedButtonPresentation(sessionStatusImage);

            var sessionStatusLabel = CreateSearchText(sessionStatus, "Label", new Color32(176, 210, 231, 255));
            sessionStatusLabel.text = "SESSION STATUS";
            sessionStatusLabel.fontSize = 8;
            sessionStatusLabel.fontStyle = FontStyle.Bold;
            sessionStatusLabel.alignment = TextAnchor.MiddleLeft;
            var sessionStatusLabelRect = sessionStatusLabel.GetComponent<RectTransform>();
            sessionStatusLabelRect.anchorMax = new Vector2(0.5f, 1f);
            sessionStatusLabelRect.offsetMin = new Vector2(10f, 1f);
            sessionStatusLabelRect.offsetMax = new Vector2(-2f, -1f);

            state.SettingsSessionStatusValue = CreateSearchText(sessionStatus, "Value", new Color32(119, 221, 144, 255));
            state.SettingsSessionStatusValue.fontSize = 9;
            state.SettingsSessionStatusValue.fontStyle = FontStyle.Bold;
            state.SettingsSessionStatusValue.alignment = TextAnchor.MiddleRight;
            var sessionStatusValueRect = state.SettingsSessionStatusValue.GetComponent<RectTransform>();
            sessionStatusValueRect.anchorMin = new Vector2(0.5f, 0f);
            sessionStatusValueRect.offsetMin = new Vector2(2f, 1f);
            sessionStatusValueRect.offsetMax = new Vector2(-10f, -1f);

            var tabs = CreateStandaloneSettingsRegion(card, "Tabs", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(10f, -115f), new Vector2(-10f, -75f));
            // These three fixed settings pages use direct anchors instead of a layout group.
            // The game can rebuild uGUI layouts while the modal opens; direct geometry keeps the
            // overlapping desktop-tab baseline stable instead of allowing preferred heights to
            // collapse to zero during that rebuild.

            state.SettingsGeneralButton = CreateStandaloneActionButton(tabs, "SettingsGeneral",
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero,
                "GENERAL", 9, out _);
            state.SettingsTiersButton = CreateStandaloneActionButton(tabs, "SettingsTiers",
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero,
                "TIERS", 9, out _);
            state.SettingsLayoutButton = CreateStandaloneActionButton(tabs, "SettingsLayout",
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero,
                "LAYOUT", 9, out _);
            ConfigureStandaloneDesktopTab(state.SettingsGeneralButton);
            ConfigureStandaloneDesktopTab(state.SettingsTiersButton);
            ConfigureStandaloneDesktopTab(state.SettingsLayoutButton);
            EventHelper.AddListener(() => SetStandaloneSettingsPage(menu, state, StandaloneBackpackSettingsPage.General),
                state.SettingsGeneralButton.onClick);
            EventHelper.AddListener(() => SetStandaloneSettingsPage(menu, state, StandaloneBackpackSettingsPage.Tiers),
                state.SettingsTiersButton.onClick);
            EventHelper.AddListener(() => SetStandaloneSettingsPage(menu, state, StandaloneBackpackSettingsPage.Layout),
                state.SettingsLayoutButton.onClick);

            var content = CreateStandaloneSettingsRegion(card, "Content", Vector2.zero, Vector2.one,
                new Vector2(10f, 10f), new Vector2(-10f, -110f));
            var contentImage = content.gameObject.AddComponent<Image>();
            contentImage.color = new Color32(16, 32, 43, 238);
            state.SettingsContentRoot = content;
            state.SettingsGeneralPage = CreateStandaloneSettingsPage(content, "GeneralPage");
            state.SettingsTiersPage = CreateStandaloneSettingsPage(content, "TiersPage");
            state.SettingsLayoutPage = CreateStandaloneSettingsPage(content, "LayoutPage");
            // The tabs are deliberately drawn above the content surface where their lower edge
            // overlaps it, matching a desktop tabbed window rather than a separated button row.
            tabs.SetAsLastSibling();
            state.SettingsRoot.gameObject.SetActive(false);
        }

        if (state.SettingsOpen)
        {
            state.SettingsRoot.gameObject.SetActive(true);
            state.SettingsRoot.SetAsLastSibling();
            RefreshStandaloneSettingsPane(menu, state);
        }
    }

    private static void ToggleStandaloneSettings(StorageMenu menu, StandaloneBackpackState state)
    {
        if (menu == null || state == null)
            return;

        state.SettingsOpen = !state.SettingsOpen;
        state.AwaitingToggleKey = false;
        if (state.SettingsOpen)
        {
            HideStandaloneDropdown(state);
            state.SearchInput?.DeactivateInputField();
        }

        EnsureStandaloneSettingsPanel(menu, state);
        if (!state.SettingsOpen && state.SettingsRoot != null)
            state.SettingsRoot.gameObject.SetActive(false);
    }

    private static void SetStandaloneSettingsPage(StorageMenu menu, StandaloneBackpackState state,
        StandaloneBackpackSettingsPage page)
    {
        if (state == null)
            return;

        if (state.SettingsPage == page)
        {
            RefreshStandaloneSettingsPane(menu, state);
            return;
        }

        state.AwaitingToggleKey = false;
        state.SettingsPage = page;
        RefreshStandaloneSettingsPane(menu, state);
    }

    private static void RefreshStandaloneSettingsPane(StorageMenu menu, StandaloneBackpackState state)
    {
        if (menu == null || state?.SettingsRoot == null || !state.SettingsOpen)
            return;

        ClearStandaloneSettingsRows(state);
        UpdateStandaloneSessionStatus(state);
        UpdateStandaloneSettingsTabs(state);
        UpdateStandaloneSettingsPageVisibility(state);
        switch (state.SettingsPage)
        {
            case StandaloneBackpackSettingsPage.Tiers:
                BuildStandaloneTierSettings(menu, state);
                break;
            case StandaloneBackpackSettingsPage.Layout:
                BuildStandaloneLayoutSettings(menu, state);
                break;
            default:
                BuildStandaloneGeneralSettings(menu, state);
                break;
        }
    }

    private static void ClearStandaloneSettingsRows(StandaloneBackpackState state)
    {
        for (var i = 0; i < state.SettingsRows.Count; i++)
        {
            if (state.SettingsRows[i] != null)
            {
                state.SettingsRows[i].SetActive(false);
                UnityEngine.Object.Destroy(state.SettingsRows[i]);
            }
        }
        state.SettingsRows.Clear();
    }

    private static void UpdateStandaloneSettingsTabs(StandaloneBackpackState state)
    {
        UpdateStandaloneSettingsTab(state.SettingsGeneralButton, state.SettingsPage == StandaloneBackpackSettingsPage.General);
        UpdateStandaloneSettingsTab(state.SettingsTiersButton, state.SettingsPage == StandaloneBackpackSettingsPage.Tiers);
        UpdateStandaloneSettingsTab(state.SettingsLayoutButton, state.SettingsPage == StandaloneBackpackSettingsPage.Layout);
    }

    private static void UpdateStandaloneSessionStatus(StandaloneBackpackState state)
    {
        if (state?.SettingsSessionStatusValue == null)
            return;

        var sessionStatus = ConfigSyncManager.GetSessionStatusLabel();
        state.SettingsSessionStatusValue.text = sessionStatus;
        state.SettingsSessionStatusValue.color = sessionStatus switch
        {
            "HOST" => new Color32(96, 196, 244, 255),
            "CLIENT" => new Color32(247, 191, 93, 255),
            _ => new Color32(119, 221, 144, 255)
        };
    }

    private static void UpdateStandaloneSettingsTab(Button button, bool selected)
    {
        if (button == null)
            return;

        var selectedColor = new Color32(48, 128, 170, 255);
        var selectedHoverColor = new Color32(64, 153, 196, 255);
        var normalColor = new Color32(20, 35, 47, 255);
        var normalHoverColor = new Color32(35, 65, 84, 255);
        var colors = button.colors;
        colors.normalColor = selected ? selectedColor : normalColor;
        colors.highlightedColor = selected ? selectedHoverColor : normalHoverColor;
        colors.pressedColor = selected ? new Color32(36, 103, 137, 255) : new Color32(16, 31, 42, 255);
        colors.selectedColor = selectedColor;
        colors.disabledColor = new Color32(58, 70, 78, 150);
        colors.colorMultiplier = 1f;
        button.colors = colors;

        var image = button?.targetGraphic as Image;
        if (image != null)
            image.color = selected ? selectedColor : normalColor;

        var rect = button.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.offsetMin = new Vector2(rect.offsetMin.x, 0f);
            rect.offsetMax = new Vector2(rect.offsetMax.x, selected ? 38f : 31f);
        }

        // The desktop tabs intentionally overlap. The active tab must therefore be the final
        // sibling drawn inside the tab strip, regardless of its logical General/Tiers/Layout
        // position. Its anchors were assigned once on creation, so this changes draw order only.
        if (selected)
            button.transform.SetAsLastSibling();
    }

    /// <summary>
    /// Activates exactly one sibling settings page. The selected tab and visible content page
    /// always move together, instead of treating tabs as unrelated buttons.
    /// </summary>
    private static void UpdateStandaloneSettingsPageVisibility(StandaloneBackpackState state)
    {
        if (state == null)
            return;

        SetStandaloneSettingsPageActive(state.SettingsGeneralPage, state.SettingsPage == StandaloneBackpackSettingsPage.General);
        SetStandaloneSettingsPageActive(state.SettingsTiersPage, state.SettingsPage == StandaloneBackpackSettingsPage.Tiers);
        SetStandaloneSettingsPageActive(state.SettingsLayoutPage, state.SettingsPage == StandaloneBackpackSettingsPage.Layout);
    }

    private static void SetStandaloneSettingsPageActive(RectTransform page, bool active)
    {
        if (page != null && page.gameObject.activeSelf != active)
            page.gameObject.SetActive(active);
    }

    private static void BuildStandaloneGeneralSettings(StorageMenu menu, StandaloneBackpackState state)
    {
        var config = Configuration.Instance;
        AddStandaloneSettingsRow(state, "TOGGLE KEY", state.AwaitingToggleKey ? "PRESS A KEY..." : config.ToggleKey.ToString(),
            "SET", () =>
            {
                state.AwaitingToggleKey = true;
                RefreshStandaloneSettingsPane(menu, state);
            });

        var canEditSession = ConfigSyncManager.CanEditSessionSettings();
        if (canEditSession)
        {
            AddStandaloneSettingsToggleRow(state, "POLICE SEARCH", config.EnableSearch, value =>
            {
                config.EnableSearch = value;
                PersistStandaloneSettings(menu, state, syncSessionSettings: true);
            });
        }
        else
        {
            AddStandaloneSettingsRow(state, "POLICE SEARCH", config.EnableSearch ? "ENABLED" : "DISABLED", "HOST ONLY");
        }

        AddStandaloneSettingsToggleRow(state, "SYNC DIAGNOSTICS", config.BackpackSyncDebugLogging, value =>
        {
            config.BackpackSyncDebugLogging = value;
            PersistStandaloneSettings(menu, state);
        });
    }

    private static void BuildStandaloneTierSettings(StorageMenu menu, StandaloneBackpackState state)
    {
        var config = Configuration.Instance;
        var maxTier = Configuration.BackpackTiers.Length - 1;
        state.SettingsTierIndex = Mathf.Clamp(state.SettingsTierIndex, 0, maxTier);
        var tierIndex = state.SettingsTierIndex;
        var tier = Configuration.BackpackTiers[tierIndex];
        var canEditSession = ConfigSyncManager.CanEditSessionSettings();

        AddStandaloneSettingsRow(state, "TIER", tier.Name.ToUpperInvariant(), "<", () =>
        {
            state.SettingsTierIndex = state.SettingsTierIndex <= 0 ? maxTier : state.SettingsTierIndex - 1;
            RefreshStandaloneSettingsPane(menu, state);
        }, ">", () =>
        {
            state.SettingsTierIndex = state.SettingsTierIndex >= maxTier ? 0 : state.SettingsTierIndex + 1;
            RefreshStandaloneSettingsPane(menu, state);
        });

        if (!canEditSession)
        {
            AddStandaloneSettingsRow(state, "HOST SETTINGS", "READ ONLY");
            AddStandaloneSettingsRow(state, "ENABLED", config.TierEnabled[tierIndex] ? "YES" : "NO");
            AddStandaloneSettingsRow(state, "SLOTS", config.TierSlotCounts[tierIndex].ToString());
            AddStandaloneSettingsRow(state, "PRICE", "$" + config.TierPrices[tierIndex].ToString("0"));
            AddStandaloneSettingsRow(state, "UNLOCK", FormatStandaloneUnlockRank(config.TierUnlockRanks[tierIndex]));
            return;
        }

        AddStandaloneSettingsToggleRow(state, "ENABLED", config.TierEnabled[tierIndex], value =>
        {
            config.TierEnabled[tierIndex] = value;
            PersistStandaloneSettings(menu, state, applyCurrentTier: true, syncSessionSettings: true);
        });
        AddStandaloneSettingsRow(state, "SLOTS", config.TierSlotCounts[tierIndex].ToString(), "-4", () =>
        {
            AdjustStandaloneTierSlots(menu, state, tierIndex, -4);
        }, "+4", () =>
        {
            AdjustStandaloneTierSlots(menu, state, tierIndex, 4);
        });
        AddStandaloneSettingsRow(state, "PRICE", "$" + config.TierPrices[tierIndex].ToString("0"), "-25", () =>
        {
            config.TierPrices[tierIndex] = Math.Max(0f, config.TierPrices[tierIndex] - 25f);
            PersistStandaloneSettings(menu, state, syncSessionSettings: true);
        }, "+25", () =>
        {
            config.TierPrices[tierIndex] += 25f;
            PersistStandaloneSettings(menu, state, syncSessionSettings: true);
        });
        AddStandaloneSettingsRow(state, "UNLOCK", FormatStandaloneUnlockRank(config.TierUnlockRanks[tierIndex]), "-", () =>
        {
            config.TierUnlockRanks[tierIndex] = OffsetStandaloneUnlockRank(config.TierUnlockRanks[tierIndex], -1);
            PersistStandaloneSettings(menu, state, syncSessionSettings: true);
        }, "+", () =>
        {
            config.TierUnlockRanks[tierIndex] = OffsetStandaloneUnlockRank(config.TierUnlockRanks[tierIndex], 1);
            PersistStandaloneSettings(menu, state, syncSessionSettings: true);
        });
    }

    private static void BuildStandaloneLayoutSettings(StorageMenu menu, StandaloneBackpackState state)
    {
        var config = Configuration.Instance;
        AddStandaloneSettingsRow(state, "STORAGE X", FormatStandaloneOffset(config.StorageOverlayOffsetX), "-10", () =>
        {
            config.StorageOverlayOffsetX -= 10f;
            PersistStandaloneSettings(menu, state);
        }, "+10", () =>
        {
            config.StorageOverlayOffsetX += 10f;
            PersistStandaloneSettings(menu, state);
        });
        AddStandaloneSettingsRow(state, "STORAGE Y", FormatStandaloneOffset(config.StorageOverlayOffsetY), "-10", () =>
        {
            config.StorageOverlayOffsetY -= 10f;
            PersistStandaloneSettings(menu, state);
        }, "+10", () =>
        {
            config.StorageOverlayOffsetY += 10f;
            PersistStandaloneSettings(menu, state);
        });
        AddStandaloneSettingsRow(state, "STATION X", FormatStandaloneOffset(config.StationOverlayOffsetX), "-10", () =>
        {
            config.StationOverlayOffsetX -= 10f;
            PersistStandaloneSettings(menu, state);
        }, "+10", () =>
        {
            config.StationOverlayOffsetX += 10f;
            PersistStandaloneSettings(menu, state);
        });
        AddStandaloneSettingsRow(state, "STATION Y", FormatStandaloneOffset(config.StationOverlayOffsetY), "-10", () =>
        {
            config.StationOverlayOffsetY -= 10f;
            PersistStandaloneSettings(menu, state);
        }, "+10", () =>
        {
            config.StationOverlayOffsetY += 10f;
            PersistStandaloneSettings(menu, state);
        });
        AddStandaloneSettingsRow(state, "DEAL X", FormatStandaloneOffset(config.HandoverOverlayOffsetX), "-10", () =>
        {
            config.HandoverOverlayOffsetX -= 10f;
            PersistStandaloneSettings(menu, state);
        }, "+10", () =>
        {
            config.HandoverOverlayOffsetX += 10f;
            PersistStandaloneSettings(menu, state);
        });
        AddStandaloneSettingsRow(state, "DEAL Y", FormatStandaloneOffset(config.HandoverOverlayOffsetY), "-10", () =>
        {
            config.HandoverOverlayOffsetY -= 10f;
            PersistStandaloneSettings(menu, state);
        }, "+10", () =>
        {
            config.HandoverOverlayOffsetY += 10f;
            PersistStandaloneSettings(menu, state);
        });
        AddStandaloneSettingsRow(state, "RESET LAYOUT", "", "RESET", () =>
        {
            config.StorageOverlayOffsetX = 0f;
            config.StorageOverlayOffsetY = 0f;
            config.StationOverlayOffsetX = 0f;
            config.StationOverlayOffsetY = 0f;
            config.HandoverOverlayOffsetX = 0f;
            config.HandoverOverlayOffsetY = 0f;
            PersistStandaloneSettings(menu, state);
        });
    }

    private static void AddStandaloneSettingsRow(StandaloneBackpackState state, string labelText, string valueText,
        string primaryCaption = null, Action primaryAction = null, string secondaryCaption = null,
        Action secondaryAction = null)
    {
        var pageRoot = GetStandaloneSettingsPageRoot(state);
        if (pageRoot == null)
            return;

        var index = state.SettingsRows.Count;
        var rowGo = new GameObject("SettingRow" + index);
        var row = rowGo.AddComponent<RectTransform>();
        row.SetParent(pageRoot, worldPositionStays: false);
        var background = rowGo.AddComponent<Image>();
        background.color = new Color32(20, 33, 44, 248);
        ApplyRoundedButtonPresentation(background);
        AddStandaloneLayoutElement(rowGo, minHeight: 28f, preferredHeight: 28f);
        var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset(8, 6, 2, 2);
        rowLayout.spacing = 5f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        var label = CreateSearchText(row, "Label", new Color32(188, 216, 235, 255));
        label.text = labelText ?? string.Empty;
        label.fontSize = 9;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleLeft;
        AddStandaloneLayoutElement(label.gameObject, minWidth: 82f, preferredWidth: 100f);

        var value = CreateSearchText(row, "Value", new Color32(245, 248, 251, 255));
        value.text = valueText ?? string.Empty;
        value.fontSize = 9;
        value.fontStyle = FontStyle.Bold;
        value.alignment = TextAnchor.MiddleCenter;
        AddStandaloneLayoutElement(value.gameObject, minWidth: 60f, flexibleWidth: 1f);

        var hasSecondaryAction = !string.IsNullOrWhiteSpace(secondaryCaption);
        if (!string.IsNullOrWhiteSpace(primaryCaption))
        {
            var primary = CreateStandaloneActionButton(row, "Primary", Vector2.zero, Vector2.zero, Vector2.zero,
                Vector2.zero, primaryCaption, 8, out _);
            AddStandaloneLayoutElement(primary.gameObject, preferredWidth: hasSecondaryAction ? 42f : 64f,
                preferredHeight: 22f);
            primary.interactable = primaryAction != null;
            if (primaryAction != null)
                EventHelper.AddListener(primaryAction, primary.onClick);
        }

        if (hasSecondaryAction)
        {
            var secondary = CreateStandaloneActionButton(row, "Secondary", Vector2.zero, Vector2.zero, Vector2.zero,
                Vector2.zero, secondaryCaption, 8, out _);
            AddStandaloneLayoutElement(secondary.gameObject, preferredWidth: 42f, preferredHeight: 22f);
            secondary.interactable = secondaryAction != null;
            if (secondaryAction != null)
                EventHelper.AddListener(secondaryAction, secondary.onClick);
        }

        state.SettingsRows.Add(rowGo);
    }

    /// <summary>
    /// Adds a real uGUI <see cref="Toggle"/> for boolean preferences. The row is recreated after
    /// a configuration write so the track and knob always reflect the persisted preference.
    /// </summary>
    private static void AddStandaloneSettingsToggleRow(StandaloneBackpackState state, string labelText, bool isOn,
        Action<bool> changedAction)
    {
        var pageRoot = GetStandaloneSettingsPageRoot(state);
        if (pageRoot == null)
            return;

        var rowGo = new GameObject("SettingToggleRow" + state.SettingsRows.Count);
        var row = rowGo.AddComponent<RectTransform>();
        row.SetParent(pageRoot, worldPositionStays: false);
        var background = rowGo.AddComponent<Image>();
        background.color = new Color32(20, 33, 44, 248);
        ApplyRoundedButtonPresentation(background);
        AddStandaloneLayoutElement(rowGo, minHeight: 28f, preferredHeight: 28f);
        var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset(8, 6, 2, 2);
        rowLayout.spacing = 5f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        var label = CreateSearchText(row, "Label", new Color32(188, 216, 235, 255));
        label.text = labelText ?? string.Empty;
        label.fontSize = 9;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleLeft;
        AddStandaloneLayoutElement(label.gameObject, minWidth: 82f, preferredWidth: 100f);

        var status = CreateSearchText(row, "Status", isOn ? new Color32(119, 221, 144, 255) : new Color32(225, 157, 157, 255));
        status.text = isOn ? "ENABLED" : "DISABLED";
        status.fontSize = 9;
        status.fontStyle = FontStyle.Bold;
        status.alignment = TextAnchor.MiddleCenter;
        AddStandaloneLayoutElement(status.gameObject, minWidth: 60f, flexibleWidth: 1f);

        var toggle = CreateStandaloneSettingsToggle(row, isOn);
        toggle.interactable = changedAction != null;
        if (changedAction != null)
            EventHelper.AddListener<bool>(changedAction, toggle.onValueChanged);

        state.SettingsRows.Add(rowGo);
    }

    private static Toggle CreateStandaloneSettingsToggle(RectTransform parent, bool isOn)
    {
        var toggleGo = new GameObject("Toggle");
        var toggleRect = toggleGo.AddComponent<RectTransform>();
        toggleRect.SetParent(parent, worldPositionStays: false);
        var track = toggleGo.AddComponent<Image>();
        track.color = isOn ? new Color32(40, 121, 157, 255) : new Color32(47, 59, 68, 255);
        ApplyPillButtonPresentation(track);
        var toggle = toggleGo.AddComponent<Toggle>();
        toggle.targetGraphic = track;
        toggle.isOn = isOn;
        AddStandaloneLayoutElement(toggleGo, preferredWidth: 52f, preferredHeight: 22f);

        var knobGo = new GameObject("Knob");
        var knob = knobGo.AddComponent<RectTransform>();
        knob.SetParent(toggleRect, worldPositionStays: false);
        knob.anchorMin = isOn ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
        knob.anchorMax = knob.anchorMin;
        knob.pivot = new Vector2(0.5f, 0.5f);
        knob.anchoredPosition = isOn ? new Vector2(-11f, 0f) : new Vector2(11f, 0f);
        knob.sizeDelta = new Vector2(16f, 16f);
        var knobImage = knobGo.AddComponent<Image>();
        knobImage.color = new Color32(240, 247, 251, 255);
        ApplyPillButtonPresentation(knobImage);
        knobImage.raycastTarget = false;

        return toggle;
    }

    private static RectTransform CreateStandaloneSettingsPage(RectTransform parent, string name)
    {
        var page = CreateStandaloneSettingsRegion(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var layout = page.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 6, 6);
        layout.spacing = 4f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        page.gameObject.SetActive(false);
        return page;
    }

    private static RectTransform GetStandaloneSettingsPageRoot(StandaloneBackpackState state)
    {
        if (state == null)
            return null;

        return state.SettingsPage switch
        {
            StandaloneBackpackSettingsPage.Tiers => state.SettingsTiersPage,
            StandaloneBackpackSettingsPage.Layout => state.SettingsLayoutPage,
            _ => state.SettingsGeneralPage
        };
    }

    private static RectTransform CreateStandaloneSettingsRegion(RectTransform parent, string name, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var regionGo = new GameObject(name);
        var region = regionGo.AddComponent<RectTransform>();
        region.SetParent(parent, worldPositionStays: false);
        region.anchorMin = anchorMin;
        region.anchorMax = anchorMax;
        region.offsetMin = offsetMin;
        region.offsetMax = offsetMax;
        return region;
    }

    private static LayoutElement AddStandaloneLayoutElement(GameObject gameObject, float minWidth = -1f,
        float preferredWidth = -1f, float flexibleWidth = -1f, float minHeight = -1f, float preferredHeight = -1f,
        float flexibleHeight = -1f)
    {
        var layoutElement = gameObject.GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
        if (minWidth >= 0f)
            layoutElement.minWidth = minWidth;
        if (preferredWidth >= 0f)
            layoutElement.preferredWidth = preferredWidth;
        if (flexibleWidth >= 0f)
            layoutElement.flexibleWidth = flexibleWidth;
        if (minHeight >= 0f)
            layoutElement.minHeight = minHeight;
        if (preferredHeight >= 0f)
            layoutElement.preferredHeight = preferredHeight;
        if (flexibleHeight >= 0f)
            layoutElement.flexibleHeight = flexibleHeight;
        return layoutElement;
    }

    private static void AdjustStandaloneTierSlots(StorageMenu menu, StandaloneBackpackState state, int tierIndex,
        int delta)
    {
        var config = Configuration.Instance;
        if (tierIndex < 0 || tierIndex >= config.TierSlotCounts.Length)
            return;

        var targetSlots = Mathf.Clamp(config.TierSlotCounts[tierIndex] + delta, 1, PlayerBackpack.MaxStorageSlots);
        if (PlayerBackpack.Instance != null && tierIndex == PlayerBackpack.Instance.CurrentTierIndex)
            targetSlots = Mathf.Max(targetSlots, GetMinimumStandaloneBackpackSlots());

        if (targetSlots == config.TierSlotCounts[tierIndex])
            return;

        config.TierSlotCounts[tierIndex] = targetSlots;
        PersistStandaloneSettings(menu, state, applyCurrentTier: true, syncSessionSettings: true);
    }

    private static int GetMinimumStandaloneBackpackSlots()
    {
        var backpackSlots = GetBackpackSlots();
        var minimum = 1;
        for (var i = 0; i < backpackSlots.Count; i++)
        {
            if (backpackSlots[i]?.ItemInstance != null)
                minimum = i + 1;
        }

        return minimum;
    }

    private static string FormatStandaloneUnlockRank(FullRank rank)
    {
        return rank.Rank + " " + Mathf.Clamp(rank.Tier, 1, 5);
    }

    private static FullRank OffsetStandaloneUnlockRank(FullRank rank, int offset)
    {
        var ranks = (ERank[])Enum.GetValues(typeof(ERank));
        if (ranks.Length == 0 || offset == 0)
            return new FullRank(rank.Rank, Mathf.Clamp(rank.Tier, 1, 5));

        var rankIndex = Array.IndexOf(ranks, rank.Rank);
        if (rankIndex < 0)
            rankIndex = 0;
        var tier = Mathf.Clamp(rank.Tier, 1, 5) + offset;
        while (tier > 5 && rankIndex < ranks.Length - 1)
        {
            tier -= 5;
            rankIndex++;
        }
        while (tier < 1 && rankIndex > 0)
        {
            tier += 5;
            rankIndex--;
        }

        return new FullRank(ranks[rankIndex], Mathf.Clamp(tier, 1, 5));
    }

    private static string FormatStandaloneOffset(float value)
    {
        return value.ToString("+0;-0;0");
    }

    private static void PersistStandaloneSettings(StorageMenu menu, StandaloneBackpackState state,
        bool applyCurrentTier = false, bool syncSessionSettings = false)
    {
        Configuration.Instance.Save();
        if (applyCurrentTier)
            PlayerBackpack.Instance?.EnsureCorrectTierApplied();
        if (syncSessionSettings)
            ConfigSyncManager.SyncCurrentConfigToClients();

        ModLogger.Info("[BackpackUI] Settings saved to MelonPreferences.");
        RefreshStandaloneSettingsPane(menu, state);
        if (state?.IsOpen == true)
            ApplyStandaloneBackpackMenu(menu);
    }

    private static void CreateStandaloneDropdown(RectTransform header, StandaloneBackpackState state)
    {
        if (header == null || state == null || state.DropdownRoot != null)
            return;

        var dropdownGo = new GameObject("PackRat_BackpackDropdown");
        var dropdownRoot = dropdownGo.AddComponent<RectTransform>();
        dropdownRoot.SetParent(header, worldPositionStays: false);
        dropdownRoot.anchorMin = new Vector2(0f, 0f);
        dropdownRoot.anchorMax = new Vector2(1f, 0f);
        dropdownRoot.pivot = new Vector2(0.5f, 1f);
        var background = dropdownGo.AddComponent<Image>();
        background.color = new Color32(12, 21, 30, 252);

        var canvas = Utils.AddComponentSafe<Canvas>(dropdownGo);
        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;
        }
        Utils.AddComponentSafe<GraphicRaycaster>(dropdownGo);

        state.DropdownRoot = dropdownRoot;
        HideStandaloneDropdown(state);
    }

    private static void ShowStandaloneDropdown(StorageMenu menu, StandaloneBackpackState state,
        StandaloneBackpackDropdown dropdown)
    {
        if (menu == null || state?.DropdownRoot == null)
            return;

        if (state.ActiveDropdown == dropdown && state.DropdownRoot.gameObject.activeSelf)
        {
            HideStandaloneDropdown(state);
            return;
        }

        var options = BuildStandaloneDropdownOptions(menu, state, dropdown);
        if (options.Count == 0)
        {
            HideStandaloneDropdown(state);
            return;
        }

        state.ActiveDropdown = dropdown;
        state.DropdownOptions.Clear();
        state.DropdownOptions.AddRange(options);
        var height = 6f + (options.Count * 24f);
        state.DropdownRoot.offsetMin = new Vector2(8f, 34f - height);
        state.DropdownRoot.offsetMax = new Vector2(-8f, 34f);
        state.DropdownRoot.gameObject.SetActive(true);
        state.DropdownRoot.SetAsLastSibling();

        for (var i = 0; i < state.DropdownOptionButtons.Count; i++)
            state.DropdownOptionButtons[i].gameObject.SetActive(i < options.Count);

        for (var i = 0; i < options.Count; i++)
            ConfigureStandaloneDropdownOption(state, i, options[i], dropdown);
    }

    private static List<StandaloneBackpackDropdownOption> BuildStandaloneDropdownOptions(StorageMenu menu,
        StandaloneBackpackState state, StandaloneBackpackDropdown dropdown)
    {
        var options = new List<StandaloneBackpackDropdownOption>();
        var backpackSlots = GetBackpackSlots();
        switch (dropdown)
        {
            case StandaloneBackpackDropdown.Type:
            {
                var values = GetAvailableStandaloneFilterOptions(backpackSlots, state, GetSlotType,
                    ignoreTypeFilter: true, ignoreQualityFilter: false);
                if (values.Count == 0)
                    return options;

                AddStandaloneDropdownOption(options, "ALL TYPES", () =>
                {
                    state.TypeFilter = string.Empty;
                    RefreshStandaloneFilterView(menu, state);
                });
                for (var i = 0; i < values.Count; i++)
                {
                    var value = values[i];
                    AddStandaloneDropdownOption(options, value.ToUpperInvariant(), () =>
                    {
                        state.TypeFilter = value;
                        RefreshStandaloneFilterView(menu, state);
                    });
                }
                break;
            }
            case StandaloneBackpackDropdown.Quality:
            {
                var values = GetAvailableStandaloneFilterOptions(backpackSlots, state, GetSlotQuality,
                    ignoreTypeFilter: false, ignoreQualityFilter: true);
                if (values.Count == 0)
                    return options;

                AddStandaloneDropdownOption(options, "ALL QUALITIES", () =>
                {
                    state.QualityFilter = string.Empty;
                    RefreshStandaloneFilterView(menu, state);
                });
                for (var i = 0; i < values.Count; i++)
                {
                    var value = values[i];
                    AddStandaloneDropdownOption(options, value.ToUpperInvariant(), () =>
                    {
                        state.QualityFilter = value;
                        RefreshStandaloneFilterView(menu, state);
                    }, showQualityStar: true, qualityStarColor: GetQualityStarColor(value));
                }
                break;
            }
            case StandaloneBackpackDropdown.Sort:
                AddStandaloneDropdownOption(options, "SLOT ORDER", () => SetStandaloneSortMode(menu, state, StandaloneBackpackSortMode.SlotOrder));
                AddStandaloneDropdownOption(options, "NAME", () => SetStandaloneSortMode(menu, state, StandaloneBackpackSortMode.Name));
                AddStandaloneDropdownOption(options, "QUANTITY", () => SetStandaloneSortMode(menu, state, StandaloneBackpackSortMode.Quantity));
                AddStandaloneDropdownOption(options, "QUALITY", () => SetStandaloneSortMode(menu, state, StandaloneBackpackSortMode.Quality));
                AddStandaloneDropdownOption(options, "TYPE", () => SetStandaloneSortMode(menu, state, StandaloneBackpackSortMode.Type));
                break;
        }

        return options;
    }

    private static void AddStandaloneDropdownOption(List<StandaloneBackpackDropdownOption> options, string label,
        Action selectAction, bool showQualityStar = false, Color qualityStarColor = default)
    {
        options.Add(new StandaloneBackpackDropdownOption
        {
            Label = label,
            SelectAction = selectAction,
            ShowQualityStar = showQualityStar,
            QualityStarColor = qualityStarColor
        });
    }

    private static Color GetQualityStarColor(string qualityName)
    {
        switch (qualityName?.ToLowerInvariant())
        {
            case "trash":
                return ItemQuality.GetColor(EQuality.Trash);
            case "poor":
                return ItemQuality.GetColor(EQuality.Poor);
            case "standard":
                return ItemQuality.GetColor(EQuality.Standard);
            case "premium":
                return ItemQuality.GetColor(EQuality.Premium);
            case "heavenly":
                return ItemQuality.GetColor(EQuality.Heavenly);
            default:
                return Color.white;
        }
    }

    private static void SetStandaloneSortMode(StorageMenu menu, StandaloneBackpackState state,
        StandaloneBackpackSortMode sortMode)
    {
        if (state == null)
            return;

        var previousSortMode = state.SortMode;
        state.SortMode = sortMode;
        UpdateStandaloneFilterLabels(state);
        ModLogger.Info($"[BackpackUI] Sort changed: {GetSortModeLabel(previousSortMode)} -> {GetSortModeLabel(sortMode)}.");
        RefreshStandaloneFilterView(menu, state);
    }

    private static void ConfigureStandaloneDropdownOption(StandaloneBackpackState state, int index,
        StandaloneBackpackDropdownOption option, StandaloneBackpackDropdown dropdown)
    {
        while (state.DropdownOptionButtons.Count <= index)
            CreateStandaloneDropdownOptionButton(state, state.DropdownOptionButtons.Count);

        var button = state.DropdownOptionButtons[index];
        var label = state.DropdownOptionLabels[index];
        var qualityStar = state.DropdownOptionQualityStars[index];
        var rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(4f, -4f - ((index + 1) * 24f));
        rect.offsetMax = new Vector2(-4f, -4f - (index * 24f));
        label.text = option.Label;
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.offsetMin = new Vector2(option.ShowQualityStar ? 30f : 8f, 1f);
        labelRect.offsetMax = new Vector2(-8f, -1f);
        qualityStar.sprite = state.QualityStarSprite;
        qualityStar.color = option.QualityStarColor;
        qualityStar.enabled = option.ShowQualityStar && state.QualityStarSprite != null;
        button.gameObject.SetActive(true);

        var oldAction = state.DropdownOptionActions[index];
        if (oldAction != null)
            EventHelper.RemoveListener(oldAction, button.onClick);

        var optionIndex = index;
        var selectAction = new Action(() => SelectStandaloneDropdownOption(state, optionIndex));
        state.DropdownOptionActions[index] = selectAction;
        EventHelper.AddListener(selectAction, button.onClick);

        var selected = IsStandaloneDropdownOptionSelected(state, dropdown, index);
        var image = button.targetGraphic as Image;
        if (image != null)
            image.color = selected ? new Color32(45, 109, 146, 255) : new Color32(25, 43, 57, 255);
    }

    private static void CreateStandaloneDropdownOptionButton(StandaloneBackpackState state, int index)
    {
        var optionGo = new GameObject("Option" + index);
        var optionRect = optionGo.AddComponent<RectTransform>();
        optionRect.SetParent(state.DropdownRoot, worldPositionStays: false);
        var image = optionGo.AddComponent<Image>();
        ApplyPillButtonPresentation(image);
        var button = optionGo.AddComponent<Button>();
        button.targetGraphic = image;
        var label = CreateSearchText(optionRect, "Label", new Color32(223, 239, 248, 255));
        label.fontSize = 11;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleLeft;

        var starGo = new GameObject("QualityStar");
        var starRect = starGo.AddComponent<RectTransform>();
        starRect.SetParent(optionRect, worldPositionStays: false);
        starRect.anchorMin = new Vector2(0f, 0.5f);
        starRect.anchorMax = new Vector2(0f, 0.5f);
        starRect.pivot = new Vector2(0f, 0.5f);
        starRect.anchoredPosition = new Vector2(7f, 0f);
        starRect.sizeDelta = new Vector2(16f, 16f);
        var qualityStar = starGo.AddComponent<Image>();
        qualityStar.raycastTarget = false;
        qualityStar.preserveAspect = true;
        qualityStar.enabled = false;

        state.DropdownOptionButtons.Add(button);
        state.DropdownOptionLabels.Add(label);
        state.DropdownOptionQualityStars.Add(qualityStar);
        state.DropdownOptionActions.Add(null);
    }

    private static bool IsStandaloneDropdownOptionSelected(StandaloneBackpackState state,
        StandaloneBackpackDropdown dropdown, int optionIndex)
    {
        if (state == null || optionIndex < 0 || optionIndex >= state.DropdownOptions.Count)
            return false;

        var label = state.DropdownOptions[optionIndex].Label;
        switch (dropdown)
        {
            case StandaloneBackpackDropdown.Type:
                return optionIndex == 0 ? string.IsNullOrEmpty(state.TypeFilter) :
                    string.Equals(label, state.TypeFilter, StringComparison.OrdinalIgnoreCase);
            case StandaloneBackpackDropdown.Quality:
                return optionIndex == 0 ? string.IsNullOrEmpty(state.QualityFilter) :
                    string.Equals(label, state.QualityFilter, StringComparison.OrdinalIgnoreCase);
            case StandaloneBackpackDropdown.Sort:
                return string.Equals(label, GetSortModeLabel(state.SortMode), StringComparison.OrdinalIgnoreCase)
                    || (state.SortMode == StandaloneBackpackSortMode.SlotOrder && label == "SLOT ORDER")
                    || (state.SortMode == StandaloneBackpackSortMode.Quantity && label == "QUANTITY");
            default:
                return false;
        }
    }

    private static void SelectStandaloneDropdownOption(StandaloneBackpackState state, int optionIndex)
    {
        if (state == null || optionIndex < 0 || optionIndex >= state.DropdownOptions.Count)
            return;

        var option = state.DropdownOptions[optionIndex];
        HideStandaloneDropdown(state);
        option.SelectAction?.Invoke();
    }

    private static void HideStandaloneDropdown(StandaloneBackpackState state)
    {
        if (state == null)
            return;

        state.ActiveDropdown = StandaloneBackpackDropdown.None;
        if (state.DropdownRoot != null)
            state.DropdownRoot.gameObject.SetActive(false);
    }

    private static List<string> GetAvailableStandaloneFilterOptions(List<ItemSlot> slots, StandaloneBackpackState state,
        Func<ItemSlot, string> valueSelector, bool ignoreTypeFilter, bool ignoreQualityFilter)
    {
        var options = new List<string>();
        if (slots == null || valueSelector == null)
            return options;

        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot?.ItemInstance == null)
                continue;
            if (!string.IsNullOrWhiteSpace(state?.SearchTerm) && !SlotMatchesSearch(slot, state.SearchTerm))
                continue;
            if (!ignoreTypeFilter && !string.IsNullOrWhiteSpace(state?.TypeFilter) &&
                !string.Equals(GetSlotType(slot), state.TypeFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!ignoreQualityFilter && !string.IsNullOrWhiteSpace(state?.QualityFilter) &&
                !string.Equals(GetSlotQuality(slot), state.QualityFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            var value = valueSelector(slot);
            if (!string.IsNullOrWhiteSpace(value) && !options.Any(option =>
                    string.Equals(option, value, StringComparison.OrdinalIgnoreCase)))
                options.Add(value);
        }

        options.Sort(StringComparer.OrdinalIgnoreCase);
        return options;
    }

    private static string GetSortModeLabel(StandaloneBackpackSortMode sortMode)
    {
        switch (sortMode)
        {
            case StandaloneBackpackSortMode.Name:
                return "NAME";
            case StandaloneBackpackSortMode.Quantity:
                return "QTY";
            case StandaloneBackpackSortMode.Quality:
                return "QUALITY";
            case StandaloneBackpackSortMode.Type:
                return "TYPE";
            default:
                return "SLOTS";
        }
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
            state.SettingsOpen = false;
            state.AwaitingToggleKey = false;
            HideStandaloneDropdown(state);
            if (state.SettingsRoot != null)
                state.SettingsRoot.gameObject.SetActive(false);
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
                var filteredSlots = GetDisplayBackpackSlots(GetBackpackSlots(), state);
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
            state.TypeFilter = string.Empty;
            state.QualityFilter = string.Empty;
            state.SortMode = StandaloneBackpackSortMode.SlotOrder;
            state.SettingsOpen = false;
            state.AwaitingToggleKey = false;
            HideStandaloneDropdown(state);
            if (state.SettingsRoot != null)
                state.SettingsRoot.gameObject.SetActive(false);
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

    /// <summary>
    /// Pages the hotkey-opened backpack with all four arrow keys. Left and up go back; right and
    /// down advance. The input is left untouched while the live search field owns focus.
    /// </summary>
    public static bool HandleStandaloneBackpackPaginationHotkeys()
    {
        var previousRequested = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.UpArrow);
        var nextRequested = Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.DownArrow);
        if (!previousRequested && !nextRequested)
            return false;

        try
        {
            var menu = Singleton<StorageMenu>.Instance;
            if (menu == null || !StandaloneBackpackPanels.TryGetValue(menu.GetInstanceID(), out var state) || !state.IsOpen)
                return false;

            if ((state.SearchInput != null && state.SearchInput.isFocused) ||
                (state.DropdownRoot != null && state.DropdownRoot.gameObject.activeInHierarchy))
                return false;

            var displaySlots = GetDisplayBackpackSlots(GetBackpackSlots(), state);
            var totalPages = Mathf.Max(1, Mathf.CeilToInt(displaySlots.Count / (float)StandaloneBackpackSlotsPerPage));
            if (state.LastPageInputFrame == Time.frameCount)
                return true;

            var requestedPage = state.CurrentPage;
            if (previousRequested)
                requestedPage--;
            else if (nextRequested)
                requestedPage++;

            state.LastPageInputFrame = Time.frameCount;
            state.CurrentPage = Mathf.Clamp(requestedPage, 0, totalPages - 1);
            if (state.CurrentPage != requestedPage)
                return true;

            ApplyStandaloneBackpackMenu(menu);
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error("StorageMenuPatch.HandleStandaloneBackpackPaginationHotkeys", ex);
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
        ApplyPillButtonPresentation(image);

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

    /// <summary>
    /// Produces the standalone backpack's display order without changing the backing storage slots.
    /// Empty slots remain in their normal order until a filter or sort is selected.
    /// </summary>
    private static List<ItemSlot> GetDisplayBackpackSlots(List<ItemSlot> backpackSlots, StandaloneBackpackState state)
    {
        if (backpackSlots == null || backpackSlots.Count == 0)
            return new List<ItemSlot>();

        var searchTerm = state?.SearchTerm ?? string.Empty;
        var filterActive = HasStandaloneFilters(state);
        if (!filterActive && state?.SortMode == StandaloneBackpackSortMode.SlotOrder)
            return GetFilteredBackpackSlots(backpackSlots, searchTerm);

        var displaySlots = new List<ItemSlot>();
        for (var i = 0; i < backpackSlots.Count; i++)
        {
            var slot = backpackSlots[i];
            if (slot?.ItemInstance == null)
                continue;
            if (!string.IsNullOrWhiteSpace(searchTerm) && !SlotMatchesSearch(slot, searchTerm))
                continue;
            if (!string.IsNullOrWhiteSpace(state?.TypeFilter) &&
                !string.Equals(GetSlotType(slot), state.TypeFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(state?.QualityFilter) &&
                !string.Equals(GetSlotQuality(slot), state.QualityFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            displaySlots.Add(slot);
        }

        if (state != null && state.SortMode != StandaloneBackpackSortMode.SlotOrder)
            displaySlots.Sort((left, right) => CompareStandaloneBackpackSlots(left, right, state.SortMode, backpackSlots));

        return displaySlots;
    }

    private static bool HasStandaloneFilters(StandaloneBackpackState state)
    {
        return state != null && (!string.IsNullOrWhiteSpace(state.SearchTerm)
            || !string.IsNullOrWhiteSpace(state.TypeFilter)
            || !string.IsNullOrWhiteSpace(state.QualityFilter));
    }

    private static int CompareStandaloneBackpackSlots(ItemSlot left, ItemSlot right, StandaloneBackpackSortMode sortMode,
        List<ItemSlot> originalSlots)
    {
        string leftValue;
        string rightValue;
        switch (sortMode)
        {
            case StandaloneBackpackSortMode.Name:
                leftValue = GetSlotName(left);
                rightValue = GetSlotName(right);
                break;
            case StandaloneBackpackSortMode.Quantity:
                var quantityComparison = GetSlotQuantity(right).CompareTo(GetSlotQuantity(left));
                return quantityComparison != 0 ? quantityComparison : originalSlots.IndexOf(left).CompareTo(originalSlots.IndexOf(right));
            case StandaloneBackpackSortMode.Quality:
                leftValue = GetSlotQuality(left);
                rightValue = GetSlotQuality(right);
                break;
            case StandaloneBackpackSortMode.Type:
                leftValue = GetSlotType(left);
                rightValue = GetSlotType(right);
                break;
            default:
                return originalSlots.IndexOf(left).CompareTo(originalSlots.IndexOf(right));
        }

        var comparison = string.Compare(leftValue, rightValue, StringComparison.OrdinalIgnoreCase);
        return comparison != 0 ? comparison : originalSlots.IndexOf(left).CompareTo(originalSlots.IndexOf(right));
    }

    private static string GetSlotName(ItemSlot slot)
    {
        return slot?.ItemInstance?.Definition?.Name ?? string.Empty;
    }

    private static string GetSlotType(ItemSlot slot)
    {
        var definition = slot?.ItemInstance?.Definition;
        if (definition == null)
            return string.Empty;

        // BaseItemDefinition.Category is intentionally broad in the current game build (many
        // unrelated objects report Product), so derive the player-facing group from the richer
        // definition and equippable metadata instead.
        var definitionType = definition.GetType().Name ?? string.Empty;
        var definitionNamespace = definition.GetType().Namespace ?? string.Empty;
        var equippableType = GetEquippableTypeName(definition);
        var identity = string.Join(" ", new[]
        {
            definition.Name ?? string.Empty,
            definition.ID ?? string.Empty,
            definitionType,
            equippableType
        });

        if (ContainsTypeToken(identity, "ammo", "bullet", "round", "shell"))
            return "Ammo";
        if (ContainsTypeToken(definitionType, "seed") || ContainsTypeToken(identity, "seed"))
            return "Seeds";
        if (ContainsTypeToken(identity, "pseudo", "acid", "phosphorus"))
            return "Reagents";
        // PropertyItemDefinition carries the product-effect list, while AdditiveDefinition
        // represents the other add-ins accepted by the game's mixing and growing workflows.
        if (ContainsTypeToken(definitionType, "additive", "propertyitem", "mixer"))
            return "Mixers";
        if (IsProductItemInstance(slot.ItemInstance))
            return "Products";
        if (ContainsTypeToken(definitionType, "buildable") || ContainsTypeToken(equippableType, "buildable"))
            return "Furniture";
        if (ContainsTypeToken(equippableType, "weapon", "gun", "melee", "ranged") ||
            ContainsTypeToken(identity, "weapon", "baseball bat", "machete", "knife", "pistol", "shotgun", "revolver",
                "m1911", "1911", "uzi", "smg", "rifle"))
            return "Weapons";
        if (ContainsTypeToken(equippableType, "watering", "clipper"))
            return "Tools";
        if (ContainsTypeToken(definitionNamespace, ".product") || ContainsTypeToken(definitionType, "product", "weed", "meth", "cocaine", "shroom"))
            return "Products";
        if (!string.IsNullOrWhiteSpace(equippableType))
            return "Tools";

        var category = definition.Category.ToString();
        return string.IsNullOrWhiteSpace(category) || string.Equals(category, "Product", StringComparison.OrdinalIgnoreCase)
            ? "Items"
            : category;
    }

    private static bool IsProductItemInstance(ItemInstance item)
    {
        if (item == null)
            return false;

#if MONO
        return item is ProductItemInstance;
#else
        return item.TryCast<ProductItemInstance>() != null;
#endif
    }

    private static string GetProductDrugType(ItemInstance item)
    {
        var definition = item?.Definition;
        if (definition == null)
            return string.Empty;

        try
        {
#if MONO
            var productDefinition = definition as ProductDefinition;
#else
            var productDefinition = definition.TryCast<ProductDefinition>();
#endif
            return productDefinition?.DrugType.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Lets the in-game settings panel capture a replacement backpack hotkey before the normal
    /// backpack hotkey handler can react to that same key press.
    /// </summary>
    public static bool HandleStandaloneBackpackSettingsInput()
    {
        try
        {
            var menu = Singleton<StorageMenu>.Instance;
            if (menu == null || !StandaloneBackpackPanels.TryGetValue(menu.GetInstanceID(), out var state) ||
                !state.IsOpen || !state.SettingsOpen || !state.AwaitingToggleKey)
                return false;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                state.AwaitingToggleKey = false;
                RefreshStandaloneSettingsPane(menu, state);
                return true;
            }

            var keyCodes = (KeyCode[])Enum.GetValues(typeof(KeyCode));
            for (var i = 0; i < keyCodes.Length; i++)
            {
                var keyCode = keyCodes[i];
                if (keyCode == KeyCode.None || (keyCode >= KeyCode.Mouse0 && keyCode <= KeyCode.Mouse6) ||
                    !Input.GetKeyDown(keyCode))
                    continue;

                Configuration.Instance.ToggleKey = keyCode;
                state.AwaitingToggleKey = false;
                PersistStandaloneSettings(menu, state);
                return true;
            }

            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error("StorageMenuPatch.HandleStandaloneBackpackSettingsInput", ex);
            return false;
        }
    }

    /// <summary>
    /// Adds player-facing search words for every normalized backpack category. These are aliases
    /// only; item-specific words (for example Weed, Cocaine, Watering Can, or Baseball Bat)
    /// still come from the definition name, ID, and concrete runtime types.
    /// </summary>
    private static string GetSlotSearchAliases(ItemSlot slot)
    {
        switch (GetSlotType(slot))
        {
            case "Ammo":
                return "ammo ammunition bullet round shell";
            case "Furniture":
                return "furniture buildable building";
            case "Mixers":
                return "mixer additive effect ingredient";
            case "Products":
                return "product drug";
            case "Reagents":
                return "reagent chemical chemistry";
            case "Seeds":
                return "seed growing cultivation";
            case "Tools":
                return "tool equipment";
            case "Weapons":
                return "weapon firearm gun melee ranged";
            default:
                return "item";
        }
    }

    private static string GetEquippableTypeName(ItemDefinition definition)
    {
        if (definition == null)
            return string.Empty;

#if MONO
        var itemDefinition = definition as ItemDefinition;
#else
        var itemDefinition = definition.TryCast<ItemDefinition>();
#endif
        return itemDefinition?.Equippable?.GetType().Name ?? string.Empty;
    }

    private static bool ContainsTypeToken(string value, params string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(value) || tokens == null)
            return false;

        for (var i = 0; i < tokens.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(tokens[i]) &&
                value.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static string GetSlotQuality(ItemSlot slot)
    {
        return GetItemInstanceQuality(slot?.ItemInstance);
    }

    /// <summary>
    /// Resolves quality through the common QualityItemInstance base type. ProductItemInstance
    /// inherits this member, so probing only its concrete runtime type misses Quality in both
    /// generated IL2CPP wrappers and the Mono hierarchy.
    /// </summary>
    private static string GetItemInstanceQuality(ItemInstance item)
    {
        if (item == null)
            return string.Empty;

#if MONO
        var qualityItem = item as QualityItemInstance;
#else
        var qualityItem = item.TryCast<QualityItemInstance>();
#endif
        if (qualityItem != null)
            return qualityItem.Quality.ToString();

        // Preserve a reflection fallback for custom or future item-instance subclasses that
        // expose quality without inheriting QualityItemInstance.
        var quality = ReflectionUtils.TryGetFieldOrProperty(item, "Quality")
            ?? ReflectionUtils.TryGetFieldOrProperty(item, "quality");
        return quality?.ToString() ?? string.Empty;
    }

    private static float GetSlotQuantity(ItemSlot slot)
    {
        if (slot?.ItemInstance == null)
            return 0f;

        var quantity = ReflectionUtils.TryGetFieldOrProperty(slot.ItemInstance, "Quantity")
            ?? ReflectionUtils.TryGetFieldOrProperty(slot.ItemInstance, "quantity")
            ?? ReflectionUtils.TryGetFieldOrProperty(slot.ItemInstance, "Amount")
            ?? ReflectionUtils.TryGetFieldOrProperty(slot.ItemInstance, "amount");
        if (quantity == null)
            return 1f;

        try
        {
            return Convert.ToSingle(quantity);
        }
        catch
        {
            return 1f;
        }
    }

    private static bool SlotMatchesSearch(ItemSlot slot, string searchTerm)
    {
        if (slot?.ItemInstance == null || string.IsNullOrWhiteSpace(searchTerm))
            return false;

        try
        {
            var item = slot.ItemInstance;
            var definition = item.Definition;
            var quality = GetItemInstanceQuality(item);
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
                quality,
                GetSlotType(slot),
                GetSlotSearchAliases(slot),
                GetEquippableTypeName(definition),
                GetProductDrugType(item)
            };

            var terms = searchTerm.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < terms.Length; i++)
            {
                var term = terms[i];
                var matchesNameOrId = MatchesSearchTerm(nameAndId, term);
                var matchesMetadata = term.Length >= SearchMetadataMinimumTermLength &&
                                      MatchesSearchTerm(metadata, term);
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
    /// Matches a term anywhere within a display field. Name/ID matching is always allowed, while
    /// metadata matching remains gated by <see cref="SearchMetadataMinimumTermLength"/>.
    /// </summary>
    private static bool MatchesSearchTerm(IEnumerable<string> fields, string term)
    {
        if (fields == null || string.IsNullOrWhiteSpace(term))
            return false;

        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field))
                continue;

            if (field.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
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
