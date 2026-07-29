using System.Collections;
using HarmonyLib;
using MelonLoader;
using PackRat.Config;
using PackRat.Extensions;
using PackRat.Helpers;
using PackRat.Networking;
using PackRat.Storage;
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
        Tiers,
        Layout
    }

    private enum StandaloneBackpackLayoutView
    {
        Backpack,
        Storage,
        Station,
        Deal
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

    private sealed class StandaloneBackpackSortTab
    {
        public StandaloneBackpackSortMode SortMode;
        public Button Button;
        public Text Label;
        public Action SelectAction;
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
        public RectTransform SettingsTiersPage;
        public RectTransform SettingsLayoutPage;
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
        public StandaloneBackpackLayoutView LayoutView;
        public bool SearchListenerBound;
        public bool SearchFocusPresented;
        public StandaloneBackpackDropdown ActiveDropdown;
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
    private const int PillSpriteSize = 32;
    private const int PillSpriteBorder = 8;
    private const float PillSpriteCornerRadius = 7f;
    private const int DesktopTabSpriteSize = 32;
    private const int DesktopTabCornerRadius = 10;
    private const string SettingsCogResourceName = "PackRat.assets.settings-cog-ui.png";
    private const int StorageBackpackSlotsPerPage = 20;
    private const int StorageBackpackGridRows = 4;
    private const float CompactPanelMargin = 24f;

    private static readonly Dictionary<int, BackpackPanelState> BackpackPanels = new Dictionary<int, BackpackPanelState>();
    private static readonly Dictionary<int, StorageMenuSlotCapacityState> StorageMenuSlotCapacities =
        new Dictionary<int, StorageMenuSlotCapacityState>();
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
        if (IsStandaloneBackpackOpen(__instance))
        {
            RecordStandaloneRecentChanges(__instance);
            BackpackStateSyncManager.CompleteLocalBackpackEdit();
            RestoreStandaloneBackpackSlotCapacity(__instance);
        }

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
        foreach (var pair in currentQuantities)
        {
            state.OpenItemQuantities.TryGetValue(pair.Key, out var openingQuantity);
            if (!Mathf.Approximately(openingQuantity, pair.Value))
                state.RecentItemTimestamps[pair.Key] = Time.unscaledTime;
        }

        state.OpenItemQuantities.Clear();
        foreach (var pair in currentQuantities)
            state.OpenItemQuantities[pair.Key] = pair.Value;
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
            state.ConsolidateLabel.text = "STACKS";

        if (state.TypeFilterButton != null)
            state.TypeFilterButton.interactable = typeOptions.Count > 0;
        if (state.QualityFilterButton != null)
            state.QualityFilterButton.interactable = qualityOptions.Count > 0;
        if (state.OrganizeButton != null)
            state.OrganizeButton.interactable = CanOrganizeStandaloneBackpack(state, backpackSlots);
        if (state.ConsolidateButton != null)
        {
            state.ConsolidateButton.gameObject.SetActive(state.IsHotkeyBackpack);
            state.ConsolidateButton.interactable = CanConsolidateStandaloneBackpack(state, backpackSlots);
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
            "STACKS", 8, out state.ConsolidateLabel);
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
            state.SettingsTabIndicator = CreateStandaloneSettingsTabIndicator(tabs);
            EventHelper.AddListener(() => SetStandaloneSettingsPage(state, StandaloneBackpackSettingsPage.General),
                state.SettingsGeneralButton.onClick);
            EventHelper.AddListener(() => SetStandaloneSettingsPage(state, StandaloneBackpackSettingsPage.Tiers),
                state.SettingsTiersButton.onClick);
            EventHelper.AddListener(() => SetStandaloneSettingsPage(state, StandaloneBackpackSettingsPage.Layout),
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
            PlayStandaloneSettingsClose(state);
            return;
        }

        state.SettingsOpen = true;
        state.AwaitingToggleKey = false;
        HideStandaloneDropdown(state);
        state.SearchInput?.DeactivateInputField();
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
            case StandaloneBackpackSettingsPage.Tiers:
                BuildStandaloneTierSettings(state);
                break;
            case StandaloneBackpackSettingsPage.Layout:
                BuildStandaloneLayoutSettings(state);
                break;
            default:
                BuildStandaloneGeneralSettings(state);
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
        var target = new Vector2((width / 3f * page) + 4f, 0f);
        indicator.sizeDelta = new Vector2((width / 3f) - 8f, 3f);
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
        SetStandaloneSettingsPageActive(state.SettingsTiersPage, state.SettingsPage == StandaloneBackpackSettingsPage.Tiers);
        SetStandaloneSettingsPageActive(state.SettingsLayoutPage, state.SettingsPage == StandaloneBackpackSettingsPage.Layout);
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
    /// transfer sequence. The operation intentionally avoids empty destinations: it reduces
    /// stack fragmentation without changing the player's established item ordering.
    /// </summary>
    private static void ConsolidateStandaloneBackpack(StandaloneBackpackState state)
    {
        var backpackSlots = GetStandaloneSourceSlots(state);
        if (!CanConsolidateStandaloneBackpack(state, backpackSlots))
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

            if (mergedStackCount == 0)
            {
                ModLogger.Info("[BackpackUI] Consolidate skipped: no compatible partial stacks were found.");
                return;
            }

            ModLogger.Info($"[BackpackUI] Consolidated {mergedStackCount} compatible stack transfers " +
                $"({movedQuantity} items moved) using the native inventory transfer path.");
            RefreshStandaloneFilterView(state);
        }
        catch (Exception ex)
        {
            ModLogger.Error("StorageMenuPatch.ConsolidateStandaloneBackpack", ex);
            RefreshStandaloneFilterView(state);
        }
    }

    private static bool CanConsolidateStandaloneBackpack(StandaloneBackpackState state, List<ItemSlot> backpackSlots)
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

        return false;
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
            RebuildStorageEntityQuickMove(openedEntity, GetBackpackSlots());
        }
        catch (Exception ex)
        {
            ModLogger.Error("StorageMenuPatch.ApplyBackpackSidePanel(StorageEntity)", ex);
        }
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
        }
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

}
