using System.Collections;
using HarmonyLib;
using MelonLoader;
using PackRat.Config;
using PackRat.Extensions;
using PackRat.Helpers;
using PackRat.Networking;
using PackRat.Routing;
using PackRat.Storage;
using UnityEngine;
using UnityEngine.UI;

#if MONO
using ScheduleOne;
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.Levelling;
using ScheduleOne.Money;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Product;
using S1Contract = ScheduleOne.Quests.Contract;
using ScheduleOne.Storage;
using ScheduleOne.UI;
using ScheduleOne.UI.Items;
using S1TMP = TMPro.TextMeshProUGUI;
using S1Action = System.Action;
#else
using Il2CppInterop.Runtime;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Product;
using S1Contract = Il2CppScheduleOne.Quests.Contract;
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
        Type,
        Favorites,
        Recent
    }

    private enum StandaloneBackpackSortDirection
    {
        Ascending,
        Descending
    }

    private enum StandaloneBackpackDropdown
    {
        None,
        Type,
        Quality,
        SortDirection
    }

    private enum StandaloneBackpackSettingsPage
    {
        General,
        Theme,
        Tiers,
        Layout,
        Routing,
        Metrics
    }

    private enum StandaloneBackpackLayoutView
    {
        Backpack,
        Storage,
        Station,
        Deal
    }

    /// <summary>
    /// PackRat's keyboard focus is deliberately independent from Unity's global selected object.
    /// The game owns that selection for drag/drop, hotbar, and its other menus, while this small
    /// state only presents a non-invasive focus accent over the shared backpack browser.
    /// </summary>
    private enum StandaloneBackpackKeyboardFocusKind
    {
        None,
        Control
    }

    private sealed class StandaloneBackpackDropdownOption
    {
        public string Label;
        public Action SelectAction;
        public bool ShowQualityStar;
        public Color QualityStarColor;
    }

    private sealed class StandaloneBackpackFavoriteControl
    {
        public ItemSlotUI SlotUi;
        public ItemSlot BoundSlot;
        public Button Button;
        public Image Background;
        public Text Label;
        public Action ToggleAction;
    }

    /// <summary>
    /// Aggregates one product definition across the actual backpack slots for the optional
    /// metrics tray. Browser search and sorting must never change these totals.
    /// </summary>
    private sealed class StandaloneBackpackProductMetric
    {
        public string Id;
        public string Name;
        public int Quantity;
        public float UnitPrice;
        public bool HasUnitPrice;
        public object Definition;
        public int ActiveOrderQuantity;
    }

    private sealed class StandaloneBackpackSortTab
    {
        public StandaloneBackpackSortMode SortMode;
        public Button Button;
        public Text Label;
        public Action SelectAction;
    }

    private sealed class StandaloneBackpackKeyboardControl
    {
        public Selectable Selectable;
        public Action ActivateAction;
        public bool IsSearchInput;
    }

    private sealed class BackpackPanelState
    {
        public StorageMenu Menu;
        public RectTransform Container;
        public RectTransform HeaderRoot;
        public RectTransform SlotContainer;
        public GridLayoutGroup SlotGridLayout;
        public ItemSlotUI[] SlotUIs;
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
        public Func<List<ItemSlot>> StorageSlotProvider;
        public RectTransform BulkTransferRoot;
        public Button BulkSelectorButton;
        public Text BulkSelectorLabel;
        public Button MoveToStorageButton;
        public Button MoveToBackpackButton;
        public Text BulkTransferStatusLabel;
        public RectTransform BulkTransferActionsRoot;
        public CanvasGroup BulkTransferActionsCanvasGroup;
        public RectTransform BulkDropdownRoot;
        public RectTransform BulkDropdownContent;
        public readonly List<Button> BulkDropdownOptionButtons = new List<Button>();
        public readonly List<Text> BulkDropdownOptionLabels = new List<Text>();
        public readonly List<Action> BulkDropdownOptionActions = new List<Action>();
        public readonly List<BulkTransferSelection> BulkTransferOptions = new List<BulkTransferSelection>();
        public BulkTransferSelection BulkTransferSelection;
        public Action BulkSelectorAction;
        public Action MoveToStorageAction;
        public Action MoveToBackpackAction;
        public string BulkTransferStatus;
        public bool SupportsStorageBulkTransfer;
        public bool BulkTransferPresentationInitialized;
        public bool BulkTransferExpanded;
        public int BulkTransferMotionGeneration;
    }

    private enum BulkTransferMatchKind
    {
        Category,
        Definition,
        WeedStrain
    }

    /// <summary>
    /// The independently selected criteria for a storage bulk move. It deliberately does not
    /// borrow the shared browser's search or display filters, which are presentation-only.
    /// </summary>
    private sealed class BulkTransferSelection
    {
        public BulkTransferMatchKind Kind;
        public string Key;
        public string Label;
    }

    /// <summary>
    /// A marijuana product's original strain. Mixed products keep a recipe pointing at their
    /// input product, allowing PackRat to group every derived mix with its parent strain even
    /// when the player gave the mix a completely different display name.
    /// </summary>
    private sealed class WeedStrainOption
    {
        public string Id;
        public string Name;
    }

    /// <summary>
    /// Tracks the StorageMenu slots that belong to the game prefab separately from the temporary
    /// slot views required by the hotkey backpack. StorageMenu is shared with vehicle trunks, so
    /// its native slot array must be restored before any non-backpack surface opens.
    /// </summary>
    private sealed class StorageMenuSlotCapacityState
    {
        public ItemSlotUI[] NativeSlots;
        public readonly List<ItemSlotUI> AddedBackpackSlots = new List<ItemSlotUI>();
        public Vector2 NativeSlotAnchorMin;
        public Vector2 NativeSlotAnchorMax;
        public Vector2 NativeSlotPivot;
        public Vector2 NativeSlotAnchoredPosition;
        public Vector2 NativeSlotSizeDelta;
        public Vector3 NativeSlotScale;
        public GridLayoutGroup.Axis NativeGridStartAxis;
        public GridLayoutGroup.Constraint NativeGridConstraint;
        public int NativeGridConstraintCount;
        public TextAnchor NativeGridAlignment;
        public Vector2 NativeGridCellSize;
        public Vector2 NativeGridSpacing;
        public RectOffset NativeGridPadding;
        public Vector3 NativeContainerLocalPosition;
        public Vector2 NativeCloseButtonPosition;
        public Vector3 NativeCloseButtonScale;
    }

    private sealed class StandaloneBackpackState
    {
        /// <summary>
        /// Re-renders the owning backpack surface after a search, filter, sort, favourite, or
        /// settings change. Keeping this callback on the state lets the exact same browser UI
        /// run inside the hotkey menu, storage panels, stations, and handover screens.
        /// </summary>
        public Action RefreshAction;
        public RectTransform PresentationRoot;
        public RectTransform VisualRoot;
        public RectTransform HeaderRoot;
        public RectTransform SortTabsRoot;
        public RectTransform SlotsPanelRoot;
        public Image HeaderAccent;
        public RectTransform DropdownRoot;
        public Vector2 DropdownAnchor;
        public float DropdownWidth;
        public RectTransform SettingsRoot;
        public RectTransform SettingsCard;
        public RectTransform SettingsTabsRoot;
        public RectTransform SettingsContentRoot;
        public RectTransform SettingsGeneralPage;
        public RectTransform SettingsThemePage;
        public RectTransform SettingsTiersPage;
        public RectTransform SettingsLayoutPage;
        public RectTransform SettingsRoutingPage;
        public RectTransform SettingsMetricsPage;
        public Text SettingsSessionStatusValue;
        public Image SettingsTabIndicator;
        public Text VisualTitleLabel;
        public Text VisualMetaLabel;
        public InputField SearchInput;
        public Image SearchBackground;
        public Text SearchText;
        public Text SearchPlaceholder;
        public Action<string> SearchAction;
        public Button TypeFilterButton;
        public Button QualityFilterButton;
        public Button SortDirectionButton;
        public Button OrganizeButton;
        public Button ConsolidateButton;
        public Button ClearFiltersButton;
        public Text TypeFilterLabel;
        public Text QualityFilterLabel;
        public Text SortDirectionLabel;
        public Text OrganizeLabel;
        public Text ConsolidateLabel;
        public Text ClearFiltersLabel;
        public Action TypeFilterAction;
        public Action QualityFilterAction;
        public Action SortDirectionAction;
        public Action OrganizeAction;
        public Action ConsolidateAction;
        public Action ClearFiltersAction;
        public RectTransform MetricsTrayRoot;
        public RectTransform MetricsTrayPanel;
        public RectTransform MetricsTrayContent;
        public Text MetricsTraySummary;
        public Button MetricsTrayToggleButton;
        public Text MetricsTrayToggleLabel;
        public CanvasGroup MetricsTrayCanvasGroup;
        public readonly List<GameObject> MetricsTrayRows = new List<GameObject>();
        public bool MetricsTrayExpanded;
        public BackpackUiTheme AppliedTheme;
        public BackpackUiThemePalette AppliedThemePalette;
        public bool AppliedThemePaletteCaptured;
        public int MetricsTrayMotionGeneration;
        public string MetricsTrayFingerprint;
        public float NextMetricsTrayRefreshTime;
        public Button SettingsButton;
        public Text SettingsLabel;
        public Button DoneButton;
        public Button SettingsCloseButton;
        public Button SettingsGeneralButton;
        public Button SettingsThemeButton;
        public Button SettingsTiersButton;
        public Button SettingsLayoutButton;
        public Button SettingsRoutingButton;
        public Button SettingsMetricsButton;
        public Text SettingsThemeValueLabel;
        public readonly List<GameObject> SettingsRows = new List<GameObject>();
        public bool SettingsOpen;
        public bool AwaitingToggleKey;
        public int KeyboardSettingsControlIndex = -1;
        public int SettingsTierIndex;
        public StandaloneBackpackSettingsPage SettingsPage;
        public StandaloneBackpackLayoutView LayoutView;
        public bool SearchListenerBound;
        public bool SearchFocusPresented;
        public StandaloneBackpackDropdown ActiveDropdown;
        public int KeyboardDropdownOptionIndex = -1;
        public readonly List<Button> DropdownOptionButtons = new List<Button>();
        public readonly List<Text> DropdownOptionLabels = new List<Text>();
        public readonly List<Image> DropdownOptionQualityStars = new List<Image>();
        public readonly List<Action> DropdownOptionActions = new List<Action>();
        public readonly List<StandaloneBackpackDropdownOption> DropdownOptions = new List<StandaloneBackpackDropdownOption>();
        public readonly List<StandaloneBackpackSortTab> SortTabs = new List<StandaloneBackpackSortTab>();
        public readonly List<StandaloneBackpackFavoriteControl> FavoriteSlotControls =
            new List<StandaloneBackpackFavoriteControl>();
        public readonly Dictionary<string, float> RecentItemTimestamps = new Dictionary<string, float>();
        public readonly Dictionary<string, float> OpenItemQuantities = new Dictionary<string, float>();
        public bool RecentBaselineCaptured;
        public Sprite QualityStarSprite;
        public CanvasGroup VisualCanvasGroup;
        public CanvasGroup SettingsRootCanvasGroup;
        public CanvasGroup SettingsCardCanvasGroup;
        public CanvasGroup DropdownCanvasGroup;
        public RectTransform PageWipeRoot;
        public RectTransform PageWipeBlock;
        public Image PageWipeEdge;
        public bool VisualPresented;
        public bool PresentationRootRestCaptured;
        public Vector2 PresentationRootRestPosition;
        public bool PresentationRootRestScaleCaptured;
        public Vector3 PresentationRootRestScale;
        public bool SettingsClosing;
        public bool SettingsCardRestCaptured;
        public Vector2 SettingsCardRestPosition;
        public Color SearchBackgroundBaseColor;
        public int BackpackOpenMotionGeneration;
        public int SettingsMotionGeneration;
        public int DropdownMotionGeneration;
        public int SearchFocusMotionGeneration;
        public int TabMotionGeneration;
        public int PageWipeMotionGeneration;
        public int LastPresentedSettingsPage = -1;
        public int PendingPageWipeDirection;
        public RectTransform PagingRoot;
        public Button PrevButton;
        public Button NextButton;
        public Text PageLabel;
        public Action PrevAction;
        public Action NextAction;
        public int CurrentPage;
        public int LastPageInputFrame;
        public bool IsOpen;
        public StandaloneBackpackKeyboardFocusKind KeyboardFocusKind;
        public int KeyboardFocusControlIndex = -1;
        /// <summary>
        /// Supplies the inventory currently projected by this shared browser. It defaults to the
        /// backpack, but lets an embedded owner reuse the exact browser for game-owned storage.
        /// </summary>
        public Func<List<ItemSlot>> SlotProvider;
        public string DisplayTitle;
        public bool IsBackpackInventory;
        public bool IsHotkeyBackpack;
        public string SearchTerm;
        public string TypeFilter;
        public string QualityFilter;
        public StandaloneBackpackSortMode SortMode;
        public StandaloneBackpackSortDirection SortDirection;
    }

    /// <summary>
    /// Minimal adapter for a PackRat backpack browser surface. The hotkey UI and the embedded
    /// storage/station/deal views intentionally share this representation so their header,
    /// search, filters, sort tabs, favourite controls, paging, and slot projection cannot drift.
    /// </summary>
    private sealed class StandaloneBackpackSurface
    {
        public int Id;
        public RectTransform Container;
        public RectTransform SlotContainer;
        public GridLayoutGroup SlotGridLayout;
        public ItemSlotUI[] SlotUIs;
        public RectTransform CloseButtonContainer;
        public S1TMP TitleLabel;
        public S1TMP SubtitleLabel;
        public StandaloneBackpackLayoutView LayoutView;
        public bool PositionCloseControl;
        public Func<List<ItemSlot>> SlotProvider;
        public string DisplayTitle;
        /// <summary>Optional fixed grid capacity for an embedded alternate inventory.</summary>
        public int VisualSlotCapacity;
    }

    private const int StandaloneBackpackSlotsPerPage = 20;
    private const int StandaloneBackpackGridRows = 4;
    private const int SearchMetadataMinimumTermLength = 2;
    private const float StandaloneCardPadding = 14f;
    private const float StandaloneHeaderHeight = 138f;
    private const float StandaloneCloseGap = 24f;
    private const float StandaloneHeaderControlInset = 3f;
    private const float StandaloneHeaderSearchBottom = 30f;
    private const float StandaloneHeaderSearchTop = 54f;
    private const float MinimumOverlayScale = 0.5f;
    private const float MaximumOverlayScale = 1.5f;
    private const float OverlayScaleStep = 0.05f;
    private const float BackpackOpenDuration = 0.16f;
    private const float SettingsBlockerDuration = 0.12f;
    private const float SettingsCardDuration = 0.18f;
    private const float SettingsCloseDuration = 0.12f;
    private const float DropdownOpenDuration = 0.12f;
    private const float SearchFocusDuration = 0.10f;
    private const float TabIndicatorDuration = 0.12f;
    private const float PageWipeDuration = 0.14f;
    private const float MetricsTrayWidth = 190f;
    private const float MetricsTrayMotionDuration = 0.16f;
    private const int PillSpriteSize = 32;
    private const int PillSpriteBorder = 8;
    private const float PillSpriteCornerRadius = 7f;
    private const int DesktopTabSpriteSize = 32;
    private const int DesktopTabCornerRadius = 10;
    private const int MetricsTrayTabSpriteSize = 32;
    private const int MetricsTrayTabCornerRadius = 8;
    private const string SettingsCogResourceName = "PackRat.assets.settings-cog-ui.png";
    private const int StorageBackpackSlotsPerPage = 20;
    private const int StorageBackpackGridRows = 4;
    private const float CompactPanelMargin = 24f;
    private const float StorageBulkTransferPagerGap = 12f;
    private const float StorageBulkTransferCompactWidth = 132f;
    private const float StorageBulkTransferCompactHeight = 26f;
    private const float StorageBulkTransferExpandedHeight = 64f;
    private const float StorageBulkTransferMotionDuration = 0.14f;

    private static readonly Dictionary<int, BackpackPanelState> BackpackPanels = new Dictionary<int, BackpackPanelState>();
    private static readonly Dictionary<int, StorageMenuSlotCapacityState> StorageMenuSlotCapacities =
        new Dictionary<int, StorageMenuSlotCapacityState>();
    private static readonly Dictionary<int, StandaloneBackpackState> StandaloneBackpackPanels = new Dictionary<int, StandaloneBackpackState>();
    private static Sprite _settingsCogSprite;
    private static Texture2D _settingsCogTexture;
    private static bool _settingsCogLoadAttemptFailed;
    private static Sprite _pillButtonSprite;
    private static Texture2D _pillButtonTexture;
    private static Sprite _desktopTabSprite;
    private static Texture2D _desktopTabTexture;
    private static Sprite _metricsTrayTabSprite;
    private static Texture2D _metricsTrayTabTexture;
    private static readonly List<ItemSlot> ActiveInventorySlots = new List<ItemSlot>();
    private static readonly List<ItemSlot> ActiveStorageSlots = new List<ItemSlot>();
    private static readonly List<ItemSlot> ActiveBackpackSlots = new List<ItemSlot>();
    private static bool _quickMoveActive;
    private static bool _backpackQuickMoveEditSessionActive;

    [HarmonyPatch("Awake")]
    [HarmonyPostfix]
    public static void Awake(StorageMenu __instance)
    {
        if (__instance?.SlotsUIs == null || StorageMenuSlotCapacities.ContainsKey(__instance.GetInstanceID()))
            return;

        var nativeSlots = new ItemSlotUI[__instance.SlotsUIs.Length];
        for (var i = 0; i < nativeSlots.Length; i++)
            nativeSlots[i] = __instance.SlotsUIs[i];

        StorageMenuSlotCapacities[__instance.GetInstanceID()] = CaptureStorageMenuSlotCapacityState(__instance, nativeSlots);
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

        ApplyOpenedStorageMenu(__instance, owner);
    }

    /// <summary>
    /// Vehicle trunks and placed storage entities open through StorageMenu's dedicated
    /// StorageEntity overload, rather than the IItemSlotOwner overload used by the hotkey
    /// backpack. Keep both routes on the same post-open path so the shared browser is injected
    /// for every normal storage screen without touching its vanilla slot hierarchy.
    /// </summary>
    [HarmonyPatch("Open", [typeof(StorageEntity), typeof(S1Action)])]
    [HarmonyPostfix]
    public static void OpenStorageEntity(StorageMenu __instance, StorageEntity entity, S1Action onClosedCallback)
    {
        if (entity == null)
            return;

        RestoreStandaloneBackpackSlotCapacity(__instance);

        ModLogger.Info(
            $"[BackpackUI] StorageMenu storage-entity branch: entity='{entity.gameObject?.name}', " +
            $"slots={entity.ItemSlots?.Count ?? 0}."
        );
        ApplyBackpackSidePanel(__instance, entity);
    }

    /// <summary>
    /// Restores the native storage menu's item binding before placing the PackRat-owned browser
    /// alongside it. This is shared by the owner and storage-entity Open overloads.
    /// </summary>
    private static void ApplyOpenedStorageMenu(StorageMenu menu, IItemSlotOwner owner)
    {
        if (menu == null)
            return;

        RestoreStandaloneBackpackSlotCapacity(menu);
        RestoreStandaloneBackpackLabels(menu);

        if (owner != null)
        {
            for (var i = 0; i < menu.SlotsUIs.Length; i++)
            {
                var slotUi = menu.SlotsUIs[i];
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

        var spacing = menu.SlotGridLayout.cellSize.y + menu.SlotGridLayout.spacing.y;
        menu.CloseButtonContainer.anchoredPosition = new Vector2(
            0f,
            menu.SlotGridLayout.constraintCount * -spacing - menu.CloseButtonContainer.sizeDelta.y
        );

        if (menu.SlotGridLayout.constraintCount <= 4)
        {
            menu.Container.localPosition = Vector3.zero;
        }
        else
        {
            menu.Container.localPosition = new Vector3(
                0f,
                (menu.SlotGridLayout.constraintCount - 4) * spacing,
                0f
            );
        }

        ApplyBackpackSidePanel(menu, owner);
    }

    [HarmonyPatch("CloseMenu")]
    [HarmonyPrefix]
    public static void CloseMenu(StorageMenu __instance)
    {
        if (IsStandaloneBackpackOpen(__instance) || _backpackQuickMoveEditSessionActive)
        {
            RecordStandaloneRecentChanges(__instance);
            BackpackStateSyncManager.CompleteLocalBackpackEdit();
        }

        RestoreStandaloneBackpackSlotCapacity(__instance);

        HideBackpackSidePanel(__instance);
        HideStandaloneBackpackPaging(__instance);
        RestoreStandaloneBackpackLabels(__instance);
        __instance.Container.localPosition = Vector3.zero;
        _quickMoveActive = false;
        _backpackQuickMoveEditSessionActive = false;
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
            if (!SmartRoutingManager.IsEnabled)
            {
                AddQuickMoveTargets(sourceSlot, ActiveStorageSlots, targets);
                AddQuickMoveTargets(sourceSlot, ActiveBackpackSlots, targets);
            }
            else if (SmartRoutingManager.ShouldPreferBackpack(sourceSlot.ItemInstance))
                AddQuickMoveTargets(sourceSlot, ActiveBackpackSlots, targets);
            else
                AddQuickMoveTargets(sourceSlot, ActiveStorageSlots, targets);
        }
        else if (ActiveStorageSlots.Contains(sourceSlot))
        {
            if (!SmartRoutingManager.IsEnabled)
            {
                AddQuickMoveTargets(sourceSlot, ActiveInventorySlots, targets);
                AddQuickMoveTargets(sourceSlot, ActiveBackpackSlots, targets);
            }
            else if (SmartRoutingManager.ShouldPreferBackpack(sourceSlot.ItemInstance))
                AddQuickMoveTargets(sourceSlot, ActiveBackpackSlots, targets);
            else
                AddQuickMoveTargets(sourceSlot, ActiveInventorySlots, targets);
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

    private static void CaptureStandaloneQualityStarSprite(ItemSlotUI[] slotUis, StandaloneBackpackState state)
    {
        if (slotUis == null || state == null || state.QualityStarSprite != null)
            return;

        for (var i = 0; i < slotUis.Length; i++)
        {
            var itemUi = slotUis[i]?.ItemUI;
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
    /// Adds a small, PackRat-owned favorite toggle to a standalone backpack slot. It occupies
    /// only the top-right corner, leaving the game's item drag/drop surface untouched.
    /// </summary>
    private static void ConfigureStandaloneFavoriteControl(StandaloneBackpackState state,
        ItemSlotUI slotUi, ItemSlot slot)
    {
        if (state == null || slotUi == null)
            return;

        var definitionId = GetSlotDefinitionId(slot);
        var control = GetStandaloneFavoriteControl(state, slotUi);
        if (control == null)
        {
            control = CreateStandaloneFavoriteControl(state, slotUi);
            if (control == null)
                return;
        }

        control.BoundSlot = slot;
        control.Button.gameObject.SetActive(!string.IsNullOrWhiteSpace(definitionId));
        if (string.IsNullOrWhiteSpace(definitionId))
            return;

        var favorited = BackpackFavorites.IsFavorite(definitionId);
        // The Image remains the tiny button hit target, but must not introduce a square backing
        // behind the star. The favorite state is communicated entirely by the star color/fill.
        control.Background.color = Color.clear;
        control.Label.color = favorited
            ? new Color32(255, 201, 53, 255)
            : new Color32(146, 168, 181, 230);
        control.Label.text = favorited ? "★" : "☆";
    }

    private static StandaloneBackpackFavoriteControl GetStandaloneFavoriteControl(StandaloneBackpackState state,
        ItemSlotUI slotUi)
    {
        for (var i = state.FavoriteSlotControls.Count - 1; i >= 0; i--)
        {
            var control = state.FavoriteSlotControls[i];
            if (control == null || control.SlotUi == null || control.Button == null)
            {
                state.FavoriteSlotControls.RemoveAt(i);
                continue;
            }

            if (control.SlotUi == slotUi)
                return control;
        }

        return null;
    }

    private static StandaloneBackpackFavoriteControl CreateStandaloneFavoriteControl(StandaloneBackpackState state,
        ItemSlotUI slotUi)
    {
        var slotRect = Utils.GetComponentSafe<RectTransform>(slotUi.gameObject);
        if (slotRect == null)
            return null;

        var favoriteGo = new GameObject("PackRat_FavoriteToggle");
        var favoriteRect = favoriteGo.AddComponent<RectTransform>();
        favoriteRect.SetParent(slotRect, worldPositionStays: false);
        favoriteRect.anchorMin = new Vector2(1f, 1f);
        favoriteRect.anchorMax = new Vector2(1f, 1f);
        favoriteRect.pivot = new Vector2(1f, 1f);
        favoriteRect.anchoredPosition = new Vector2(-3f, -3f);
        favoriteRect.sizeDelta = new Vector2(17f, 17f);

        var background = favoriteGo.AddComponent<Image>();
        background.raycastTarget = true;
        var button = favoriteGo.AddComponent<Button>();
        button.targetGraphic = background;
        var label = CreateSearchText(favoriteRect, "Star", new Color32(146, 168, 181, 230));
        label.text = "☆";
        label.fontSize = 13;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;

        var control = new StandaloneBackpackFavoriteControl
        {
            SlotUi = slotUi,
            Button = button,
            Background = background,
            Label = label
        };
        control.ToggleAction = () =>
        {
            var definitionId = GetSlotDefinitionId(control.BoundSlot);
            if (string.IsNullOrWhiteSpace(definitionId))
                return;

            var isFavorite = BackpackFavorites.Toggle(definitionId);
            ModLogger.Info($"[BackpackUI] Favorite {(isFavorite ? "added" : "removed")}: {definitionId}.");
            if (state.IsOpen)
                state.RefreshAction?.Invoke();
        };
        EventHelper.AddListener(control.ToggleAction, button.onClick);
        state.FavoriteSlotControls.Add(control);
        return control;
    }

    private static void CaptureStandaloneRecentBaseline(StandaloneBackpackState state, List<ItemSlot> backpackSlots)
    {
        if (state == null || state.RecentBaselineCaptured)
            return;

        state.OpenItemQuantities.Clear();
        AddStandaloneItemQuantities(state.OpenItemQuantities, backpackSlots);
        state.RecentBaselineCaptured = true;
    }

    private static void RecordStandaloneRecentChanges(StorageMenu menu)
    {
        if (menu == null || !StandaloneBackpackPanels.TryGetValue(menu.GetInstanceID(), out var state) ||
            !state.RecentBaselineCaptured)
            return;

        UpdateStandaloneRecentChanges(state);
        state.OpenItemQuantities.Clear();
        state.RecentBaselineCaptured = false;
    }

    private static void UpdateStandaloneRecentChanges(StandaloneBackpackState state)
    {
        if (state == null || !state.RecentBaselineCaptured)
            return;

        var currentQuantities = new Dictionary<string, float>();
        AddStandaloneItemQuantities(currentQuantities, GetStandaloneSourceSlots(state));
        var quantitiesChanged = currentQuantities.Count != state.OpenItemQuantities.Count;
        foreach (var pair in currentQuantities)
        {
            state.OpenItemQuantities.TryGetValue(pair.Key, out var openingQuantity);
            if (!Mathf.Approximately(openingQuantity, pair.Value))
            {
                state.RecentItemTimestamps[pair.Key] = Time.unscaledTime;
                quantitiesChanged = true;
            }
        }

        state.OpenItemQuantities.Clear();
        foreach (var pair in currentQuantities)
            state.OpenItemQuantities[pair.Key] = pair.Value;

        // Drag/drop remains game-owned, but the tray reads the same backing slots. Refresh its
        // lightweight aggregate only when quantities actually change so it stays current without
        // rebuilding UI every frame.
        var shouldRefreshMetrics = quantitiesChanged || Time.unscaledTime >= state.NextMetricsTrayRefreshTime;
        if (shouldRefreshMetrics && state.IsHotkeyBackpack && state.MetricsTrayRoot != null)
        {
            state.NextMetricsTrayRefreshTime = Time.unscaledTime + 1f;
            RefreshStandaloneMetricsTray(state, GetStandaloneSourceSlots(state));
        }
    }

    private static void AddStandaloneItemQuantities(Dictionary<string, float> quantities, List<ItemSlot> slots)
    {
        if (quantities == null || slots == null)
            return;

        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var definitionId = GetSlotDefinitionId(slot);
            if (string.IsNullOrWhiteSpace(definitionId))
                continue;

            quantities.TryGetValue(definitionId, out var quantity);
            quantities[definitionId] = quantity + GetSlotQuantity(slot);
        }
    }

    /// <summary>
    /// Keeps the standalone backpack menu at a fixed, readable grid size. Large bags are paged
    /// instead of increasing the grid row count, which previously pushed the close button and
    /// content beyond smaller or scaled displays.
    /// </summary>
    private static void ApplyStandaloneBackpackMenu(StorageMenu menu)
    {
        if (menu == null || menu.SlotGridLayout == null || !EnsureStandaloneBackpackSlotCapacity(menu))
            return;

        ApplyStandaloneBackpackSurface(new StandaloneBackpackSurface
        {
            Id = menu.GetInstanceID(),
            Container = menu.Container,
            SlotContainer = menu.SlotContainer,
            SlotGridLayout = menu.SlotGridLayout,
            SlotUIs = menu.SlotsUIs,
            CloseButtonContainer = menu.CloseButtonContainer,
            TitleLabel = menu.TitleLabel,
            SubtitleLabel = menu.SubtitleLabel,
            LayoutView = StandaloneBackpackLayoutView.Backpack,
            PositionCloseControl = true
        });
    }

    /// <summary>
    /// Expands the shared StorageMenu only while the backpack hotkey is open. Larger bags page
    /// through a fixed projection, so twenty temporary views are sufficient and do not need to
    /// persist into vehicle trunks or other vanilla storage owners.
    /// </summary>
    private static bool EnsureStandaloneBackpackSlotCapacity(StorageMenu menu)
    {
        if (menu?.SlotsUIs == null || menu.SlotContainer == null)
            return false;

        var menuId = menu.GetInstanceID();
        if (!StorageMenuSlotCapacities.TryGetValue(menuId, out var state))
        {
            var nativeSlots = new ItemSlotUI[menu.SlotsUIs.Length];
            for (var i = 0; i < nativeSlots.Length; i++)
                nativeSlots[i] = menu.SlotsUIs[i];

            state = CaptureStorageMenuSlotCapacityState(menu, nativeSlots);
            StorageMenuSlotCapacities[menuId] = state;
        }

        var targetSlotCount = StandaloneBackpackSlotsPerPage;
        if (menu.SlotsUIs.Length >= targetSlotCount)
            return true;

        ItemSlotUI slotTemplate = null;
        for (var i = 0; i < menu.SlotsUIs.Length; i++)
        {
            if (menu.SlotsUIs[i] != null)
            {
                slotTemplate = menu.SlotsUIs[i];
                break;
            }
        }

        if (slotTemplate == null)
        {
            ModLogger.Warn("[BackpackUI] Standalone slot expansion skipped: no source ItemSlotUI was available.");
            return false;
        }

        var expandedSlots = new ItemSlotUI[targetSlotCount];
        for (var i = 0; i < menu.SlotsUIs.Length && i < expandedSlots.Length; i++)
            expandedSlots[i] = menu.SlotsUIs[i];

        for (var i = menu.SlotsUIs.Length; i < expandedSlots.Length; i++)
        {
            var slotObject = UnityEngine.Object.Instantiate(slotTemplate.gameObject, menu.SlotContainer);
            slotObject.name = $"PackRat_BackpackTemporarySlot ({i + 1})";
#if !MONO
            var slotUi = Utils.GetComponentSafe<ItemSlotUI>(slotObject);
#else
            var slotUi = slotObject.GetComponent<ItemSlotUI>();
#endif
            if (slotUi == null)
            {
                UnityEngine.Object.Destroy(slotObject);
                continue;
            }

            ResetSlotUi(slotUi);
            slotUi.ClearSlot();
            slotUi.gameObject.SetActive(false);
            state.AddedBackpackSlots.Add(slotUi);
            expandedSlots[i] = slotUi;
        }

        menu.SlotsUIs = expandedSlots;
        return true;
    }

    /// <summary>
    /// Returns the shared StorageMenu to its exact prefab slot array and destroys PackRat's
    /// temporary hotkey-only views. This must run before a trunk opens because the game reuses
    /// the same menu instance for every storage owner.
    /// </summary>
    private static void RestoreStandaloneBackpackSlotCapacity(StorageMenu menu)
    {
        if (menu == null || !StorageMenuSlotCapacities.TryGetValue(menu.GetInstanceID(), out var state))
            return;

        if (state.NativeSlots != null)
            menu.SlotsUIs = state.NativeSlots;

        RestoreNativeStorageMenuGeometry(menu, state);

        for (var i = 0; i < state.AddedBackpackSlots.Count; i++)
        {
            var slotUi = state.AddedBackpackSlots[i];
            if (slotUi == null)
                continue;

            ResetSlotUi(slotUi);
            UnityEngine.Object.Destroy(slotUi.gameObject);
        }

        state.AddedBackpackSlots.Clear();
    }

    private static StorageMenuSlotCapacityState CaptureStorageMenuSlotCapacityState(StorageMenu menu,
        ItemSlotUI[] nativeSlots)
    {
        var state = new StorageMenuSlotCapacityState
        {
            NativeSlots = nativeSlots,
            NativeContainerLocalPosition = menu?.Container != null ? menu.Container.localPosition : Vector3.zero,
            NativeCloseButtonPosition = menu?.CloseButtonContainer != null
                ? menu.CloseButtonContainer.anchoredPosition
                : Vector2.zero,
            NativeCloseButtonScale = menu?.CloseButtonContainer != null
                ? menu.CloseButtonContainer.localScale
                : Vector3.one
        };

        var slotContainer = menu?.SlotContainer;
        if (slotContainer != null)
        {
            state.NativeSlotAnchorMin = slotContainer.anchorMin;
            state.NativeSlotAnchorMax = slotContainer.anchorMax;
            state.NativeSlotPivot = slotContainer.pivot;
            state.NativeSlotAnchoredPosition = slotContainer.anchoredPosition;
            state.NativeSlotSizeDelta = slotContainer.sizeDelta;
            state.NativeSlotScale = slotContainer.localScale;
        }

        var grid = menu?.SlotGridLayout;
        if (grid != null)
        {
            state.NativeGridStartAxis = grid.startAxis;
            state.NativeGridConstraint = grid.constraint;
            state.NativeGridConstraintCount = grid.constraintCount;
            state.NativeGridAlignment = grid.childAlignment;
            state.NativeGridCellSize = grid.cellSize;
            state.NativeGridSpacing = grid.spacing;
            var padding = grid.padding;
            state.NativeGridPadding = padding == null
                ? new RectOffset()
                : new RectOffset(padding.left, padding.right, padding.top, padding.bottom);
        }

        return state;
    }

    private static void RestoreNativeStorageMenuGeometry(StorageMenu menu, StorageMenuSlotCapacityState state)
    {
        if (menu == null || state == null)
            return;

        if (menu.Container != null)
            menu.Container.localPosition = state.NativeContainerLocalPosition;
        if (menu.CloseButtonContainer != null)
        {
            menu.CloseButtonContainer.anchoredPosition = state.NativeCloseButtonPosition;
            menu.CloseButtonContainer.localScale = state.NativeCloseButtonScale;
        }

        var slotContainer = menu.SlotContainer;
        if (slotContainer != null)
        {
            slotContainer.anchorMin = state.NativeSlotAnchorMin;
            slotContainer.anchorMax = state.NativeSlotAnchorMax;
            slotContainer.pivot = state.NativeSlotPivot;
            slotContainer.sizeDelta = state.NativeSlotSizeDelta;
            slotContainer.anchoredPosition = state.NativeSlotAnchoredPosition;
            slotContainer.localScale = state.NativeSlotScale;
        }

        var grid = menu.SlotGridLayout;
        if (grid != null)
        {
            grid.startAxis = state.NativeGridStartAxis;
            grid.constraint = state.NativeGridConstraint;
            grid.constraintCount = state.NativeGridConstraintCount;
            grid.childAlignment = state.NativeGridAlignment;
            grid.cellSize = state.NativeGridCellSize;
            grid.spacing = state.NativeGridSpacing;
            var padding = state.NativeGridPadding;
            grid.padding = padding == null
                ? new RectOffset()
                : new RectOffset(padding.left, padding.right, padding.top, padding.bottom);
        }
    }

    /// <summary>
    /// Renders the complete main-backpack browser on any compatible slot surface. The caller
    /// supplies only game-owned slots and host transforms; every PackRat control is built from
    /// this shared path so embedded views remain functionally and visually identical to the
    /// backpack hotkey UI.
    /// </summary>
    private static void ApplyStandaloneBackpackSurface(StandaloneBackpackSurface surface)
    {
        if (surface?.SlotUIs == null || surface.SlotGridLayout == null || surface.SlotContainer == null ||
            surface.Container == null)
            return;

        var backpackSlots = GetSurfaceSlots(surface);
        var state = EnsureStandaloneBackpackPaging(surface);
        if (state == null)
            return;

        state.SlotProvider = surface.SlotProvider;
        state.DisplayTitle = surface.DisplayTitle;
        state.IsBackpackInventory = surface.SlotProvider == null;
        // Consolidation physically changes the PackRat inventory. Embedded panels intentionally
        // reuse the browser presentation, but must never expose a bulk action for another
        // storage owner.
        state.IsHotkeyBackpack = state.IsBackpackInventory && surface.PositionCloseControl &&
            surface.LayoutView == StandaloneBackpackLayoutView.Backpack;
        state.DoneButton = surface.PositionCloseControl && surface.CloseButtonContainer != null
            ? surface.CloseButtonContainer.GetComponentInChildren<Button>(includeInactive: true)
            : null;
        state.RefreshAction = () => ApplyStandaloneBackpackSurface(surface);
        state.IsOpen = true;
        CaptureStandaloneRecentBaseline(state, backpackSlots);
        var displaySlots = GetDisplayBackpackSlots(backpackSlots, state);
        var totalPages = Mathf.Max(1, Mathf.CeilToInt(displaySlots.Count / (float)StandaloneBackpackSlotsPerPage));
        state.CurrentPage = Mathf.Clamp(state.CurrentPage, 0, totalPages - 1);
        var firstSlotIndex = state.CurrentPage * StandaloneBackpackSlotsPerPage;
        var visibleSlotCount = Mathf.Clamp(displaySlots.Count - firstSlotIndex, 1, StandaloneBackpackSlotsPerPage);
        // The card represents the backpack's capacity, not the number of current search hits.
        // Keep its geometry fixed while a filter only changes the slots populated within it.
        var gridSlotCount = Mathf.Clamp(surface.VisualSlotCapacity > 0
            ? surface.VisualSlotCapacity
            : backpackSlots.Count, 1, StandaloneBackpackSlotsPerPage);
        var gridSize = ConfigureStandaloneBackpackGrid(surface, gridSlotCount);
        UpdateStandaloneBackpackPresentationAnchor(surface, state);
        EnsureStandaloneBackpackVisuals(surface, state, backpackSlots.Count, CountUsedStandaloneSlots(backpackSlots),
            displaySlots.Count, totalPages);
        var revealPageWipe = BeginStandalonePageWipe(surface, state);

        // First remove every previous layout child, then populate the compact projection in a
        // second pass. Updating active and inactive GridLayoutGroup children together leaves
        // stale positions behind for type/quality/sort projections on some game UI prefabs.
        for (var i = 0; i < surface.SlotUIs.Length; i++)
        {
            var slotUi = surface.SlotUIs[i];
            if (slotUi == null)
                continue;

            ResetSlotUi(slotUi);
            slotUi.ClearSlot();
            slotUi.gameObject.SetActive(false);
        }

        Canvas.ForceUpdateCanvases();

        for (var i = 0; i < surface.SlotUIs.Length; i++)
        {
            var slotUi = surface.SlotUIs[i];
            if (slotUi == null)
                continue;

            var slotIndex = firstSlotIndex + i;
            if (i < StandaloneBackpackSlotsPerPage && slotIndex < displaySlots.Count)
            {
                // Some game-owned ItemSlotUI prefabs only construct their visual children while
                // active. This is especially visible on the handover screen, whose source slots
                // are normally hidden by the game. Activate the clone before binding the
                // ItemSlot so the shared browser can render the same native slot surface on
                // every owner panel without disrupting the page-wipe overlay order.
                slotUi.gameObject.SetActive(true);
                slotUi.AssignSlot(displaySlots[slotIndex]);
                slotUi.gameObject.SetActive(true);
                ConfigureStandaloneFavoriteControl(state, slotUi, displaySlots[slotIndex]);
            }
        }

        CaptureStandaloneQualityStarSprite(surface.SlotUIs, state);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(surface.SlotContainer);

        if (surface.LayoutView == StandaloneBackpackLayoutView.Deal)
        {
            var activeSlotCount = 0;
            for (var i = 0; i < surface.SlotUIs.Length; i++)
            {
                if (surface.SlotUIs[i] != null && surface.SlotUIs[i].gameObject.activeInHierarchy)
                    activeSlotCount++;
            }

            ModLogger.Info(
                $"[BackpackUI] Deal browser slot bind: sourceSlots={surface.SlotUIs.Length}, " +
                $"backpackSlots={backpackSlots.Count}, displaySlots={displaySlots.Count}, activeSlots={activeSlotCount}."
            );
        }
        if (revealPageWipe)
            RevealStandalonePageWipe(state);

        surface.Container.localPosition = Vector3.zero;
        var backpackScale = GetStandaloneBackpackScale(surface.LayoutView);
        if (surface.PositionCloseControl && surface.CloseButtonContainer != null)
        {
            surface.CloseButtonContainer.localScale = Vector3.one * backpackScale;
            surface.CloseButtonContainer.anchoredPosition = new Vector2(
                surface.SlotContainer.anchoredPosition.x,
                surface.SlotContainer.anchoredPosition.y - (gridSize.y * backpackScale * 0.5f) -
                (surface.CloseButtonContainer.sizeDelta.y * backpackScale) -
                StandaloneCloseGap
            );
        }
        PositionStandalonePaging(surface, state, gridSize);
        UpdateStandalonePager(state, totalPages);
        RefreshStandaloneKeyboardFocusPresentation(state);

        ModLogger.Info(
            $"[BackpackUI] Standalone layout applied: capacitySlots={gridSlotCount}, visibleSlots={visibleSlotCount}, gridSize={gridSize}, " +
            $"gridPosition={surface.SlotContainer.anchoredPosition}, view={surface.LayoutView}."
        );
    }

    /// <summary>
    /// Applies the complete main backpack browser to an injected view. Callers retain ownership
    /// of their game screen, slots, close controls, and transfer mechanics; this method owns
    /// only PackRat's browser presentation and its filtered projection of backpack slots.
    /// </summary>
    internal static void ApplyEmbeddedBackpackBrowser(RectTransform hostRoot, RectTransform slotContainer,
        GridLayoutGroup slotGridLayout, ItemSlotUI[] slotUis, int layoutView)
    {
        if (hostRoot == null || slotContainer == null || slotGridLayout == null || slotUis == null)
            return;

        // GridLayoutGroup only controls direct children. Refuse to project foreign slot views
        // into this surface: in handover that would otherwise clear/rebind the live vehicle UI.
        var localSlotUis = new List<ItemSlotUI>();
        for (var i = 0; i < slotUis.Length; i++)
        {
            var slotUi = slotUis[i];
            if (slotUi != null && slotUi.transform.parent == slotContainer)
                localSlotUis.Add(slotUi);
        }

        if (localSlotUis.Count == 0)
        {
            ModLogger.Warn($"[BackpackUI] Embedded browser skipped: no direct slot views for {layoutView} surface.");
            return;
        }

        var requestedView = (StandaloneBackpackLayoutView)Mathf.Clamp(layoutView, 0, 3);
        ApplyStandaloneBackpackSurface(new StandaloneBackpackSurface
        {
            Id = hostRoot.GetInstanceID(),
            Container = hostRoot,
            SlotContainer = slotContainer,
            SlotGridLayout = slotGridLayout,
            SlotUIs = localSlotUis.ToArray(),
            LayoutView = requestedView,
            PositionCloseControl = false
        });
    }

    /// <summary>
    /// Projects another game-owned inventory through the same responsive browser used by the
    /// backpack. The caller supplies native slots, so vanilla drag/drop remains intact.
    /// </summary>
    internal static void ApplyEmbeddedInventoryBrowser(RectTransform hostRoot, RectTransform slotContainer,
        GridLayoutGroup slotGridLayout, ItemSlotUI[] slotUis, int layoutView, Func<List<ItemSlot>> slotProvider,
        string displayTitle)
    {
        if (hostRoot == null || slotContainer == null || slotGridLayout == null || slotUis == null ||
            slotProvider == null)
            return;

        var localSlotUis = new List<ItemSlotUI>();
        for (var i = 0; i < slotUis.Length; i++)
        {
            var slotUi = slotUis[i];
            if (slotUi != null && slotUi.transform.parent == slotContainer)
                localSlotUis.Add(slotUi);
        }

        if (localSlotUis.Count == 0)
        {
            ModLogger.Warn($"[BackpackUI] Embedded inventory browser skipped: no direct slot views for {layoutView} surface.");
            return;
        }

        var requestedView = (StandaloneBackpackLayoutView)Mathf.Clamp(layoutView, 0, 3);
        ApplyStandaloneBackpackSurface(new StandaloneBackpackSurface
        {
            Id = hostRoot.GetInstanceID(),
            Container = hostRoot,
            SlotContainer = slotContainer,
            SlotGridLayout = slotGridLayout,
            SlotUIs = localSlotUis.ToArray(),
            LayoutView = requestedView,
            PositionCloseControl = false,
            SlotProvider = slotProvider,
            DisplayTitle = displayTitle,
            VisualSlotCapacity = StandaloneBackpackSlotsPerPage
        });
    }

    private static List<ItemSlot> GetSurfaceSlots(StandaloneBackpackSurface surface)
    {
        try
        {
            return surface?.SlotProvider?.Invoke() ?? GetBackpackSlots();
        }
        catch (Exception ex)
        {
            ModLogger.Error("StorageMenuPatch.GetSurfaceSlots", ex);
            return new List<ItemSlot>();
        }
    }

    private static List<ItemSlot> GetStandaloneSourceSlots(StandaloneBackpackState state)
    {
        try
        {
            return state?.SlotProvider?.Invoke() ?? GetBackpackSlots();
        }
        catch (Exception ex)
        {
            ModLogger.Error("StorageMenuPatch.GetStandaloneSourceSlots", ex);
            return new List<ItemSlot>();
        }
    }

    /// <summary>
    /// Gives the hotkey-opened backpack a fixed four-row grid. Its anchor is derived from the
    /// surrounding card bounds, so the grid/header card is centered independently of the Done
    /// button and pagination controls that remain elsewhere in the menu hierarchy.
    /// </summary>
    private static Vector2 ConfigureStandaloneBackpackGrid(StandaloneBackpackSurface surface, int visibleSlotCount)
    {
        var grid = surface?.SlotGridLayout;
        var slotContainer = surface?.SlotContainer;
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
        var scale = GetStandaloneBackpackScale(surface.LayoutView);
        var cardAnchor = GetStandaloneBackpackCardAnchor(surface.LayoutView);
        if (surface.LayoutView != StandaloneBackpackLayoutView.Backpack)
            cardAnchor = ClampEmbeddedBrowserAnchor(surface, cardAnchor, gridSize, scale);
        slotContainer.anchoredPosition = cardAnchor;
        slotContainer.localScale = Vector3.one * scale;
        return gridSize;
    }

    /// <summary>
    /// Keeps the full browser card on-screen as the game UI or display resolution changes. The
    /// per-view offsets still select its intended workspace; clamping only protects the card
    /// from leaving that workspace on narrow or heavily scaled displays.
    /// </summary>
    private static Vector2 ClampEmbeddedBrowserAnchor(StandaloneBackpackSurface surface, Vector2 desired,
        Vector2 gridSize, float scale)
    {
        var host = surface?.Container;
        if (host == null || host.rect.width <= 0f || host.rect.height <= 0f)
            return desired;

        const float margin = 24f;
        var visualWidth = (gridSize.x + (StandaloneCardPadding * 2f)) * scale;
        var visualHeight = (gridSize.y + StandaloneHeaderHeight + (StandaloneCardPadding * 2f)) * scale;
        var halfWidth = Mathf.Max(0f, host.rect.width * 0.5f - visualWidth * 0.5f - margin);
        var halfHeight = Mathf.Max(0f, host.rect.height * 0.5f - visualHeight * 0.5f - margin);
        return new Vector2(
            Mathf.Clamp(desired.x, -halfWidth, halfWidth),
            Mathf.Clamp(desired.y, -halfHeight, halfHeight)
        );
    }

    /// <summary>
    /// Returns the slot-grid anchor that centers the visual card, not the complete storage-menu
    /// hierarchy. The card extends <see cref="StandaloneHeaderHeight"/> above the grid but uses
    /// equal padding above and below it, so its center is exactly half the header height above the
    /// grid center. Compensating for that geometry keeps the card centered at every grid size.
    /// </summary>
    private static Vector2 GetStandaloneBackpackCardAnchor(StandaloneBackpackLayoutView layoutView)
    {
        var scale = GetStandaloneBackpackScale(layoutView);
        var offsetX = GetStandaloneLayoutOffsetX(layoutView);
        var offsetY = GetStandaloneLayoutOffsetY(layoutView);
        return new Vector2(
            offsetX,
            offsetY - (StandaloneHeaderHeight * scale * 0.5f)
        );
    }

    private static float GetStandaloneBackpackScale(StandaloneBackpackLayoutView layoutView)
    {
        return Mathf.Clamp(GetStandaloneLayoutScale(layoutView), MinimumOverlayScale, MaximumOverlayScale);
    }

    /// <summary>
    /// Updates the presentation motion's resting position after a live layout adjustment. Without
    /// this, disabling motion or an in-flight open tween could restore the previous anchor.
    /// </summary>
    private static void UpdateStandaloneBackpackPresentationAnchor(StandaloneBackpackSurface surface,
        StandaloneBackpackState state)
    {
        if (surface?.SlotContainer == null || state == null)
            return;

        var root = surface.SlotContainer;
        var positionChanged = !state.PresentationRootRestCaptured ||
            (state.PresentationRootRestPosition - root.anchoredPosition).sqrMagnitude > 0.01f;
        var scaleChanged = !state.PresentationRootRestScaleCaptured ||
            (state.PresentationRootRestScale - root.localScale).sqrMagnitude > 0.0001f;

        state.PresentationRoot = root;
        state.PresentationRootRestPosition = root.anchoredPosition;
        state.PresentationRootRestCaptured = true;
        state.PresentationRootRestScale = root.localScale;
        state.PresentationRootRestScaleCaptured = true;

        if ((!positionChanged && !scaleChanged) || !state.VisualPresented)
            return;

        ++state.BackpackOpenMotionGeneration;
        if (state.VisualCanvasGroup != null)
            state.VisualCanvasGroup.alpha = 1f;
        root.localScale = state.PresentationRootRestScale;
    }

    private static void EnsureStandaloneBackpackVisuals(StandaloneBackpackSurface surface,
        StandaloneBackpackState state, int slotCount, int usedSlotCount, int filteredSlotCount, int totalPages)
    {
        if (surface?.SlotContainer == null || state == null)
            return;

        state.PresentationRoot = surface.SlotContainer;

        if (state.VisualRoot == null)
        {
            var visualGo = new GameObject("PackRat_BackpackVisual");
            var visualRoot = visualGo.AddComponent<RectTransform>();
            visualRoot.SetParent(surface.SlotContainer, worldPositionStays: false);
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
            state.HeaderAccent = accentImage;

            state.VisualTitleLabel = CreateBackpackVisualLabel(header, "Title", 18, FontStyle.Bold,
                TextAnchor.MiddleLeft, new Color32(244, 247, 250, 255), new Vector2(12f, 12f), new Vector2(-12f, -10f));
            state.VisualMetaLabel = CreateBackpackVisualLabel(header, "Meta", 11, FontStyle.Bold,
                TextAnchor.LowerLeft, new Color32(166, 205, 229, 255), new Vector2(12f, 5f), new Vector2(-12f, 6f));
            CreateStandaloneSearchInput(header, state);
            CreateStandaloneFilterControls(header, state);
            CreateStandaloneConsolidateButton(header, state);
            CreateStandaloneSettingsButton(header, state);
            CreateStandaloneDropdown(header, state);
        }

        if (state.VisualRoot.parent != surface.SlotContainer)
            state.VisualRoot.SetParent(surface.SlotContainer, worldPositionStays: false);

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
        CreateStandaloneSortTabs(state.HeaderRoot, state);
        EnsureStandaloneSlotsPanel(surface.SlotContainer, state);
        ConfigureStandaloneOverlayLayers(surface, state);
        CreateStandaloneSettingsButton(state.HeaderRoot, state);
        CreateStandaloneConsolidateButton(state.HeaderRoot, state);
        EnsureStandaloneSettingsPanel(surface, state);
        EnsureStandaloneMetricsTray(state, GetStandaloneSourceSlots(state));

        ConfigureStandaloneHeaderLabels(state);
        BindStandaloneSearchInput(state);
        BindStandaloneFilterControls(state);

        if (surface.TitleLabel != null)
            surface.TitleLabel.gameObject.SetActive(false);
        if (surface.SubtitleLabel != null)
            surface.SubtitleLabel.gameObject.SetActive(false);

        state.VisualRoot.gameObject.SetActive(true);
        PresentStandaloneBackpackVisual(state);
        var title = string.IsNullOrWhiteSpace(state.DisplayTitle)
            ? PlayerBackpack.Instance?.CurrentTier?.Name ?? PlayerBackpack.StorageName
            : state.DisplayTitle;
        if (state.VisualTitleLabel != null)
            state.VisualTitleLabel.text = title.ToUpperInvariant();
        if (state.VisualMetaLabel != null)
        {
            var filterActive = HasStandaloneFilters(state);
            var filterSummary = filterActive ? $" • {filteredSlotCount} MATCHES" : string.Empty;
            state.VisualMetaLabel.text =
                $"{usedSlotCount}/{slotCount} USED{filterSummary}  •  PAGE {state.CurrentPage + 1}/{Mathf.Max(1, totalPages)}";
        }

        ApplyStandaloneBackpackTheme(state);
    }

    /// <summary>
    /// Applies the selected preset only to PackRat-created roots. The native slot widgets remain
    /// siblings of these roots, so their game-owned quality, drag, and tooltip presentation is
    /// never recoloured by the mod.
    /// </summary>
    private static void ApplyStandaloneBackpackTheme(StandaloneBackpackState state)
    {
        if (state == null)
            return;

        var config = Configuration.Instance;
        var palette = BackpackUiThemes.Get(config.BackpackUiTheme, config.CustomBackpackUiPrimaryColor);
        var previousPalette = state.AppliedThemePaletteCaptured ? state.AppliedThemePalette : BackpackUiThemes.Get(BackpackUiTheme.S1Blue);
        ApplyStandaloneThemeToRoot(state.VisualRoot, palette, previousPalette);
        ApplyStandaloneThemeToRoot(state.SettingsRoot, palette, previousPalette);
        ApplyStandaloneThemeToRoot(state.DropdownRoot, palette, previousPalette);
        ApplyStandaloneThemeToRoot(state.PageWipeRoot, palette, previousPalette);

        if (state.SlotsPanelRoot != null)
        {
            var panel = state.SlotsPanelRoot.GetComponent<Image>();
            if (panel != null)
                panel.color = GetStandaloneThemeColor(panel.color, palette, previousPalette);
        }

        state.SearchBackgroundBaseColor = palette.Search;
        if (state.SearchBackground != null && (state.SearchInput == null || !state.SearchInput.isFocused))
            state.SearchBackground.color = palette.Search;
        state.AppliedTheme = config.BackpackUiTheme;
        state.AppliedThemePalette = palette;
        state.AppliedThemePaletteCaptured = true;
    }

    /// <summary>
    /// Repaints every currently open projection after a local theme preference changes. This is
    /// intentionally presentation-only; the state and inventory ownership stay untouched.
    /// </summary>
    public static void RefreshActiveUiThemes()
    {
        try
        {
            foreach (var state in StandaloneBackpackPanels.Values)
            {
                if (state?.VisualRoot == null)
                    continue;
                ApplyStandaloneBackpackTheme(state);
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("StorageMenuPatch.RefreshActiveUiThemes", ex);
        }
    }

    private static void ApplyStandaloneThemeToRoot(RectTransform root, BackpackUiThemePalette palette,
        BackpackUiThemePalette previousPalette)
    {
        if (root == null)
            return;

        var graphics = root.GetComponentsInChildren<Graphic>(includeInactive: true);
        for (var i = 0; i < graphics.Length; i++)
        {
            if (graphics[i] != null)
                graphics[i].color = GetStandaloneThemeColor(graphics[i].color, palette, previousPalette);
        }

        var selectables = root.GetComponentsInChildren<Selectable>(includeInactive: true);
        for (var i = 0; i < selectables.Length; i++)
        {
            var selectable = selectables[i];
            if (selectable == null || selectable.transition != Selectable.Transition.ColorTint)
                continue;

            var colors = selectable.colors;
            colors.normalColor = GetStandaloneThemeColor(colors.normalColor, palette, previousPalette);
            colors.highlightedColor = GetStandaloneThemeColor(colors.highlightedColor, palette, previousPalette);
            colors.pressedColor = GetStandaloneThemeColor(colors.pressedColor, palette, previousPalette);
            colors.selectedColor = GetStandaloneThemeColor(colors.selectedColor, palette, previousPalette);
            colors.disabledColor = GetStandaloneThemeColor(colors.disabledColor, palette, previousPalette);
            selectable.colors = colors;
        }
    }

    private static Color GetStandaloneThemeColor(Color source, BackpackUiThemePalette target,
        BackpackUiThemePalette previousPalette)
    {
        var source32 = NormalizeStandaloneThemeColor((Color32)source, previousPalette);
        var r = source32.r;
        var g = source32.g;
        var b = source32.b;
        var a = source32.a;
        if (MatchesStandaloneThemeColor(r, g, b, 15, 21, 28) || MatchesStandaloneThemeColor(r, g, b, 11, 20, 29))
            return WithStandaloneThemeAlpha(target.Card, a);
        if (MatchesStandaloneThemeColor(r, g, b, 35, 61, 86))
            return WithStandaloneThemeAlpha(target.Header, a);
        if (MatchesStandaloneThemeColor(r, g, b, 76, 173, 229) || MatchesStandaloneThemeColor(r, g, b, 109, 205, 251))
            return WithStandaloneThemeAlpha(target.Accent, a);
        if (MatchesStandaloneThemeColor(r, g, b, 18, 30, 40) || MatchesStandaloneThemeColor(r, g, b, 25, 43, 57) ||
            MatchesStandaloneThemeColor(r, g, b, 24, 43, 57))
            return WithStandaloneThemeAlpha(target.Control, a);
        if (MatchesStandaloneThemeColor(r, g, b, 20, 35, 47) || MatchesStandaloneThemeColor(r, g, b, 18, 36, 49) ||
            MatchesStandaloneThemeColor(r, g, b, 20, 33, 44) || MatchesStandaloneThemeColor(r, g, b, 35, 65, 84))
            return WithStandaloneThemeAlpha(target.ControlAlt, a);
        if (MatchesStandaloneThemeColor(r, g, b, 48, 128, 170) || MatchesStandaloneThemeColor(r, g, b, 45, 109, 146) ||
            MatchesStandaloneThemeColor(r, g, b, 40, 121, 157) || MatchesStandaloneThemeColor(r, g, b, 64, 153, 196) ||
            MatchesStandaloneThemeColor(r, g, b, 36, 103, 137))
            return WithStandaloneThemeAlpha(target.SelectedControl, a);
        if (MatchesStandaloneThemeColor(r, g, b, 10, 15, 20) || MatchesStandaloneThemeColor(r, g, b, 12, 21, 30))
            return WithStandaloneThemeAlpha(target.Search, a);
        if (MatchesStandaloneThemeColor(r, g, b, 24, 74, 102))
            return WithStandaloneThemeAlpha(target.SearchFocused, a);
        if (MatchesStandaloneThemeColor(r, g, b, 10, 23, 31) || MatchesStandaloneThemeColor(r, g, b, 10, 24, 33))
            return WithStandaloneThemeAlpha(target.ModalCard, a);
        if (MatchesStandaloneThemeColor(r, g, b, 16, 32, 43))
            return WithStandaloneThemeAlpha(target.ModalContent, a);
        if (MatchesStandaloneThemeColor(r, g, b, 9, 19, 27))
            return WithStandaloneThemeAlpha(target.Drawer, a);
        if (MatchesStandaloneThemeColor(r, g, b, 23, 42, 56))
            return WithStandaloneThemeAlpha(target.DrawerRow, a);
        if (MatchesStandaloneThemeColor(r, g, b, 244, 247, 250) || MatchesStandaloneThemeColor(r, g, b, 242, 247, 251) ||
            MatchesStandaloneThemeColor(r, g, b, 245, 248, 251) || MatchesStandaloneThemeColor(r, g, b, 237, 245, 250))
            return WithStandaloneThemeAlpha(target.PrimaryText, a);
        if (MatchesStandaloneThemeColor(r, g, b, 166, 205, 229) || MatchesStandaloneThemeColor(r, g, b, 190, 221, 241) ||
            MatchesStandaloneThemeColor(r, g, b, 190, 212, 225) || MatchesStandaloneThemeColor(r, g, b, 188, 216, 235) ||
            MatchesStandaloneThemeColor(r, g, b, 176, 210, 231) || MatchesStandaloneThemeColor(r, g, b, 144, 167, 181) ||
            MatchesStandaloneThemeColor(r, g, b, 144, 171, 188) || MatchesStandaloneThemeColor(r, g, b, 141, 196, 226) ||
            MatchesStandaloneThemeColor(r, g, b, 135, 191, 222) || MatchesStandaloneThemeColor(r, g, b, 217, 236, 248) ||
            MatchesStandaloneThemeColor(r, g, b, 223, 239, 248))
            return WithStandaloneThemeAlpha(target.SecondaryText, a);
        return source;
    }

    private static Color32 NormalizeStandaloneThemeColor(Color32 source, BackpackUiThemePalette previousPalette)
    {
        var defaultPalette = BackpackUiThemes.Get(BackpackUiTheme.S1Blue);
        if (TryNormalizeStandaloneThemeColor(source, previousPalette, defaultPalette, out var normalized))
            return normalized;

        foreach (BackpackUiTheme theme in Enum.GetValues(typeof(BackpackUiTheme)))
        {
            var palette = BackpackUiThemes.Get(theme);
            if (TryNormalizeStandaloneThemeColor(source, palette, defaultPalette, out normalized))
                return normalized;
        }
        return source;
    }

    private static bool TryNormalizeStandaloneThemeColor(Color32 source, BackpackUiThemePalette palette,
        BackpackUiThemePalette defaultPalette, out Color32 normalized)
    {
        if (SameStandaloneThemeColor(source, palette.Card)) { normalized = (Color32)defaultPalette.Card; return true; }
        if (SameStandaloneThemeColor(source, palette.Header)) { normalized = (Color32)defaultPalette.Header; return true; }
        if (SameStandaloneThemeColor(source, palette.Accent)) { normalized = (Color32)defaultPalette.Accent; return true; }
        if (SameStandaloneThemeColor(source, palette.Control)) { normalized = (Color32)defaultPalette.Control; return true; }
        if (SameStandaloneThemeColor(source, palette.ControlAlt)) { normalized = (Color32)defaultPalette.ControlAlt; return true; }
        if (SameStandaloneThemeColor(source, palette.SelectedControl)) { normalized = (Color32)defaultPalette.SelectedControl; return true; }
        if (SameStandaloneThemeColor(source, palette.Search)) { normalized = (Color32)defaultPalette.Search; return true; }
        if (SameStandaloneThemeColor(source, palette.SearchFocused)) { normalized = (Color32)defaultPalette.SearchFocused; return true; }
        if (SameStandaloneThemeColor(source, palette.ModalCard)) { normalized = (Color32)defaultPalette.ModalCard; return true; }
        if (SameStandaloneThemeColor(source, palette.ModalContent)) { normalized = (Color32)defaultPalette.ModalContent; return true; }
        if (SameStandaloneThemeColor(source, palette.Drawer)) { normalized = (Color32)defaultPalette.Drawer; return true; }
        if (SameStandaloneThemeColor(source, palette.DrawerRow)) { normalized = (Color32)defaultPalette.DrawerRow; return true; }
        if (SameStandaloneThemeColor(source, palette.PrimaryText)) { normalized = (Color32)defaultPalette.PrimaryText; return true; }
        if (SameStandaloneThemeColor(source, palette.SecondaryText)) { normalized = (Color32)defaultPalette.SecondaryText; return true; }
        normalized = source;
        return false;
    }

    private static bool SameStandaloneThemeColor(Color32 source, Color expected)
    {
        var expected32 = (Color32)expected;
        return source.r == expected32.r && source.g == expected32.g && source.b == expected32.b;
    }

    private static bool MatchesStandaloneThemeColor(byte r, byte g, byte b, byte expectedR, byte expectedG, byte expectedB)
    {
        return r == expectedR && g == expectedG && b == expectedB;
    }

    private static Color WithStandaloneThemeAlpha(Color color, byte alpha)
    {
        color.a = alpha / 255f;
        return color;
    }

    /// <summary>
    /// Builds the hotkey-only product tray as a child of the card background. Anchoring against
    /// that background means the tray automatically follows the grid's responsive scale and its
    /// visual height, while its content expands outward into the unused left-side space.
    /// </summary>
    private static void EnsureStandaloneMetricsTray(StandaloneBackpackState state, List<ItemSlot> backpackSlots)
    {
        if (state?.VisualRoot == null)
            return;

        var shouldShow = state.IsHotkeyBackpack && Configuration.Instance.ShowMetricsTray;
        if (!shouldShow)
        {
            if (state.MetricsTrayRoot != null)
                state.MetricsTrayRoot.gameObject.SetActive(false);
            if (state.MetricsTrayToggleButton != null)
                state.MetricsTrayToggleButton.gameObject.SetActive(false);
            state.MetricsTrayExpanded = false;
            return;
        }

        if (state.MetricsTrayRoot == null)
            CreateStandaloneMetricsTray(state);

        if (state.MetricsTrayRoot == null || state.MetricsTrayToggleButton == null)
            return;

        state.MetricsTrayRoot.gameObject.SetActive(true);
        state.MetricsTrayToggleButton.gameObject.SetActive(true);
        RefreshStandaloneMetricsTray(state, backpackSlots);
        if (!state.MetricsTrayExpanded)
            SnapStandaloneMetricsTray(state);
    }

    private static void CreateStandaloneMetricsTray(StandaloneBackpackState state)
    {
        var visualRoot = state?.VisualRoot;
        if (visualRoot == null)
            return;

        var trayGo = new GameObject("PackRat_BackpackMetricsTray");
        var tray = trayGo.AddComponent<RectTransform>();
        tray.SetParent(visualRoot, worldPositionStays: false);
        tray.anchorMin = new Vector2(0f, 0f);
        tray.anchorMax = new Vector2(0f, 1f);
        tray.pivot = new Vector2(1f, 0.5f);
        // Keep the extension mechanically joined to the card; the edge-to-edge seam makes it
        // read as an opened backpack compartment rather than another floating modal.
        tray.anchoredPosition = Vector2.zero;
        tray.sizeDelta = new Vector2(0f, 0f);
        var trayImage = trayGo.AddComponent<Image>();
        trayImage.color = Color.clear;
        trayImage.raycastTarget = false;
        trayGo.AddComponent<RectMask2D>();
        state.MetricsTrayRoot = tray;
        state.MetricsTrayCanvasGroup = Utils.GetOrAddComponentSafe<CanvasGroup>(trayGo);

        var panelGo = new GameObject("Panel");
        var panel = panelGo.AddComponent<RectTransform>();
        panel.SetParent(tray, worldPositionStays: false);
        panel.anchorMin = new Vector2(1f, 0f);
        panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 0.5f);
        panel.sizeDelta = new Vector2(MetricsTrayWidth, 0f);
        panel.anchoredPosition = Vector2.zero;
        var panelImage = panelGo.AddComponent<Image>();
        panelImage.color = new Color32(9, 19, 27, 252);
        panelImage.raycastTarget = true;
        state.MetricsTrayPanel = panel;

        var title = CreateSearchText(panel, "Title", new Color32(217, 236, 248, 255));
        title.text = "PRODUCT METRICS";
        title.fontSize = 9;
        title.fontStyle = FontStyle.Bold;
        title.alignment = TextAnchor.MiddleLeft;
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(10f, -28f);
        titleRect.offsetMax = new Vector2(-10f, -7f);

        state.MetricsTraySummary = CreateSearchText(panel, "Summary", new Color32(135, 191, 222, 255));
        state.MetricsTraySummary.fontSize = 7;
        state.MetricsTraySummary.fontStyle = FontStyle.Bold;
        state.MetricsTraySummary.alignment = TextAnchor.MiddleLeft;
        var summaryRect = state.MetricsTraySummary.GetComponent<RectTransform>();
        summaryRect.anchorMin = new Vector2(0f, 0f);
        summaryRect.anchorMax = new Vector2(1f, 0f);
        summaryRect.pivot = new Vector2(0.5f, 0f);
        summaryRect.offsetMin = new Vector2(10f, 7f);
        summaryRect.offsetMax = new Vector2(-10f, 27f);

        var scrollGo = new GameObject("Scroll");
        var scrollRect = scrollGo.AddComponent<RectTransform>();
        scrollRect.SetParent(panel, worldPositionStays: false);
        scrollRect.anchorMin = new Vector2(0f, 0f);
        scrollRect.anchorMax = new Vector2(1f, 1f);
        scrollRect.offsetMin = new Vector2(8f, 31f);
        scrollRect.offsetMax = new Vector2(-8f, -32f);
        var scrollImage = scrollGo.AddComponent<Image>();
        scrollImage.color = Color.clear;
        scrollImage.raycastTarget = true;
        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 18f;

        var viewportGo = new GameObject("Viewport");
        var viewport = viewportGo.AddComponent<RectTransform>();
        viewport.SetParent(scrollRect, worldPositionStays: false);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        var viewportImage = viewportGo.AddComponent<Image>();
        viewportImage.color = Color.clear;
        viewportImage.raycastTarget = true;
        viewportGo.AddComponent<RectMask2D>();

        var contentGo = new GameObject("Content");
        var content = contentGo.AddComponent<RectTransform>();
        content.SetParent(viewport, worldPositionStays: false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        scroll.viewport = viewport;
        scroll.content = content;
        state.MetricsTrayContent = content;

        var toggle = CreateStandaloneActionButton(visualRoot, "MetricsTrayToggle",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(-28f, -24f), new Vector2(0f, 24f),
            "<<", 7, out state.MetricsTrayToggleLabel);
        state.MetricsTrayToggleButton = toggle;
        ApplyMetricsTrayTabPresentation(toggle.targetGraphic as Image);
        // The tray owns this control's colour and geometry so the default Button tint cannot
        // make its side tab look like a vertically-focused desktop tab.
        toggle.transition = Selectable.Transition.None;
        EventHelper.AddListener(() => ToggleStandaloneMetricsTray(state), toggle.onClick);
    }

    private static void ToggleStandaloneMetricsTray(StandaloneBackpackState state)
    {
        if (state?.MetricsTrayRoot == null || !Configuration.Instance.ShowMetricsTray)
            return;

        state.MetricsTrayExpanded = !state.MetricsTrayExpanded;
        if (state.MetricsTrayExpanded)
            RefreshStandaloneMetricsTray(state, GetStandaloneSourceSlots(state));
        PlayStandaloneMetricsTrayMotion(state);
    }

    private static void PlayStandaloneMetricsTrayMotion(StandaloneBackpackState state)
    {
        if (state?.MetricsTrayRoot == null)
            return;

        var generation = ++state.MetricsTrayMotionGeneration;
        if (!Configuration.Instance.EnableUiAnimations || Configuration.Instance.ReduceUiMotion)
        {
            SnapStandaloneMetricsTray(state);
            return;
        }

        var startWidth = state.MetricsTrayRoot.sizeDelta.x;
        var startAlpha = state.MetricsTrayCanvasGroup?.alpha ?? 1f;
        var targetWidth = state.MetricsTrayExpanded ? MetricsTrayWidth : 0f;
        var targetAlpha = state.MetricsTrayExpanded ? 1f : 0f;
        MelonCoroutines.Start(RunStandaloneMetricsTrayMotion(state, generation, startWidth, targetWidth, startAlpha,
            targetAlpha));
    }

    private static IEnumerator RunStandaloneMetricsTrayMotion(StandaloneBackpackState state, int generation,
        float startWidth, float targetWidth, float startAlpha, float targetAlpha)
    {
        var elapsed = 0f;
        while (state != null && state.MetricsTrayMotionGeneration == generation && elapsed < MetricsTrayMotionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            var t = EaseOutCubic(Mathf.Clamp01(elapsed / MetricsTrayMotionDuration));
            if (state.MetricsTrayRoot != null)
                state.MetricsTrayRoot.sizeDelta = new Vector2(Mathf.Lerp(startWidth, targetWidth, t), 0f);
            PositionStandaloneMetricsTrayToggle(state, state.MetricsTrayRoot?.sizeDelta.x ?? targetWidth);
            if (state.MetricsTrayCanvasGroup != null)
                state.MetricsTrayCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            yield return null;
        }

        if (state != null && state.MetricsTrayMotionGeneration == generation)
            SnapStandaloneMetricsTray(state);
    }

    private static void SnapStandaloneMetricsTray(StandaloneBackpackState state)
    {
        if (state?.MetricsTrayRoot == null)
            return;

        var expanded = state.MetricsTrayExpanded && Configuration.Instance.ShowMetricsTray;
        state.MetricsTrayRoot.sizeDelta = new Vector2(expanded ? MetricsTrayWidth : 0f, 0f);
        PositionStandaloneMetricsTrayToggle(state, state.MetricsTrayRoot.sizeDelta.x);
        if (state.MetricsTrayCanvasGroup != null)
        {
            state.MetricsTrayCanvasGroup.alpha = expanded ? 1f : 0f;
            state.MetricsTrayCanvasGroup.blocksRaycasts = expanded;
        }
        if (state.MetricsTrayToggleLabel != null)
            state.MetricsTrayToggleLabel.text = expanded ? ">>" : "<<";
        var toggleImage = state.MetricsTrayToggleButton?.targetGraphic as Image;
        if (toggleImage != null)
            toggleImage.color = expanded
                ? new Color32(48, 128, 170, 255)
                : new Color32(20, 35, 47, 255);
    }

    /// <summary>
    /// Keeps the side tab attached to the drawer's exposed edge. The drawer itself needs a
    /// RectMask2D for the wipe animation, so the control remains a sibling and follows the
    /// drawer's measured width instead of being clipped as a child of that mask.
    /// </summary>
    private static void PositionStandaloneMetricsTrayToggle(StandaloneBackpackState state, float trayWidth)
    {
        var toggleRect = state?.MetricsTrayToggleButton?.GetComponent<RectTransform>();
        if (toggleRect == null)
            return;

        trayWidth = Mathf.Max(0f, trayWidth);
        toggleRect.offsetMin = new Vector2(-trayWidth - 28f, -24f);
        toggleRect.offsetMax = new Vector2(-trayWidth, 24f);
    }

    private static void RefreshStandaloneMetricsTray(StandaloneBackpackState state, List<ItemSlot> backpackSlots)
    {
        if (state?.MetricsTrayContent == null)
            return;

        var metrics = GetStandaloneBackpackProductMetrics(backpackSlots);
        var fingerprint = BuildStandaloneMetricsFingerprint(metrics);
        if (string.Equals(state.MetricsTrayFingerprint, fingerprint, StringComparison.Ordinal))
            return;

        state.MetricsTrayFingerprint = fingerprint;
        for (var i = 0; i < state.MetricsTrayRows.Count; i++)
        {
            if (state.MetricsTrayRows[i] != null)
                UnityEngine.Object.Destroy(state.MetricsTrayRows[i]);
        }
        state.MetricsTrayRows.Clear();

        for (var i = 0; i < metrics.Count; i++)
            CreateStandaloneMetricsTrayRow(state, metrics[i]);

        if (metrics.Count == 0)
        {
            var empty = CreateSearchText(state.MetricsTrayContent, "Empty", new Color32(144, 171, 188, 255));
            empty.text = "NO PRODUCTS IN BACKPACK";
            empty.fontSize = 7;
            empty.fontStyle = FontStyle.Bold;
            empty.alignment = TextAnchor.MiddleCenter;
            var emptyRect = empty.GetComponent<RectTransform>();
            emptyRect.anchorMin = new Vector2(0f, 1f);
            emptyRect.anchorMax = new Vector2(1f, 1f);
            emptyRect.pivot = new Vector2(0.5f, 1f);
            emptyRect.offsetMin = new Vector2(2f, -30f);
            emptyRect.offsetMax = new Vector2(-2f, -2f);
            state.MetricsTrayRows.Add(empty.gameObject);
        }

        if (state.MetricsTraySummary != null)
        {
            var config = Configuration.Instance;
            var summaryParts = new List<string> { metrics.Count + " TYPES" };
            if (config.ShowProductQuantityTotalMetric)
                summaryParts.Add("QTY " + metrics.Sum(metric => metric.Quantity));
            if (config.ShowProductTotalPriceMetric)
            {
                var totalPrice = metrics.Where(metric => metric.HasUnitPrice)
                    .Sum(metric => metric.UnitPrice * metric.Quantity);
                summaryParts.Add("VALUE " + FormatStandaloneProductPrice(totalPrice));
            }
            state.MetricsTraySummary.text = string.Join("  •  ", summaryParts);
        }

        // Avoid ContentSizeFitter here. It is rebuilt by the host game's menu while this
        // injected card is animating, which can collapse the viewport to zero. Explicit row
        // geometry keeps the content scrollable and visible on every scale.
        state.MetricsTrayContent.sizeDelta = new Vector2(0f, Mathf.Max(1f, 2f + (state.MetricsTrayRows.Count * 40f)));
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(state.MetricsTrayContent);
    }

    private static List<StandaloneBackpackProductMetric> GetStandaloneBackpackProductMetrics(List<ItemSlot> slots)
    {
        var metricsById = new Dictionary<string, StandaloneBackpackProductMetric>(StringComparer.OrdinalIgnoreCase);
        if (slots == null)
            return new List<StandaloneBackpackProductMetric>();

        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var item = slot?.ItemInstance;
            if (!IsProductItemInstance(item))
                continue;

            var id = GetSlotDefinitionId(slot);
            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (!metricsById.TryGetValue(id, out var metric))
            {
                metric = new StandaloneBackpackProductMetric
                {
                    Id = id,
                    Name = GetSlotName(slot),
                    Definition = item.Definition,
                    HasUnitPrice = TryGetStandaloneProductUnitPrice(item, out var unitPrice),
                    UnitPrice = unitPrice
                };
                metricsById[id] = metric;
            }

            metric.Quantity += GetWholeStandaloneSlotQuantity(slot);
        }

        var metrics = metricsById.Values
            .OrderBy(metric => metric.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        for (var index = 0; index < metrics.Count; index++)
            metrics[index].ActiveOrderQuantity = GetStandaloneActiveOrderQuantity(metrics[index]);

        return metrics;
    }

    private static bool TryGetStandaloneProductUnitPrice(ItemInstance item, out float unitPrice)
    {
        unitPrice = 0f;
        var definition = item?.Definition;
        if (definition == null)
            return false;

        try
        {
            // Use the game's typed APIs first. IL2CPP wrappers do not always expose inherited
            // ProductDefinition members through ordinary reflection, which was leaving the tray
            // with a false zero even though the item itself was a valid product.
#if MONO
            var productDefinition = definition as ProductDefinition;
            var productItem = item as ProductItemInstance;
#else
            var productDefinition = definition.TryCast<ProductDefinition>();
            var productItem = item.TryCast<ProductItemInstance>();
#endif
            if (productDefinition != null)
            {
                var livePrice = Mathf.Max(0f, productDefinition.Price);
                if (livePrice > 0f)
                {
                    unitPrice = livePrice;
                    return true;
                }

                var definitionMarketValue = Mathf.Max(0f, productDefinition.MarketValue);
                if (definitionMarketValue > 0f)
                {
                    unitPrice = definitionMarketValue;
                    return true;
                }

                var basePrice = Mathf.Max(0f, productDefinition.BasePrice);
                if (basePrice > 0f)
                {
                    unitPrice = basePrice;
                    return true;
                }
            }

            // This is the exact value the game assigns to the stack. It remains a useful last
            // typed fallback for future product subclasses that customise their definition.
            if (productItem != null)
            {
                var monetaryValue = Mathf.Max(0f, productItem.GetMonetaryValue());
                var stackQuantityValue = ReflectionUtils.TryGetFieldOrProperty(productItem, "Quantity");
                var stackQuantity = stackQuantityValue != null ? Mathf.Max(1f, Convert.ToSingle(stackQuantityValue)) : 1f;
                if (monetaryValue > 0f)
                {
                    unitPrice = monetaryValue / stackQuantity;
                    return unitPrice > 0f;
                }
            }

            // Retain field/property discovery for modded definition types which may not inherit
            // the base product definition directly.
            var price = ReflectionUtils.TryGetFieldOrProperty(definition, "Price");
            if (price != null)
            {
                unitPrice = Mathf.Max(0f, Convert.ToSingle(price));
                if (unitPrice > 0f)
                    return true;
            }

            // ProductManager can briefly return zero while generated products are registering.
            // MarketValue is the product definition's persistent fallback used by the game's
            // monetary-value calculation, so the tray remains useful during that window.
            var marketValue = ReflectionUtils.TryGetFieldOrProperty(definition, "MarketValue")
                ?? ReflectionUtils.TryGetFieldOrProperty(definition, "BasePrice");
            if (marketValue == null)
                return false;

            unitPrice = Mathf.Max(0f, Convert.ToSingle(marketValue));
            return unitPrice > 0f;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the outstanding quantity across the player's active contracts for this product.
    /// Marijuana mixes additionally match their shared base strain, so a Granddaddy Purple
    /// order remains visible beside a Granddaddy Purple product with added effects.
    /// </summary>
    private static int GetStandaloneActiveOrderQuantity(StandaloneBackpackProductMetric metric)
    {
        if (metric == null)
            return 0;

        var metricProductIds = GetStandaloneProductIdentityIds(metric.Definition, metric.Id);
        if (metricProductIds.Count == 0)
            return 0;

        try
        {
            object contracts = S1Contract.Contracts;
            var contractCount = ReflectionUtils.TryGetListCount(contracts);
            var outstandingQuantity = 0;
            for (var contractIndex = 0; contractIndex < contractCount; contractIndex++)
            {
                var contract = ReflectionUtils.TryGetListItem(contracts, contractIndex);
                if (contract == null || !string.Equals(ReflectionUtils.TryGetFieldOrProperty(contract, "State")?.ToString(),
                        "Active", StringComparison.OrdinalIgnoreCase))
                    continue;

                var productList = ReflectionUtils.TryGetFieldOrProperty(contract, "ProductList");
                var entries = ReflectionUtils.TryGetFieldOrProperty(productList, "entries")
                    ?? ReflectionUtils.TryGetFieldOrProperty(productList, "Entries");
                var entryCount = ReflectionUtils.TryGetListCount(entries);
                for (var entryIndex = 0; entryIndex < entryCount; entryIndex++)
                {
                    var entry = ReflectionUtils.TryGetListItem(entries, entryIndex);
                    var productId = ReflectionUtils.TryGetFieldOrProperty(entry, "ProductID")?.ToString();
                    if (string.IsNullOrWhiteSpace(productId) || !TryGetStandaloneMetricQuantity(
                            ReflectionUtils.TryGetFieldOrProperty(entry, "Quantity"), out var quantity) || quantity <= 0)
                        continue;

                    var orderedDefinition = GetStandaloneRegisteredItemDefinition(productId);
                    var orderProductIds = GetStandaloneProductIdentityIds(orderedDefinition, productId);
                    if (metricProductIds.Overlaps(orderProductIds))
                        outstandingQuantity += quantity;
                }
            }

            return outstandingQuantity;
        }
        catch (Exception ex)
        {
            ModLogger.Debug("Unable to read active product orders for metrics tray: " + ex.Message);
            return 0;
        }
    }

    private static HashSet<string> GetStandaloneProductIdentityIds(object definition, string fallbackId)
    {
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddStandaloneProductIdentity(identities, fallbackId);
        AddStandaloneProductIdentity(identities, GetReflectedDefinitionId(definition));
        if (!IsMarijuanaProductDefinition(definition))
            return identities;

        var baseStrains = new Dictionary<string, WeedStrainOption>(StringComparer.OrdinalIgnoreCase);
        ResolveWeedBaseStrains(definition, new HashSet<string>(StringComparer.OrdinalIgnoreCase), baseStrains);
        foreach (var strain in baseStrains.Values)
            AddStandaloneProductIdentity(identities, strain?.Id);
        return identities;
    }

    private static void AddStandaloneProductIdentity(ISet<string> identities, string value)
    {
        if (identities == null || string.IsNullOrWhiteSpace(value))
            return;

        var buffer = new char[value.Length];
        var count = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsLetterOrDigit(value[index]))
                continue;

            buffer[count++] = char.ToUpperInvariant(value[index]);
        }

        if (count > 0)
            identities.Add(new string(buffer, 0, count));
    }

    private static object GetStandaloneRegisteredItemDefinition(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return null;

        try
        {
            return Registry.ItemExists(itemId) ? Registry.GetItem(itemId) : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetStandaloneMetricQuantity(object value, out int quantity)
    {
        quantity = 0;
        if (value == null)
            return false;

        try
        {
            quantity = Convert.ToInt32(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildStandaloneMetricsFingerprint(List<StandaloneBackpackProductMetric> metrics)
    {
        var config = Configuration.Instance;
        var rows = metrics?.Select(metric => metric.Id + ":" + metric.Quantity + ":" + metric.UnitPrice.ToString("0.###") +
                ":" + metric.ActiveOrderQuantity)
            ?? Enumerable.Empty<string>();
        return string.Join("|", rows) + ";" + config.ShowProductQuantityMetric + ";" +
            config.ShowProductQuantityTotalMetric + ";" + config.ShowProductUnitPriceMetric + ";" +
            config.ShowProductTotalPriceMetric;
    }

    private static void CreateStandaloneMetricsTrayRow(StandaloneBackpackState state,
        StandaloneBackpackProductMetric metric)
    {
        if (state?.MetricsTrayContent == null || metric == null)
            return;

        var rowGo = new GameObject("ProductMetricRow");
        var row = rowGo.AddComponent<RectTransform>();
        row.SetParent(state.MetricsTrayContent, worldPositionStays: false);
        var rowIndex = state.MetricsTrayRows.Count;
        var rowTop = 2f + (rowIndex * 40f);
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.offsetMin = new Vector2(1f, -rowTop - 36f);
        row.offsetMax = new Vector2(-1f, -rowTop);
        var background = rowGo.AddComponent<Image>();
        background.color = new Color32(23, 42, 56, 238);
        background.raycastTarget = false;
        ApplyRoundedButtonPresentation(background);

        var name = CreateSearchText(row, "Name", new Color32(237, 245, 250, 255));
        name.text = string.IsNullOrWhiteSpace(metric.Name) ? metric.Id : metric.Name.ToUpperInvariant();
        name.fontSize = 7;
        name.fontStyle = FontStyle.Bold;
        name.alignment = TextAnchor.MiddleLeft;
        var nameRect = name.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0f, 0.5f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.offsetMin = new Vector2(7f, 0f);
        nameRect.offsetMax = new Vector2(-7f, -2f);

        var config = Configuration.Instance;
        var primaryDetails = new List<string>();
        var secondaryDetails = new List<string>();
        if (config.ShowProductQuantityMetric)
            primaryDetails.Add("QTY " + metric.Quantity);
        if (config.ShowProductUnitPriceMetric && metric.HasUnitPrice)
            primaryDetails.Add("EA " + FormatStandaloneProductPrice(metric.UnitPrice));
        if (config.ShowProductTotalPriceMetric && metric.HasUnitPrice)
            secondaryDetails.Add("TOTAL " + FormatStandaloneProductPrice(metric.UnitPrice * metric.Quantity));
        if (metric.ActiveOrderQuantity > 0)
            secondaryDetails.Add("ORDERS " + metric.ActiveOrderQuantity);

        var detail = CreateSearchText(row, "Details", new Color32(141, 196, 226, 255));
        var firstLine = primaryDetails.Count == 0 ? "PRODUCT" : string.Join("  •  ", primaryDetails);
        detail.text = secondaryDetails.Count == 0 ? firstLine : firstLine + "\n" + string.Join("  •  ", secondaryDetails);
        detail.fontSize = 6;
        detail.fontStyle = FontStyle.Bold;
        detail.alignment = TextAnchor.MiddleLeft;
        var detailRect = detail.GetComponent<RectTransform>();
        detailRect.anchorMin = new Vector2(0f, 0f);
        detailRect.anchorMax = new Vector2(1f, 0.5f);
        detailRect.offsetMin = new Vector2(7f, 1f);
        detailRect.offsetMax = new Vector2(-7f, 0f);
        state.MetricsTrayRows.Add(rowGo);
    }

    private static string FormatStandaloneProductPrice(float price)
    {
        return "$" + Mathf.Max(0f, price).ToString("0");
    }

    /// <summary>
    /// Plays only presentation motion on PackRat-owned roots. The storage menu and its inventory
    /// slots remain fully game-owned, so a close path never waits on this cosmetic transition.
    /// </summary>
    private static void PresentStandaloneBackpackVisual(StandaloneBackpackState state)
    {
        var root = state?.PresentationRoot ?? state?.VisualRoot;
        if (root == null)
            return;

        state.VisualCanvasGroup ??= Utils.GetOrAddComponentSafe<CanvasGroup>(root.gameObject);
        if (state.VisualCanvasGroup == null)
            return;

        if (state.VisualPresented)
        {
            if (!Configuration.Instance.EnableUiAnimations)
                SnapStandaloneBackpackVisual(state);
            return;
        }

        state.VisualPresented = true;
        var generation = ++state.BackpackOpenMotionGeneration;
        if (!Configuration.Instance.EnableUiAnimations)
        {
            SnapStandaloneBackpackVisual(state);
            return;
        }

        state.PresentationRootRestPosition = root.anchoredPosition;
        state.PresentationRootRestCaptured = true;
        state.PresentationRootRestScale = root.localScale;
        state.PresentationRootRestScaleCaptured = true;
        var restingPosition = state.PresentationRootRestPosition;
        var restingScale = state.PresentationRootRestScale;
        state.VisualCanvasGroup.alpha = 0f;
        root.localScale = Configuration.Instance.ReduceUiMotion ? restingScale : GetStandaloneOpenMotionScale(restingScale);
        root.anchoredPosition = Configuration.Instance.ReduceUiMotion
            ? restingPosition
            : restingPosition + new Vector2(0f, -8f);
        MelonCoroutines.Start(RunStandaloneBackpackOpenMotion(state, generation, restingPosition, restingScale));
    }

    private static IEnumerator RunStandaloneBackpackOpenMotion(StandaloneBackpackState state, int generation,
        Vector2 restingPosition, Vector3 restingScale)
    {
        var elapsed = 0f;
        while (state != null && state.BackpackOpenMotionGeneration == generation && elapsed < BackpackOpenDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            var t = EaseOutCubic(Mathf.Clamp01(elapsed / BackpackOpenDuration));
            if (state.VisualCanvasGroup != null)
                state.VisualCanvasGroup.alpha = t;
            var root = state.PresentationRoot ?? state.VisualRoot;
            if (root != null && !Configuration.Instance.ReduceUiMotion)
            {
                root.localScale = Vector3.Lerp(GetStandaloneOpenMotionScale(restingScale), restingScale, t);
                root.anchoredPosition = Vector2.Lerp(restingPosition + new Vector2(0f, -8f), restingPosition, t);
            }
            yield return null;
        }

        if (state != null && state.BackpackOpenMotionGeneration == generation)
            SnapStandaloneBackpackVisual(state);
    }

    private static void SnapStandaloneBackpackVisual(StandaloneBackpackState state)
    {
        var root = state?.PresentationRoot ?? state?.VisualRoot;
        if (root == null)
            return;

        if (state.VisualCanvasGroup != null)
            state.VisualCanvasGroup.alpha = 1f;
        root.localScale = state.PresentationRootRestScaleCaptured
            ? state.PresentationRootRestScale
            : Vector3.one;
        if (state.PresentationRootRestCaptured)
            root.anchoredPosition = state.PresentationRootRestPosition;
    }

    private static Vector3 GetStandaloneOpenMotionScale(Vector3 restingScale)
    {
        return new Vector3(restingScale.x * 0.96f, restingScale.y * 0.96f, restingScale.z);
    }

    private static void PlayStandaloneSearchFocus(StandaloneBackpackState state, bool focused)
    {
        if (state?.SearchBackground == null)
            return;

        var target = focused ? (Color)new Color32(24, 74, 102, 250) : state.SearchBackgroundBaseColor;
        var generation = ++state.SearchFocusMotionGeneration;
        if (!Configuration.Instance.EnableUiAnimations)
        {
            state.SearchBackground.color = target;
            return;
        }

        MelonCoroutines.Start(RunSearchFocusMotion(state, generation, state.SearchBackground.color, target));
    }

    private static IEnumerator RunSearchFocusMotion(StandaloneBackpackState state, int generation, Color from, Color to)
    {
        var elapsed = 0f;
        while (state != null && state.SearchFocusMotionGeneration == generation && elapsed < SearchFocusDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (state.SearchBackground != null)
                state.SearchBackground.color = Color.Lerp(from, to, EaseOutCubic(Mathf.Clamp01(elapsed / SearchFocusDuration)));
            yield return null;
        }

        if (state != null && state.SearchFocusMotionGeneration == generation && state.SearchBackground != null)
            state.SearchBackground.color = to;
    }

    private static bool BeginStandalonePageWipe(StandaloneBackpackSurface surface, StandaloneBackpackState state)
    {
        if (state == null || state.PendingPageWipeDirection == 0)
        {
            HideStandalonePageWipe(state);
            return false;
        }

        if (!Configuration.Instance.EnableUiAnimations || Configuration.Instance.ReduceUiMotion ||
            surface?.SlotContainer == null)
        {
            state.PendingPageWipeDirection = 0;
            HideStandalonePageWipe(state);
            return false;
        }

        EnsureStandalonePageWipe(surface, state);
        if (state.PageWipeRoot == null || state.PageWipeBlock == null)
        {
            state.PendingPageWipeDirection = 0;
            return false;
        }

        Canvas.ForceUpdateCanvases();
        var size = surface.SlotContainer.rect.size;
        if (size.x <= 0f || size.y <= 0f)
        {
            state.PendingPageWipeDirection = 0;
            return false;
        }

        state.PageWipeRoot.gameObject.SetActive(true);
        state.PageWipeRoot.SetAsLastSibling();
        state.PageWipeBlock.sizeDelta = size;
        state.PageWipeBlock.anchoredPosition = Vector2.zero;
        ConfigureStandalonePageWipeEdge(state, state.PendingPageWipeDirection);
        ++state.PageWipeMotionGeneration;
        return true;
    }

    private static void EnsureStandalonePageWipe(StandaloneBackpackSurface surface, StandaloneBackpackState state)
    {
        if (surface?.SlotContainer == null || state == null || state.PageWipeRoot != null)
            return;

        var rootGo = new GameObject("PackRat_BackpackPageWipe");
        var root = rootGo.AddComponent<RectTransform>();
        root.SetParent(surface.SlotContainer, worldPositionStays: false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
        rootGo.AddComponent<RectMask2D>();
        var layoutElement = rootGo.AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = true;

        var wipeGo = new GameObject("WipeBlock");
        var wipe = wipeGo.AddComponent<RectTransform>();
        wipe.SetParent(root, worldPositionStays: false);
        wipe.anchorMin = new Vector2(0.5f, 0.5f);
        wipe.anchorMax = new Vector2(0.5f, 0.5f);
        wipe.pivot = new Vector2(0.5f, 0.5f);
        var wipeImage = wipeGo.AddComponent<Image>();
        wipeImage.color = new Color32(15, 21, 28, 252);
        wipeImage.raycastTarget = false;

        var edgeGo = new GameObject("LeadingEdge");
        var edge = edgeGo.AddComponent<RectTransform>();
        edge.SetParent(wipe, worldPositionStays: false);
        var edgeImage = edgeGo.AddComponent<Image>();
        edgeImage.color = new Color32(76, 173, 229, 245);
        edgeImage.raycastTarget = false;

        state.PageWipeRoot = root;
        state.PageWipeBlock = wipe;
        state.PageWipeEdge = edgeImage;
        root.gameObject.SetActive(false);
    }

    private static void ConfigureStandalonePageWipeEdge(StandaloneBackpackState state, int direction)
    {
        if (state?.PageWipeEdge == null)
            return;

        var edge = state.PageWipeEdge.GetComponent<RectTransform>();
        if (edge == null)
            return;

        var next = direction > 0;
        edge.anchorMin = next ? new Vector2(1f, 0f) : Vector2.zero;
        edge.anchorMax = next ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        edge.pivot = next ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
        edge.anchoredPosition = Vector2.zero;
        edge.sizeDelta = new Vector2(3f, 0f);
    }

    private static void RevealStandalonePageWipe(StandaloneBackpackState state)
    {
        if (state?.PageWipeRoot == null || state.PageWipeBlock == null)
            return;

        var direction = state.PendingPageWipeDirection;
        state.PendingPageWipeDirection = 0;
        if (direction == 0 || !Configuration.Instance.EnableUiAnimations || Configuration.Instance.ReduceUiMotion)
        {
            HideStandalonePageWipe(state);
            return;
        }

        var width = Mathf.Max(1f, state.PageWipeRoot.rect.width);
        var generation = state.PageWipeMotionGeneration;
        MelonCoroutines.Start(RunStandalonePageWipe(state, generation, direction > 0 ? -width : width));
    }

    private static IEnumerator RunStandalonePageWipe(StandaloneBackpackState state, int generation, float targetX)
    {
        var elapsed = 0f;
        while (state != null && state.PageWipeMotionGeneration == generation && elapsed < PageWipeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (state.PageWipeBlock != null)
                state.PageWipeBlock.anchoredPosition = new Vector2(
                    Mathf.Lerp(0f, targetX, EaseOutCubic(Mathf.Clamp01(elapsed / PageWipeDuration))), 0f);
            yield return null;
        }

        if (state != null && state.PageWipeMotionGeneration == generation)
            HideStandalonePageWipe(state);
    }

    private static void HideStandalonePageWipe(StandaloneBackpackState state)
    {
        if (state == null)
            return;

        ++state.PageWipeMotionGeneration;
        state.PendingPageWipeDirection = 0;
        if (state.PageWipeRoot != null)
            state.PageWipeRoot.gameObject.SetActive(false);
    }

    private static float EaseOutCubic(float value)
    {
        var inverse = 1f - Mathf.Clamp01(value);
        return 1f - inverse * inverse * inverse;
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

    private static void CreateStandaloneSearchInput(RectTransform header, StandaloneBackpackState state)
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
        state.SearchBackground = background;
        state.SearchBackgroundBaseColor = background.color;
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

    private static void BindStandaloneSearchInput(StandaloneBackpackState state)
    {
        if (state?.SearchInput == null)
            return;

        if (state.SearchAction == null)
        {
            state.SearchAction = value =>
            {
                state.SearchTerm = value ?? string.Empty;
                state.CurrentPage = 0;
                if (state.IsOpen)
                    state.RefreshAction?.Invoke();
            };
        }

        if (!state.SearchListenerBound)
        {
            EventHelper.AddListener<string>(state.SearchAction, state.SearchInput.onValueChanged);
            state.SearchListenerBound = true;
        }
        state.SearchInput.SetTextWithoutNotify(state.SearchTerm ?? string.Empty);
    }

    private static void CreateStandaloneFilterControls(RectTransform header, StandaloneBackpackState state)
    {
        if (header == null || state == null || state.TypeFilterButton != null)
            return;

        state.TypeFilterButton = CreateStandaloneHeaderButton(header, "TypeFilter", 0f, 0.2f, out state.TypeFilterLabel);
        state.QualityFilterButton = CreateStandaloneHeaderButton(header, "QualityFilter", 0.2f, 0.4f,
            out state.QualityFilterLabel);
        state.SortDirectionButton = CreateStandaloneHeaderButton(header, "SortDirection", 0.4f, 0.6f,
            out state.SortDirectionLabel);
        state.OrganizeButton = CreateStandaloneHeaderButton(header, "Organize", 0.6f, 0.8f, out state.OrganizeLabel);
        state.ClearFiltersButton = CreateStandaloneHeaderButton(header, "Clear", 0.8f, 1f, out state.ClearFiltersLabel);
    }

    /// <summary>
    /// Keeps the header rail responsive when the shared browser is projecting an alternate
    /// inventory. Only the PackRat backpack can be physically reorganized, so the vehicle view
    /// uses four equal controls while the backpack gets a fifth Organize control.
    /// </summary>
    private static void ConfigureStandaloneHeaderControls(StandaloneBackpackState state)
    {
        if (state == null)
            return;

        var showOrganize = state.IsBackpackInventory;
        if (state.OrganizeButton != null)
            state.OrganizeButton.gameObject.SetActive(showOrganize);

        var buttonCount = showOrganize ? 5 : 4;
        var nextIndex = 0;
        SetStandaloneHeaderButtonBounds(state.TypeFilterButton, nextIndex++, buttonCount);
        SetStandaloneHeaderButtonBounds(state.QualityFilterButton, nextIndex++, buttonCount);
        SetStandaloneHeaderButtonBounds(state.SortDirectionButton, nextIndex++, buttonCount);
        if (showOrganize)
            SetStandaloneHeaderButtonBounds(state.OrganizeButton, nextIndex++, buttonCount);
        SetStandaloneHeaderButtonBounds(state.ClearFiltersButton, nextIndex, buttonCount);
    }

    private static void SetStandaloneHeaderButtonBounds(Button button, int index, int buttonCount)
    {
        if (button == null || buttonCount <= 0)
            return;

        var rect = button.GetComponent<RectTransform>();
        if (rect == null)
            return;

        var minX = index / (float)buttonCount;
        var maxX = (index + 1) / (float)buttonCount;
        rect.anchorMin = new Vector2(minX, 0f);
        rect.anchorMax = new Vector2(maxX, 0f);
        rect.offsetMin = new Vector2(StandaloneHeaderControlInset, 59f);
        rect.offsetMax = new Vector2(-StandaloneHeaderControlInset, 80f);
    }

    private static Button CreateStandaloneHeaderButton(RectTransform parent, string name, float minX, float maxX, out Text label)
    {
        var buttonGo = new GameObject("PackRat_Backpack" + name + "Button");
        var rect = buttonGo.AddComponent<RectTransform>();
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = new Vector2(minX, 0f);
        rect.anchorMax = new Vector2(maxX, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.offsetMin = new Vector2(StandaloneHeaderControlInset, 59f);
        rect.offsetMax = new Vector2(-StandaloneHeaderControlInset, 80f);

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

    /// <summary>
    /// Adds the available sort modes as direct desktop-style tabs. This replaces the old sort
    /// dropdown so players can see the current ordering and switch in one click.
    /// </summary>
    private static void CreateStandaloneSortTabs(RectTransform header, StandaloneBackpackState state)
    {
        if (header == null || state == null)
            return;

        if (state.SortTabsRoot == null)
        {
            var rootGo = new GameObject("SortTabs");
            var root = rootGo.AddComponent<RectTransform>();
            root.SetParent(header, worldPositionStays: false);
            root.anchorMin = new Vector2(0f, 0f);
            root.anchorMax = new Vector2(1f, 0f);
            root.pivot = new Vector2(0.5f, 0f);
            // The tab faces stop at the accent's upper edge (three logical pixels above the
            // header baseline). This makes the accent a single shared rule beneath the tabs,
            // rather than a separately positioned screen-space element.
            root.offsetMin = new Vector2(StandaloneHeaderControlInset, 3f);
            root.offsetMax = new Vector2(-StandaloneHeaderControlInset, 27f);
            state.SortTabsRoot = root;
        }
        else if (state.SortTabsRoot.parent != header)
        {
            state.SortTabsRoot.SetParent(header, worldPositionStays: false);
        }

        var layout = Utils.GetOrAddComponentSafe<HorizontalLayoutGroup>(state.SortTabsRoot.gameObject);
        if (layout != null)
        {
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.LowerCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
        }

        if (state.SortTabs.Count == 0)
        {
            AddStandaloneSortTab(state, StandaloneBackpackSortMode.SlotOrder);
            AddStandaloneSortTab(state, StandaloneBackpackSortMode.Favorites);
            AddStandaloneSortTab(state, StandaloneBackpackSortMode.Name);
            AddStandaloneSortTab(state, StandaloneBackpackSortMode.Quantity);
            AddStandaloneSortTab(state, StandaloneBackpackSortMode.Quality);
            AddStandaloneSortTab(state, StandaloneBackpackSortMode.Type);
            AddStandaloneSortTab(state, StandaloneBackpackSortMode.Recent);
        }

        UpdateStandaloneSortTabs(state);
        state.SortTabsRoot.SetAsLastSibling();
    }

    private static void AddStandaloneSortTab(StandaloneBackpackState state, StandaloneBackpackSortMode sortMode)
    {
        var tab = new StandaloneBackpackSortTab { SortMode = sortMode };
        tab.Button = CreateStandaloneActionButton(state.SortTabsRoot, "Sort" + GetSortModeLabel(sortMode),
            Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, GetSortModeLabel(sortMode), 8, out tab.Label);
        ApplyDesktopTabPresentation(tab.Button.targetGraphic as Image);
        // Button's built-in ColorTint was restoring every tab to its original black image color
        // after UpdateStandaloneSortTab applied the selected state. The tab strip owns its visual
        // state directly, so leave click handling enabled but disable that competing tint.
        tab.Button.transition = Selectable.Transition.None;
        tab.Label.fontSize = 8;
        tab.Label.fontStyle = FontStyle.Bold;
        tab.Label.alignment = TextAnchor.MiddleCenter;
        var layoutElement = tab.Button.gameObject.AddComponent<LayoutElement>();
        layoutElement.minHeight = 24f;
        layoutElement.preferredHeight = 24f;
        layoutElement.flexibleWidth = 1f;
        tab.SelectAction = () => SetStandaloneSortMode(state, sortMode);
        EventHelper.AddListener(tab.SelectAction, tab.Button.onClick);
        state.SortTabs.Add(tab);
    }

    /// <summary>
    /// Adds a dark surface behind the game-owned slots. The only active-color boundary is the
    /// shared header accent, avoiding a competing outline around the grid.
    /// </summary>
    private static void EnsureStandaloneSlotsPanel(RectTransform slotContainer, StandaloneBackpackState state)
    {
        if (slotContainer == null || state == null)
            return;

        if (state.SlotsPanelRoot == null)
        {
            var rootGo = new GameObject("SlotsPanel");
            var root = rootGo.AddComponent<RectTransform>();
            root.SetParent(slotContainer, worldPositionStays: false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            rootGo.AddComponent<LayoutElement>().ignoreLayout = true;
            var panel = rootGo.AddComponent<Image>();
            panel.color = new Color32(10, 24, 33, 248);
            panel.raycastTarget = false;
            ApplyRoundedButtonPresentation(panel);
            state.SlotsPanelRoot = root;
        }
        else if (state.SlotsPanelRoot.parent != slotContainer)
        {
            state.SlotsPanelRoot.SetParent(slotContainer, worldPositionStays: false);
        }

        // VisualRoot remains the first child and owns the dark card surface. The panel is just
        // above it but below the game's live ItemSlotUI instances.
        state.SlotsPanelRoot.SetSiblingIndex(Mathf.Min(1, slotContainer.childCount - 1));
    }

    private static void UpdateStandaloneSortTabs(StandaloneBackpackState state)
    {
        if (state == null)
            return;

        var selectedColor = new Color32(48, 128, 170, 255);
        for (var i = 0; i < state.SortTabs.Count; i++)
        {
            var tab = state.SortTabs[i];
            if (tab?.Button == null)
                continue;

            var selected = tab.SortMode == state.SortMode;
            UpdateStandaloneSortTab(tab.Button, selected);
            if (tab.Label != null)
                tab.Label.color = selected ? Color.white : new Color32(190, 212, 225, 255);
        }
        if (state.HeaderAccent != null)
            state.HeaderAccent.color = selectedColor;
    }

    private static void UpdateStandaloneSortTab(Button button, bool selected)
    {
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

        var image = button.targetGraphic as Image;
        if (image != null)
            image.color = selected ? selectedColor : normalColor;
        // The HorizontalLayoutGroup owns visual order and position. Unlike the overlapping
        // settings tabs, these tabs must keep their semantic left-to-right order when selected.
    }

    private static void BindStandaloneFilterControls(StandaloneBackpackState state)
    {
        if (state == null)
            return;

        if (state.TypeFilterAction == null)
            state.TypeFilterAction = () =>
            {
                ShowStandaloneDropdown(state, StandaloneBackpackDropdown.Type);
            };
        if (state.QualityFilterAction == null)
            state.QualityFilterAction = () =>
            {
                ShowStandaloneDropdown(state, StandaloneBackpackDropdown.Quality);
            };
        if (state.SortDirectionAction == null)
            state.SortDirectionAction = () =>
            {
                ShowStandaloneDropdown(state, StandaloneBackpackDropdown.SortDirection);
            };
        if (state.OrganizeAction == null)
            state.OrganizeAction = () => OrganizeStandaloneBackpack(state);
        if (state.ConsolidateAction == null)
            state.ConsolidateAction = () => ConsolidateStandaloneBackpack(state);
        if (state.ClearFiltersAction == null)
            state.ClearFiltersAction = () =>
            {
                state.SearchTerm = string.Empty;
                state.TypeFilter = string.Empty;
                state.QualityFilter = string.Empty;
                state.SortMode = StandaloneBackpackSortMode.SlotOrder;
                state.SortDirection = StandaloneBackpackSortDirection.Ascending;
                state.SearchInput?.SetTextWithoutNotify(string.Empty);
                HideStandaloneDropdown(state);
                RefreshStandaloneFilterView(state);
            };

        // The game may rebuild or reactivate this owner panel while the backpack remains open.
        // Rebind the permanent actions each refresh so controls cannot retain a stale listener;
        // RemoveListener keeps this idempotent rather than stacking callbacks on each layout pass.
        RebindHeaderButton(state.TypeFilterButton, state.TypeFilterAction);
        RebindHeaderButton(state.QualityFilterButton, state.QualityFilterAction);
        RebindHeaderButton(state.SortDirectionButton, state.SortDirectionAction);
        RebindHeaderButton(state.OrganizeButton, state.OrganizeAction);
        RebindHeaderButton(state.ConsolidateButton, state.ConsolidateAction);
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

    private static void RefreshStandaloneFilterView(StandaloneBackpackState state)
    {
        if (state == null)
            return;

        state.CurrentPage = 0;
        if (state.IsOpen)
            state.RefreshAction?.Invoke();
    }

    private static void UpdateStandaloneFilterLabels(StandaloneBackpackState state)
    {
        if (state == null)
            return;

        var backpackSlots = GetStandaloneSourceSlots(state);
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
        if (state.SortDirectionLabel != null)
            state.SortDirectionLabel.text = state.SortDirection == StandaloneBackpackSortDirection.Ascending
                ? "ORDER: ASC"
                : "ORDER: DESC";
        if (state.ClearFiltersLabel != null)
            state.ClearFiltersLabel.text = "CLEAR";
        if (state.OrganizeLabel != null)
            state.OrganizeLabel.text = "ORGANIZE";
        if (state.ConsolidateLabel != null)
            state.ConsolidateLabel.text = "STACK";

        if (state.TypeFilterButton != null)
            state.TypeFilterButton.interactable = typeOptions.Count > 0;
        if (state.QualityFilterButton != null)
            state.QualityFilterButton.interactable = qualityOptions.Count > 0;
        if (state.OrganizeButton != null)
            state.OrganizeButton.interactable = CanOrganizeStandaloneBackpack(state, backpackSlots);
        if (state.ConsolidateButton != null)
        {
            state.ConsolidateButton.gameObject.SetActive(state.IsHotkeyBackpack);
            state.ConsolidateButton.interactable = CanStackStandaloneBackpack(state, backpackSlots);
        }
        ConfigureStandaloneHeaderControls(state);
        UpdateStandaloneSortTabs(state);
    }

    private static void CreateStandaloneSettingsButton(RectTransform header, StandaloneBackpackState state)
    {
        if (header == null || state == null || state.SettingsButton != null)
            return;

        state.SettingsButton = CreateStandaloneActionButton(header, "SettingsCog",
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-31f, -31f), new Vector2(-8f, -8f),
            string.Empty, 15, out state.SettingsLabel);
        CreateStandaloneCogIcon(state.SettingsButton.GetComponent<RectTransform>());
        EventHelper.AddListener(() => ToggleStandaloneSettings(state), state.SettingsButton.onClick);
    }

    /// <summary>
    /// Adds the hotkey-only stack consolidation action beside the settings cog. It deliberately
    /// lives outside the responsive filter rail so that it remains a primary inventory action
    /// without crowding type, quality, and sort controls at compact embedded scales.
    /// </summary>
    private static void CreateStandaloneConsolidateButton(RectTransform header, StandaloneBackpackState state)
    {
        if (header == null || state == null || state.ConsolidateButton != null)
            return;

        state.ConsolidateButton = CreateStandaloneActionButton(header, "Consolidate",
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-86f, -31f), new Vector2(-35f, -8f),
            "STACK", 8, out state.ConsolidateLabel);
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
        if (image.sprite == null) _settingsCogLoadAttemptFailed = true;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private static Sprite GetStandaloneSettingsCogSprite()
    {
        if (_settingsCogSprite != null)
            return _settingsCogSprite;
        if (_settingsCogLoadAttemptFailed)
            return null;

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

    /// <summary>
    /// Gives the metrics handle the same solid tab language as the backpack controls while
    /// orienting its rounded corners toward the open side of the drawer. Its right edge is flat
    /// so it reads as mechanically attached to the drawer rather than as a vertically focused
    /// top tab.
    /// </summary>
    private static void ApplyMetricsTrayTabPresentation(Image image)
    {
        if (image == null)
            return;

        var tabSprite = GetMetricsTrayTabSprite();
        if (tabSprite == null)
            return;

        image.sprite = tabSprite;
        image.type = Image.Type.Sliced;
    }

    private static Sprite GetMetricsTrayTabSprite()
    {
        if (_metricsTrayTabSprite != null)
            return _metricsTrayTabSprite;

        try
        {
            _metricsTrayTabTexture = new Texture2D(MetricsTrayTabSpriteSize, MetricsTrayTabSpriteSize,
                TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[MetricsTrayTabSpriteSize * MetricsTrayTabSpriteSize];
            var radius = MetricsTrayTabCornerRadius;
            for (var y = 0; y < MetricsTrayTabSpriteSize; y++)
            {
                for (var x = 0; x < MetricsTrayTabSpriteSize; x++)
                {
                    var alpha = 1f;
                    if (x < radius && (y < radius || y >= MetricsTrayTabSpriteSize - radius))
                    {
                        var centerY = y < radius
                            ? radius - 0.5f
                            : MetricsTrayTabSpriteSize - radius - 0.5f;
                        var distance = Vector2.Distance(new Vector2(x, y),
                            new Vector2(radius - 0.5f, centerY));
                        alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    }

                    pixels[(y * MetricsTrayTabSpriteSize) + x] = new Color32(255, 255, 255,
                        (byte)(alpha * 255f));
                }
            }

            _metricsTrayTabTexture.SetPixels32(pixels);
            _metricsTrayTabTexture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            _metricsTrayTabSprite = Sprite.Create(_metricsTrayTabTexture,
                new Rect(0f, 0f, MetricsTrayTabSpriteSize, MetricsTrayTabSpriteSize), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect,
                new Vector4(MetricsTrayTabCornerRadius, MetricsTrayTabCornerRadius, 0f, 0f));
            return _metricsTrayTabSprite;
        }
        catch (Exception ex)
        {
            ModLogger.Warn("[BackpackUI] Metrics tray tab sprite was unavailable: " + ex.Message);
            return null;
        }
    }

    private static void ConfigureStandaloneDesktopTab(Button button, int index, int tabCount)
    {
        if (button == null || tabCount <= 0)
            return;

        ApplyDesktopTabPresentation(button.targetGraphic as Image);
        var rect = button.GetComponent<RectTransform>();
        if (rect != null)
        {
            index = Mathf.Clamp(index, 0, tabCount - 1);
            var minX = index / (float)tabCount;
            var maxX = (index + 1) / (float)tabCount;
            rect.anchorMin = new Vector2(minX, 0f);
            rect.anchorMax = new Vector2(maxX, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            // Preserve a compact desktop-tab group, but leave a visible six-pixel gutter between
            // faces so their labels and rounded upper corners do not read as one control.
            rect.offsetMin = new Vector2(index == 0 ? 0f : 3f, 0f);
            rect.offsetMax = new Vector2(index == tabCount - 1 ? 0f : -3f, 31f);
        }

        var label = button.GetComponentInChildren<Text>();
        if (label != null)
        {
            label.fontSize = 10;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
        }
    }

    private static void EnsureStandaloneSettingsPanel(StandaloneBackpackSurface surface, StandaloneBackpackState state)
    {
        if (surface == null || state?.VisualRoot == null)
            return;

        if (state.SettingsRoot == null)
        {
            var settingsGo = new GameObject("PackRat_BackpackSettings");
            var root = settingsGo.AddComponent<RectTransform>();
            // The modal belongs to the owning screen, not the scaled grid. This lets it cover
            // the full injected view and avoids being clipped by a compact embedded surface.
            root.SetParent(surface.Container, worldPositionStays: false);
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
                canvas.sortingOrder = 3000;
            }
            Utils.AddComponentSafe<GraphicRaycaster>(settingsGo);
            state.SettingsRoot = root;
            state.SettingsRootCanvasGroup = Utils.GetOrAddComponentSafe<CanvasGroup>(settingsGo);

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
            state.SettingsCardCanvasGroup = Utils.GetOrAddComponentSafe<CanvasGroup>(cardGo);
            state.SettingsCardRestPosition = card.anchoredPosition;
            state.SettingsCardRestCaptured = true;

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
            state.SettingsCloseButton = closeButton;
            AddStandaloneLayoutElement(closeButton.gameObject, preferredWidth: 58f, preferredHeight: 25f);
            EventHelper.AddListener(() => ToggleStandaloneSettings(state), closeButton.onClick);

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
            state.SettingsTabsRoot = tabs;
            // These fixed settings pages use direct anchors instead of a layout group.
            // The game can rebuild uGUI layouts while the modal opens; direct geometry keeps the
            // overlapping desktop-tab baseline stable instead of allowing preferred heights to
            // collapse to zero during that rebuild.

            state.SettingsGeneralButton = CreateStandaloneActionButton(tabs, "SettingsGeneral",
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero,
                "GENERAL", 9, out _);
            state.SettingsThemeButton = CreateStandaloneActionButton(tabs, "SettingsTheme",
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero,
                "THEME", 9, out _);
            state.SettingsTiersButton = CreateStandaloneActionButton(tabs, "SettingsTiers",
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero,
                "TIERS", 9, out _);
            state.SettingsLayoutButton = CreateStandaloneActionButton(tabs, "SettingsLayout",
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero,
                "LAYOUT", 9, out _);
            state.SettingsRoutingButton = CreateStandaloneActionButton(tabs, "SettingsRouting",
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero,
                "ROUTING", 9, out _);
            state.SettingsMetricsButton = CreateStandaloneActionButton(tabs, "SettingsMetrics",
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero,
                "METRICS", 9, out _);
            ConfigureStandaloneDesktopTab(state.SettingsGeneralButton, 0, 6);
            ConfigureStandaloneDesktopTab(state.SettingsThemeButton, 1, 6);
            ConfigureStandaloneDesktopTab(state.SettingsTiersButton, 2, 6);
            ConfigureStandaloneDesktopTab(state.SettingsLayoutButton, 3, 6);
            ConfigureStandaloneDesktopTab(state.SettingsRoutingButton, 4, 6);
            ConfigureStandaloneDesktopTab(state.SettingsMetricsButton, 5, 6);
            state.SettingsTabIndicator = CreateStandaloneSettingsTabIndicator(tabs);
            EventHelper.AddListener(() => SetStandaloneSettingsPage(state, StandaloneBackpackSettingsPage.General),
                state.SettingsGeneralButton.onClick);
            EventHelper.AddListener(() => SetStandaloneSettingsPage(state, StandaloneBackpackSettingsPage.Theme),
                state.SettingsThemeButton.onClick);
            EventHelper.AddListener(() => SetStandaloneSettingsPage(state, StandaloneBackpackSettingsPage.Tiers),
                state.SettingsTiersButton.onClick);
            EventHelper.AddListener(() => SetStandaloneSettingsPage(state, StandaloneBackpackSettingsPage.Layout),
                state.SettingsLayoutButton.onClick);
            EventHelper.AddListener(() => SetStandaloneSettingsPage(state, StandaloneBackpackSettingsPage.Routing),
                state.SettingsRoutingButton.onClick);
            EventHelper.AddListener(() => SetStandaloneSettingsPage(state, StandaloneBackpackSettingsPage.Metrics),
                state.SettingsMetricsButton.onClick);

            var content = CreateStandaloneSettingsRegion(card, "Content", Vector2.zero, Vector2.one,
                new Vector2(10f, 10f), new Vector2(-10f, -110f));
            var contentImage = content.gameObject.AddComponent<Image>();
            contentImage.color = new Color32(16, 32, 43, 238);
            state.SettingsContentRoot = content;
            state.SettingsGeneralPage = CreateStandaloneSettingsPage(content, "GeneralPage");
            state.SettingsThemePage = CreateStandaloneSettingsPage(content, "ThemePage");
            state.SettingsTiersPage = CreateStandaloneSettingsPage(content, "TiersPage");
            state.SettingsLayoutPage = CreateStandaloneSettingsPage(content, "LayoutPage");
            state.SettingsRoutingPage = CreateStandaloneSettingsPage(content, "RoutingPage");
            state.SettingsMetricsPage = CreateStandaloneSettingsPage(content, "MetricsPage");
            // The tabs are deliberately drawn above the content surface where their lower edge
            // overlaps it, matching a desktop tabbed window rather than a separated button row.
            tabs.SetAsLastSibling();
            state.SettingsRoot.gameObject.SetActive(false);
        }

        if (state.SettingsRoot.parent != surface.Container)
        {
            state.SettingsRoot.SetParent(surface.Container, worldPositionStays: false);
            state.SettingsRoot.anchorMin = Vector2.zero;
            state.SettingsRoot.anchorMax = Vector2.one;
            state.SettingsRoot.offsetMin = Vector2.zero;
            state.SettingsRoot.offsetMax = Vector2.zero;
        }

        if (state.SettingsOpen)
        {
            var wasInactive = !state.SettingsRoot.gameObject.activeSelf || state.SettingsClosing;
            state.SettingsRoot.gameObject.SetActive(true);
            state.SettingsRoot.SetAsLastSibling();
            RefreshStandaloneSettingsPane(state);
            if (wasInactive)
                PlayStandaloneSettingsOpen(state);
        }
    }

    private static void ToggleStandaloneSettings(StandaloneBackpackState state)
    {
        if (state == null)
            return;

        if (state.SettingsOpen || state.SettingsClosing)
        {
            state.SettingsOpen = false;
            state.AwaitingToggleKey = false;
            state.KeyboardSettingsControlIndex = -1;
            PlayStandaloneSettingsClose(state);
            return;
        }

        var openedFromKeyboard = state.KeyboardFocusKind == StandaloneBackpackKeyboardFocusKind.Control;
        state.SettingsOpen = true;
        state.AwaitingToggleKey = false;
        HideStandaloneDropdown(state);
        ClearStandaloneKeyboardFocus(state);
        state.KeyboardSettingsControlIndex = openedFromKeyboard ? 0 : -1;
        state.RefreshAction?.Invoke();
    }

    private static Image CreateStandaloneSettingsTabIndicator(RectTransform tabs)
    {
        if (tabs == null)
            return null;

        var indicatorGo = new GameObject("ActiveTabIndicator");
        var indicator = indicatorGo.AddComponent<RectTransform>();
        indicator.SetParent(tabs, worldPositionStays: false);
        indicator.anchorMin = Vector2.zero;
        indicator.anchorMax = Vector2.zero;
        indicator.pivot = new Vector2(0f, 0f);
        indicator.sizeDelta = new Vector2(1f, 3f);
        var image = indicatorGo.AddComponent<Image>();
        image.color = new Color32(109, 205, 251, 255);
        image.raycastTarget = false;
        indicatorGo.SetActive(false);
        return image;
    }

    private static void PlayStandaloneSettingsOpen(StandaloneBackpackState state)
    {
        if (state?.SettingsRoot == null || state.SettingsCard == null)
            return;

        state.SettingsRootCanvasGroup ??= Utils.GetOrAddComponentSafe<CanvasGroup>(state.SettingsRoot.gameObject);
        state.SettingsCardCanvasGroup ??= Utils.GetOrAddComponentSafe<CanvasGroup>(state.SettingsCard.gameObject);
        if (state.SettingsRootCanvasGroup == null || state.SettingsCardCanvasGroup == null)
            return;

        state.SettingsClosing = false;
        if (!state.SettingsCardRestCaptured)
        {
            state.SettingsCardRestPosition = state.SettingsCard.anchoredPosition;
            state.SettingsCardRestCaptured = true;
        }

        var generation = ++state.SettingsMotionGeneration;
        state.SettingsRootCanvasGroup.blocksRaycasts = true;
        state.SettingsRootCanvasGroup.interactable = true;
        state.SettingsCardCanvasGroup.interactable = true;
        if (!Configuration.Instance.EnableUiAnimations)
        {
            SnapStandaloneSettingsMotion(state);
            return;
        }

        state.SettingsRootCanvasGroup.alpha = 0f;
        state.SettingsCardCanvasGroup.alpha = 0f;
        state.SettingsCard.localScale = Configuration.Instance.ReduceUiMotion ? Vector3.one : new Vector3(0.94f, 0.94f, 1f);
        state.SettingsCard.anchoredPosition = Configuration.Instance.ReduceUiMotion
            ? state.SettingsCardRestPosition
            : state.SettingsCardRestPosition + new Vector2(0f, -10f);
        MelonCoroutines.Start(RunStandaloneSettingsOpenMotion(state, generation));
    }

    private static IEnumerator RunStandaloneSettingsOpenMotion(StandaloneBackpackState state, int generation)
    {
        var elapsed = 0f;
        var duration = Mathf.Max(SettingsBlockerDuration, SettingsCardDuration);
        while (state != null && state.SettingsMotionGeneration == generation && elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            var blockerT = EaseOutCubic(Mathf.Clamp01(elapsed / SettingsBlockerDuration));
            var cardT = EaseOutCubic(Mathf.Clamp01(elapsed / SettingsCardDuration));
            if (state.SettingsRootCanvasGroup != null)
                state.SettingsRootCanvasGroup.alpha = blockerT;
            if (state.SettingsCardCanvasGroup != null)
                state.SettingsCardCanvasGroup.alpha = cardT;
            if (state.SettingsCard != null && !Configuration.Instance.ReduceUiMotion)
            {
                state.SettingsCard.localScale = Vector3.Lerp(new Vector3(0.94f, 0.94f, 1f), Vector3.one, cardT);
                state.SettingsCard.anchoredPosition = Vector2.Lerp(
                    state.SettingsCardRestPosition + new Vector2(0f, -10f), state.SettingsCardRestPosition, cardT);
            }
            yield return null;
        }

        if (state != null && state.SettingsMotionGeneration == generation && state.SettingsOpen)
            SnapStandaloneSettingsMotion(state);
    }

    private static void PlayStandaloneSettingsClose(StandaloneBackpackState state)
    {
        if (state?.SettingsRoot == null || !state.SettingsRoot.gameObject.activeSelf)
            return;

        state.SettingsRootCanvasGroup ??= Utils.GetOrAddComponentSafe<CanvasGroup>(state.SettingsRoot.gameObject);
        state.SettingsCardCanvasGroup ??= Utils.GetOrAddComponentSafe<CanvasGroup>(state.SettingsCard.gameObject);
        if (state.SettingsRootCanvasGroup == null || state.SettingsCardCanvasGroup == null)
        {
            state.SettingsRoot.gameObject.SetActive(false);
            return;
        }

        state.SettingsClosing = true;
        var generation = ++state.SettingsMotionGeneration;
        state.SettingsRootCanvasGroup.blocksRaycasts = true;
        state.SettingsRootCanvasGroup.interactable = false;
        state.SettingsCardCanvasGroup.interactable = false;
        if (!Configuration.Instance.EnableUiAnimations)
        {
            state.SettingsRoot.gameObject.SetActive(false);
            state.SettingsClosing = false;
            SnapStandaloneSettingsMotion(state);
            return;
        }

        MelonCoroutines.Start(RunStandaloneSettingsCloseMotion(state, generation,
            state.SettingsRootCanvasGroup.alpha, state.SettingsCardCanvasGroup.alpha,
            state.SettingsCard.localScale, state.SettingsCard.anchoredPosition));
    }

    private static IEnumerator RunStandaloneSettingsCloseMotion(StandaloneBackpackState state, int generation,
        float rootAlpha, float cardAlpha, Vector3 cardScale, Vector2 cardPosition)
    {
        var elapsed = 0f;
        while (state != null && state.SettingsMotionGeneration == generation && elapsed < SettingsCloseDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            var t = EaseOutCubic(Mathf.Clamp01(elapsed / SettingsCloseDuration));
            if (state.SettingsRootCanvasGroup != null)
                state.SettingsRootCanvasGroup.alpha = Mathf.Lerp(rootAlpha, 0f, t);
            if (state.SettingsCardCanvasGroup != null)
                state.SettingsCardCanvasGroup.alpha = Mathf.Lerp(cardAlpha, 0f, t);
            if (state.SettingsCard != null && !Configuration.Instance.ReduceUiMotion)
            {
                state.SettingsCard.localScale = Vector3.Lerp(cardScale, new Vector3(0.96f, 0.96f, 1f), t);
                state.SettingsCard.anchoredPosition = Vector2.Lerp(cardPosition,
                    state.SettingsCardRestPosition + new Vector2(0f, -6f), t);
            }
            yield return null;
        }

        if (state != null && state.SettingsMotionGeneration == generation && !state.SettingsOpen)
        {
            state.SettingsRoot.gameObject.SetActive(false);
            state.SettingsClosing = false;
            SnapStandaloneSettingsMotion(state);
        }
    }

    private static void SnapStandaloneSettingsMotion(StandaloneBackpackState state)
    {
        if (state == null)
            return;

        if (state.SettingsRootCanvasGroup != null)
        {
            state.SettingsRootCanvasGroup.alpha = 1f;
            state.SettingsRootCanvasGroup.blocksRaycasts = true;
            state.SettingsRootCanvasGroup.interactable = true;
        }
        if (state.SettingsCardCanvasGroup != null)
        {
            state.SettingsCardCanvasGroup.alpha = 1f;
            state.SettingsCardCanvasGroup.interactable = true;
        }
        if (state.SettingsCard != null)
        {
            state.SettingsCard.localScale = Vector3.one;
            if (state.SettingsCardRestCaptured)
                state.SettingsCard.anchoredPosition = state.SettingsCardRestPosition;
        }
    }

    private static void SnapStandaloneMotionState(StandaloneBackpackState state)
    {
        if (state == null)
            return;

        ++state.BackpackOpenMotionGeneration;
        ++state.SettingsMotionGeneration;
        ++state.DropdownMotionGeneration;
        ++state.SearchFocusMotionGeneration;
        ++state.TabMotionGeneration;
        SnapStandaloneBackpackVisual(state);
        SnapStandaloneSettingsMotion(state);
        HideStandalonePageWipe(state);
        if (state.SearchBackground != null)
            state.SearchBackground.color = state.SearchInput != null && state.SearchInput.isFocused
                ? new Color32(24, 74, 102, 250)
                : state.SearchBackgroundBaseColor;
    }

    private static void SetStandaloneSettingsPage(StandaloneBackpackState state,
        StandaloneBackpackSettingsPage page)
    {
        if (state == null)
            return;

        if (state.SettingsPage == page)
        {
            RefreshStandaloneSettingsPane(state);
            return;
        }

        state.AwaitingToggleKey = false;
        state.SettingsPage = page;
        RefreshStandaloneSettingsPane(state);
    }

    private static void RefreshStandaloneSettingsPane(StandaloneBackpackState state)
    {
        if (state?.SettingsRoot == null || !state.SettingsOpen)
            return;

        ClearStandaloneSettingsRows(state);
        UpdateStandaloneSessionStatus(state);
        UpdateStandaloneSettingsTabs(state);
        UpdateStandaloneSettingsPageVisibility(state);
        switch (state.SettingsPage)
        {
            case StandaloneBackpackSettingsPage.Theme:
                BuildStandaloneThemeSettings(state);
                break;
            case StandaloneBackpackSettingsPage.Tiers:
                BuildStandaloneTierSettings(state);
                break;
            case StandaloneBackpackSettingsPage.Layout:
                BuildStandaloneLayoutSettings(state);
                break;
            case StandaloneBackpackSettingsPage.Routing:
                BuildStandaloneRoutingSettings(state);
                break;
            case StandaloneBackpackSettingsPage.Metrics:
                BuildStandaloneMetricsSettings(state);
                break;
            default:
                BuildStandaloneGeneralSettings(state);
                break;
        }

        RefreshStandaloneSettingsKeyboardFocusPresentation(state);
        ApplyStandaloneBackpackTheme(state);
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
        UpdateStandaloneSettingsTab(state.SettingsThemeButton, state.SettingsPage == StandaloneBackpackSettingsPage.Theme);
        UpdateStandaloneSettingsTab(state.SettingsTiersButton, state.SettingsPage == StandaloneBackpackSettingsPage.Tiers);
        UpdateStandaloneSettingsTab(state.SettingsLayoutButton, state.SettingsPage == StandaloneBackpackSettingsPage.Layout);
        UpdateStandaloneSettingsTab(state.SettingsRoutingButton, state.SettingsPage == StandaloneBackpackSettingsPage.Routing);
        UpdateStandaloneSettingsTab(state.SettingsMetricsButton, state.SettingsPage == StandaloneBackpackSettingsPage.Metrics);
        UpdateStandaloneSettingsTabIndicator(state);
    }

    private static void UpdateStandaloneSettingsTabIndicator(StandaloneBackpackState state)
    {
        if (state?.SettingsTabIndicator == null || state.SettingsTabsRoot == null)
            return;

        Canvas.ForceUpdateCanvases();
        var width = state.SettingsTabsRoot.rect.width;
        if (width <= 0f)
            return;

        var indicator = state.SettingsTabIndicator.GetComponent<RectTransform>();
        if (indicator == null)
            return;

        var page = (int)state.SettingsPage;
        const int tabCount = 6;
        var target = new Vector2((width / tabCount * page) + 4f, 0f);
        indicator.sizeDelta = new Vector2((width / tabCount) - 8f, 3f);
        indicator.gameObject.SetActive(true);
        indicator.SetAsLastSibling();

        var firstPresentation = state.LastPresentedSettingsPage < 0;
        state.LastPresentedSettingsPage = page;
        var generation = ++state.TabMotionGeneration;
        if (firstPresentation || !Configuration.Instance.EnableUiAnimations || Configuration.Instance.ReduceUiMotion)
        {
            indicator.anchoredPosition = target;
            return;
        }

        var start = indicator.anchoredPosition;
        MelonCoroutines.Start(RunStandaloneTabIndicatorMotion(state, generation, start, target));
    }

    private static IEnumerator RunStandaloneTabIndicatorMotion(StandaloneBackpackState state, int generation,
        Vector2 start, Vector2 target)
    {
        var elapsed = 0f;
        while (state != null && state.TabMotionGeneration == generation && elapsed < TabIndicatorDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            var indicator = state.SettingsTabIndicator?.GetComponent<RectTransform>();
            if (indicator != null)
                indicator.anchoredPosition = Vector2.Lerp(start, target, EaseOutCubic(Mathf.Clamp01(elapsed / TabIndicatorDuration)));
            yield return null;
        }

        if (state != null && state.TabMotionGeneration == generation)
        {
            var indicator = state.SettingsTabIndicator?.GetComponent<RectTransform>();
            if (indicator != null)
                indicator.anchoredPosition = target;
        }
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
        SetStandaloneSettingsPageActive(state.SettingsThemePage, state.SettingsPage == StandaloneBackpackSettingsPage.Theme);
        SetStandaloneSettingsPageActive(state.SettingsTiersPage, state.SettingsPage == StandaloneBackpackSettingsPage.Tiers);
        SetStandaloneSettingsPageActive(state.SettingsLayoutPage, state.SettingsPage == StandaloneBackpackSettingsPage.Layout);
        SetStandaloneSettingsPageActive(state.SettingsRoutingPage, state.SettingsPage == StandaloneBackpackSettingsPage.Routing);
        SetStandaloneSettingsPageActive(state.SettingsMetricsPage, state.SettingsPage == StandaloneBackpackSettingsPage.Metrics);
    }

    private static void SetStandaloneSettingsPageActive(RectTransform page, bool active)
    {
        if (page != null && page.gameObject.activeSelf != active)
            page.gameObject.SetActive(active);
    }

    private static void BuildStandaloneGeneralSettings(StandaloneBackpackState state)
    {
        var config = Configuration.Instance;
        AddStandaloneSettingsRow(state, "TOGGLE KEY", state.AwaitingToggleKey ? "PRESS A KEY..." : config.ToggleKey.ToString(),
            "SET", () =>
            {
                state.AwaitingToggleKey = true;
                RefreshStandaloneSettingsPane(state);
            });

        var canEditSession = ConfigSyncManager.CanEditSessionSettings();
        if (canEditSession)
        {
            AddStandaloneSettingsToggleRow(state, "POLICE SEARCH", config.EnableSearch, value =>
            {
                config.EnableSearch = value;
                PersistStandaloneSettings(state, syncSessionSettings: true);
            });
        }
        else
        {
            AddStandaloneSettingsRow(state, "POLICE SEARCH", config.EnableSearch ? "ENABLED" : "DISABLED", "HOST ONLY");
        }

        AddStandaloneSettingsToggleRow(state, "SYNC DIAGNOSTICS", config.BackpackSyncDebugLogging, value =>
        {
            config.BackpackSyncDebugLogging = value;
            PersistStandaloneSettings(state);
        });
        AddStandaloneSettingsToggleRow(state, "UI ANIMATIONS", config.EnableUiAnimations, value =>
        {
            config.EnableUiAnimations = value;
            if (!value)
                SnapStandaloneMotionState(state);
            PersistStandaloneSettings(state);
        });
        AddStandaloneSettingsToggleRow(state, "REDUCED MOTION", config.ReduceUiMotion, value =>
        {
            config.ReduceUiMotion = value;
            if (value)
                SnapStandaloneMotionState(state);
            PersistStandaloneSettings(state);
        });
        AddStandaloneSettingsToggleRow(state, "PROTECT FAVORITES", config.ProtectFavoritesFromOrganization, value =>
        {
            config.ProtectFavoritesFromOrganization = value;
            PersistStandaloneSettings(state);
        });
    }

    /// <summary>
    /// Selects a local visual preset. Repainting is immediate so players can compare each theme
    /// against the active world lighting without closing the backpack or reloading the game.
    /// </summary>
    private static void BuildStandaloneThemeSettings(StandaloneBackpackState state)
    {
        var config = Configuration.Instance;
        state.SettingsThemeValueLabel = AddStandaloneSettingsRow(state, "COLOR THEME",
            BackpackUiThemes.GetLabel(config.BackpackUiTheme), "<", () =>
        {
            config.BackpackUiTheme = BackpackUiThemes.Offset(config.BackpackUiTheme, -1);
            PersistStandaloneSettings(state);
        }, ">", () =>
        {
            config.BackpackUiTheme = BackpackUiThemes.Offset(config.BackpackUiTheme, 1);
            PersistStandaloneSettings(state);
        });
        AddStandaloneThemeColorPicker(state);
    }

    /// <summary>
    /// Builds the primary-colour picker directly below the preset selector. Presets seed the
    /// picker with their current header colour; moving a picker channel promotes that colour to
    /// the persisted Custom preset and repaints the active backpack immediately.
    /// </summary>
    private static void AddStandaloneThemeColorPicker(StandaloneBackpackState state)
    {
        var config = Configuration.Instance;
        var palette = BackpackUiThemes.Get(config.BackpackUiTheme, config.CustomBackpackUiPrimaryColor);
        Color.RGBToHSV(palette.Header, out var hue, out var saturation, out var value);
        var preview = AddStandaloneThemePreviewRow(state, palette.Header);
        Action apply = () =>
        {
            var selected = Color.HSVToRGB(hue, saturation, value);
            config.CustomBackpackUiPrimaryColor = selected;
            config.BackpackUiTheme = BackpackUiTheme.Custom;
            config.Save();
            if (state.SettingsThemeValueLabel != null)
                state.SettingsThemeValueLabel.text = BackpackUiThemes.GetLabel(BackpackUiTheme.Custom);
            if (preview.ValueLabel != null)
                preview.ValueLabel.text = FormatStandaloneThemeColor(selected);
            if (preview.Swatch != null)
                preview.Swatch.color = selected;
            RefreshActiveUiThemes();
            RefreshActiveStorageBackpackLayouts();
            StationBackpackPanelPatch.RefreshActiveLayouts();
            HandoverScreenPatch.RefreshActiveLayouts();
        };

        AddStandaloneThemeSliderRow(state, "HUE", hue, valueChanged =>
        {
            hue = valueChanged;
            apply();
        }, current => Mathf.RoundToInt(current * 360f) + "°");
        AddStandaloneThemeSliderRow(state, "SATURATION", saturation, valueChanged =>
        {
            saturation = valueChanged;
            apply();
        }, current => Mathf.RoundToInt(current * 100f) + "%");
        AddStandaloneThemeSliderRow(state, "BRIGHTNESS", value, valueChanged =>
        {
            value = valueChanged;
            apply();
        }, current => Mathf.RoundToInt(current * 100f) + "%");
    }

    private sealed class StandaloneThemePreview
    {
        public Image Swatch;
        public Text ValueLabel;
    }

    private static StandaloneThemePreview AddStandaloneThemePreviewRow(StandaloneBackpackState state, Color color)
    {
        var pageRoot = GetStandaloneSettingsPageRoot(state);
        if (pageRoot == null)
            return new StandaloneThemePreview();

        var rowGo = new GameObject("ThemePrimaryPreview");
        var row = rowGo.AddComponent<RectTransform>();
        row.SetParent(pageRoot, worldPositionStays: false);
        var background = rowGo.AddComponent<Image>();
        background.color = new Color32(20, 33, 44, 248);
        ApplyRoundedButtonPresentation(background);
        AddStandaloneLayoutElement(rowGo, minHeight: 30f, preferredHeight: 30f);
        var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 3, 3);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var label = CreateSearchText(row, "Label", new Color32(188, 216, 235, 255));
        label.text = "PRIMARY COLOR";
        label.fontSize = 9;
        label.fontStyle = FontStyle.Bold;
        AddStandaloneLayoutElement(label.gameObject, preferredWidth: 100f);

        var swatchGo = new GameObject("Swatch");
        var swatchRect = swatchGo.AddComponent<RectTransform>();
        swatchRect.SetParent(row, worldPositionStays: false);
        var swatch = swatchGo.AddComponent<Image>();
        swatch.color = color;
        ApplyPillButtonPresentation(swatch);
        AddStandaloneLayoutElement(swatchGo, preferredWidth: 48f, preferredHeight: 20f);

        var value = CreateSearchText(row, "Value", new Color32(245, 248, 251, 255));
        value.text = FormatStandaloneThemeColor(color);
        value.fontSize = 9;
        value.fontStyle = FontStyle.Bold;
        value.alignment = TextAnchor.MiddleRight;
        AddStandaloneLayoutElement(value.gameObject, flexibleWidth: 1f);
        state.SettingsRows.Add(rowGo);
        return new StandaloneThemePreview { Swatch = swatch, ValueLabel = value };
    }

    private static void AddStandaloneThemeSliderRow(StandaloneBackpackState state, string labelText, float currentValue,
        Action<float> changedAction, Func<float, string> valueFormatter)
    {
        var pageRoot = GetStandaloneSettingsPageRoot(state);
        if (pageRoot == null)
            return;

        var rowGo = new GameObject("Theme" + labelText + "Row");
        var row = rowGo.AddComponent<RectTransform>();
        row.SetParent(pageRoot, worldPositionStays: false);
        var background = rowGo.AddComponent<Image>();
        background.color = new Color32(20, 33, 44, 248);
        ApplyRoundedButtonPresentation(background);
        AddStandaloneLayoutElement(rowGo, minHeight: 28f, preferredHeight: 28f);
        var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 3, 3);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var label = CreateSearchText(row, "Label", new Color32(188, 216, 235, 255));
        label.text = labelText;
        label.fontSize = 9;
        label.fontStyle = FontStyle.Bold;
        AddStandaloneLayoutElement(label.gameObject, preferredWidth: 78f);

        var sliderGo = new GameObject("Slider");
        var sliderRect = sliderGo.AddComponent<RectTransform>();
        sliderRect.SetParent(row, worldPositionStays: false);
        AddStandaloneLayoutElement(sliderGo, minWidth: 84f, flexibleWidth: 1f, preferredHeight: 18f);
        var slider = sliderGo.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.direction = Slider.Direction.LeftToRight;

        var trackGo = new GameObject("Track");
        var track = trackGo.AddComponent<RectTransform>();
        track.SetParent(sliderRect, worldPositionStays: false);
        track.anchorMin = new Vector2(0f, 0.5f);
        track.anchorMax = new Vector2(1f, 0.5f);
        track.offsetMin = new Vector2(6f, -4f);
        track.offsetMax = new Vector2(-6f, 4f);
        var trackImage = trackGo.AddComponent<Image>();
        trackImage.color = new Color32(12, 21, 30, 252);
        ApplyPillButtonPresentation(trackImage);

        var fillGo = new GameObject("Fill");
        var fill = fillGo.AddComponent<RectTransform>();
        fill.SetParent(track, worldPositionStays: false);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(0f, 1f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        var fillImage = fillGo.AddComponent<Image>();
        fillImage.color = new Color32(76, 173, 229, 255);
        ApplyPillButtonPresentation(fillImage);
        slider.fillRect = fill;

        var handleGo = new GameObject("Handle");
        var handle = handleGo.AddComponent<RectTransform>();
        handle.SetParent(sliderRect, worldPositionStays: false);
        handle.anchorMin = new Vector2(0f, 0.5f);
        handle.anchorMax = new Vector2(0f, 0.5f);
        handle.pivot = new Vector2(0.5f, 0.5f);
        handle.sizeDelta = new Vector2(14f, 14f);
        var handleImage = handleGo.AddComponent<Image>();
        handleImage.color = new Color32(240, 247, 251, 255);
        ApplyPillButtonPresentation(handleImage);
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;

        var value = CreateSearchText(row, "Value", new Color32(245, 248, 251, 255));
        value.fontSize = 8;
        value.fontStyle = FontStyle.Bold;
        value.alignment = TextAnchor.MiddleRight;
        value.text = valueFormatter(currentValue);
        AddStandaloneLayoutElement(value.gameObject, preferredWidth: 38f);

        slider.SetValueWithoutNotify(Mathf.Clamp01(currentValue));
        EventHelper.AddListener<float>(changedValue =>
        {
            value.text = valueFormatter(changedValue);
            changedAction?.Invoke(changedValue);
        }, slider.onValueChanged);
        state.SettingsRows.Add(rowGo);
    }

    private static string FormatStandaloneThemeColor(Color color)
    {
        var value = (Color32)color;
        return "#" + value.r.ToString("X2") + value.g.ToString("X2") + value.b.ToString("X2");
    }

    /// <summary>
    /// Builds the local quick-move routing preferences. These settings intentionally remain per
    /// player: the resulting backpack mutation is synchronized only after the storage session closes.
    /// </summary>
    private static void BuildStandaloneRoutingSettings(StandaloneBackpackState state)
    {
        var config = Configuration.Instance;
        AddStandaloneSettingsToggleRow(state, "SMART ROUTING", config.EnableSmartRouting, value =>
        {
            config.EnableSmartRouting = value;
            PersistStandaloneSettings(state);
        });
        AddStandaloneSettingsToggleRow(state, "ROUTE PRODUCTS", config.RouteProducts, value =>
        {
            config.RouteProducts = value;
            PersistStandaloneSettings(state);
        });
        AddStandaloneSettingsToggleRow(state, "ROUTE SEEDS", config.RouteSeeds, value =>
        {
            config.RouteSeeds = value;
            PersistStandaloneSettings(state);
        });
        AddStandaloneSettingsToggleRow(state, "ROUTE MIXERS", config.RouteMixers, value =>
        {
            config.RouteMixers = value;
            PersistStandaloneSettings(state);
        });
        AddStandaloneSettingsToggleRow(state, "ROUTE REAGENTS", config.RouteReagents, value =>
        {
            config.RouteReagents = value;
            PersistStandaloneSettings(state);
        });
    }

    /// <summary>
    /// Lets each player choose the information density of the local product metrics tray without
    /// affecting any inventory or multiplayer state.
    /// </summary>
    private static void BuildStandaloneMetricsSettings(StandaloneBackpackState state)
    {
        var config = Configuration.Instance;
        AddStandaloneSettingsToggleRow(state, "SHOW METRICS TRAY", config.ShowMetricsTray, value =>
        {
            config.ShowMetricsTray = value;
            if (!value)
                state.MetricsTrayExpanded = false;
            PersistStandaloneSettings(state);
        });
        AddStandaloneSettingsToggleRow(state, "SHOW QUANTITY", config.ShowProductQuantityMetric, value =>
        {
            config.ShowProductQuantityMetric = value;
            PersistStandaloneSettings(state);
        });
        AddStandaloneSettingsToggleRow(state, "SHOW QUANTITY TOTAL", config.ShowProductQuantityTotalMetric, value =>
        {
            config.ShowProductQuantityTotalMetric = value;
            PersistStandaloneSettings(state);
        });
        AddStandaloneSettingsToggleRow(state, "SHOW UNIT PRICE", config.ShowProductUnitPriceMetric, value =>
        {
            config.ShowProductUnitPriceMetric = value;
            PersistStandaloneSettings(state);
        });
        AddStandaloneSettingsToggleRow(state, "SHOW TOTAL PRICE", config.ShowProductTotalPriceMetric, value =>
        {
            config.ShowProductTotalPriceMetric = value;
            PersistStandaloneSettings(state);
        });
    }

    private static void BuildStandaloneTierSettings(StandaloneBackpackState state)
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
            RefreshStandaloneSettingsPane(state);
        }, ">", () =>
        {
            state.SettingsTierIndex = state.SettingsTierIndex >= maxTier ? 0 : state.SettingsTierIndex + 1;
            RefreshStandaloneSettingsPane(state);
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
            PersistStandaloneSettings(state, applyCurrentTier: true, syncSessionSettings: true);
        });
        AddStandaloneSettingsRow(state, "SLOTS", config.TierSlotCounts[tierIndex].ToString(), "-4", () =>
        {
            AdjustStandaloneTierSlots(state, tierIndex, -4);
        }, "+4", () =>
        {
            AdjustStandaloneTierSlots(state, tierIndex, 4);
        });
        AddStandaloneSettingsRow(state, "PRICE", "$" + config.TierPrices[tierIndex].ToString("0"), "-25", () =>
        {
            config.TierPrices[tierIndex] = Math.Max(0f, config.TierPrices[tierIndex] - 25f);
            PersistStandaloneSettings(state, syncSessionSettings: true);
        }, "+25", () =>
        {
            config.TierPrices[tierIndex] += 25f;
            PersistStandaloneSettings(state, syncSessionSettings: true);
        });
        AddStandaloneSettingsRow(state, "UNLOCK", FormatStandaloneUnlockRank(config.TierUnlockRanks[tierIndex]), "-", () =>
        {
            config.TierUnlockRanks[tierIndex] = OffsetStandaloneUnlockRank(config.TierUnlockRanks[tierIndex], -1);
            PersistStandaloneSettings(state, syncSessionSettings: true);
        }, "+", () =>
        {
            config.TierUnlockRanks[tierIndex] = OffsetStandaloneUnlockRank(config.TierUnlockRanks[tierIndex], 1);
            PersistStandaloneSettings(state, syncSessionSettings: true);
        });
    }

    private static void BuildStandaloneLayoutSettings(StandaloneBackpackState state)
    {
        var view = state.LayoutView;
        AddStandaloneSettingsRow(state, "LAYOUT VIEW", GetStandaloneLayoutViewLabel(view), "<", () =>
        {
            state.LayoutView = OffsetStandaloneLayoutView(state.LayoutView, -1);
            RefreshStandaloneSettingsPane(state);
        }, ">", () =>
        {
            state.LayoutView = OffsetStandaloneLayoutView(state.LayoutView, 1);
            RefreshStandaloneSettingsPane(state);
        });
        AddStandaloneSettingsRow(state, "POSITION X", FormatStandaloneOffset(GetStandaloneLayoutOffsetX(view)), "-10", () =>
        {
            SetStandaloneLayoutOffsetX(view, GetStandaloneLayoutOffsetX(view) - 10f);
            PersistStandaloneSettings(state);
        }, "+10", () =>
        {
            SetStandaloneLayoutOffsetX(view, GetStandaloneLayoutOffsetX(view) + 10f);
            PersistStandaloneSettings(state);
        });
        AddStandaloneSettingsRow(state, "POSITION Y", FormatStandaloneOffset(GetStandaloneLayoutOffsetY(view)), "-10", () =>
        {
            SetStandaloneLayoutOffsetY(view, GetStandaloneLayoutOffsetY(view) - 10f);
            PersistStandaloneSettings(state);
        }, "+10", () =>
        {
            SetStandaloneLayoutOffsetY(view, GetStandaloneLayoutOffsetY(view) + 10f);
            PersistStandaloneSettings(state);
        });
        AddStandaloneSettingsRow(state, "UI SCALE", FormatStandaloneScale(GetStandaloneLayoutScale(view)), "-5%", () =>
        {
            SetStandaloneLayoutScale(view, GetStandaloneLayoutScale(view) - OverlayScaleStep);
            PersistStandaloneSettings(state);
        }, "+5%", () =>
        {
            SetStandaloneLayoutScale(view, GetStandaloneLayoutScale(view) + OverlayScaleStep);
            PersistStandaloneSettings(state);
        });
        AddStandaloneSettingsRow(state, "RESET VIEW", string.Empty, "RESET", () =>
        {
            SetStandaloneLayoutOffsetX(view, GetStandaloneLayoutDefaultOffsetX(view));
            SetStandaloneLayoutOffsetY(view, 0f);
            SetStandaloneLayoutScale(view, GetStandaloneLayoutDefaultScale(view));
            PersistStandaloneSettings(state);
        });
    }

    private static float GetStandaloneLayoutDefaultOffsetX(StandaloneBackpackLayoutView view)
    {
        return view == StandaloneBackpackLayoutView.Backpack ? 0f : -430f;
    }

    private static float GetStandaloneLayoutDefaultScale(StandaloneBackpackLayoutView view)
    {
        return view == StandaloneBackpackLayoutView.Backpack ? 1f : 0.85f;
    }

    private static StandaloneBackpackLayoutView OffsetStandaloneLayoutView(StandaloneBackpackLayoutView view, int offset)
    {
        const int count = 4;
        var value = ((int)view + offset) % count;
        return (StandaloneBackpackLayoutView)(value < 0 ? value + count : value);
    }

    private static string GetStandaloneLayoutViewLabel(StandaloneBackpackLayoutView view)
    {
        return view switch
        {
            StandaloneBackpackLayoutView.Storage => "STORAGE",
            StandaloneBackpackLayoutView.Station => "STATION",
            StandaloneBackpackLayoutView.Deal => "DEAL",
            _ => "BACKPACK"
        };
    }

    private static float GetStandaloneLayoutOffsetX(StandaloneBackpackLayoutView view)
    {
        var config = Configuration.Instance;
        return view switch
        {
            StandaloneBackpackLayoutView.Storage => config.StorageOverlayOffsetX,
            StandaloneBackpackLayoutView.Station => config.StationOverlayOffsetX,
            StandaloneBackpackLayoutView.Deal => config.HandoverOverlayOffsetX,
            _ => config.BackpackOverlayOffsetX
        };
    }

    private static void SetStandaloneLayoutOffsetX(StandaloneBackpackLayoutView view, float value)
    {
        var config = Configuration.Instance;
        switch (view)
        {
            case StandaloneBackpackLayoutView.Storage:
                config.StorageOverlayOffsetX = value;
                break;
            case StandaloneBackpackLayoutView.Station:
                config.StationOverlayOffsetX = value;
                break;
            case StandaloneBackpackLayoutView.Deal:
                config.HandoverOverlayOffsetX = value;
                break;
            default:
                config.BackpackOverlayOffsetX = value;
                break;
        }
    }

    private static float GetStandaloneLayoutOffsetY(StandaloneBackpackLayoutView view)
    {
        var config = Configuration.Instance;
        return view switch
        {
            StandaloneBackpackLayoutView.Storage => config.StorageOverlayOffsetY,
            StandaloneBackpackLayoutView.Station => config.StationOverlayOffsetY,
            StandaloneBackpackLayoutView.Deal => config.HandoverOverlayOffsetY,
            _ => config.BackpackOverlayOffsetY
        };
    }

    private static void SetStandaloneLayoutOffsetY(StandaloneBackpackLayoutView view, float value)
    {
        var config = Configuration.Instance;
        switch (view)
        {
            case StandaloneBackpackLayoutView.Storage:
                config.StorageOverlayOffsetY = value;
                break;
            case StandaloneBackpackLayoutView.Station:
                config.StationOverlayOffsetY = value;
                break;
            case StandaloneBackpackLayoutView.Deal:
                config.HandoverOverlayOffsetY = value;
                break;
            default:
                config.BackpackOverlayOffsetY = value;
                break;
        }
    }

    private static float GetStandaloneLayoutScale(StandaloneBackpackLayoutView view)
    {
        var config = Configuration.Instance;
        return view switch
        {
            StandaloneBackpackLayoutView.Storage => config.StorageOverlayScale,
            StandaloneBackpackLayoutView.Station => config.StationOverlayScale,
            StandaloneBackpackLayoutView.Deal => config.HandoverOverlayScale,
            _ => config.BackpackOverlayScale
        };
    }

    private static void SetStandaloneLayoutScale(StandaloneBackpackLayoutView view, float value)
    {
        value = Mathf.Clamp(value, MinimumOverlayScale, MaximumOverlayScale);
        var config = Configuration.Instance;
        switch (view)
        {
            case StandaloneBackpackLayoutView.Storage:
                config.StorageOverlayScale = value;
                break;
            case StandaloneBackpackLayoutView.Station:
                config.StationOverlayScale = value;
                break;
            case StandaloneBackpackLayoutView.Deal:
                config.HandoverOverlayScale = value;
                break;
            default:
                config.BackpackOverlayScale = value;
                break;
        }
    }

    private static Text AddStandaloneSettingsRow(StandaloneBackpackState state, string labelText, string valueText,
        string primaryCaption = null, Action primaryAction = null, string secondaryCaption = null,
        Action secondaryAction = null)
    {
        var pageRoot = GetStandaloneSettingsPageRoot(state);
        if (pageRoot == null)
            return null;

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
        return value;
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
            StandaloneBackpackSettingsPage.Theme => state.SettingsThemePage,
            StandaloneBackpackSettingsPage.Tiers => state.SettingsTiersPage,
            StandaloneBackpackSettingsPage.Layout => state.SettingsLayoutPage,
            StandaloneBackpackSettingsPage.Routing => state.SettingsRoutingPage,
            StandaloneBackpackSettingsPage.Metrics => state.SettingsMetricsPage,
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

    private static void AdjustStandaloneTierSlots(StandaloneBackpackState state, int tierIndex,
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
        PersistStandaloneSettings(state, applyCurrentTier: true, syncSessionSettings: true);
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

    private static string FormatStandaloneScale(float value)
    {
        return Mathf.RoundToInt(Mathf.Clamp(value, MinimumOverlayScale, MaximumOverlayScale) * 100f) + "%";
    }

    private static void PersistStandaloneSettings(StandaloneBackpackState state,
        bool applyCurrentTier = false, bool syncSessionSettings = false)
    {
        Configuration.Instance.Save();
        if (applyCurrentTier)
            PlayerBackpack.Instance?.EnsureCorrectTierApplied();
        if (syncSessionSettings)
            ConfigSyncManager.SyncCurrentConfigToClients();

        ModLogger.Info("[BackpackUI] Settings saved to MelonPreferences.");
        RefreshStandaloneSettingsPane(state);
        RefreshActiveUiThemes();
        RefreshActiveStorageBackpackLayouts();
        StationBackpackPanelPatch.RefreshActiveLayouts();
        HandoverScreenPatch.RefreshActiveLayouts();
        if (state?.IsOpen == true)
            state.RefreshAction?.Invoke();
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
        background.raycastTarget = false;

        var canvas = Utils.AddComponentSafe<Canvas>(dropdownGo);
        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = 2900;
        }
        Utils.AddComponentSafe<GraphicRaycaster>(dropdownGo);

        state.DropdownRoot = dropdownRoot;
        HideStandaloneDropdown(state);
    }

    /// <summary>
    /// Moves transient controls out of the scaled grid hierarchy. Filter menus and settings
    /// must be allowed to extend beyond the compact backpack body in handover, station, and
    /// storage views without being obscured by the slot surface.
    /// </summary>
    private static void ConfigureStandaloneOverlayLayers(StandaloneBackpackSurface surface,
        StandaloneBackpackState state)
    {
        if (surface?.Container == null || state == null)
            return;

        if (state.DropdownRoot != null && state.HeaderRoot != null)
        {
            var header = state.HeaderRoot;
            var host = surface.Container;
            Canvas.ForceUpdateCanvases();
            var headerBottom = header.TransformPoint(new Vector3(0f, header.rect.yMin, 0f));
            var headerLeft = header.TransformPoint(new Vector3(header.rect.xMin, header.rect.yMin, 0f));
            var headerRight = header.TransformPoint(new Vector3(header.rect.xMax, header.rect.yMin, 0f));
            state.DropdownAnchor = host.InverseTransformPoint(headerBottom) + new Vector3(0f, -4f, 0f);
            state.DropdownWidth = Mathf.Max(140f, Vector3.Distance(headerLeft, headerRight));

            if (state.DropdownRoot.parent != host)
                state.DropdownRoot.SetParent(host, worldPositionStays: false);

            state.DropdownRoot.anchorMin = new Vector2(0.5f, 0.5f);
            state.DropdownRoot.anchorMax = new Vector2(0.5f, 0.5f);
            state.DropdownRoot.pivot = new Vector2(0.5f, 1f);
            state.DropdownRoot.anchoredPosition = state.DropdownAnchor;
            state.DropdownRoot.sizeDelta = new Vector2(state.DropdownWidth, 1f);
            state.DropdownRoot.localScale = Vector3.one;
        }
    }

    private static void ShowStandaloneDropdown(StandaloneBackpackState state,
        StandaloneBackpackDropdown dropdown)
    {
        if (state?.DropdownRoot == null)
            return;

        var options = BuildStandaloneDropdownOptions(state, dropdown);
        if (options.Count == 0)
        {
            HideStandaloneDropdown(state);
            return;
        }

        state.ActiveDropdown = dropdown;
        state.DropdownOptions.Clear();
        state.DropdownOptions.AddRange(options);
        var height = 6f + (options.Count * 24f);
        state.DropdownRoot.sizeDelta = new Vector2(Mathf.Max(140f, state.DropdownWidth), height);
        state.DropdownRoot.anchoredPosition = state.DropdownAnchor;
        state.DropdownRoot.gameObject.SetActive(true);
        state.DropdownRoot.SetAsLastSibling();

        ModLogger.Debug($"[BackpackUI] Dropdown opened: {dropdown}, options={options.Count}.");

        for (var i = 0; i < state.DropdownOptionButtons.Count; i++)
            state.DropdownOptionButtons[i].gameObject.SetActive(i < options.Count);

        for (var i = 0; i < options.Count; i++)
            ConfigureStandaloneDropdownOption(state, i, options[i], dropdown);

        if (state.KeyboardFocusKind == StandaloneBackpackKeyboardFocusKind.Control)
        {
            state.KeyboardDropdownOptionIndex = GetStandaloneSelectedDropdownOptionIndex(state, dropdown);
            if (state.KeyboardDropdownOptionIndex < 0)
                state.KeyboardDropdownOptionIndex = 0;
        }
        else
        {
            state.KeyboardDropdownOptionIndex = -1;
        }
        RefreshStandaloneDropdownKeyboardFocusPresentation(state);

        PlayStandaloneDropdownOpen(state);
    }

    private static List<StandaloneBackpackDropdownOption> BuildStandaloneDropdownOptions(
        StandaloneBackpackState state, StandaloneBackpackDropdown dropdown)
    {
        var options = new List<StandaloneBackpackDropdownOption>();
        var backpackSlots = GetStandaloneSourceSlots(state);
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
                    RefreshStandaloneFilterView(state);
                });
                for (var i = 0; i < values.Count; i++)
                {
                    var value = values[i];
                    AddStandaloneDropdownOption(options, value.ToUpperInvariant(), () =>
                    {
                        state.TypeFilter = value;
                        RefreshStandaloneFilterView(state);
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
                    RefreshStandaloneFilterView(state);
                });
                for (var i = 0; i < values.Count; i++)
                {
                    var value = values[i];
                    AddStandaloneDropdownOption(options, value.ToUpperInvariant(), () =>
                    {
                        state.QualityFilter = value;
                        RefreshStandaloneFilterView(state);
                    }, showQualityStar: true, qualityStarColor: GetQualityStarColor(value));
                }
                break;
            }
            case StandaloneBackpackDropdown.SortDirection:
                AddStandaloneDropdownOption(options, "ASCENDING", () => SetStandaloneSortDirection(state,
                    StandaloneBackpackSortDirection.Ascending));
                AddStandaloneDropdownOption(options, "DESCENDING", () => SetStandaloneSortDirection(state,
                    StandaloneBackpackSortDirection.Descending));
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

    private static void SetStandaloneSortMode(StandaloneBackpackState state,
        StandaloneBackpackSortMode sortMode)
    {
        if (state == null)
            return;

        var previousSortMode = state.SortMode;
        state.SortMode = sortMode;
        UpdateStandaloneFilterLabels(state);
        ModLogger.Info($"[BackpackUI] Sort changed: {GetSortModeLabel(previousSortMode)} -> {GetSortModeLabel(sortMode)} "
            + $"({GetSortDirectionLabel(state.SortDirection)}).");
        RefreshStandaloneFilterView(state);
    }

    private static void SetStandaloneSortDirection(StandaloneBackpackState state,
        StandaloneBackpackSortDirection sortDirection)
    {
        if (state == null)
            return;

        var previousSortDirection = state.SortDirection;
        state.SortDirection = sortDirection;
        UpdateStandaloneFilterLabels(state);
        ModLogger.Info($"[BackpackUI] Sort order changed: {GetSortDirectionLabel(previousSortDirection)} -> "
            + $"{GetSortDirectionLabel(sortDirection)}.");
        RefreshStandaloneFilterView(state);
    }

    /// <summary>
    /// Commits a predictable organization layout to the local backpack's backing slots. The
    /// browser's display tabs remain a harmless projection; this action explicitly persists
    /// Type, Name, Quality, and Quantity ordering through saves and multiplayer snapshots.
    /// </summary>
    private static void OrganizeStandaloneBackpack(StandaloneBackpackState state)
    {
        var backpackSlots = GetStandaloneSourceSlots(state);
        if (!CanOrganizeStandaloneBackpack(state, backpackSlots))
            return;

        try
        {
            var movableSlots = new List<ItemSlot>();
            var protectedSlotCount = 0;
            for (var i = 0; i < backpackSlots.Count; i++)
            {
                var slot = backpackSlots[i];
                if (slot == null)
                    continue;

                if (ShouldKeepStandaloneSlotFixed(slot))
                {
                    protectedSlotCount++;
                    continue;
                }

                movableSlots.Add(slot);
            }

            var orderedSourceSlots = movableSlots.Where(slot => slot.ItemInstance != null).ToList();
            if (orderedSourceSlots.Count == 0)
                return;

            orderedSourceSlots.Sort((left, right) => CompareStandaloneOrganizationSlots(left, right, backpackSlots));

            var targetAssignments = new Dictionary<ItemSlot, ItemInstance>();
            for (var i = 0; i < movableSlots.Count; i++)
                targetAssignments[movableSlots[i]] = i < orderedSourceSlots.Count
                    ? orderedSourceSlots[i].ItemInstance
                    : null;

            var changedSlots = new List<ItemSlot>();
            foreach (var pair in targetAssignments)
            {
                if (!AreSameStandaloneItemInstance(pair.Key.ItemInstance, pair.Value))
                    changedSlots.Add(pair.Key);
            }

            if (changedSlots.Count == 0)
            {
                ModLogger.Info($"[BackpackUI] Organize skipped: the backpack is already ordered by "
                    + "Type, Name, Quality, and Quantity.");
                return;
            }

            // Clear every changed destination before assigning an item to any of them. This avoids
            // transient duplicate references when two occupied slots swap and keeps ItemSlot's
            // own owner/network notifications in the normal game path.
            for (var i = 0; i < changedSlots.Count; i++)
            {
                var slot = changedSlots[i];
                if (slot.ItemInstance != null)
                    slot.ClearStoredInstance();
            }

            for (var i = 0; i < changedSlots.Count; i++)
            {
                var slot = changedSlots[i];
                var item = targetAssignments[slot];
                if (item != null)
                    slot.SetStoredItem(item);
            }

            state.CurrentPage = 0;
            ModLogger.Info($"[BackpackUI] Organized {orderedSourceSlots.Count} backpack item stacks by "
                + "Type, Name, Quality, and Quantity; "
                + $"changedSlots={changedSlots.Count}, protectedSlots={protectedSlotCount}.");
            RefreshStandaloneFilterView(state);
        }
        catch (Exception ex)
        {
            ModLogger.Error("StorageMenuPatch.OrganizeStandaloneBackpack", ex);
        }
    }

    private static bool CanOrganizeStandaloneBackpack(StandaloneBackpackState state, List<ItemSlot> backpackSlots)
    {
        return state != null && state.IsBackpackInventory && backpackSlots != null && backpackSlots.Count > 1 &&
            CountUsedStandaloneSlots(backpackSlots) > 0;
    }

    /// <summary>
    /// Defines PackRat's persistent storage layout. Categories take priority so related drug
    /// products stay together; names then identify the product, and higher quality / larger
    /// stacks are kept first within otherwise equivalent entries.
    /// </summary>
    private static int CompareStandaloneOrganizationSlots(ItemSlot left, ItemSlot right, List<ItemSlot> originalSlots)
    {
        var comparison = string.Compare(GetSlotType(left), GetSlotType(right), StringComparison.OrdinalIgnoreCase);
        if (comparison != 0)
            return comparison;

        comparison = string.Compare(GetSlotName(left), GetSlotName(right), StringComparison.OrdinalIgnoreCase);
        if (comparison != 0)
            return comparison;

        var qualityComparison = GetQualitySortRank(GetSlotQuality(right)).CompareTo(GetQualitySortRank(GetSlotQuality(left)));
        if (qualityComparison != 0)
            return qualityComparison;

        var quantityComparison = GetSlotQuantity(right).CompareTo(GetSlotQuantity(left));
        if (quantityComparison != 0)
            return quantityComparison;

        return originalSlots.IndexOf(left).CompareTo(originalSlots.IndexOf(right));
    }

    private static bool ShouldKeepStandaloneSlotFixed(ItemSlot slot)
    {
        if (slot == null)
            return true;

        if (slot.IsLocked || slot.IsAddLocked || slot.IsRemovalLocked)
            return true;

        return Configuration.Instance.ProtectFavoritesFromOrganization &&
            BackpackFavorites.IsFavorite(GetSlotDefinitionId(slot));
    }

    /// <summary>
    /// Merges compatible partial stacks in the main backpack using Schedule I's own quick-move
    /// transfer sequence, then packs the surviving stacks into the earliest available movable
    /// slots. The action reduces fragmentation while keeping the current item order intact.
    /// </summary>
    private static void ConsolidateStandaloneBackpack(StandaloneBackpackState state)
    {
        var backpackSlots = GetStandaloneSourceSlots(state);
        if (!CanStackStandaloneBackpack(state, backpackSlots))
            return;

        var movedQuantity = 0;
        var mergedStackCount = 0;
        try
        {
            // Work from a stable slot snapshot. Each game-owned transfer can clear a source,
            // but never adds or removes a slot from the backpack owner.
            var slotSnapshot = backpackSlots.Where(slot => slot != null).ToList();
            for (var sourceIndex = 0; sourceIndex < slotSnapshot.Count; sourceIndex++)
            {
                var source = slotSnapshot[sourceIndex];
                if (ShouldKeepStandaloneSlotFixed(source) || source.ItemInstance == null)
                    continue;

                var sourceItem = source.ItemInstance;
                var sourceQuantity = GetWholeStandaloneSlotQuantity(source);
                if (sourceQuantity <= 0)
                    continue;

                for (var targetIndex = 0; targetIndex < slotSnapshot.Count && source.ItemInstance != null;
                     targetIndex++)
                {
                    var target = slotSnapshot[targetIndex];
                    if (ReferenceEquals(source, target) || ShouldKeepStandaloneSlotFixed(target) ||
                        target.ItemInstance == null)
                        continue;

                    // These checks mirror the game's quick-move guards but limit the action to
                    // existing compatible stacks. Empty slots are intentionally not considered.
                    if (!target.DoesItemMatchHardFilters(sourceItem) ||
                        !target.ItemInstance.CanStackWith(sourceItem, checkQuantities: false))
                        continue;

                    var capacity = target.GetCapacityForItem(sourceItem, checkPlayerFilters: false);
                    var amount = Mathf.Min(capacity, sourceQuantity);
                    if (amount <= 0)
                        continue;

                    var transfer = sourceItem.GetCopy(amount);
                    if (transfer == null)
                    {
                        ModLogger.Warn("[BackpackUI] Consolidate aborted: Schedule I could not copy a source stack.");
                        RefreshStandaloneFilterView(state);
                        return;
                    }

                    var targetBefore = GetWholeStandaloneSlotQuantity(target);
                    sourceQuantity = GetWholeStandaloneSlotQuantity(source);
                    if (sourceQuantity < amount)
                    {
                        ModLogger.Warn("[BackpackUI] Consolidate aborted: source quantity changed before transfer.");
                        RefreshStandaloneFilterView(state);
                        return;
                    }

                    // This ordering is the exact native ItemUIManager quick-move sequence:
                    // add a game-created copy to the target, then remove that amount from source.
                    target.AddItem(transfer);
                    if (GetWholeStandaloneSlotQuantity(target) != targetBefore + amount)
                    {
                        ModLogger.Warn("[BackpackUI] Consolidate aborted: target rejected the native transfer.");
                        RefreshStandaloneFilterView(state);
                        return;
                    }

                    source.ChangeQuantity(-amount);
                    if (GetWholeStandaloneSlotQuantity(source) != sourceQuantity - amount)
                    {
                        ModLogger.Warn("[BackpackUI] Consolidate aborted: source did not acknowledge the transfer.");
                        RefreshStandaloneFilterView(state);
                        return;
                    }

                    movedQuantity += amount;
                    mergedStackCount++;
                    MarkStandaloneRecentChange(state, source);
                    sourceItem = source.ItemInstance;
                    sourceQuantity = GetWholeStandaloneSlotQuantity(source);
                }
            }

            var compactedSlotCount = CompactStandaloneBackpackSlots(state, slotSnapshot);
            if (mergedStackCount == 0 && compactedSlotCount == 0)
            {
                ModLogger.Info("[BackpackUI] Stack skipped: the backpack is already consolidated and compact.");
                return;
            }

            ModLogger.Info($"[BackpackUI] Stacked {mergedStackCount} compatible transfers " +
                $"({movedQuantity} items moved) and compacted {compactedSlotCount} slots.");
            RefreshStandaloneFilterView(state);
        }
        catch (Exception ex)
        {
            ModLogger.Error("StorageMenuPatch.ConsolidateStandaloneBackpack", ex);
            RefreshStandaloneFilterView(state);
        }
    }

    private static bool CanStackStandaloneBackpack(StandaloneBackpackState state, List<ItemSlot> backpackSlots)
    {
        if (state == null || !state.IsHotkeyBackpack || backpackSlots == null || backpackSlots.Count < 2)
            return false;

        for (var sourceIndex = 0; sourceIndex < backpackSlots.Count; sourceIndex++)
        {
            var source = backpackSlots[sourceIndex];
            if (ShouldKeepStandaloneSlotFixed(source) || source?.ItemInstance == null ||
                GetWholeStandaloneSlotQuantity(source) <= 0)
                continue;

            for (var targetIndex = 0; targetIndex < backpackSlots.Count; targetIndex++)
            {
                var target = backpackSlots[targetIndex];
                if (ReferenceEquals(source, target) || ShouldKeepStandaloneSlotFixed(target) ||
                    target?.ItemInstance == null)
                    continue;

                if (target.DoesItemMatchHardFilters(source.ItemInstance) &&
                    target.ItemInstance.CanStackWith(source.ItemInstance, checkQuantities: false) &&
                    target.GetCapacityForItem(source.ItemInstance, checkPlayerFilters: false) > 0)
                    return true;
            }
        }

        return CanCompactStandaloneBackpackSlots(backpackSlots);
    }

    /// <summary>
    /// Reports whether empty movable slots exist before a surviving item. This is deliberately
    /// based on backing-slot order, so a stack action draws items forward across page boundaries
    /// without re-sorting the player's categories, names, or favorites.
    /// </summary>
    private static bool CanCompactStandaloneBackpackSlots(List<ItemSlot> backpackSlots)
    {
        if (backpackSlots == null || backpackSlots.Count < 2)
            return false;

        var movableSlots = backpackSlots.Where(slot => slot != null && !ShouldKeepStandaloneSlotFixed(slot)).ToList();
        var sourceItems = movableSlots.Where(slot => slot.ItemInstance != null)
            .Select(slot => slot.ItemInstance).ToList();
        for (var i = 0; i < movableSlots.Count; i++)
        {
            var expectedItem = i < sourceItems.Count ? sourceItems[i] : null;
            if (!AreSameStandaloneItemInstance(movableSlots[i].ItemInstance, expectedItem))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Packs existing stack instances into the first non-protected backing slots. Quantities are
    /// never written here: all merging happened through native transfers before this reorder.
    /// Clearing destinations before assigning their final game-owned instances avoids transient
    /// duplicate references while items move between pages.
    /// </summary>
    private static int CompactStandaloneBackpackSlots(StandaloneBackpackState state, List<ItemSlot> backpackSlots)
    {
        if (!CanCompactStandaloneBackpackSlots(backpackSlots))
            return 0;

        var movableSlots = backpackSlots.Where(slot => slot != null && !ShouldKeepStandaloneSlotFixed(slot)).ToList();
        var sourceItems = movableSlots.Where(slot => slot.ItemInstance != null)
            .Select(slot => slot.ItemInstance).ToList();
        var changedSlots = new List<ItemSlot>();
        var assignments = new Dictionary<ItemSlot, ItemInstance>();
        for (var i = 0; i < movableSlots.Count; i++)
        {
            var item = i < sourceItems.Count ? sourceItems[i] : null;
            assignments[movableSlots[i]] = item;
            if (!AreSameStandaloneItemInstance(movableSlots[i].ItemInstance, item))
                changedSlots.Add(movableSlots[i]);
        }

        for (var i = 0; i < changedSlots.Count; i++)
        {
            if (changedSlots[i].ItemInstance != null)
                changedSlots[i].ClearStoredInstance();
        }

        for (var i = 0; i < changedSlots.Count; i++)
        {
            var item = assignments[changedSlots[i]];
            if (item == null)
                continue;

            changedSlots[i].SetStoredItem(item);
            MarkStandaloneRecentChange(state, changedSlots[i]);
        }

        return changedSlots.Count;
    }

    private static int GetWholeStandaloneSlotQuantity(ItemSlot slot)
    {
        return Mathf.Max(0, Mathf.FloorToInt(GetSlotQuantity(slot)));
    }

    private static void MarkStandaloneRecentChange(StandaloneBackpackState state, ItemSlot slot)
    {
        if (state == null)
            return;

        var definitionId = GetSlotDefinitionId(slot);
        if (!string.IsNullOrWhiteSpace(definitionId))
            state.RecentItemTimestamps[definitionId] = Time.unscaledTime;
    }

    private static bool AreSameStandaloneItemInstance(ItemInstance left, ItemInstance right)
    {
        return ReferenceEquals(left, right) || (left != null && left.Equals(right));
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
            case StandaloneBackpackDropdown.SortDirection:
                if (label == "ASCENDING")
                    return state.SortDirection == StandaloneBackpackSortDirection.Ascending;
                if (label == "DESCENDING")
                    return state.SortDirection == StandaloneBackpackSortDirection.Descending;
                return false;
            default:
                return false;
        }
    }

    private static int GetStandaloneSelectedDropdownOptionIndex(StandaloneBackpackState state,
        StandaloneBackpackDropdown dropdown)
    {
        if (state == null)
            return -1;

        for (var i = 0; i < state.DropdownOptions.Count; i++)
        {
            if (IsStandaloneDropdownOptionSelected(state, dropdown, i))
                return i;
        }

        return -1;
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
        state.KeyboardDropdownOptionIndex = -1;
        ++state.DropdownMotionGeneration;
        if (state.DropdownRoot != null)
        {
            if (state.DropdownCanvasGroup != null)
            {
                state.DropdownCanvasGroup.alpha = 1f;
                state.DropdownCanvasGroup.blocksRaycasts = false;
                state.DropdownCanvasGroup.interactable = false;
            }
            state.DropdownRoot.localScale = Vector3.one;
            state.DropdownRoot.gameObject.SetActive(false);
        }
    }

    private static void PlayStandaloneDropdownOpen(StandaloneBackpackState state)
    {
        if (state?.DropdownRoot == null)
            return;

        state.DropdownCanvasGroup ??= Utils.GetOrAddComponentSafe<CanvasGroup>(state.DropdownRoot.gameObject);
        if (state.DropdownCanvasGroup == null)
            return;

        var generation = ++state.DropdownMotionGeneration;
        state.DropdownCanvasGroup.blocksRaycasts = true;
        state.DropdownCanvasGroup.interactable = true;
        if (!Configuration.Instance.EnableUiAnimations)
        {
            state.DropdownCanvasGroup.alpha = 1f;
            state.DropdownRoot.localScale = Vector3.one;
            return;
        }

        state.DropdownCanvasGroup.alpha = 0f;
        state.DropdownRoot.localScale = Configuration.Instance.ReduceUiMotion ? Vector3.one : new Vector3(0.98f, 0.98f, 1f);
        MelonCoroutines.Start(RunStandaloneDropdownOpenMotion(state, generation));
    }

    private static IEnumerator RunStandaloneDropdownOpenMotion(StandaloneBackpackState state, int generation)
    {
        var elapsed = 0f;
        while (state != null && state.DropdownMotionGeneration == generation && elapsed < DropdownOpenDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            var t = EaseOutCubic(Mathf.Clamp01(elapsed / DropdownOpenDuration));
            if (state.DropdownCanvasGroup != null)
                state.DropdownCanvasGroup.alpha = t;
            if (state.DropdownRoot != null && !Configuration.Instance.ReduceUiMotion)
                state.DropdownRoot.localScale = Vector3.Lerp(new Vector3(0.98f, 0.98f, 1f), Vector3.one, t);
            yield return null;
        }

        if (state != null && state.DropdownMotionGeneration == generation && state.DropdownRoot != null)
        {
            if (state.DropdownCanvasGroup != null)
                state.DropdownCanvasGroup.alpha = 1f;
            state.DropdownRoot.localScale = Vector3.one;
        }
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
            case StandaloneBackpackSortMode.Favorites:
                return "FAV";
            case StandaloneBackpackSortMode.Recent:
                return "RECENT";
            default:
                return "ALL";
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

        if (menu.SlotContainer != null)
            menu.SlotContainer.localScale = Vector3.one;
        if (menu.CloseButtonContainer != null)
            menu.CloseButtonContainer.localScale = Vector3.one;

        if (menu.TitleLabel != null)
            menu.TitleLabel.gameObject.SetActive(true);
        if (menu.SubtitleLabel != null)
            menu.SubtitleLabel.gameObject.SetActive(true);

        if (StandaloneBackpackPanels.TryGetValue(menu.GetInstanceID(), out var state))
        {
            SnapStandaloneMotionState(state);
            state.IsOpen = false;
            state.SettingsOpen = false;
            state.SettingsClosing = false;
            state.VisualPresented = false;
            state.AwaitingToggleKey = false;
            HideStandaloneDropdown(state);
            if (state.SettingsRoot != null)
                state.SettingsRoot.gameObject.SetActive(false);
            if (state.VisualRoot != null)
                state.VisualRoot.gameObject.SetActive(false);
            if (state.PagingRoot != null)
                state.PagingRoot.localScale = Vector3.one;
        }
    }

    private static StandaloneBackpackState EnsureStandaloneBackpackPaging(StandaloneBackpackSurface surface)
    {
        if (surface?.Container == null)
            return null;

        var id = surface.Id;
        if (!StandaloneBackpackPanels.TryGetValue(id, out var state))
        {
            state = new StandaloneBackpackState();
            StandaloneBackpackPanels[id] = state;
        }

        if (state.PagingRoot == null)
        {
            var pagingGo = new GameObject("PackRat_BackpackPaging");
            var pagingRt = pagingGo.AddComponent<RectTransform>();
            pagingRt.SetParent(surface.Container, worldPositionStays: false);
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
                state.PendingPageWipeDirection = -1;
                state.RefreshAction?.Invoke();
            };

        if (state.NextAction == null)
            state.NextAction = () =>
            {
                var filteredSlots = GetDisplayBackpackSlots(GetStandaloneSourceSlots(state), state);
                var totalPages = Mathf.Max(1, Mathf.CeilToInt(filteredSlots.Count / (float)StandaloneBackpackSlotsPerPage));
                if (state.LastPageInputFrame == Time.frameCount || state.CurrentPage >= totalPages - 1)
                    return;

                state.LastPageInputFrame = Time.frameCount;
                state.CurrentPage++;
                state.PendingPageWipeDirection = 1;
                state.RefreshAction?.Invoke();
            };

        EventHelper.RemoveListener(state.PrevAction, state.PrevButton.onClick);
        EventHelper.AddListener(state.PrevAction, state.PrevButton.onClick);
        EventHelper.RemoveListener(state.NextAction, state.NextButton.onClick);
        EventHelper.AddListener(state.NextAction, state.NextButton.onClick);
        return state;
    }

    private static void PositionStandalonePaging(StandaloneBackpackSurface surface, StandaloneBackpackState state,
        Vector2 gridSize)
    {
        if (surface?.SlotContainer == null || state?.PagingRoot == null)
            return;

        var scale = GetStandaloneBackpackScale(surface.LayoutView);
        state.PagingRoot.localScale = Vector3.one * scale;
        var gridBottom = surface.SlotContainer.anchoredPosition.y - gridSize.y * scale * 0.5f;
        var closeHeight = surface.PositionCloseControl && surface.CloseButtonContainer != null
            ? surface.CloseButtonContainer.sizeDelta.y * scale
            : 0f;
        state.PagingRoot.anchoredPosition = new Vector2(surface.SlotContainer.anchoredPosition.x,
            gridBottom - closeHeight - 32f * scale);
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
            state.SortDirection = StandaloneBackpackSortDirection.Ascending;
            state.RecentItemTimestamps.Clear();
            state.OpenItemQuantities.Clear();
            state.RecentBaselineCaptured = false;
            state.SettingsOpen = false;
            state.SettingsClosing = false;
            state.VisualPresented = false;
            state.AwaitingToggleKey = false;
            SnapStandaloneMotionState(state);
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
    /// A mouse click inside the PackRat-owned browser returns it to mouse mode. This only clears
    /// PackRat's presentation state; it neither consumes the click nor changes the game's slot
    /// selection, drag/drop, or tooltip behavior.
    /// </summary>
    public static void ClearStandaloneBackpackKeyboardFocusOnPointerInput()
    {
        if (!Input.GetMouseButtonDown(0))
            return;

        try
        {
            var menu = Singleton<StorageMenu>.Instance;
            if (menu == null || !StandaloneBackpackPanels.TryGetValue(menu.GetInstanceID(), out var state) ||
                !state.IsOpen || !state.IsHotkeyBackpack || !IsPointerOverStandaloneBackpackInterface(state))
                return;

            state.KeyboardFocusKind = StandaloneBackpackKeyboardFocusKind.None;
            state.KeyboardFocusControlIndex = -1;
            state.KeyboardDropdownOptionIndex = -1;
            state.KeyboardSettingsControlIndex = -1;
            RefreshStandaloneKeyboardFocusPresentation(state);
            RefreshStandaloneDropdownKeyboardFocusPresentation(state);
            RefreshStandaloneSettingsKeyboardFocusPresentation(state);
        }
        catch (Exception ex)
        {
            ModLogger.Error("StorageMenuPatch.ClearStandaloneBackpackKeyboardFocusOnPointerInput", ex);
        }
    }

    private static bool IsPointerOverStandaloneBackpackInterface(StandaloneBackpackState state)
    {
        if (state == null)
            return false;

        var pointer = (Vector2)Input.mousePosition;
        return IsPointerOverStandaloneRect(state.VisualRoot, pointer) ||
            IsPointerOverStandaloneRect(state.DropdownRoot, pointer) ||
            IsPointerOverStandaloneRect(state.SettingsRoot, pointer) ||
            IsPointerOverStandaloneRect(state.PagingRoot, pointer);
    }

    private static bool IsPointerOverStandaloneRect(RectTransform rect, Vector2 pointer)
    {
        return rect != null && rect.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(rect, pointer);
    }

    /// <summary>
    /// Routes keyboard focus only while the standalone hotkey backpack is open. It deliberately
    /// targets PackRat-owned controls, leaving the game's slots and drag/drop interaction under
    /// mouse ownership until a dedicated keyboard item-action model is designed.
    /// </summary>
    public static bool HandleStandaloneBackpackKeyboardNavigation()
    {
        var tabRequested = Input.GetKeyDown(KeyCode.Tab);
        var escapeRequested = Input.GetKeyDown(KeyCode.Escape);
        var activateRequested = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) ||
            Input.GetKeyDown(KeyCode.Space);
        var leftRequested = Input.GetKeyDown(KeyCode.LeftArrow);
        var rightRequested = Input.GetKeyDown(KeyCode.RightArrow);
        var upRequested = Input.GetKeyDown(KeyCode.UpArrow);
        var downRequested = Input.GetKeyDown(KeyCode.DownArrow);
        if (!tabRequested && !escapeRequested && !activateRequested && !leftRequested && !rightRequested &&
            !upRequested && !downRequested)
            return false;

        try
        {
            var menu = Singleton<StorageMenu>.Instance;
            if (menu == null || !StandaloneBackpackPanels.TryGetValue(menu.GetInstanceID(), out var state) ||
                !state.IsOpen || !state.IsHotkeyBackpack)
                return false;

            if (state.SettingsOpen)
            {
                return HandleStandaloneSettingsKeyboardNavigation(state, tabRequested, escapeRequested,
                    activateRequested, leftRequested, rightRequested, upRequested, downRequested);
            }

            if (state.DropdownRoot != null && state.DropdownRoot.gameObject.activeInHierarchy)
            {
                return HandleStandaloneDropdownKeyboardNavigation(state, tabRequested, escapeRequested,
                    activateRequested, leftRequested, rightRequested, upRequested, downRequested);
            }

            if (state.SearchInput != null && state.SearchInput.isFocused)
            {
                if (tabRequested)
                {
                    MoveStandaloneKeyboardControlFocus(state, Input.GetKey(KeyCode.LeftShift) ||
                        Input.GetKey(KeyCode.RightShift) ? -1 : 1);
                    return true;
                }

                if (escapeRequested)
                {
                    ClearStandaloneKeyboardFocus(state);
                    return true;
                }

                // The focused InputField owns normal text editing, including its cursor keys.
                return false;
            }

            if (tabRequested)
            {
                MoveStandaloneKeyboardControlFocus(state, Input.GetKey(KeyCode.LeftShift) ||
                    Input.GetKey(KeyCode.RightShift) ? -1 : 1);
                return true;
            }

            if (escapeRequested && state.KeyboardFocusKind != StandaloneBackpackKeyboardFocusKind.None)
            {
                ClearStandaloneKeyboardFocus(state);
                return true;
            }

            if (activateRequested && state.KeyboardFocusKind == StandaloneBackpackKeyboardFocusKind.Control)
            {
                ActivateStandaloneKeyboardControl(state);
                return true;
            }

            if (state.KeyboardFocusKind == StandaloneBackpackKeyboardFocusKind.Control)
            {
                if (leftRequested)
                {
                    MoveStandaloneKeyboardControlFocus(state, -1);
                    return true;
                }

                if (rightRequested)
                {
                    MoveStandaloneKeyboardControlFocus(state, 1);
                    return true;
                }

                // Keep the old arrow-key pagination dormant after focus deliberately enters a
                // control. Escape returns to the unfocused state, where that pagination remains
                // exactly as it was before keyboard navigation was added.
                return upRequested || downRequested;
            }

            return false;
        }
        catch (Exception ex)
        {
            ModLogger.Error("StorageMenuPatch.HandleStandaloneBackpackKeyboardNavigation", ex);
            return false;
        }
    }

    /// <summary>
    /// Keeps a PackRat dropdown self-contained while it is open: navigation never falls through
    /// to the backpack's page controls or the game's global selection state.
    /// </summary>
    private static bool HandleStandaloneDropdownKeyboardNavigation(StandaloneBackpackState state, bool tabRequested,
        bool escapeRequested, bool activateRequested, bool leftRequested, bool rightRequested, bool upRequested,
        bool downRequested)
    {
        if (state == null || state.DropdownOptions.Count == 0)
            return false;

        if (escapeRequested)
        {
            HideStandaloneDropdown(state);
            RefreshStandaloneKeyboardFocusPresentation(state);
            return true;
        }

        var direction = 0;
        if (tabRequested)
            direction = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
        else if (leftRequested || upRequested)
            direction = -1;
        else if (rightRequested || downRequested)
            direction = 1;

        if (direction != 0)
        {
            MoveStandaloneDropdownKeyboardFocus(state, direction);
            return true;
        }

        if (activateRequested)
        {
            if (state.KeyboardDropdownOptionIndex < 0)
                state.KeyboardDropdownOptionIndex = 0;
            SelectStandaloneDropdownOption(state, state.KeyboardDropdownOptionIndex);
            RefreshStandaloneKeyboardFocusPresentation(state);
            return true;
        }

        return false;
    }

    private static void MoveStandaloneDropdownKeyboardFocus(StandaloneBackpackState state, int direction)
    {
        if (state == null || state.DropdownOptions.Count == 0)
            return;

        var index = state.KeyboardDropdownOptionIndex;
        if (index < 0)
            index = direction < 0 ? state.DropdownOptions.Count - 1 : 0;
        else
            index = (index + direction) % state.DropdownOptions.Count;

        state.KeyboardDropdownOptionIndex = index < 0 ? index + state.DropdownOptions.Count : index;
        RefreshStandaloneDropdownKeyboardFocusPresentation(state);
    }

    private static void RefreshStandaloneDropdownKeyboardFocusPresentation(StandaloneBackpackState state)
    {
        if (state == null)
            return;

        for (var i = 0; i < state.DropdownOptionButtons.Count; i++)
        {
            var button = state.DropdownOptionButtons[i];
            SetStandaloneKeyboardFocusVisual(button?.transform,
                state.ActiveDropdown != StandaloneBackpackDropdown.None && i == state.KeyboardDropdownOptionIndex);
        }
    }

    /// <summary>
    /// Provides a complete keyboard loop for the PackRat-owned settings modal. The game remains
    /// responsible for inventory slots and drag/drop; this route only invokes settings controls.
    /// </summary>
    private static bool HandleStandaloneSettingsKeyboardNavigation(StandaloneBackpackState state, bool tabRequested,
        bool escapeRequested, bool activateRequested, bool leftRequested, bool rightRequested, bool upRequested,
        bool downRequested)
    {
        if (state == null)
            return false;

        if (escapeRequested)
        {
            ToggleStandaloneSettings(state);
            return true;
        }

        var direction = 0;
        if (tabRequested)
            direction = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
        else if (leftRequested || upRequested)
            direction = -1;
        else if (rightRequested || downRequested)
            direction = 1;

        if (direction != 0)
        {
            MoveStandaloneSettingsKeyboardFocus(state, direction);
            return true;
        }

        if (activateRequested)
        {
            ActivateStandaloneSettingsKeyboardControl(state);
            return true;
        }

        return false;
    }

    private static List<StandaloneBackpackKeyboardControl> GetStandaloneKeyboardControls(
        StandaloneBackpackState state)
    {
        var controls = new List<StandaloneBackpackKeyboardControl>();
        if (state == null)
            return controls;

        AddStandaloneKeyboardControl(controls, state.SearchInput, null, isSearchInput: true);
        AddStandaloneKeyboardControl(controls, state.TypeFilterButton, state.TypeFilterAction);
        AddStandaloneKeyboardControl(controls, state.QualityFilterButton, state.QualityFilterAction);
        AddStandaloneKeyboardControl(controls, state.SortDirectionButton, state.SortDirectionAction);
        AddStandaloneKeyboardControl(controls, state.OrganizeButton, state.OrganizeAction);
        AddStandaloneKeyboardControl(controls, state.ConsolidateButton, state.ConsolidateAction);
        // Stack and Settings are visually adjacent in the header, so preserve that adjacency
        // for keyboard traversal instead of making the user cross filters and sort tabs first.
        AddStandaloneKeyboardControl(controls, state.SettingsButton, null);
        AddStandaloneKeyboardControl(controls, state.ClearFiltersButton, state.ClearFiltersAction);

        for (var i = 0; i < state.SortTabs.Count; i++)
        {
            var tab = state.SortTabs[i];
            AddStandaloneKeyboardControl(controls, tab?.Button, tab?.SelectAction);
        }

        AddStandaloneKeyboardControl(controls, state.DoneButton, null);
        AddStandaloneKeyboardControl(controls, state.PrevButton, state.PrevAction);
        AddStandaloneKeyboardControl(controls, state.NextButton, state.NextAction);
        return controls;
    }

    private static List<StandaloneBackpackKeyboardControl> GetStandaloneSettingsKeyboardControls(
        StandaloneBackpackState state)
    {
        var controls = new List<StandaloneBackpackKeyboardControl>();
        if (state == null || !state.SettingsOpen)
            return controls;

        // Keep the modal chrome at the head of the traversal even though the active desktop tab
        // changes sibling order to draw above the others.
        AddStandaloneKeyboardControl(controls, state.SettingsCloseButton, null);
        AddStandaloneKeyboardControl(controls, state.SettingsGeneralButton, null);
        AddStandaloneKeyboardControl(controls, state.SettingsThemeButton, null);
        AddStandaloneKeyboardControl(controls, state.SettingsTiersButton, null);
        AddStandaloneKeyboardControl(controls, state.SettingsLayoutButton, null);
        AddStandaloneKeyboardControl(controls, state.SettingsRoutingButton, null);
        AddStandaloneKeyboardControl(controls, state.SettingsMetricsButton, null);

        var page = GetStandaloneSettingsPageRoot(state);
        if (page == null || !page.gameObject.activeInHierarchy)
            return controls;

        var pageControls = page.GetComponentsInChildren<Selectable>(includeInactive: false);
        for (var i = 0; i < pageControls.Length; i++)
            AddStandaloneKeyboardControl(controls, pageControls[i], null);

        return controls;
    }

    private static void AddStandaloneKeyboardControl(List<StandaloneBackpackKeyboardControl> controls,
        Selectable selectable, Action activateAction, bool isSearchInput = false)
    {
        if (controls == null || selectable == null || !selectable.gameObject.activeInHierarchy ||
            !selectable.interactable)
            return;

        controls.Add(new StandaloneBackpackKeyboardControl
        {
            Selectable = selectable,
            ActivateAction = activateAction,
            IsSearchInput = isSearchInput
        });
    }

    private static void MoveStandaloneSettingsKeyboardFocus(StandaloneBackpackState state, int direction)
    {
        var controls = GetStandaloneSettingsKeyboardControls(state);
        if (controls.Count == 0)
        {
            state.KeyboardSettingsControlIndex = -1;
            return;
        }

        var index = state.KeyboardSettingsControlIndex;
        if (index < 0)
            index = direction < 0 ? controls.Count - 1 : 0;
        else
            index = (index + direction) % controls.Count;

        FocusStandaloneSettingsKeyboardControl(state, index, controls);
    }

    private static void FocusStandaloneSettingsKeyboardControl(StandaloneBackpackState state, int index,
        List<StandaloneBackpackKeyboardControl> controls = null)
    {
        controls = controls ?? GetStandaloneSettingsKeyboardControls(state);
        if (state == null || controls.Count == 0)
        {
            if (state != null)
                state.KeyboardSettingsControlIndex = -1;
            return;
        }

        state.KeyboardSettingsControlIndex = Mathf.Clamp(index, 0, controls.Count - 1);
        RefreshStandaloneSettingsKeyboardFocusPresentation(state);
    }

    private static void ActivateStandaloneSettingsKeyboardControl(StandaloneBackpackState state)
    {
        var controls = GetStandaloneSettingsKeyboardControls(state);
        if (state == null || controls.Count == 0)
            return;

        if (state.KeyboardSettingsControlIndex < 0 || state.KeyboardSettingsControlIndex >= controls.Count)
            state.KeyboardSettingsControlIndex = 0;

        var control = controls[state.KeyboardSettingsControlIndex];
        if (control.ActivateAction != null)
            control.ActivateAction.Invoke();
        else if (control.Selectable is Button button)
            button.onClick.Invoke();
        else if (control.Selectable is Toggle toggle)
            toggle.isOn = !toggle.isOn;

        // Setting changes rebuild their current page to reflect MelonPreferences immediately.
        // Repaint the surviving or rebuilt control at the same logical index.
        RefreshStandaloneSettingsKeyboardFocusPresentation(state);
    }

    private static void RefreshStandaloneSettingsKeyboardFocusPresentation(StandaloneBackpackState state)
    {
        if (state == null)
            return;

        var controls = GetStandaloneSettingsKeyboardControls(state);
        if (state.KeyboardSettingsControlIndex >= controls.Count)
            state.KeyboardSettingsControlIndex = controls.Count > 0 ? 0 : -1;

        for (var i = 0; i < controls.Count; i++)
            SetStandaloneKeyboardFocusVisual(controls[i].Selectable?.transform, i == state.KeyboardSettingsControlIndex);
    }

    private static void MoveStandaloneKeyboardControlFocus(StandaloneBackpackState state, int direction)
    {
        var controls = GetStandaloneKeyboardControls(state);
        if (controls.Count == 0)
        {
            ClearStandaloneKeyboardFocus(state);
            return;
        }

        var index = state.KeyboardFocusKind == StandaloneBackpackKeyboardFocusKind.Control
            ? state.KeyboardFocusControlIndex + direction
            : (direction < 0 ? controls.Count - 1 : 0);
        index = (index % controls.Count + controls.Count) % controls.Count;
        FocusStandaloneKeyboardControl(state, index, controls);
    }

    private static void FocusStandaloneKeyboardControl(StandaloneBackpackState state, int index,
        List<StandaloneBackpackKeyboardControl> controls = null)
    {
        controls = controls ?? GetStandaloneKeyboardControls(state);
        if (state == null || controls.Count == 0)
        {
            ClearStandaloneKeyboardFocus(state);
            return;
        }

        index = Mathf.Clamp(index, 0, controls.Count - 1);
        if (state.SearchInput != null && state.SearchInput.isFocused)
            state.SearchInput.DeactivateInputField();

        state.KeyboardFocusKind = StandaloneBackpackKeyboardFocusKind.Control;
        state.KeyboardFocusControlIndex = index;
        RefreshStandaloneKeyboardFocusPresentation(state);

        if (controls[index].IsSearchInput && state.SearchInput != null)
            state.SearchInput.ActivateInputField();
    }

    private static void ActivateStandaloneKeyboardControl(StandaloneBackpackState state)
    {
        var controls = GetStandaloneKeyboardControls(state);
        if (state == null || state.KeyboardFocusControlIndex < 0 ||
            state.KeyboardFocusControlIndex >= controls.Count)
            return;

        var control = controls[state.KeyboardFocusControlIndex];
        if (control.IsSearchInput && state.SearchInput != null)
        {
            state.SearchInput.ActivateInputField();
            return;
        }

        if (control.ActivateAction != null)
            control.ActivateAction.Invoke();
        else if (control.Selectable is Button button)
            button.onClick.Invoke();
        else if (control.Selectable is Toggle toggle)
            toggle.isOn = !toggle.isOn;

        RefreshStandaloneKeyboardFocusPresentation(state);
    }

    private static void ClearStandaloneKeyboardFocus(StandaloneBackpackState state)
    {
        if (state == null)
            return;

        if (state.SearchInput != null && state.SearchInput.isFocused)
            state.SearchInput.DeactivateInputField();
        state.KeyboardFocusKind = StandaloneBackpackKeyboardFocusKind.None;
        state.KeyboardFocusControlIndex = -1;
        RefreshStandaloneKeyboardFocusPresentation(state);
    }

    private static void RefreshStandaloneKeyboardFocusPresentation(StandaloneBackpackState state)
    {
        if (state == null)
            return;

        var controls = GetStandaloneKeyboardControls(state);
        if (state.KeyboardFocusKind == StandaloneBackpackKeyboardFocusKind.Control &&
            (state.KeyboardFocusControlIndex < 0 || state.KeyboardFocusControlIndex >= controls.Count))
        {
            state.KeyboardFocusKind = StandaloneBackpackKeyboardFocusKind.None;
            state.KeyboardFocusControlIndex = -1;
        }

        for (var i = 0; i < controls.Count; i++)
        {
            SetStandaloneKeyboardFocusVisual(controls[i].Selectable?.transform,
                state.KeyboardFocusKind == StandaloneBackpackKeyboardFocusKind.Control &&
                i == state.KeyboardFocusControlIndex);
        }
    }

    private static void SetStandaloneKeyboardFocusVisual(Transform target, bool visible)
    {
        if (target == null)
            return;

        // Older panel instances may still carry the rectangular child-edge treatment from the
        // first keyboard-navigation pass. Remove it before using Unity's alpha-aware Outline:
        // the component follows the target graphic's own sliced sprite and therefore preserves
        // pills, gentle rounded corners, and the settings desktop-tab silhouette.
        var legacyOutline = target.Find("PackRat_KeyboardFocusOutline");
        if (legacyOutline != null)
            UnityEngine.Object.Destroy(legacyOutline.gameObject);

        // Most PackRat controls keep their Image on the selectable root, but the vanilla Done
        // button may use a child target graphic. Outline that actual graphic so its focus ring
        // follows the rendered button rather than an invisible layout container.
        var selectable = target.GetComponent<Selectable>();
        var graphic = selectable?.targetGraphic ?? target.GetComponent<Graphic>();
        if (graphic == null)
            return;

        var outline = Utils.GetOrAddComponentSafe<Outline>(graphic.gameObject);
        if (outline == null)
            return;

        outline.effectColor = new Color32(54, 177, 239, 255);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        outline.useGraphicAlpha = true;
        outline.enabled = visible;
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
            var pageDirection = 0;
            if (previousRequested)
            {
                requestedPage--;
                pageDirection = -1;
            }
            else if (nextRequested)
            {
                requestedPage++;
                pageDirection = 1;
            }

            state.LastPageInputFrame = Time.frameCount;
            state.CurrentPage = Mathf.Clamp(requestedPage, 0, totalPages - 1);
            if (state.CurrentPage != requestedPage)
                return true;

            state.PendingPageWipeDirection = pageDirection;
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

            if (GetBackpackSlots().Count == 0)
                return;

            var panel = EnsureBackpackPanel(menu);
            if (panel?.Container == null || panel.SlotUIs == null)
                return;

            panel.Container.gameObject.SetActive(true);
            if (panel.PagingRoot != null)
                panel.PagingRoot.gameObject.SetActive(false);
            ApplyEmbeddedBackpackBrowser(panel.Container, panel.SlotContainer, panel.SlotGridLayout, panel.SlotUIs,
                layoutView: (int)StandaloneBackpackLayoutView.Storage);
            panel.StorageSlotProvider = () => openedOwner.ItemSlots.AsEnumerable().Where(slot => slot != null).ToList();
            // This path is owned by StorageMenu. Station screens use their own station-interface
            // patch and must never inherit storage bulk-transfer actions.
            panel.SupportsStorageBulkTransfer = true;
            EnsureStorageBulkTransferControls(panel);
            RebuildStorageQuickMove(openedOwner, GetBackpackSlots());
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
            existing.Menu = menu;
            EnsureOverlaySorting(existing.Container, menu.Container);
            ConfigureCompactSidePanel(menu, existing);
            EnsurePagingControls(existing);
            return existing;
        }

        var panel = existing ?? new BackpackPanelState();
        panel.Menu = menu;

        var rootObject = new GameObject("PackRat_BackpackStoragePanel");
        var root = rootObject.AddComponent<RectTransform>();
        root.SetParent(menu.Container.parent, worldPositionStays: false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.pivot = new Vector2(0.5f, 0.5f);
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
        root.localScale = Vector3.one;
        root.gameObject.SetActive(false);
        EnsureOverlaySorting(root, menu.Container);

        panel.Container = root;

        // Do not clone the opened container's hierarchy here. Vehicle trunks and several other
        // StorageMenu owners put their live ItemSlotUIs under nested layout/visibility parents.
        // Cloning then rebinding that hierarchy made the PackRat browser either disappear or
        // inherit the trunk's one-column layout. This dedicated, flat grid is PackRat-owned;
        // the native storage container remains untouched beside it.
        var slotContainerObject = new GameObject("PackRat_BackpackSlotContainer");
        var slotContainer = slotContainerObject.AddComponent<RectTransform>();
        slotContainer.SetParent(root, worldPositionStays: false);
        slotContainer.anchorMin = new Vector2(0.5f, 0.5f);
        slotContainer.anchorMax = new Vector2(0.5f, 0.5f);
        slotContainer.pivot = new Vector2(0.5f, 0.5f);
        panel.SlotContainer = slotContainer;

        var sourceGrid = menu.SlotGridLayout;
        var grid = slotContainerObject.AddComponent<GridLayoutGroup>();
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 5;
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.cellSize = GetDedicatedStorageSlotSize(sourceGrid);
        grid.spacing = new Vector2(6f, 6f);
        grid.padding = new RectOffset(0, 0, 0, 0);
        panel.SlotGridLayout = grid;

        var slotTemplate = GetDedicatedStorageSlotTemplate(menu);
        if (slotTemplate == null)
        {
            ModLogger.Warn("[BackpackUI] Storage browser skipped: no ItemSlotUI template was available.");
            UnityEngine.Object.Destroy(rootObject);
            return null;
        }

        var slotUis = new List<ItemSlotUI>(StorageBackpackSlotsPerPage);
        while (slotUis.Count < StorageBackpackSlotsPerPage)
        {
            var slotObject = UnityEngine.Object.Instantiate(slotTemplate.gameObject, slotContainer);
            slotObject.name = $"PackRat_BackpackStorageSlot ({slotUis.Count + 1})";
#if !MONO
            var slotUi = Utils.GetComponentSafe<ItemSlotUI>(slotObject);
#else
            var slotUi = slotObject.GetComponent<ItemSlotUI>();
#endif
            if (slotUi == null)
            {
                UnityEngine.Object.Destroy(slotObject);
                break;
            }

            ResetSlotUi(slotUi);
            slotUi.ClearSlot();
            EnsureDedicatedStorageSlotVisualState(slotUi);
            slotUi.gameObject.SetActive(false);
            slotUis.Add(slotUi);
        }

        panel.SlotUIs = slotUis.ToArray();
        panel.SlotsPerPage = StorageBackpackSlotsPerPage;
        ConfigureCompactSidePanel(menu, panel);
        panel.Initialized = true;
        BackpackPanels[id] = panel;

        EnsurePagingControls(panel);
        return panel;
    }

    /// <summary>
    /// Retrieves the canonical item-slot prefab first so the storage overlay does not inherit a
    /// vehicle trunk's nested layout or hidden presentation state. The live storage menu is a
    /// compatibility fallback only for game versions without the canonical field.
    /// </summary>
    private static ItemSlotUI GetDedicatedStorageSlotTemplate(StorageMenu menu)
    {
        try
        {
            var prefab = Singleton<ItemUIManager>.Instance?.ItemSlotUIPrefab;
            if (prefab != null)
                return prefab;
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"[BackpackUI] Canonical storage slot prefab unavailable: {ex.Message}");
        }

        if (menu?.SlotsUIs == null)
            return null;

        for (var i = 0; i < menu.SlotsUIs.Length; i++)
        {
            if (menu.SlotsUIs[i] != null)
                return menu.SlotsUIs[i];
        }

        return null;
    }

    /// <summary>
    /// Keeps the shared browser's direct grid stable even when the opened storage surface is a
    /// one-column trunk. The source cell size is retained when sane so PackRat continues to
    /// match the game's scale without inheriting its layout constraint or spacing.
    /// </summary>
    private static Vector2 GetDedicatedStorageSlotSize(GridLayoutGroup sourceGrid)
    {
        var sourceSize = sourceGrid != null ? sourceGrid.cellSize : new Vector2(72f, 72f);
        return new Vector2(
            Mathf.Clamp(sourceSize.x, 56f, 96f),
            Mathf.Clamp(sourceSize.y, 56f, 96f)
        );
    }

    /// <summary>
    /// Storage slot prefabs can originate below an inactive or faded container. Restore their
    /// own presentation state after cloning while leaving the original storage slot untouched.
    /// </summary>
    private static void EnsureDedicatedStorageSlotVisualState(ItemSlotUI slotUi)
    {
        if (slotUi == null)
            return;

        var canvasGroups = slotUi.GetComponentsInChildren<CanvasGroup>(includeInactive: true);
        if (canvasGroups == null)
            return;

        for (var i = 0; i < canvasGroups.Length; i++)
        {
            var canvasGroup = canvasGroups[i];
            if (canvasGroup == null)
                continue;

            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
    }

    /// <summary>
    /// Adds the shared browser beside a native StorageEntity view, such as a vehicle trunk.
    /// StorageEntity deliberately is not an IItemSlotOwner in current beta builds, and its own
    /// Open overload has already bound the vanilla slots by the time this postfix runs. Leave
    /// that surface alone; only create PackRat's isolated slot projection and quick-move list.
    /// </summary>
    private static void ApplyBackpackSidePanel(StorageMenu menu, StorageEntity openedEntity)
    {
        try
        {
            HideBackpackSidePanel(menu);

            if (menu == null || openedEntity == null || GetBackpackSlots().Count == 0)
                return;

            var panel = EnsureBackpackPanel(menu);
            if (panel?.Container == null || panel.SlotUIs == null)
                return;

            panel.Container.gameObject.SetActive(true);
            if (panel.PagingRoot != null)
                panel.PagingRoot.gameObject.SetActive(false);

            ApplyEmbeddedBackpackBrowser(panel.Container, panel.SlotContainer, panel.SlotGridLayout, panel.SlotUIs,
                layoutView: (int)StandaloneBackpackLayoutView.Storage);
            panel.StorageSlotProvider = () => openedEntity.ItemSlots.AsEnumerable().Where(slot => slot != null).ToList();
            panel.SupportsStorageBulkTransfer = true;
            EnsureStorageBulkTransferControls(panel);
            RebuildStorageEntityQuickMove(openedEntity, GetBackpackSlots());
        }
        catch (Exception ex)
        {
            ModLogger.Error("StorageMenuPatch.ApplyBackpackSidePanel(StorageEntity)", ex);
        }
    }

    /// <summary>
    /// Adds a storage-only bulk-transfer surface beneath the shared backpack browser. The target
    /// selector has intentionally separate state from search and display filters, so narrowing
    /// the browser never changes the set of items a player is about to move.
    /// </summary>
    private static void EnsureStorageBulkTransferControls(BackpackPanelState panel)
    {
        if (panel?.Container == null || panel.SlotContainer == null || panel.StorageSlotProvider == null ||
            !panel.SupportsStorageBulkTransfer)
            return;

        if (panel.BulkTransferRoot == null)
        {
            var rootGo = new GameObject("PackRat_StorageBulkTransferControls");
            var root = rootGo.AddComponent<RectTransform>();
            root.SetParent(panel.Container, worldPositionStays: false);
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 1f);
            var layoutElement = rootGo.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;
            var background = rootGo.AddComponent<Image>();
            background.color = new Color32(15, 26, 35, 238);
            background.raycastTarget = false;
            panel.BulkTransferRoot = root;

            panel.BulkSelectorButton = CreateStandaloneActionButton(root, "BulkSelector",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(4f, -26f), new Vector2(-4f, -3f),
                "BULK MOVE", 9, out panel.BulkSelectorLabel);
            panel.BulkSelectorLabel.alignment = TextAnchor.MiddleCenter;

            var actionsGo = new GameObject("BulkActions");
            var actionsRoot = actionsGo.AddComponent<RectTransform>();
            actionsRoot.SetParent(root, worldPositionStays: false);
            actionsRoot.anchorMin = Vector2.zero;
            actionsRoot.anchorMax = Vector2.one;
            actionsRoot.offsetMin = Vector2.zero;
            actionsRoot.offsetMax = Vector2.zero;
            panel.BulkTransferActionsRoot = actionsRoot;
            panel.BulkTransferActionsCanvasGroup = actionsGo.AddComponent<CanvasGroup>();

            panel.MoveToStorageButton = CreateStandaloneActionButton(actionsRoot, "MoveToStorage",
                new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(4f, 21f), new Vector2(-2f, -28f),
                "TO STORAGE", 8, out _);
            panel.MoveToBackpackButton = CreateStandaloneActionButton(actionsRoot, "MoveToBackpack",
                new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(2f, 21f), new Vector2(-4f, -28f),
                "TO BACKPACK", 8, out _);

            var statusGo = new GameObject("Status");
            var statusRect = statusGo.AddComponent<RectTransform>();
            statusRect.SetParent(actionsRoot, worldPositionStays: false);
            statusRect.anchorMin = new Vector2(0f, 0f);
            statusRect.anchorMax = new Vector2(1f, 0f);
            statusRect.pivot = new Vector2(0.5f, 0f);
            statusRect.offsetMin = new Vector2(4f, 3f);
            statusRect.offsetMax = new Vector2(-4f, 18f);
            panel.BulkTransferStatusLabel = statusGo.AddComponent<Text>();
            panel.BulkTransferStatusLabel.font = ResolveUiFont(root);
            panel.BulkTransferStatusLabel.fontSize = 8;
            panel.BulkTransferStatusLabel.fontStyle = FontStyle.Bold;
            panel.BulkTransferStatusLabel.alignment = TextAnchor.MiddleCenter;
            panel.BulkTransferStatusLabel.color = new Color32(168, 207, 229, 255);
            panel.BulkTransferStatusLabel.raycastTarget = false;

            panel.BulkSelectorAction = () => ToggleStorageBulkDropdown(panel);
            panel.MoveToStorageAction = () => ExecuteStorageBulkTransfer(panel, moveToStorage: true);
            panel.MoveToBackpackAction = () => ExecuteStorageBulkTransfer(panel, moveToStorage: false);
            EventHelper.AddListener(panel.BulkSelectorAction, panel.BulkSelectorButton.onClick);
            EventHelper.AddListener(panel.MoveToStorageAction, panel.MoveToStorageButton.onClick);
            EventHelper.AddListener(panel.MoveToBackpackAction, panel.MoveToBackpackButton.onClick);

            CreateStorageBulkDropdown(panel);
        }

        panel.BulkTransferRoot.gameObject.SetActive(true);
        RefreshStorageBulkTransferControls(panel);
    }

    private static void PositionStorageBulkTransferControls(BackpackPanelState panel)
    {
        if (panel?.BulkTransferRoot == null || panel.SlotContainer == null)
            return;

        Canvas.ForceUpdateCanvases();
        var scale = GetStandaloneBackpackScale(StandaloneBackpackLayoutView.Storage);
        var gridBottom = panel.SlotContainer.anchoredPosition.y - panel.SlotContainer.rect.height * scale * 0.5f;

        // The embedded browser owns a separate pager state keyed to the PackRat panel root.
        // Its RectTransform is the authoritative bottom boundary: anchoring the bulk rail to the
        // grid alone made the two independent controls overlap at some responsive scales.
        var bulkTop = gridBottom - 92f * scale;
        if (panel.Container != null &&
            StandaloneBackpackPanels.TryGetValue(panel.Container.GetInstanceID(), out var embeddedState) &&
            embeddedState?.PagingRoot != null)
        {
            var pager = embeddedState.PagingRoot;
            var pagerScale = Mathf.Max(0.01f, pager.localScale.y);
            var pagerBottom = pager.anchoredPosition.y - pager.rect.height * pagerScale;
            bulkTop = pagerBottom - StorageBulkTransferPagerGap * scale;
        }

        panel.BulkTransferRoot.anchoredPosition = new Vector2(panel.SlotContainer.anchoredPosition.x,
            bulkTop);
        panel.BulkTransferRoot.localScale = Vector3.one * scale;
        panel.BulkTransferRoot.SetAsLastSibling();
    }

    /// <summary>
    /// Keeps bulk transfer unobtrusive until the player has chosen an independent transfer
    /// selection. The selector stays available in the compact state; the destination control and
    /// feedback surface only appear after a selection makes that action meaningful.
    /// </summary>
    private static void UpdateStorageBulkTransferPresentation(BackpackPanelState panel, bool expanded)
    {
        if (panel?.BulkTransferRoot == null || panel.SlotContainer == null)
            return;

        PositionStorageBulkTransferControls(panel);
        var scale = GetStandaloneBackpackScale(StandaloneBackpackLayoutView.Storage);
        var expandedWidth = Mathf.Clamp(panel.SlotContainer.rect.width * scale, 280f, 440f);
        var targetSize = expanded
            ? new Vector2(expandedWidth, StorageBulkTransferExpandedHeight)
            : new Vector2(StorageBulkTransferCompactWidth * scale, StorageBulkTransferCompactHeight);

        var shouldAnimate = panel.BulkTransferPresentationInitialized && panel.BulkTransferExpanded != expanded &&
            Configuration.Instance.EnableUiAnimations && !Configuration.Instance.ReduceUiMotion;
        panel.BulkTransferExpanded = expanded;
        panel.BulkTransferPresentationInitialized = true;

        if (panel.BulkTransferActionsRoot != null && expanded)
            panel.BulkTransferActionsRoot.gameObject.SetActive(true);

        if (!shouldAnimate)
        {
            SnapStorageBulkTransferPresentation(panel, targetSize, expanded);
            return;
        }

        var generation = ++panel.BulkTransferMotionGeneration;
        var startSize = panel.BulkTransferRoot.sizeDelta;
        var startAlpha = panel.BulkTransferActionsCanvasGroup?.alpha ?? (expanded ? 0f : 1f);
        MelonCoroutines.Start(RunStorageBulkTransferPresentation(panel, generation, startSize, targetSize,
            startAlpha, expanded));
    }

    private static IEnumerator RunStorageBulkTransferPresentation(BackpackPanelState panel, int generation,
        Vector2 startSize, Vector2 targetSize, float startActionsAlpha, bool expanded)
    {
        var elapsed = 0f;
        while (panel != null && panel.BulkTransferMotionGeneration == generation && elapsed < StorageBulkTransferMotionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            var rawT = Mathf.Clamp01(elapsed / StorageBulkTransferMotionDuration);
            var t = EaseOutCubic(rawT);
            if (panel.BulkTransferRoot != null)
                panel.BulkTransferRoot.sizeDelta = Vector2.Lerp(startSize, targetSize, t);
            if (panel.BulkTransferActionsCanvasGroup != null)
            {
                var targetAlpha = expanded ? 1f : 0f;
                var actionT = expanded ? Mathf.Clamp01((rawT - 0.35f) / 0.65f) : rawT;
                panel.BulkTransferActionsCanvasGroup.alpha = Mathf.Lerp(startActionsAlpha, targetAlpha, actionT);
                panel.BulkTransferActionsCanvasGroup.interactable = expanded;
                panel.BulkTransferActionsCanvasGroup.blocksRaycasts = expanded;
            }

            yield return null;
        }

        if (panel == null || panel.BulkTransferMotionGeneration != generation)
            yield break;

        SnapStorageBulkTransferPresentation(panel, targetSize, expanded);
    }

    private static void SnapStorageBulkTransferPresentation(BackpackPanelState panel, Vector2 targetSize, bool expanded)
    {
        if (panel?.BulkTransferRoot != null)
            panel.BulkTransferRoot.sizeDelta = targetSize;

        if (panel?.BulkTransferActionsCanvasGroup != null)
        {
            panel.BulkTransferActionsCanvasGroup.alpha = expanded ? 1f : 0f;
            panel.BulkTransferActionsCanvasGroup.interactable = expanded;
            panel.BulkTransferActionsCanvasGroup.blocksRaycasts = expanded;
        }

        if (panel?.BulkTransferActionsRoot != null)
            panel.BulkTransferActionsRoot.gameObject.SetActive(expanded);
    }

    private static void CreateStorageBulkDropdown(BackpackPanelState panel)
    {
        if (panel?.Container == null || panel.BulkDropdownRoot != null)
            return;

        var dropdownGo = new GameObject("PackRat_StorageBulkTransferDropdown");
        var dropdown = dropdownGo.AddComponent<RectTransform>();
        dropdown.SetParent(panel.Container, worldPositionStays: false);
        dropdown.anchorMin = new Vector2(0.5f, 0.5f);
        dropdown.anchorMax = new Vector2(0.5f, 0.5f);
        dropdown.pivot = new Vector2(0.5f, 1f);
        var background = dropdownGo.AddComponent<Image>();
        background.color = new Color32(11, 20, 29, 254);

        var canvas = Utils.AddComponentSafe<Canvas>(dropdownGo);
        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = 3300;
        }

        var raycaster = Utils.AddComponentSafe<GraphicRaycaster>(dropdownGo);
        RegisterItemUiRaycaster(raycaster);

        var viewportGo = new GameObject("Viewport");
        var viewport = viewportGo.AddComponent<RectTransform>();
        viewport.SetParent(dropdown, worldPositionStays: false);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(3f, 3f);
        viewport.offsetMax = new Vector2(-3f, -3f);
        var viewportImage = viewportGo.AddComponent<Image>();
        viewportImage.color = Color.white;
        var mask = viewportGo.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        var contentGo = new GameObject("Content");
        var content = contentGo.AddComponent<RectTransform>();
        content.SetParent(viewport, worldPositionStays: false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        var layout = contentGo.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 3f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = dropdownGo.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;

        panel.BulkDropdownRoot = dropdown;
        panel.BulkDropdownContent = content;
        dropdown.gameObject.SetActive(false);
    }

    private static void ToggleStorageBulkDropdown(BackpackPanelState panel)
    {
        if (panel?.BulkDropdownRoot == null)
            return;

        if (panel.BulkDropdownRoot.gameObject.activeSelf)
        {
            panel.BulkDropdownRoot.gameObject.SetActive(false);
            return;
        }

        RefreshStorageBulkTransferOptions(panel);
        if (panel.BulkTransferOptions.Count == 0)
        {
            SetStorageBulkTransferStatus(panel, "NO MOVABLE ITEMS FOUND", new Color32(220, 190, 105, 255));
            return;
        }

        PositionStorageBulkDropdown(panel);
        panel.BulkDropdownRoot.gameObject.SetActive(true);
        panel.BulkDropdownRoot.SetAsLastSibling();
    }

    private static void PositionStorageBulkDropdown(BackpackPanelState panel)
    {
        if (panel?.BulkDropdownRoot == null || panel.BulkTransferRoot == null || panel.Container == null)
            return;

        Canvas.ForceUpdateCanvases();
        var source = panel.BulkTransferRoot;
        var left = source.TransformPoint(new Vector3(source.rect.xMin, source.rect.yMin, 0f));
        var right = source.TransformPoint(new Vector3(source.rect.xMax, source.rect.yMin, 0f));
        var bottom = source.TransformPoint(new Vector3(0f, source.rect.yMin, 0f));
        var width = Mathf.Clamp(Vector3.Distance(left, right), 260f, 440f);
        var height = Mathf.Min(216f, 6f + panel.BulkTransferOptions.Count * 27f);
        panel.BulkDropdownRoot.sizeDelta = new Vector2(width, Mathf.Max(30f, height));
        panel.BulkDropdownRoot.anchoredPosition = panel.Container.InverseTransformPoint(bottom) + new Vector3(0f, -3f, 0f);
        panel.BulkDropdownRoot.localScale = Vector3.one;
    }

    private static void RefreshStorageBulkTransferControls(BackpackPanelState panel)
    {
        if (panel == null)
            return;

        RefreshStorageBulkTransferOptions(panel, rebuildVisibleButtons: false);
        var hasSelection = panel.BulkTransferSelection != null;
        UpdateStorageBulkTransferPresentation(panel, hasSelection);
        if (panel.BulkSelectorLabel != null)
        {
            panel.BulkSelectorLabel.text = !hasSelection
                ? "BULK MOVE"
                : "BULK: " + panel.BulkTransferSelection.Label;
        }

        if (panel.MoveToStorageButton != null)
            panel.MoveToStorageButton.interactable = hasSelection &&
                HasStorageBulkTransferMatches(GetBackpackSlots(), panel.StorageSlotProvider?.Invoke(), panel.BulkTransferSelection);
        if (panel.MoveToBackpackButton != null)
            panel.MoveToBackpackButton.interactable = hasSelection &&
                HasStorageBulkTransferMatches(panel.StorageSlotProvider?.Invoke(), GetBackpackSlots(), panel.BulkTransferSelection);

        if (panel.BulkTransferStatusLabel != null)
        {
            panel.BulkTransferStatusLabel.gameObject.SetActive(hasSelection &&
                !string.IsNullOrWhiteSpace(panel.BulkTransferStatus));
            panel.BulkTransferStatusLabel.text = panel.BulkTransferStatus ?? string.Empty;
        }
    }

    private static void RefreshStorageBulkTransferOptions(BackpackPanelState panel, bool rebuildVisibleButtons = true)
    {
        if (panel == null)
            return;

        panel.BulkTransferOptions.Clear();
        var allSlots = new List<ItemSlot>();
        allSlots.AddRange(GetBackpackSlots().Where(slot => slot?.ItemInstance != null));
        var storageSlots = panel.StorageSlotProvider?.Invoke();
        if (storageSlots != null)
            allSlots.AddRange(storageSlots.Where(slot => slot?.ItemInstance != null));

        if (allSlots.Count > 0)
        {
            panel.BulkTransferOptions.Add(new BulkTransferSelection
            {
                Kind = BulkTransferMatchKind.Category,
                Key = string.Empty,
                Label = "ALL ITEMS"
            });

            var categories = allSlots.Select(GetSlotType)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            for (var i = 0; i < categories.Count; i++)
            {
                var category = categories[i];
                panel.BulkTransferOptions.Add(new BulkTransferSelection
                {
                    Kind = BulkTransferMatchKind.Category,
                    Key = category,
                    Label = string.Equals(category, "Products", StringComparison.OrdinalIgnoreCase)
                        ? "ALL DRUGS"
                        : "ALL " + category.ToUpperInvariant()
                });
            }

            // Keep the strain groups separate from the browser's visual filters. A strain
            // selection follows the product's recipe ancestry, so "GRANDDADDY PURPLE" includes
            // named mixes and effect variants made from that strain as well as the base product.
            var weedStrains = GetWeedStrainOptions(allSlots);
            for (var i = 0; i < weedStrains.Count; i++)
            {
                var strain = weedStrains[i];
                panel.BulkTransferOptions.Add(new BulkTransferSelection
                {
                    Kind = BulkTransferMatchKind.WeedStrain,
                    Key = strain.Id,
                    Label = "STRAIN: " + strain.Name.ToUpperInvariant()
                });
            }

            var definitions = allSlots
                .Select(slot => new { Id = GetSlotDefinitionId(slot), Name = GetSlotName(slot) })
                .Where(value => !string.IsNullOrWhiteSpace(value.Id))
                .GroupBy(value => value.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            for (var i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                panel.BulkTransferOptions.Add(new BulkTransferSelection
                {
                    Kind = BulkTransferMatchKind.Definition,
                    Key = definition.Id,
                    Label = (string.IsNullOrWhiteSpace(definition.Name) ? definition.Id : definition.Name).ToUpperInvariant()
                });
            }
        }

        if (panel.BulkTransferSelection != null && !panel.BulkTransferOptions.Any(option =>
                option.Kind == panel.BulkTransferSelection.Kind &&
                string.Equals(option.Key, panel.BulkTransferSelection.Key, StringComparison.OrdinalIgnoreCase)))
        {
            panel.BulkTransferSelection = null;
        }

        if (!rebuildVisibleButtons || panel.BulkDropdownContent == null)
            return;

        for (var index = 0; index < panel.BulkTransferOptions.Count; index++)
        {
            while (panel.BulkDropdownOptionButtons.Count <= index)
                CreateStorageBulkDropdownOption(panel);

            var option = panel.BulkTransferOptions[index];
            var button = panel.BulkDropdownOptionButtons[index];
            var label = panel.BulkDropdownOptionLabels[index];
            label.text = option.Label;
            button.gameObject.SetActive(true);
            var buttonImage = button.targetGraphic as Image;
            if (buttonImage != null)
            {
                var selected = panel.BulkTransferSelection != null &&
                    panel.BulkTransferSelection.Kind == option.Kind &&
                    string.Equals(panel.BulkTransferSelection.Key, option.Key, StringComparison.OrdinalIgnoreCase);
                buttonImage.color = selected ? new Color32(45, 109, 146, 255) : new Color32(24, 43, 57, 255);
            }

            var oldAction = panel.BulkDropdownOptionActions[index];
            if (oldAction != null)
                EventHelper.RemoveListener(oldAction, button.onClick);
            var optionIndex = index;
            var action = new Action(() => SelectStorageBulkTransferOption(panel, optionIndex));
            panel.BulkDropdownOptionActions[index] = action;
            EventHelper.AddListener(action, button.onClick);
        }

        for (var index = panel.BulkTransferOptions.Count; index < panel.BulkDropdownOptionButtons.Count; index++)
            panel.BulkDropdownOptionButtons[index].gameObject.SetActive(false);

        LayoutRebuilder.ForceRebuildLayoutImmediate(panel.BulkDropdownContent);
    }

    private static void CreateStorageBulkDropdownOption(BackpackPanelState panel)
    {
        var optionGo = new GameObject("Option" + panel.BulkDropdownOptionButtons.Count);
        var optionRect = optionGo.AddComponent<RectTransform>();
        optionRect.SetParent(panel.BulkDropdownContent, worldPositionStays: false);
        var image = optionGo.AddComponent<Image>();
        image.color = new Color32(24, 43, 57, 255);
        ApplyPillButtonPresentation(image);
        var button = optionGo.AddComponent<Button>();
        button.targetGraphic = image;
        var layoutElement = optionGo.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = 24f;
        layoutElement.minHeight = 24f;
        var label = CreateSearchText(optionRect, "Label", new Color32(223, 239, 248, 255));
        label.fontSize = 10;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;

        panel.BulkDropdownOptionButtons.Add(button);
        panel.BulkDropdownOptionLabels.Add(label);
        panel.BulkDropdownOptionActions.Add(null);
    }

    private static void SelectStorageBulkTransferOption(BackpackPanelState panel, int optionIndex)
    {
        if (panel == null || optionIndex < 0 || optionIndex >= panel.BulkTransferOptions.Count)
            return;

        var selected = panel.BulkTransferOptions[optionIndex];
        panel.BulkTransferSelection = new BulkTransferSelection
        {
            Kind = selected.Kind,
            Key = selected.Key,
            Label = selected.Label
        };
        panel.BulkTransferStatus = null;
        if (panel.BulkDropdownRoot != null)
            panel.BulkDropdownRoot.gameObject.SetActive(false);
        RefreshStorageBulkTransferControls(panel);
    }

    private static bool HasStorageBulkTransferMatches(IEnumerable<ItemSlot> sourceSlots, IEnumerable<ItemSlot> destinationSlots,
        BulkTransferSelection selection)
    {
        if (sourceSlots == null || destinationSlots == null || selection == null)
            return false;

        var destinations = destinationSlots.Where(slot => slot != null).ToList();
        foreach (var source in sourceSlots)
        {
            if (!DoesStorageBulkSelectionMatch(source, selection) || source.IsRemovalLocked ||
                GetWholeStandaloneSlotQuantity(source) <= 0)
                continue;

            var item = source.ItemInstance;
            if (item == null)
                continue;

            for (var index = 0; index < destinations.Count; index++)
            {
                var destination = destinations[index];
                if (!CanStorageBulkMoveToSlot(source, destination))
                    continue;
                if (destination.GetCapacityForItem(item, checkPlayerFilters: false) > 0)
                    return true;
            }
        }

        return false;
    }

    private static bool DoesStorageBulkSelectionMatch(ItemSlot slot, BulkTransferSelection selection)
    {
        if (slot?.ItemInstance == null || selection == null)
            return false;

        if (selection.Kind == BulkTransferMatchKind.Definition)
            return string.Equals(GetSlotDefinitionId(slot), selection.Key, StringComparison.OrdinalIgnoreCase);

        if (selection.Kind == BulkTransferMatchKind.WeedStrain)
        {
            var strains = GetWeedBaseStrains(slot);
            return strains.Any(strain => string.Equals(strain.Id, selection.Key, StringComparison.OrdinalIgnoreCase));
        }

        return string.IsNullOrWhiteSpace(selection.Key) ||
            string.Equals(GetSlotType(slot), selection.Key, StringComparison.OrdinalIgnoreCase);
    }

    private static void ExecuteStorageBulkTransfer(BackpackPanelState panel, bool moveToStorage)
    {
        if (panel?.BulkTransferSelection == null)
            return;

        try
        {
            var backpackSlots = GetBackpackSlots();
            var storageSlots = panel.StorageSlotProvider?.Invoke();
            if (storageSlots == null)
                return;

            var sources = moveToStorage ? backpackSlots : storageSlots;
            var destinations = moveToStorage ? storageSlots : backpackSlots;
            var totalMoved = 0;
            var movedStacks = 0;
            var sourceSnapshot = sources.Where(slot => slot?.ItemInstance != null).ToList();
            for (var index = 0; index < sourceSnapshot.Count; index++)
            {
                var source = sourceSnapshot[index];
                if (!DoesStorageBulkSelectionMatch(source, panel.BulkTransferSelection))
                    continue;

                var moved = MoveStorageBulkSourceToDestinations(source, destinations);
                if (moved <= 0)
                    continue;

                totalMoved += moved;
                movedStacks++;
            }

            if (totalMoved <= 0)
            {
                SetStorageBulkTransferStatus(panel, "NO MATCHING ITEMS COULD BE MOVED", new Color32(220, 190, 105, 255));
                return;
            }

            // One authoritative backpack sync covers the entire batch, including moves out of
            // the bag, instead of emitting a network update for every source stack.
            BackpackStateSyncManager.CompleteLocalBackpackEdit();
            var destinationLabel = moveToStorage ? "STORAGE" : "BACKPACK";
            SetStorageBulkTransferStatus(panel,
                $"MOVED {totalMoved} ITEM{(totalMoved == 1 ? string.Empty : "S")} FROM {movedStacks} STACK" +
                $"{(movedStacks == 1 ? string.Empty : "S")} → {destinationLabel}", new Color32(105, 209, 140, 255));
            ModLogger.Info($"[BackpackUI] Bulk moved {totalMoved} items from {movedStacks} matching stacks to {destinationLabel}. " +
                $"Selection='{panel.BulkTransferSelection.Label}'.");
            RefreshStorageBulkTransferSurface(panel);
        }
        catch (Exception ex)
        {
            SetStorageBulkTransferStatus(panel, "BULK MOVE FAILED — SEE LOG", new Color32(238, 125, 112, 255));
            ModLogger.Error("StorageMenuPatch.ExecuteStorageBulkTransfer", ex);
        }
    }

    private static int MoveStorageBulkSourceToDestinations(ItemSlot source, List<ItemSlot> destinations)
    {
        if (source?.ItemInstance == null || source.IsRemovalLocked || destinations == null)
            return 0;

        var movedTotal = 0;
        var sourceItem = source.ItemInstance;
        var remaining = GetWholeStandaloneSlotQuantity(source);
        for (var index = 0; index < destinations.Count && remaining > 0; index++)
        {
            var destination = destinations[index];
            if (!CanStorageBulkMoveToSlot(source, destination))
                continue;

            var capacity = Mathf.Max(0, destination.GetCapacityForItem(sourceItem, checkPlayerFilters: false));
            var requestedMove = Mathf.Min(remaining, capacity);
            if (requestedMove <= 0)
                continue;

            var transfer = sourceItem.GetCopy(requestedMove);
            if (transfer == null)
                continue;

            var destinationBefore = GetWholeStandaloneSlotQuantity(destination);
            destination.AddItem(transfer);
            var moved = Mathf.Clamp(GetWholeStandaloneSlotQuantity(destination) - destinationBefore, 0, requestedMove);
            if (moved <= 0)
                continue;

            var sourceBefore = GetWholeStandaloneSlotQuantity(source);
            source.ChangeQuantity(-moved);
            if (GetWholeStandaloneSlotQuantity(source) != sourceBefore - moved)
            {
                ModLogger.Warn("[BackpackUI] Bulk move aborted: source did not acknowledge the transfer.");
                break;
            }

            movedTotal += moved;
            remaining -= moved;
        }

        return movedTotal;
    }

    private static bool CanStorageBulkMoveToSlot(ItemSlot source, ItemSlot destination)
    {
        if (source?.ItemInstance == null || destination == null || ReferenceEquals(source, destination) ||
            destination.IsLocked || destination.IsAddLocked || destination.IsRemovalLocked ||
            !destination.DoesItemMatchHardFilters(source.ItemInstance))
            return false;

        return destination.ItemInstance == null || destination.ItemInstance.CanStackWith(source.ItemInstance,
            checkQuantities: false);
    }

    private static void RefreshStorageBulkTransferSurface(BackpackPanelState panel)
    {
        if (panel?.Container == null || panel.SlotContainer == null || panel.SlotGridLayout == null || panel.SlotUIs == null)
            return;

        ApplyEmbeddedBackpackBrowser(panel.Container, panel.SlotContainer, panel.SlotGridLayout, panel.SlotUIs,
            layoutView: (int)StandaloneBackpackLayoutView.Storage);
        EnsureStorageBulkTransferControls(panel);
    }

    private static void SetStorageBulkTransferStatus(BackpackPanelState panel, string text, Color color)
    {
        if (panel == null)
            return;

        panel.BulkTransferStatus = text;
        if (panel.BulkTransferStatusLabel == null)
            return;

        panel.BulkTransferStatusLabel.gameObject.SetActive(!string.IsNullOrWhiteSpace(text));
        panel.BulkTransferStatusLabel.text = text ?? string.Empty;
        panel.BulkTransferStatusLabel.color = color;
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
        var scale = Mathf.Clamp(config.StorageOverlayScale, MinimumOverlayScale, MaximumOverlayScale);
        var desired = new Vector2(
            original.anchoredPosition.x - original.rect.width * 0.5f - clone.rect.width * scale * 0.5f - CompactPanelMargin
                + config.StorageOverlayOffsetX,
            original.anchoredPosition.y + config.StorageOverlayOffsetY
        );
        clone.anchoredPosition = ClampToParentBounds(clone, desired, CompactPanelMargin);
    }

    private static void ConfigureCompactSidePanel(StorageMenu menu, BackpackPanelState panel)
    {
        if (panel?.Container == null || panel.SlotContainer == null)
            return;

        panel.Container.anchorMin = Vector2.zero;
        panel.Container.anchorMax = Vector2.one;
        panel.Container.offsetMin = Vector2.zero;
        panel.Container.offsetMax = Vector2.zero;
        panel.Container.localScale = Vector3.one;
        if (panel.HeaderRoot != null)
            panel.HeaderRoot.gameObject.SetActive(false);
    }

    private static void EnsureCompactPanelHeader(BackpackPanelState panel)
    {
        if (panel?.Container == null)
            return;

        if (panel.HeaderRoot == null)
        {
            var headerGo = new GameObject("PackRat_BackpackStorageHeader");
            var header = headerGo.AddComponent<RectTransform>();
            header.SetParent(panel.Container, worldPositionStays: false);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
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
            panel.HeaderRoot = header;
        }

        panel.HeaderRoot.anchorMin = new Vector2(0f, 1f);
        panel.HeaderRoot.anchorMax = new Vector2(1f, 1f);
        panel.HeaderRoot.offsetMin = new Vector2(10f, -62f);
        panel.HeaderRoot.offsetMax = new Vector2(-10f, -8f);
        panel.HeaderRoot.SetAsFirstSibling();
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

        var halfWidth = Mathf.Max(0f, parent.rect.width * 0.5f - rectTransform.rect.width * rectTransform.localScale.x * 0.5f - margin);
        var halfHeight = Mathf.Max(0f, parent.rect.height * 0.5f - rectTransform.rect.height * rectTransform.localScale.y * 0.5f - margin);
        return new Vector2(
            Mathf.Clamp(desired.x, -halfWidth, halfWidth),
            Mathf.Clamp(desired.y, -halfHeight, halfHeight)
        );
    }

    private static void AssignBackpackSlots(BackpackPanelState panel, List<ItemSlot> backpackSlots)
    {
        if (panel.SlotGridLayout != null)
        {
            panel.SlotGridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            panel.SlotGridLayout.constraintCount = StorageBackpackGridRows;
        }

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

        if (panel.BulkDropdownRoot != null)
            panel.BulkDropdownRoot.gameObject.SetActive(false);
        panel.BulkTransferMotionGeneration++;
        panel.BulkTransferStatus = null;
    }

    private static void RefreshActiveStorageBackpackLayouts()
    {
        foreach (var panel in BackpackPanels.Values)
        {
            if (panel?.Menu == null || panel.Container == null || !panel.Container.gameObject.activeSelf)
                continue;

            ConfigureCompactSidePanel(panel.Menu, panel);
            ApplyEmbeddedBackpackBrowser(panel.Container, panel.SlotContainer, panel.SlotGridLayout, panel.SlotUIs,
                layoutView: (int)StandaloneBackpackLayoutView.Storage);
            if (panel.SupportsStorageBulkTransfer)
                EnsureStorageBulkTransferControls(panel);
            else
                HideStorageBulkTransferControls(panel);
        }
    }

    private static void HideStorageBulkTransferControls(BackpackPanelState panel)
    {
        if (panel == null)
            return;

        panel.BulkTransferMotionGeneration++;
        if (panel.BulkTransferRoot != null)
            panel.BulkTransferRoot.gameObject.SetActive(false);
        if (panel.BulkDropdownRoot != null)
            panel.BulkDropdownRoot.gameObject.SetActive(false);
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
        BeginBackpackQuickMoveEditSession();
    }

    /// <summary>
    /// StorageEntity uses the same ItemSlot collection as normal storage but does not implement
    /// IItemSlotOwner on the current beta. Build the equivalent quick-move routing directly from
    /// that collection so vehicle trunks retain their native menu implementation.
    /// </summary>
    private static void RebuildStorageEntityQuickMove(StorageEntity openedEntity, List<ItemSlot> backpackSlots)
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
        if (inventory == null || openedEntity?.ItemSlots == null)
            return;

        foreach (var slot in inventory.GetAllInventorySlots().AsEnumerable())
        {
            if (slot != null)
                ActiveInventorySlots.Add(slot);
        }

        foreach (var slot in openedEntity.ItemSlots.AsEnumerable())
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
        _quickMoveActive = ActiveInventorySlots.Count > 0 &&
            (ActiveStorageSlots.Count > 0 || ActiveBackpackSlots.Count > 0);
        BeginBackpackQuickMoveEditSession();
    }

    /// <summary>
    /// Captures one baseline for a storage screen that can mutate the backpack. Closing that
    /// screen then emits one authoritative state update rather than one network message per
    /// native quick-move transaction.
    /// </summary>
    private static void BeginBackpackQuickMoveEditSession()
    {
        if (_backpackQuickMoveEditSessionActive || !_quickMoveActive || ActiveBackpackSlots.Count == 0)
            return;

        BackpackStateSyncManager.BeginLocalBackpackEdit();
        _backpackQuickMoveEditSessionActive = true;
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
            if (state?.SortMode == StandaloneBackpackSortMode.Favorites &&
                !BackpackFavorites.IsFavorite(GetSlotDefinitionId(slot)))
                continue;
            // Recent is deliberately a history-backed view, not a fallback ordering. Until this
            // backpack session observes a changed item, there is no truthful recent result to
            // display, so leave the projection empty.
            if (state?.SortMode == StandaloneBackpackSortMode.Recent &&
                !HasStandaloneRecentHistory(state, slot))
                continue;

            displaySlots.Add(slot);
        }

        if (state != null && state.SortMode != StandaloneBackpackSortMode.SlotOrder)
            displaySlots.Sort((left, right) => CompareStandaloneBackpackSlots(left, right, state, state.SortMode,
                state.SortDirection, backpackSlots));

        return displaySlots;
    }

    private static int CountUsedStandaloneSlots(List<ItemSlot> slots)
    {
        if (slots == null)
            return 0;

        var usedSlotCount = 0;
        for (var i = 0; i < slots.Count; i++)
        {
            if (slots[i]?.ItemInstance != null)
                usedSlotCount++;
        }

        return usedSlotCount;
    }

    private static bool HasStandaloneFilters(StandaloneBackpackState state)
    {
        return state != null && (!string.IsNullOrWhiteSpace(state.SearchTerm)
            || !string.IsNullOrWhiteSpace(state.TypeFilter)
            || !string.IsNullOrWhiteSpace(state.QualityFilter)
            || state.SortMode == StandaloneBackpackSortMode.Favorites
            || state.SortMode == StandaloneBackpackSortMode.Recent);
    }

    private static bool HasStandaloneRecentHistory(StandaloneBackpackState state, ItemSlot slot)
    {
        if (state == null || state.RecentItemTimestamps.Count == 0)
            return false;

        var definitionId = GetSlotDefinitionId(slot);
        return !string.IsNullOrWhiteSpace(definitionId) &&
            state.RecentItemTimestamps.TryGetValue(definitionId, out var changedAt) && changedAt > 0f;
    }

    private static int CompareStandaloneBackpackSlots(ItemSlot left, ItemSlot right, StandaloneBackpackState state,
        StandaloneBackpackSortMode sortMode, StandaloneBackpackSortDirection sortDirection, List<ItemSlot> originalSlots)
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
                var quantityComparison = GetSlotQuantity(left).CompareTo(GetSlotQuantity(right));
                return quantityComparison != 0
                    ? ApplyStandaloneSortDirection(quantityComparison, sortDirection)
                    : originalSlots.IndexOf(left).CompareTo(originalSlots.IndexOf(right));
            case StandaloneBackpackSortMode.Quality:
                var leftQualityRank = GetQualitySortRank(GetSlotQuality(left));
                var rightQualityRank = GetQualitySortRank(GetSlotQuality(right));
                if (leftQualityRank < 0 || rightQualityRank < 0)
                {
                    if (leftQualityRank != rightQualityRank)
                        return leftQualityRank < 0 ? 1 : -1;

                    return originalSlots.IndexOf(left).CompareTo(originalSlots.IndexOf(right));
                }

                var qualityComparison = leftQualityRank.CompareTo(rightQualityRank);
                return qualityComparison != 0
                    ? ApplyStandaloneSortDirection(qualityComparison, sortDirection)
                    : originalSlots.IndexOf(left).CompareTo(originalSlots.IndexOf(right));
            case StandaloneBackpackSortMode.Type:
                leftValue = GetSlotType(left);
                rightValue = GetSlotType(right);
                break;
            case StandaloneBackpackSortMode.Favorites:
                var leftFavorite = BackpackFavorites.IsFavorite(GetSlotDefinitionId(left));
                var rightFavorite = BackpackFavorites.IsFavorite(GetSlotDefinitionId(right));
                var favoriteComparison = rightFavorite.CompareTo(leftFavorite);
                return favoriteComparison != 0
                    ? favoriteComparison
                    : originalSlots.IndexOf(left).CompareTo(originalSlots.IndexOf(right));
            case StandaloneBackpackSortMode.Recent:
                var leftRecentTime = 0f;
                var rightRecentTime = 0f;
                if (state != null)
                {
                    state.RecentItemTimestamps.TryGetValue(GetSlotDefinitionId(left), out leftRecentTime);
                    state.RecentItemTimestamps.TryGetValue(GetSlotDefinitionId(right), out rightRecentTime);
                }
                if (leftRecentTime <= 0f || rightRecentTime <= 0f)
                {
                    if (!Mathf.Approximately(leftRecentTime, rightRecentTime))
                        return leftRecentTime <= 0f ? 1 : -1;

                    return originalSlots.IndexOf(left).CompareTo(originalSlots.IndexOf(right));
                }

                var recentComparison = leftRecentTime.CompareTo(rightRecentTime);
                return recentComparison != 0
                    ? ApplyStandaloneSortDirection(recentComparison, sortDirection)
                    : originalSlots.IndexOf(left).CompareTo(originalSlots.IndexOf(right));
            default:
                return originalSlots.IndexOf(left).CompareTo(originalSlots.IndexOf(right));
        }

        var comparison = string.Compare(leftValue, rightValue, StringComparison.OrdinalIgnoreCase);
        return comparison != 0
            ? ApplyStandaloneSortDirection(comparison, sortDirection)
            : originalSlots.IndexOf(left).CompareTo(originalSlots.IndexOf(right));
    }

    private static int ApplyStandaloneSortDirection(int comparison, StandaloneBackpackSortDirection sortDirection)
    {
        return sortDirection == StandaloneBackpackSortDirection.Descending ? -comparison : comparison;
    }

    /// <summary>
    /// Gives the game's quality progression an explicit order for the inventory projection.
    /// Items that do not expose Quality are deliberately ranked below Trash rather than being
    /// sorted as an empty string ahead of every quality-bearing item.
    /// </summary>
    private static int GetQualitySortRank(string qualityName)
    {
        switch (qualityName?.Trim().ToLowerInvariant())
        {
            case "heavenly":
                return 5;
            case "premium":
                return 4;
            case "standard":
                return 3;
            case "poor":
                return 2;
            case "trash":
                return 1;
            default:
                return string.IsNullOrWhiteSpace(qualityName) ? -1 : 0;
        }
    }

    private static string GetSortDirectionLabel(StandaloneBackpackSortDirection sortDirection)
    {
        return sortDirection == StandaloneBackpackSortDirection.Descending ? "descending" : "ascending";
    }

    private static string GetSlotName(ItemSlot slot)
    {
        return slot?.ItemInstance?.Definition?.Name ?? string.Empty;
    }

    private static string GetSlotDefinitionId(ItemSlot slot)
    {
        var definition = slot?.ItemInstance?.Definition;
        var definitionId = definition?.ID;
        if (!string.IsNullOrWhiteSpace(definitionId))
            return definitionId;

        // Some equippable definitions (including current firearm definitions) do not expose an
        // ID through the item-instance wrapper. Their player-facing definition name is stable
        // across saves and gives them the same definition-level favorite behavior as products.
        var definitionName = definition?.Name;
        return string.IsNullOrWhiteSpace(definitionName) ? string.Empty : "name:" + definitionName.Trim();
    }

    private static string GetSlotType(ItemSlot slot)
    {
        return GetItemCategory(slot?.ItemInstance);
    }

    /// <summary>
    /// Returns the same player-facing item category used by the backpack filter UI. Shared
    /// pickup features use this rather than reproducing fragile runtime type checks.
    /// </summary>
    internal static string GetItemCategory(ItemInstance item)
    {
        var definition = item?.Definition;
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
        if (IsProductItemInstance(item))
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
            // ProductDefinition is not always exposed through the same concrete wrapper after
            // the game generates a mixed product. Prefer the common member first so the search
            // surface remains stable in both Mono and IL2CPP.
            var reflectedDrugType = ReflectionUtils.TryGetFieldOrProperty(definition, "DrugType")
                ?? ReflectionUtils.TryGetFieldOrProperty(definition, "drugType")
                ?? ReflectionUtils.TryGetFieldOrProperty(item, "DrugType")
                ?? ReflectionUtils.TryGetFieldOrProperty(item, "drugType");
            if (!string.IsNullOrWhiteSpace(reflectedDrugType?.ToString()))
                return reflectedDrugType.ToString();

#if MONO
            var productDefinition = definition as ProductDefinition;
#else
            var il2CppDefinition = definition as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
            var productDefinition = il2CppDefinition?.TryCast<ProductDefinition>();
#endif
            if (productDefinition != null)
                return productDefinition.DrugType.ToString();

            // Generated products can temporarily report only their concrete definition type.
            // Preserve their player-facing drug family as a final fallback.
            if (IsMarijuanaProductDefinition(definition))
                return "Marijuana";

            var identity = string.Join(" ", new[]
            {
                GetReflectedDefinitionName(definition, string.Empty),
                GetReflectedDefinitionId(definition),
                definition.GetType().Name ?? string.Empty
            });
            if (ContainsTypeToken(identity, "methamphetamine", "meth", "pseudo"))
                return "Methamphetamine";
            if (ContainsTypeToken(identity, "cocaine", "coke"))
                return "Cocaine";
            if (ContainsTypeToken(identity, "mushroom", "shroom"))
                return "Mushrooms";

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Returns search-only product terms without changing the backing inventory or visual
    /// filters. Mixed marijuana products include their original strain names so a search for a
    /// base strain finds every derivative made from that strain.
    /// </summary>
    private static IEnumerable<string> GetProductSearchAliases(ItemSlot slot)
    {
        var item = slot?.ItemInstance;
        var definition = item?.Definition;
        if (item == null || definition == null || !IsProductItemInstance(item))
            yield break;

        var drugType = GetProductDrugType(item);
        if (!string.IsNullOrWhiteSpace(drugType))
            yield return drugType;

        foreach (var alias in GetDrugTypeSearchAliases(drugType))
            yield return alias;

        // A recipe-derived marijuana product may not include its original strain in its own
        // display name. Reuse the ancestry resolver used by the bulk-move selector so search
        // and transfer semantics agree.
        var baseStrains = GetWeedBaseStrains(slot);
        for (var index = 0; index < baseStrains.Count; index++)
        {
            var strain = baseStrains[index];
            if (strain == null)
                continue;

            if (!string.IsNullOrWhiteSpace(strain.Name))
                yield return strain.Name;
            if (!string.IsNullOrWhiteSpace(strain.Id))
                yield return strain.Id;
        }
    }

    private static IEnumerable<string> GetDrugTypeSearchAliases(string drugType)
    {
        if (string.IsNullOrWhiteSpace(drugType))
            yield break;

        if (drugType.IndexOf("marijuana", StringComparison.OrdinalIgnoreCase) >= 0 ||
            drugType.IndexOf("weed", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            yield return "weed";
            yield return "marijuana";
            yield return "cannabis";
            yield return "pot";
            yield break;
        }

        if (drugType.IndexOf("meth", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            yield return "meth";
            yield return "methamphetamine";
            yield return "pseudo";
            yield break;
        }

        if (drugType.IndexOf("cocaine", StringComparison.OrdinalIgnoreCase) >= 0 ||
            drugType.IndexOf("coke", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            yield return "cocaine";
            yield return "coke";
            yield break;
        }

        if (drugType.IndexOf("mushroom", StringComparison.OrdinalIgnoreCase) >= 0 ||
            drugType.IndexOf("shroom", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            yield return "mushroom";
            yield return "mushrooms";
            yield return "shroom";
            yield return "shrooms";
        }
    }

    /// <summary>
    /// Produces one selector entry per original marijuana strain represented by the provided
    /// slots. A selected strain includes its base product and every mixed product whose recipe
    /// ancestry leads back to that base product.
    /// </summary>
    private static List<WeedStrainOption> GetWeedStrainOptions(IEnumerable<ItemSlot> slots)
    {
        var strainsById = new Dictionary<string, WeedStrainOption>(StringComparer.OrdinalIgnoreCase);
        if (slots == null)
            return new List<WeedStrainOption>();

        foreach (var slot in slots)
        {
            var strains = GetWeedBaseStrains(slot);
            for (var index = 0; index < strains.Count; index++)
            {
                var strain = strains[index];
                if (string.IsNullOrWhiteSpace(strain?.Id) || strainsById.ContainsKey(strain.Id))
                    continue;

                strainsById[strain.Id] = strain;
            }
        }

        return strainsById.Values
            .OrderBy(strain => strain.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Resolves the base marijuana product(s) for a slot by walking the game's persisted mix
    /// recipes. Product definitions own the recipes that created them; each such recipe includes
    /// the preceding product and a mixer ingredient. This avoids relying on custom mix names.
    /// </summary>
    private static List<WeedStrainOption> GetWeedBaseStrains(ItemSlot slot)
    {
        var definition = slot?.ItemInstance?.Definition;
        if (!IsMarijuanaProductDefinition(definition))
            return new List<WeedStrainOption>();

        var strainsById = new Dictionary<string, WeedStrainOption>(StringComparer.OrdinalIgnoreCase);
        ResolveWeedBaseStrains(definition, new HashSet<string>(StringComparer.OrdinalIgnoreCase), strainsById);
        return strainsById.Values.ToList();
    }

    private static void ResolveWeedBaseStrains(object productDefinition, HashSet<string> recursionPath,
        IDictionary<string, WeedStrainOption> strainsById)
    {
        if (!IsMarijuanaProductDefinition(productDefinition) || recursionPath == null || strainsById == null)
            return;

        var definitionId = GetReflectedDefinitionId(productDefinition);
        if (string.IsNullOrWhiteSpace(definitionId) || !recursionPath.Add(definitionId))
            return;

        try
        {
            var hasParentProduct = false;
            var recipes = GetWeedProductRecipes(productDefinition, definitionId);
            var recipeCount = ReflectionUtils.TryGetListCount(recipes);
            for (var recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
            {
                var recipe = ReflectionUtils.TryGetListItem(recipes, recipeIndex);
                hasParentProduct |= ResolveWeedRecipeInputs(recipe, recursionPath, strainsById);
            }

            // The game's save/network format owns mix recipes as Product -> Mixer -> Output.
            // Some runtime definitions don't retain their producing recipe locally, so look up
            // every known recipe whose output is this product and walk back to its parent.
            if (!hasParentProduct)
            {
                foreach (var recipe in GetWeedRecipesProducing(definitionId))
                    hasParentProduct |= ResolveWeedRecipeInputs(recipe, recursionPath, strainsById);
            }

            if (!hasParentProduct)
            {
                strainsById[definitionId] = new WeedStrainOption
                {
                    Id = definitionId,
                    Name = GetReflectedDefinitionName(productDefinition, definitionId)
                };
            }
        }
        catch (Exception ex)
        {
            // Product definitions can be rebuilt as a client receives product data. A failed
            // read should leave this one slot out of the optional grouping rather than breaking
            // the storage UI or a bulk transfer.
            ModLogger.Debug("Unable to resolve marijuana strain ancestry: " + ex.Message);
        }
        finally
        {
            recursionPath.Remove(definitionId);
        }
    }

    private static bool ResolveWeedRecipeInputs(object recipe, HashSet<string> recursionPath,
        IDictionary<string, WeedStrainOption> strainsById)
    {
        if (recipe == null)
            return false;

        var foundParentProduct = false;
        var ingredients = ReflectionUtils.TryGetFieldOrProperty(recipe, "Ingredients");
        var ingredientCount = ReflectionUtils.TryGetListCount(ingredients);
        for (var ingredientIndex = 0; ingredientIndex < ingredientCount; ingredientIndex++)
        {
            var ingredient = ReflectionUtils.TryGetListItem(ingredients, ingredientIndex);
            var items = ReflectionUtils.TryGetFieldOrProperty(ingredient, "Items");
            var itemCount = ReflectionUtils.TryGetListCount(items);
            var foundMarijuanaInput = false;
            for (var itemIndex = 0; itemIndex < itemCount; itemIndex++)
            {
                var inputDefinition = ReflectionUtils.TryGetListItem(items, itemIndex);
                if (!IsMarijuanaProductDefinition(inputDefinition))
                    continue;

                foundParentProduct = true;
                foundMarijuanaInput = true;
                ResolveWeedBaseStrains(inputDefinition, recursionPath, strainsById);
            }

            // Recipes created at runtime can persist the chosen ingredient as Item rather than
            // leave it in the selectable Items list.
            if (foundMarijuanaInput)
                continue;

            var resolvedInput = ReflectionUtils.TryGetFieldOrProperty(ingredient, "Item")
                ?? ReflectionUtils.TryGetFieldOrProperty(ingredient, "ingredientVariant");
            if (!IsMarijuanaProductDefinition(resolvedInput))
                continue;

            foundParentProduct = true;
            ResolveWeedBaseStrains(resolvedInput, recursionPath, strainsById);
        }

        return foundParentProduct;
    }

    private static IEnumerable<object> GetWeedRecipesProducing(string outputDefinitionId)
    {
        if (string.IsNullOrWhiteSpace(outputDefinitionId))
            yield break;

        // ProductDefinition.Recipes is useful for authored recipes, but generated mixes are
        // recorded by the live ProductManager instead. This is the same Product -> Mixer ->
        // Output graph that the save data serializes, so it is the authoritative source for
        // a player-created name such as Alaskan Snorlax.
        var foundRuntimeRecipe = false;
        foreach (var recipe in GetRuntimeMixRecipes())
        {
            var product = ReflectionUtils.TryGetFieldOrProperty(recipe, "Product");
            var outputDefinition = ReflectionUtils.TryGetFieldOrProperty(product, "Item");
            if (!string.Equals(GetReflectedDefinitionId(outputDefinition), outputDefinitionId,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            foundRuntimeRecipe = true;
            yield return recipe;
        }

        if (foundRuntimeRecipe)
            yield break;

        foreach (var productDefinition in GetKnownRuntimeProductDefinitions())
        {
            var recipes = ReflectionUtils.TryGetFieldOrProperty(productDefinition, "Recipes")
                ?? ReflectionUtils.TryGetFieldOrProperty(productDefinition, "recipes");
            var recipeCount = ReflectionUtils.TryGetListCount(recipes);
            for (var recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
            {
                var recipe = ReflectionUtils.TryGetListItem(recipes, recipeIndex);
                var product = ReflectionUtils.TryGetFieldOrProperty(recipe, "Product");
                var outputDefinition = ReflectionUtils.TryGetFieldOrProperty(product, "Item");
                if (string.Equals(GetReflectedDefinitionId(outputDefinition), outputDefinitionId,
                        StringComparison.OrdinalIgnoreCase))
                    yield return recipe;
            }
        }
    }

    /// <summary>
    /// Returns the live mix recipes maintained by ProductManager. Runtime-generated products
    /// do not necessarily retain their ancestry on their individual definitions.
    /// </summary>
    private static IEnumerable<object> GetRuntimeMixRecipes()
    {
        ProductManager productManager = null;
        try
        {
            productManager = ProductManager.Instance ?? Utils.FindObjectOfTypeSafe<ProductManager>();
        }
        catch
        {
            // ProductManager is unavailable while a scene is being torn down or before the
            // product save data is loaded. The caller will fall back to definition recipes.
        }

        if (productManager == null)
            yield break;

        // This property is present in both generated API sets. Access it through the typed
        // wrapper instead of reflection: IL2CPP's wrapper can expose the backing member but
        // reject a reflected getter even though the list is fully populated in-game.
        var recipes = productManager.mixRecipes;
        if (recipes == null)
            yield break;

        var recipeCount = recipes.Count;
        for (var recipeIndex = 0; recipeIndex < recipeCount; recipeIndex++)
        {
            var recipe = recipes[recipeIndex];
            if (recipe != null)
                yield return recipe;
        }
    }

    private static IEnumerable<object> GetKnownRuntimeProductDefinitions()
    {
        ProductManager productManager;
        try
        {
            productManager = ProductManager.Instance ?? Utils.FindObjectOfTypeSafe<ProductManager>();
        }
        catch
        {
            yield break;
        }

        if (productManager == null)
            yield break;

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var memberName in new[] { "AllProducts", "createdProducts", "DefaultKnownProducts" })
        {
            var definitions = ReflectionUtils.TryGetFieldOrProperty(productManager, memberName);
            var definitionCount = ReflectionUtils.TryGetListCount(definitions);
            for (var index = 0; index < definitionCount; index++)
            {
                var definition = ReflectionUtils.TryGetListItem(definitions, index);
                var definitionId = GetReflectedDefinitionId(definition);
                if (string.IsNullOrWhiteSpace(definitionId) || !seenIds.Add(definitionId))
                    continue;

                yield return definition;
            }
        }
    }

    /// <summary>
    /// Returns the recipe collection from a runtime product definition. Saved/generated products
    /// may expose a lightweight definition with no recipes, while the registry retains the full
    /// recipe graph under the same item ID.
    /// </summary>
    private static object GetWeedProductRecipes(object productDefinition, string definitionId)
    {
        var recipes = ReflectionUtils.TryGetFieldOrProperty(productDefinition, "Recipes")
            ?? ReflectionUtils.TryGetFieldOrProperty(productDefinition, "recipes");
        if (ReflectionUtils.TryGetListCount(recipes) > 0 || string.IsNullOrWhiteSpace(definitionId))
            return recipes;

        var registeredDefinition = GetStandaloneRegisteredItemDefinition(definitionId);
        if (registeredDefinition == null)
            return recipes;

        var registeredRecipes = ReflectionUtils.TryGetFieldOrProperty(registeredDefinition, "Recipes")
            ?? ReflectionUtils.TryGetFieldOrProperty(registeredDefinition, "recipes");
        return ReflectionUtils.TryGetListCount(registeredRecipes) > 0 ? registeredRecipes : recipes;
    }

    private static bool IsMarijuanaProductDefinition(object definition)
    {
        if (definition == null)
            return false;

        try
        {
#if MONO
            var productDefinition = definition as ProductDefinition;
#else
            var il2CppDefinition = definition as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
            var productDefinition = il2CppDefinition?.TryCast<ProductDefinition>();
#endif
            // Generated mixes are general ProductDefinitions rather than WeedDefinitions. Their
            // typed DrugType is the stable discriminator on both Mono and IL2CPP.
            if (productDefinition != null && string.Equals(productDefinition.DrugType.ToString(), "Marijuana",
                    StringComparison.OrdinalIgnoreCase))
                return true;

            var drugType = ReflectionUtils.TryGetFieldOrProperty(definition, "DrugType")?.ToString();
            if (string.Equals(drugType, "Marijuana", StringComparison.OrdinalIgnoreCase))
                return true;

            // The game uses WeedDefinition for its marijuana product subtype. This fallback
            // keeps the grouping available while an in-flight generated product is exposing its
            // base members through a different IL2CPP wrapper layer.
            var typeName = definition.GetType().Name ?? string.Empty;
            return typeName.IndexOf("WeedDefinition", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            return false;
        }
    }

    private static string GetReflectedDefinitionId(object definition)
    {
        var id = ReflectionUtils.TryGetFieldOrProperty(definition, "ID")
            ?? ReflectionUtils.TryGetFieldOrProperty(definition, "Id")
            ?? ReflectionUtils.TryGetFieldOrProperty(definition, "id");
        return id?.ToString()?.Trim() ?? string.Empty;
    }

    private static string GetReflectedDefinitionName(object definition, string fallback)
    {
        var name = ReflectionUtils.TryGetFieldOrProperty(definition, "Name")
            ?? ReflectionUtils.TryGetFieldOrProperty(definition, "name");
        var value = name?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
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
                !state.IsOpen)
                return false;

            UpdateStandaloneSearchFocusState(state);
            UpdateStandaloneRecentChanges(state);
            if (!state.SettingsOpen || !state.AwaitingToggleKey)
                return false;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                state.AwaitingToggleKey = false;
                RefreshStandaloneSettingsPane(state);
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
                PersistStandaloneSettings(state);
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

    private static void UpdateStandaloneSearchFocusState(StandaloneBackpackState state)
    {
        if (state?.SearchInput == null || state.SearchBackground == null)
            return;

        var focused = state.SearchInput.isFocused;
        if (focused == state.SearchFocusPresented)
            return;

        state.SearchFocusPresented = focused;
        PlayStandaloneSearchFocus(state, focused);
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
                return "product products drug drugs";
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
            var metadata = new List<string>
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
            metadata.AddRange(GetProductSearchAliases(slot));

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

}
