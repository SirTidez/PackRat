using UnityEngine;
using UnityEngine.UI;

namespace PackRat.Helpers;

/// <summary>
/// Holds the validated controls from the editor-authored standalone backpack pane. Native game
/// slot views remain outside this tree and render over its intentionally empty slot framework.
/// </summary>
internal sealed class EditorUiStandaloneBrowserBinding
{
    public EditorUiPane SourcePane { get; set; }
    public RectTransform Root { get; set; }
    public RectTransform Header { get; set; }
    public Image HeaderAccent { get; set; }
    public Text Title { get; set; }
    public Text Meta { get; set; }
    public InputField SearchInput { get; set; }
    public Image SearchBackground { get; set; }
    public Text SearchText { get; set; }
    public Text SearchPlaceholder { get; set; }
    public Button StackButton { get; set; }
    public Text StackLabel { get; set; }
    public Button SettingsButton { get; set; }
    public Image SettingsIcon { get; set; }
    public Button TypeButton { get; set; }
    public Text TypeLabel { get; set; }
    public Button QualityButton { get; set; }
    public Text QualityLabel { get; set; }
    public Button OrderButton { get; set; }
    public Text OrderLabel { get; set; }
    public Button OrganizeButton { get; set; }
    public Text OrganizeLabel { get; set; }
    public Button ClearButton { get; set; }
    public Text ClearLabel { get; set; }
    public RectTransform SortTabs { get; set; }
    public Button AllTab { get; set; }
    public Button FavoritesTab { get; set; }
    public Button NameTab { get; set; }
    public Button QuantityTab { get; set; }
    public Button QualityTab { get; set; }
    public Button TypeTab { get; set; }
    public Button RecentTab { get; set; }
    public RectTransform SlotViewport { get; set; }
    public RectTransform SlotGrid { get; set; }
    public bool AuthoredGeometryCaptured { get; set; }
    public Vector2 AuthoredRootSize { get; set; }
    public Vector2 AuthoredSlotGridSize { get; set; }
    public Vector2 AppliedRootSize { get; set; }
    public RectTransform Footer { get; set; }
    public Button PreviousButton { get; set; }
    public Text PageLabel { get; set; }
    public Button NextButton { get; set; }
    public Button DoneButton { get; set; }
    public RectTransform OverlayHost { get; set; }
    public RectTransform ActiveFilterTab { get; set; }
    public Text ActiveFilterTabLabel { get; set; }
    public RectTransform MetricsTrayRoot { get; set; }
    public RectTransform MetricsTrayPanel { get; set; }
    public RectTransform MetricsTrayContent { get; set; }
    public Text MetricsTraySummary { get; set; }
    public Text MetricsTrayEmptyLabel { get; set; }
    public GameObject MetricsTrayRowTemplate { get; set; }
    public Image MetricsTrayRowTemplateAccent { get; set; }
    public Text MetricsTrayRowTemplateName { get; set; }
    public Text MetricsTrayRowTemplateDetails { get; set; }
    public Scrollbar MetricsTrayScrollbar { get; set; }
    public Button MetricsTrayToggleButton { get; set; }
    public Image MetricsTrayOpenIcon { get; set; }
    public Image MetricsTrayCloseIcon { get; set; }
    public float MetricsTrayExpandedWidth { get; set; }
    public float MetricsTraySeamOverlap { get; set; }

    // Embedded storage/station contract. These remain null for standalone and handover panes.
    public RectTransform BulkTransferRow { get; set; }
    public Button BulkSelectorButton { get; set; }
    public Text BulkSelectorLabel { get; set; }
    public Button MoveToStorageButton { get; set; }
    public Button MoveToBackpackButton { get; set; }

    // Handover-only contract. The existing handover controller retains ownership of mode and
    // transfer behavior; these fields replace only its runtime-created presentation.
    public RectTransform ModeRow { get; set; }
    public Button BackpackModeButton { get; set; }
    public Button VehicleModeButton { get; set; }
    public RectTransform TransferRow { get; set; }
    public Button AutoFillButton { get; set; }
    public Text TransferStatusLabel { get; set; }

    // Embedded and handover panes both author the side-mounted visibility rail. Runtime detaches
    // it into a sibling host so the restore handle can remain visible while the browser is hidden.
    public RectTransform CollapseRail { get; set; }
    public Button HideButton { get; set; }
    public RectTransform CollapsedHandle { get; set; }
    public Button ShowButton { get; set; }
}
