using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PackRat.Helpers;

/// <summary>
/// Validates and extracts runtime-facing control contracts from editor-authored PackRat prefabs.
/// </summary>
internal static class EditorUiAssetBundleBindings
{
    private const string CollapseRailPath = "CollapseRail";
    private const string HideButtonPath = "CollapseRail/HideButton";
    private const string CollapseIconPath = "CollapseRail/HideButton/CollapseIcon";
    private const string HideTooltipLabelPath = "CollapseRail/Tooltip/Label";
    private const string CollapsedHandlePath = "CollapsedHandle";
    private const string ShowButtonPath = "CollapsedHandle/ShowButton";
    private const string ExpandIconPath = "CollapsedHandle/ShowButton/ExpandIcon";
    private const string ShowTooltipLabelPath = "CollapsedHandle/Tooltip/Label";

    /// <summary>
    /// Instantiates and validates the complete standalone browser presentation. This compatibility
    /// wrapper keeps the original call site explicit while all browser panes share one extractor.
    /// </summary>
    internal static bool TryCreateStandaloneBrowser(Transform parent,
        out EditorUiStandaloneBrowserBinding binding)
    {
        return TryCreateBrowser(EditorUiPane.Standalone, parent, out binding);
    }

    /// <summary>
    /// Instantiates and validates one complete editor-authored browser pane. This method does not
    /// bind behavior or move native game slots; it exposes shared controls plus the exact optional
    /// contract owned by the requested surface.
    /// </summary>
    internal static bool TryCreateBrowser(EditorUiPane pane, Transform parent,
        out EditorUiStandaloneBrowserBinding binding)
    {
        binding = null;
        if (pane != EditorUiPane.Standalone && pane != EditorUiPane.Embedded && pane != EditorUiPane.Handover)
            return false;
        if (parent == null || !EditorUiAssetBundle.TryInstantiate(pane, parent, out var instance))
            return false;

        try
        {
            var root = instance.transform;
            binding = new EditorUiStandaloneBrowserBinding
            {
                SourcePane = pane,
                Root = Utils.GetComponentSafe<RectTransform>(instance),
                Header = FindRequiredComponent<RectTransform>(root, "Header"),
                HeaderAccent = FindRequiredComponent<Image>(root, "Header/Accent"),
                Title = FindRequiredComponent<Text>(root, "Header/Title"),
                Meta = FindRequiredComponent<Text>(root, "Header/Meta"),
                SearchInput = FindRequiredComponent<InputField>(root, "Header/Search"),
                SearchBackground = FindRequiredComponent<Image>(root, "Header/Search"),
                SearchText = FindRequiredComponent<Text>(root, "Header/Search/InputText"),
                SearchPlaceholder = FindRequiredComponent<Text>(root, "Header/Search/Placeholder"),
                StackButton = FindRequiredComponent<Button>(root, "Header/PrimaryActions/StackButton"),
                StackLabel = FindRequiredComponent<Text>(root, "Header/PrimaryActions/StackButton/Label"),
                SettingsButton = FindRequiredComponent<Button>(root, "Header/PrimaryActions/SettingsButton"),
                SettingsIcon = FindRequiredComponent<Image>(root,
                    "Header/PrimaryActions/SettingsButton/SettingsIcon"),
                TypeButton = FindRequiredComponent<Button>(root, "Header/FilterRow/TypeButton"),
                TypeLabel = FindRequiredComponent<Text>(root, "Header/FilterRow/TypeButton/Label"),
                QualityButton = FindRequiredComponent<Button>(root, "Header/FilterRow/QualityButton"),
                QualityLabel = FindRequiredComponent<Text>(root, "Header/FilterRow/QualityButton/Label"),
                OrderButton = FindRequiredComponent<Button>(root, "Header/FilterRow/OrderButton"),
                OrderLabel = FindRequiredComponent<Text>(root, "Header/FilterRow/OrderButton/Label"),
                OrganizeButton = FindRequiredComponent<Button>(root, "Header/FilterRow/OrganizeButton"),
                OrganizeLabel = FindRequiredComponent<Text>(root, "Header/FilterRow/OrganizeButton/Label"),
                ClearButton = FindRequiredComponent<Button>(root, "Header/FilterRow/ClearButton"),
                ClearLabel = FindRequiredComponent<Text>(root, "Header/FilterRow/ClearButton/Label"),
                SortTabs = FindRequiredComponent<RectTransform>(root, "Header/SortTabs"),
                AllTab = FindRequiredComponent<Button>(root, "Header/SortTabs/AllButton"),
                FavoritesTab = FindRequiredComponent<Button>(root, "Header/SortTabs/FavoritesButton"),
                NameTab = FindRequiredComponent<Button>(root, "Header/SortTabs/NameButton"),
                QuantityTab = FindRequiredComponent<Button>(root, "Header/SortTabs/QuantityButton"),
                QualityTab = FindRequiredComponent<Button>(root, "Header/SortTabs/QualityButton"),
                TypeTab = FindRequiredComponent<Button>(root, "Header/SortTabs/TypeButton"),
                RecentTab = FindRequiredComponent<Button>(root, "Header/SortTabs/RecentButton"),
                SlotViewport = FindRequiredComponent<RectTransform>(root, "SlotViewport"),
                SlotGrid = FindRequiredComponent<RectTransform>(root, "SlotViewport/SlotGrid"),
                Footer = FindRequiredComponent<RectTransform>(root, "Footer"),
                PreviousButton = FindRequiredComponent<Button>(root, "Footer/PreviousButton"),
                PageLabel = FindRequiredComponent<Text>(root, "Footer/PageLabel"),
                NextButton = FindRequiredComponent<Button>(root, "Footer/NextButton"),
                DoneButton = FindRequiredComponent<Button>(root, "Footer/DoneButton"),
                OverlayHost = FindRequiredComponent<RectTransform>(root, "OverlayHost"),
                ActiveFilterTab = FindRequiredComponent<RectTransform>(root, "OverlayHost/ActiveFilterTab"),
                ActiveFilterTabLabel = FindRequiredComponent<Text>(root, "OverlayHost/ActiveFilterTab/Label")
            };

            if (pane == EditorUiPane.Standalone)
                PopulateMetricsContract(root, binding);
            else
                PopulateRailContract(root, binding);

            if (pane == EditorUiPane.Embedded)
            {
                binding.BulkTransferRow = FindRequiredComponent<RectTransform>(root, "BulkTransferRow");
                binding.BulkSelectorButton = FindRequiredComponent<Button>(root,
                    "BulkTransferRow/BulkSelectorButton");
                binding.BulkSelectorLabel = FindRequiredComponent<Text>(root,
                    "BulkTransferRow/BulkSelectorButton/Label");
                binding.MoveToStorageButton = FindRequiredComponent<Button>(root,
                    "BulkTransferRow/MoveToStorageButton");
                binding.MoveToBackpackButton = FindRequiredComponent<Button>(root,
                    "BulkTransferRow/MoveToBackpackButton");
            }
            else if (pane == EditorUiPane.Handover)
            {
                binding.ModeRow = FindRequiredComponent<RectTransform>(root, "ModeRow");
                binding.BackpackModeButton = FindRequiredComponent<Button>(root, "ModeRow/BackpackButton");
                binding.VehicleModeButton = FindRequiredComponent<Button>(root, "ModeRow/VehicleButton");
                binding.TransferRow = FindRequiredComponent<RectTransform>(root, "TransferRow");
                binding.AutoFillButton = FindRequiredComponent<Button>(root, "TransferRow/AutoFillButton");
                binding.TransferStatusLabel = FindRequiredComponent<Text>(root, "TransferRow/StatusLabel");
            }

            if (!HasBrowserContract(binding))
            {
                ModLogger.Error(
                    $"[EditorUI] {pane} browser contract is incomplete; retaining the C# browser.");
                UnityEngine.Object.Destroy(instance);
                binding = null;
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            if (instance != null)
                UnityEngine.Object.Destroy(instance);
            binding = null;
            ModLogger.Error($"EditorUiAssetBundleBindings.TryCreateBrowser({pane})", ex);
            return false;
        }
    }

    private static void PopulateMetricsContract(Transform root, EditorUiStandaloneBrowserBinding binding)
    {
        binding.MetricsTrayRoot = FindRequiredComponent<RectTransform>(root, "OverlayHost/MetricsTray");
        binding.MetricsTrayPanel = FindRequiredComponent<RectTransform>(root, "OverlayHost/MetricsTray/Panel");
        binding.MetricsTraySummary = FindRequiredComponent<Text>(root, "OverlayHost/MetricsTray/Panel/Summary");
        binding.MetricsTrayContent = FindRequiredComponent<RectTransform>(root,
            "OverlayHost/MetricsTray/Panel/Scroll/Viewport/Content");
        binding.MetricsTrayEmptyLabel = FindRequiredComponent<Text>(root,
            "OverlayHost/MetricsTray/Panel/Scroll/Viewport/EmptyLabel");
        binding.MetricsTrayScrollbar = FindRequiredComponent<Scrollbar>(root,
            "OverlayHost/MetricsTray/Panel/Scroll/Scrollbar");
        binding.MetricsTrayToggleButton = FindRequiredComponent<Button>(root, "OverlayHost/MetricsToggle");
        binding.MetricsTrayOpenIcon = FindRequiredComponent<Image>(root, "OverlayHost/MetricsToggle/OpenIcon");
        binding.MetricsTrayCloseIcon = FindRequiredComponent<Image>(root, "OverlayHost/MetricsToggle/CloseIcon");

        var rowTemplate = root.Find("OverlayHost/MetricsTray/Panel/Scroll/Viewport/Content/RowTemplate");
        binding.MetricsTrayRowTemplate = rowTemplate?.gameObject;
        binding.MetricsTrayRowTemplateAccent = rowTemplate != null
            ? FindRequiredComponent<Image>(rowTemplate, "Accent")
            : null;
        binding.MetricsTrayRowTemplateName = rowTemplate != null
            ? FindRequiredComponent<Text>(rowTemplate, "Name")
            : null;
        binding.MetricsTrayRowTemplateDetails = rowTemplate != null
            ? FindRequiredComponent<Text>(rowTemplate, "Details")
            : null;
        binding.MetricsTrayExpandedWidth = binding.MetricsTrayRoot?.sizeDelta.x ?? 0f;
        binding.MetricsTraySeamOverlap = binding.MetricsTrayRoot?.anchoredPosition.x ?? 0f;
    }

    private static void PopulateRailContract(Transform root, EditorUiStandaloneBrowserBinding binding)
    {
        binding.CollapseRail = FindRequiredComponent<RectTransform>(root, CollapseRailPath);
        binding.HideButton = FindRequiredComponent<Button>(root, HideButtonPath);
        binding.CollapsedHandle = FindRequiredComponent<RectTransform>(root, CollapsedHandlePath);
        binding.ShowButton = FindRequiredComponent<Button>(root, ShowButtonPath);
    }

    /// <summary>
    /// Instantiates the PackRat-owned overlay canvas used by the handover browser. The serialized
    /// scaler and safe-area hosts remain authoritative; runtime supplies only screen bounds and
    /// pane placement.
    /// </summary>
    internal static bool TryCreateDedicatedCanvas(Transform temporaryParent,
        out EditorUiDedicatedCanvasBinding binding)
    {
        binding = null;
        if (temporaryParent == null ||
            !EditorUiAssetBundle.TryInstantiate(EditorUiPane.DedicatedCanvas, temporaryParent, out var instance))
            return false;

        try
        {
            binding = new EditorUiDedicatedCanvasBinding
            {
                Root = Utils.GetComponentSafe<RectTransform>(instance),
                Canvas = Utils.GetComponentSafe<Canvas>(instance),
                Scaler = Utils.GetComponentSafe<CanvasScaler>(instance),
                Raycaster = Utils.GetComponentSafe<GraphicRaycaster>(instance),
                SafeAreaRoot = FindRequiredComponent<RectTransform>(instance.transform, "SafeAreaRoot"),
                PaneHost = FindRequiredComponent<RectTransform>(instance.transform, "SafeAreaRoot/PaneHost"),
                PaneHostCanvasGroup = FindRequiredComponent<CanvasGroup>(instance.transform,
                    "SafeAreaRoot/PaneHost")
            };

            if (binding.Root == null || binding.Canvas == null || binding.Scaler == null ||
                binding.Raycaster == null || binding.SafeAreaRoot == null || binding.PaneHost == null ||
                binding.PaneHostCanvasGroup == null)
            {
                ModLogger.Error(
                    "[EditorUI] Dedicated canvas contract is incomplete; retaining the C# canvas.");
                UnityEngine.Object.Destroy(instance);
                binding = null;
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            UnityEngine.Object.Destroy(instance);
            binding = null;
            ModLogger.Error("EditorUiAssetBundleBindings.TryCreateDedicatedCanvas", ex);
            return false;
        }
    }

    /// <summary>
    /// Instantiates and validates the editor-authored settings modal. Authoring preview rows are
    /// disabled before the modal can be shown; runtime settings rows then populate the same six
    /// layout-owned page hosts.
    /// </summary>
    internal static bool TryCreateSettingsOverlay(Transform parent, out EditorUiSettingsBinding binding)
    {
        binding = null;
        if (parent == null || !EditorUiAssetBundle.TryInstantiate(EditorUiPane.Settings, parent, out var instance))
            return false;

        try
        {
            instance.SetActive(false);
            var root = instance.transform;
            binding = new EditorUiSettingsBinding
            {
                Root = Utils.GetComponentSafe<RectTransform>(instance),
                RootCanvasGroup = Utils.GetComponentSafe<CanvasGroup>(instance),
                BlockerButton = FindRequiredComponent<Button>(root, "Blocker"),
                Card = FindRequiredComponent<RectTransform>(root, "Card"),
                CardCanvasGroup = FindRequiredComponent<CanvasGroup>(root, "Card"),
                CloseButton = FindRequiredComponent<Button>(root, "Card/Header/CloseButton"),
                SessionStatusValue = FindRequiredComponent<Text>(root, "Card/SessionStatus/Value"),
                Tabs = FindRequiredComponent<RectTransform>(root, "Card/Tabs"),
                GeneralButton = FindRequiredComponent<Button>(root, "Card/Tabs/GeneralButton"),
                ThemeButton = FindRequiredComponent<Button>(root, "Card/Tabs/ThemeButton"),
                TiersButton = FindRequiredComponent<Button>(root, "Card/Tabs/TiersButton"),
                LayoutButton = FindRequiredComponent<Button>(root, "Card/Tabs/LayoutButton"),
                RoutingButton = FindRequiredComponent<Button>(root, "Card/Tabs/RoutingButton"),
                MetricsButton = FindRequiredComponent<Button>(root, "Card/Tabs/MetricsButton"),
                Content = FindRequiredComponent<RectTransform>(root, "Card/Content"),
                ScrollRect = FindRequiredComponent<ScrollRect>(root, "Card/Content"),
                GeneralPage = FindRequiredComponent<RectTransform>(root, "Card/Content/Viewport/GeneralPage"),
                ThemePage = FindRequiredComponent<RectTransform>(root, "Card/Content/Viewport/ThemePage"),
                TiersPage = FindRequiredComponent<RectTransform>(root, "Card/Content/Viewport/TiersPage"),
                LayoutPage = FindRequiredComponent<RectTransform>(root, "Card/Content/Viewport/LayoutPage"),
                RoutingPage = FindRequiredComponent<RectTransform>(root, "Card/Content/Viewport/RoutingPage"),
                MetricsPage = FindRequiredComponent<RectTransform>(root, "Card/Content/Viewport/MetricsPage")
            };

            if (!HasSettingsContract(binding))
            {
                ModLogger.Error(
                    "[EditorUI] Settings overlay contract is incomplete; retaining the C# settings modal.");
                UnityEngine.Object.Destroy(instance);
                binding = null;
                return false;
            }

            DisableSettingsPreviewRows(binding.GeneralPage);
            DisableSettingsPreviewRows(binding.ThemePage);
            DisableSettingsPreviewRows(binding.TiersPage);
            DisableSettingsPreviewRows(binding.LayoutPage);
            DisableSettingsPreviewRows(binding.RoutingPage);
            DisableSettingsPreviewRows(binding.MetricsPage);
            return true;
        }
        catch (Exception ex)
        {
            if (instance != null)
                UnityEngine.Object.Destroy(instance);
            binding = null;
            ModLogger.Error("EditorUiAssetBundleBindings.TryCreateSettingsOverlay", ex);
            return false;
        }
    }

    /// <summary>
    /// Instantiates and strictly validates the embedded prefab contract, then retains only its
    /// approved rail hierarchies under a PackRat-owned runtime host. Any missing node or component
    /// fails the whole binding so the caller can keep the established C# controls.
    /// </summary>
    internal static bool TryCreateEmbeddedRail(EditorUiPane pane, RectTransform host,
        out EditorUiEmbeddedRailBinding binding)
    {
        binding = null;
        if (pane != EditorUiPane.Embedded && pane != EditorUiPane.Handover)
            return false;
        if (host == null || !TryCreateBrowser(pane, host, out var browser))
            return false;

        try
        {
            browser.Root.gameObject.SetActive(false);
            var detached = TryDetachEmbeddedRail(browser, host, out binding);
            UnityEngine.Object.Destroy(browser.Root.gameObject);
            return detached;
        }
        catch (Exception ex)
        {
            if (browser?.Root != null)
                UnityEngine.Object.Destroy(browser.Root.gameObject);
            ModLogger.Error("EditorUiAssetBundleBindings.TryCreateEmbeddedRail", ex);
            return false;
        }
    }

    /// <summary>
    /// Detaches the authored side rail from a live complete browser. The browser can then be
    /// hidden independently while the restore handle remains active in its sibling host.
    /// </summary>
    internal static bool TryDetachEmbeddedRail(EditorUiStandaloneBrowserBinding browser, RectTransform host,
        out EditorUiEmbeddedRailBinding binding)
    {
        binding = null;
        if (browser == null || host == null ||
            (browser.SourcePane != EditorUiPane.Embedded && browser.SourcePane != EditorUiPane.Handover) ||
            !HasRailContract(browser))
            return false;

        browser.CollapseRail.SetParent(host, worldPositionStays: false);
        browser.CollapsedHandle.SetParent(host, worldPositionStays: false);
        browser.CollapseRail.gameObject.SetActive(true);
        browser.CollapsedHandle.gameObject.SetActive(false);
        binding = new EditorUiEmbeddedRailBinding(browser.SourcePane, host, browser.CollapseRail,
            browser.HideButton, browser.CollapsedHandle, browser.ShowButton);
        return true;
    }

    private static T FindRequiredComponent<T>(Transform root, string path) where T : Component
    {
        var node = root?.Find(path);
        return node != null ? Utils.GetComponentSafe<T>(node.gameObject) : null;
    }

    private static bool HasIconContract(Image icon, string spriteName)
    {
        return icon != null && icon.sprite != null && icon.sprite.name == spriteName && icon.preserveAspect &&
            !icon.raycastTarget;
    }

    private static bool HasTooltipContract(Text label, string copy, EventTrigger trigger)
    {
        // Unity 2022.3's editor API exposes EventTrigger.triggers, but Schedule I's generated
        // IL2CPP UnityEngine.UI wrapper does not provide that getter. The exact four serialized
        // callbacks are validated before the AssetBundle is exported; runtime binding only checks
        // that the authored EventTrigger component survived loading.
        return label != null && label.text == copy && !label.raycastTarget && trigger != null;
    }

    private static bool HasBrowserContract(EditorUiStandaloneBrowserBinding binding)
    {
        if (!(binding?.Root != null
            && binding.Header != null
            && binding.HeaderAccent != null
            && binding.Title != null
            && binding.Meta != null
            && binding.SearchInput != null
            && binding.SearchBackground != null
            && binding.SearchText != null
            && binding.SearchPlaceholder != null
            && binding.StackButton != null
            && binding.StackLabel != null
            && binding.SettingsButton != null
            && HasIconContract(binding.SettingsIcon, "SettingsSliders")
            && binding.TypeButton != null
            && binding.TypeLabel != null
            && binding.QualityButton != null
            && binding.QualityLabel != null
            && binding.OrderButton != null
            && binding.OrderLabel != null
            && binding.OrganizeButton != null
            && binding.OrganizeLabel != null
            && binding.ClearButton != null
            && binding.ClearLabel != null
            && binding.SortTabs != null
            && binding.AllTab != null
            && binding.FavoritesTab != null
            && binding.NameTab != null
            && binding.QuantityTab != null
            && binding.QualityTab != null
            && binding.TypeTab != null
            && binding.RecentTab != null
            && binding.SlotViewport != null
            && binding.SlotGrid != null
            && binding.SlotGrid.childCount == 0
            && binding.Footer != null
            && binding.PreviousButton != null
            && binding.PageLabel != null
            && binding.NextButton != null
            && binding.DoneButton != null
            && binding.OverlayHost != null
            && binding.ActiveFilterTab != null
            && binding.ActiveFilterTabLabel != null))
            return false;

        return binding.SourcePane switch
        {
            EditorUiPane.Standalone => HasMetricsContract(binding),
            EditorUiPane.Embedded => HasRailContract(binding)
                && binding.BulkTransferRow != null
                && binding.BulkSelectorButton != null
                && binding.BulkSelectorLabel != null
                && binding.MoveToStorageButton != null
                && binding.MoveToBackpackButton != null,
            EditorUiPane.Handover => HasRailContract(binding)
                && binding.ModeRow != null
                && binding.BackpackModeButton != null
                && binding.VehicleModeButton != null
                && binding.TransferRow != null
                && binding.AutoFillButton != null
                && binding.TransferStatusLabel != null,
            _ => false
        };
    }

    private static bool HasMetricsContract(EditorUiStandaloneBrowserBinding binding)
    {
        return binding.MetricsTrayRoot != null
            && binding.MetricsTrayPanel != null
            && binding.MetricsTraySummary != null
            && binding.MetricsTrayContent != null
            && binding.MetricsTrayEmptyLabel != null
            && binding.MetricsTrayRowTemplate != null
            && binding.MetricsTrayRowTemplateAccent != null
            && binding.MetricsTrayRowTemplateName != null
            && binding.MetricsTrayRowTemplateDetails != null
            && binding.MetricsTrayScrollbar != null
            && binding.MetricsTrayToggleButton != null
            && HasIconContract(binding.MetricsTrayOpenIcon, "ChevronsLeft")
            && HasIconContract(binding.MetricsTrayCloseIcon, "ChevronsRight")
            && binding.MetricsTrayExpandedWidth > 0f;
    }

    private static bool HasRailContract(EditorUiStandaloneBrowserBinding binding)
    {
        var hideIcon = binding?.HideButton != null
            ? FindRequiredComponent<Image>(binding.HideButton.transform, "CollapseIcon")
            : null;
        var hideTooltipLabel = binding?.CollapseRail != null
            ? FindRequiredComponent<Text>(binding.CollapseRail, "Tooltip/Label")
            : null;
        var showIcon = binding?.ShowButton != null
            ? FindRequiredComponent<Image>(binding.ShowButton.transform, "ExpandIcon")
            : null;
        var showTooltipLabel = binding?.CollapsedHandle != null
            ? FindRequiredComponent<Text>(binding.CollapsedHandle, "Tooltip/Label")
            : null;
        var hideTrigger = binding?.HideButton != null
            ? Utils.GetComponentSafe<EventTrigger>(binding.HideButton.gameObject)
            : null;
        var showTrigger = binding?.ShowButton != null
            ? Utils.GetComponentSafe<EventTrigger>(binding.ShowButton.gameObject)
            : null;

        return binding?.CollapseRail != null
            && binding.HideButton != null
            && HasIconContract(hideIcon, "ChevronsLeft")
            && HasTooltipContract(hideTooltipLabel, "Hide backpack", hideTrigger)
            && binding.CollapsedHandle != null
            && binding.ShowButton != null
            && HasIconContract(showIcon, "ChevronsRight")
            && HasTooltipContract(showTooltipLabel, "Show backpack", showTrigger);
    }

    private static bool HasSettingsContract(EditorUiSettingsBinding binding)
    {
        return binding?.Root != null
            && binding.RootCanvasGroup != null
            && binding.BlockerButton != null
            && binding.Card != null
            && binding.CardCanvasGroup != null
            && binding.CloseButton != null
            && binding.SessionStatusValue != null
            && binding.Tabs != null
            && binding.GeneralButton != null
            && binding.ThemeButton != null
            && binding.TiersButton != null
            && binding.LayoutButton != null
            && binding.RoutingButton != null
            && binding.MetricsButton != null
            && binding.Content != null
            && binding.ScrollRect != null
            && binding.GeneralPage != null
            && binding.ThemePage != null
            && binding.TiersPage != null
            && binding.LayoutPage != null
            && binding.RoutingPage != null
            && binding.MetricsPage != null;
    }

    private static void DisableSettingsPreviewRows(RectTransform page)
    {
        if (page == null)
            return;

        for (var index = page.childCount - 1; index >= 0; index--)
        {
            var child = page.GetChild(index);
            if (child == null || !child.name.StartsWith("Preview_", StringComparison.Ordinal))
                continue;

            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
    }
}
