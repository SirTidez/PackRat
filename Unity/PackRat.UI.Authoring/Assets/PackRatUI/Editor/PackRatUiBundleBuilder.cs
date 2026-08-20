using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PackRat.UI.Authoring.Editor
{
    /// <summary>
    /// Creates editable, runtime-script-free uGUI prefabs and exports them as a deterministic
    /// Windows AssetBundle compatible with Schedule I's Unity 2022.3.62f2 player.
    /// </summary>
    public static class PackRatUiBundleBuilder
    {
        public const string BundleName = "packrat_ui_windows.bundle";
        public const string StandalonePrefabPath = "Assets/PackRatUI/Prefabs/PackRatStandalonePane.prefab";
        public const string EmbeddedPrefabPath = "Assets/PackRatUI/Prefabs/PackRatEmbeddedPane.prefab";
        public const string HandoverPrefabPath = "Assets/PackRatUI/Prefabs/PackRatHandoverPane.prefab";
        public const string SettingsPrefabPath = "Assets/PackRatUI/Prefabs/PackRatSettingsOverlay.prefab";
        public const string DedicatedCanvasPrefabPath = "Assets/PackRatUI/Prefabs/PackRatDedicatedCanvas.prefab";

        private const string GeneratedDirectory = "Assets/PackRatUI/Generated";
        private const string PrefabDirectory = "Assets/PackRatUI/Prefabs";
        private const string RoundedSpritePath = GeneratedDirectory + "/RoundedPanel.png";
        private const string RoundedTopSpritePath = GeneratedDirectory + "/RoundedTopPanel.png";
        private const string RoundedBottomSpritePath = GeneratedDirectory + "/RoundedBottomPanel.png";
        private const string ControlSpritePath = GeneratedDirectory + "/RoundedControl.png";
        private const string TopControlSpritePath = GeneratedDirectory + "/RoundedTopControl.png";
        private const string LeftPanelSpritePath = GeneratedDirectory + "/RoundedLeftPanel.png";
        private const string TopLeftPanelSpritePath = GeneratedDirectory + "/RoundedTopLeftPanel.png";
        private const string BottomLeftPanelSpritePath = GeneratedDirectory + "/RoundedBottomLeftPanel.png";
        private const string LeftControlSpritePath = GeneratedDirectory + "/RoundedLeftControl.png";
        private const string PillSpritePath = GeneratedDirectory + "/Pill.png";
        private const string SettingsIconSpritePath = GeneratedDirectory + "/SettingsSliders.png";
        private const string CollapseIconSpritePath = GeneratedDirectory + "/ChevronsLeft.png";
        private const string ExpandIconSpritePath = GeneratedDirectory + "/ChevronsRight.png";

        private static readonly Color32 Card = new Color32(15, 21, 28, 242);
        private static readonly Color32 Header = new Color32(35, 61, 86, 252);
        private static readonly Color32 Accent = new Color32(76, 173, 229, 255);
        private static readonly Color32 Control = new Color32(18, 30, 40, 250);
        private static readonly Color32 ControlAlt = new Color32(20, 35, 47, 255);
        private static readonly Color32 Selected = new Color32(48, 128, 170, 255);
        private static readonly Color32 Search = new Color32(10, 15, 20, 250);
        private static readonly Color32 PrimaryText = new Color32(244, 247, 250, 255);
        private static readonly Color32 SecondaryText = new Color32(166, 205, 229, 255);

        private static Font _font;
        private static Sprite _roundedSprite;
        private static Sprite _roundedTopSprite;
        private static Sprite _roundedBottomSprite;
        private static Sprite _controlSprite;
        private static Sprite _topControlSprite;
        private static Sprite _leftPanelSprite;
        private static Sprite _topLeftPanelSprite;
        private static Sprite _bottomLeftPanelSprite;
        private static Sprite _leftControlSprite;
        private static Sprite _settingsIconSprite;
        private static Sprite _collapseIconSprite;
        private static Sprite _expandIconSprite;

        [MenuItem("PackRat UI/Create or Refresh Prefabs")]
        public static void CreateOrRefreshPrefabs()
        {
            EnsureProjectAssets();
            SavePrefab(CreateStandalonePane(), StandalonePrefabPath);
            SavePrefab(CreateEmbeddedPane(), EmbeddedPrefabPath);
            SavePrefab(CreateHandoverPane(), HandoverPrefabPath);
            SavePrefab(CreateSettingsOverlay(), SettingsPrefabPath);
            SavePrefab(CreateDedicatedCanvas(), DedicatedCanvasPrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("PackRat UI: refreshed five editor-authored prefabs.");
        }

        [MenuItem("PackRat UI/Validate Prefabs")]
        public static void ValidatePrefabs()
        {
            var failures = new List<string>();
            ValidateBrowserPrefab(StandalonePrefabPath, false, failures);
            ValidateBrowserPrefab(EmbeddedPrefabPath, true, failures);
            ValidateHandoverPrefab(failures);
            ValidateSettingsPrefab(failures);
            ValidateDedicatedCanvas(failures);
            ValidateResolutionMatrix(failures);

            if (failures.Count > 0)
                throw new InvalidOperationException("PackRat UI prefab validation failed:\n - " +
                    string.Join("\n - ", failures));

            Debug.Log("PackRat UI: prefab binding and responsive-layout validation passed.");
        }

        [MenuItem("PackRat UI/Build Windows AssetBundle")]
        public static void BuildWindowsAssetBundle()
        {
            ValidatePrefabs();
            var outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BuildArtifacts"));
            Directory.CreateDirectory(outputDirectory);

            var build = new AssetBundleBuild
            {
                assetBundleName = BundleName,
                assetNames = new[]
                {
                    StandalonePrefabPath,
                    EmbeddedPrefabPath,
                    HandoverPrefabPath,
                    SettingsPrefabPath,
                    DedicatedCanvasPrefabPath
                }
            };

            var manifest = BuildPipeline.BuildAssetBundles(
                outputDirectory,
                new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64);
            if (manifest == null)
                throw new InvalidOperationException("PackRat UI AssetBundle build returned no manifest.");

            var builtBundle = Path.Combine(outputDirectory, BundleName);
            if (!File.Exists(builtBundle))
                throw new FileNotFoundException("Expected AssetBundle was not produced.", builtBundle);

            ValidateBuiltBundle(builtBundle);
            Debug.Log("PackRat UI: reopened and validated the built Windows AssetBundle.");
            var repositoryAssets = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "assets"));
            Directory.CreateDirectory(repositoryAssets);
            var destination = Path.Combine(repositoryAssets, BundleName);
            File.Copy(builtBundle, destination, true);
            File.Copy(builtBundle + ".manifest", destination + ".manifest", true);
            Debug.Log("PackRat UI: exported " + destination);
        }

        [MenuItem("PackRat UI/Build and Validate All")]
        public static void BuildAndValidateAll()
        {
            // The serialized prefabs are the approved editor-owned source of truth. Recreating
            // them here would discard manual editor refinements and generate unstable file IDs.
            // Template regeneration remains an explicit, separately named menu action.
            EnsureProjectAssets();
            BuildWindowsAssetBundle();
            Debug.Log("PackRat UI: authoring pipeline completed successfully.");
        }

        private static GameObject CreateStandalonePane()
        {
            var root = CreatePanel("PackRatStandalonePane", null, Card, _roundedSprite);
            ConfigureFixedCard(root.GetComponent<RectTransform>(), new Vector2(448f, 604f));
            CreateSharedBrowser(root.transform, "RUCKSACK", "8/8 USED  •  PAGE 1/1", 4,
                includeMetricsDrawer: true);
            return root;
        }

        private static GameObject CreateEmbeddedPane()
        {
            var root = CreatePanel("PackRatEmbeddedPane", null, Card, _roundedSprite);
            // The embedded framework is authored around the canonical 5x4, 72 px ItemSlotUI
            // projection. Runtime may expand it further for game owners with larger slot prefabs.
            ConfigureFixedCard(root.GetComponent<RectTransform>(), new Vector2(420f, 606f));
            CreateSharedBrowser(root.transform, "BACKPACK", "8/8 USED  •  PAGE 1/1", 4,
                contentBottom: 100f, includeBulkTransfer: true);
            CreateCollapseRails(root.transform);
            return root;
        }

        private static GameObject CreateHandoverPane()
        {
            var root = CreatePanel("PackRatHandoverPane", null, Card, _roundedSprite);
            ConfigureFixedCard(root.GetComponent<RectTransform>(), new Vector2(420f, 660f));
            CreateSharedBrowser(root.transform, "BACKPACK", "DEAL STORAGE  •  PAGE 1/1", 4,
                contentBottom: 154f);
            CreateCollapseRails(root.transform);

            var modeRow = CreateRegion("ModeRow", root.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(14f, 62f), new Vector2(-14f, 98f));
            ConfigureHorizontal(modeRow.gameObject, 6f, new RectOffset(0, 0, 0, 0), true);
            AddFlexibleButton(modeRow, "BackpackButton", "BACKPACK", Selected, 1f);
            AddFlexibleButton(modeRow, "VehicleButton", "VEHICLE", ControlAlt, 1f);

            var transfer = CreateRegion("TransferRow", root.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(14f, 14f), new Vector2(-14f, 56f));
            ConfigureHorizontal(transfer.gameObject, 8f, new RectOffset(0, 0, 0, 0), true);
            AddFlexibleButton(transfer, "AutoFillButton", "AUTO-FILL DEAL", Accent, 0.8f);
            var status = CreateText("StatusLabel", transfer, "READY", 10, FontStyle.Bold,
                TextAnchor.MiddleRight, SecondaryText);
            AddLayout(status.gameObject, 100f, 34f, 1f);
            return root;
        }

        private static GameObject CreateSettingsOverlay()
        {
            var root = new GameObject("PackRatSettingsOverlay", typeof(RectTransform), typeof(CanvasGroup));
            Stretch(root.GetComponent<RectTransform>());

            var blocker = CreatePanel("Blocker", root.transform, new Color32(4, 9, 13, 170), null);
            Stretch(blocker.GetComponent<RectTransform>());
            blocker.GetComponent<Image>().raycastTarget = true;
            var blockerButton = blocker.AddComponent<Button>();
            blockerButton.targetGraphic = blocker.GetComponent<Image>();
            blockerButton.transition = Selectable.Transition.None;

            var card = CreatePanel("Card", root.transform, new Color32(10, 23, 31, 255), _roundedSprite);
            ConfigureFixedCard(card.GetComponent<RectTransform>(), new Vector2(620f, 480f));
            card.AddComponent<CanvasGroup>();

            var header = CreateRegion("Header", card.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(14f, -56f), new Vector2(-14f, -14f));
            ConfigureHorizontal(header.gameObject, 10f, new RectOffset(10, 10, 4, 4), true);
            var title = CreateText("Title", header, "BACKPACK SETTINGS", 18, FontStyle.Bold,
                TextAnchor.MiddleLeft, PrimaryText);
            AddLayout(title.gameObject, 220f, 34f, 1f);
            var close = CreateButton("CloseButton", header, "CLOSE", 10, Control);
            AddLayout(close, 72f, 32f, 0f);

            var session = CreatePanel("SessionStatus", card.transform, ControlAlt, _roundedSprite);
            SetRect(session.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one,
                new Vector2(0.5f, 1f), new Vector2(-28f, 32f), new Vector2(0f, -62f));
            var sessionLabel = CreateText("Label", session.transform, "SESSION STATUS", 10, FontStyle.Bold,
                TextAnchor.MiddleLeft, SecondaryText);
            SetStretchOffsets(sessionLabel.GetComponent<RectTransform>(), 10f, 300f, 2f, 2f);
            var value = CreateText("Value", session.transform, "LOCAL / EDITABLE", 10, FontStyle.Bold,
                TextAnchor.MiddleRight, new Color32(119, 221, 144, 255));
            SetStretchOffsets(value.GetComponent<RectTransform>(), 300f, 10f, 2f, 2f);

            var tabs = CreateRegion("Tabs", card.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(14f, -138f), new Vector2(-14f, -100f));
            ConfigureHorizontal(tabs.gameObject, 3f, new RectOffset(0, 0, 0, 0), true);
            AddFlexibleButton(tabs, "GeneralButton", "GENERAL", Selected, 1f);
            AddFlexibleButton(tabs, "ThemeButton", "THEME", ControlAlt, 1f);
            AddFlexibleButton(tabs, "TiersButton", "TIERS", ControlAlt, 1f);
            AddFlexibleButton(tabs, "LayoutButton", "LAYOUT", ControlAlt, 1f);
            AddFlexibleButton(tabs, "RoutingButton", "ROUTING", ControlAlt, 1f);
            AddFlexibleButton(tabs, "MetricsButton", "METRICS", ControlAlt, 1f);

            var content = CreatePanel("Content", card.transform, new Color32(16, 32, 43, 245), _roundedSprite);
            SetStretchOffsets(content.GetComponent<RectTransform>(), 14f, 14f, 144f, 14f);
            var scroll = content.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = CreateRegion("Viewport", content.transform, Vector2.zero, Vector2.one,
                new Vector2(8f, 8f), new Vector2(-8f, -8f));
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = viewport;
            foreach (var pageName in new[] { "GeneralPage", "ThemePage", "TiersPage", "LayoutPage", "RoutingPage", "MetricsPage" })
            {
                var page = CreateRegion(pageName, viewport, new Vector2(0f, 1f), Vector2.one, Vector2.zero, Vector2.zero);
                ConfigureVertical(page.gameObject, 7f, new RectOffset(6, 6, 6, 6));
                page.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                CreateSettingsPreviewRows(page, pageName);
                page.gameObject.SetActive(pageName == "GeneralPage");
            }
            scroll.content = viewport.Find("GeneralPage") as RectTransform;
            return root;
        }

        private static GameObject CreateDedicatedCanvas()
        {
            var root = new GameObject("PackRatDedicatedCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            Stretch(root.GetComponent<RectTransform>());
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            scaler.referencePixelsPerUnit = 100f;

            var safeArea = CreateRegion("SafeAreaRoot", root.transform, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            var paneHost = CreateRegion("PaneHost", safeArea, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            paneHost.gameObject.AddComponent<CanvasGroup>();
            return root;
        }

        private static void CreateSharedBrowser(Transform root, string titleText, string metaText, int columns,
            float contentBottom = 58f, bool includeBulkTransfer = false, bool includeMetricsDrawer = false)
        {
            var header = CreatePanel("Header", root, Header, _roundedTopSprite);
            SetRect(header.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one,
                new Vector2(0.5f, 1f), new Vector2(-20f, 174f), new Vector2(0f, -10f));
            var accent = CreatePanel("Accent", header.transform, Accent, null);
            SetRect(accent.GetComponent<RectTransform>(), Vector2.zero, new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), new Vector2(2f, 3f), new Vector2(-1f, 0f));

            var title = CreateText("Title", header.transform, titleText, 20, FontStyle.Bold,
                TextAnchor.MiddleLeft, PrimaryText);
            SetRect(title.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0.72f, 1f),
                new Vector2(0f, 1f), new Vector2(-14f, 30f), new Vector2(12f, -10f));
            var meta = CreateText("Meta", header.transform, metaText, 11, FontStyle.Bold,
                TextAnchor.MiddleLeft, SecondaryText);
            SetRect(meta.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0.72f, 1f),
                new Vector2(0f, 1f), new Vector2(-14f, 18f), new Vector2(12f, -42f));

            var primary = CreateRegion("PrimaryActions", header.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-124f, -44f), new Vector2(-10f, -10f));
            ConfigureHorizontal(primary.gameObject, 5f, new RectOffset(0, 0, 0, 0), true);
            AddFlexibleButton(primary, "StackButton", "STACK", Control, 1f);
            var settings = AddFlexibleButton(primary, "SettingsButton", string.Empty, Control, 0f, 34f);
            CreateSettingsIcon(settings.transform);

            var filter = CreateRegion("FilterRow", header.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(10f, -92f), new Vector2(-10f, -64f));
            ConfigureHorizontal(filter.gameObject, 4f, new RectOffset(0, 0, 0, 0), true);
            AddFlexibleButton(filter, "TypeButton", "TYPE: ALL", ControlAlt, 1.2f);
            AddFlexibleButton(filter, "QualityButton", "QUALITY: ALL", ControlAlt, 1.35f, -1f, 8);
            AddFlexibleButton(filter, "OrderButton", "ORDER: ASC", ControlAlt, 1.2f);
            AddFlexibleButton(filter, "OrganizeButton", "ORGANIZE", ControlAlt, 1.1f);
            AddFlexibleButton(filter, "ClearButton", "CLEAR", ControlAlt, 0.8f);

            CreateSearch("Search", header.transform, "Search name, quality, or type");
            var searchRect = header.transform.Find("Search").GetComponent<RectTransform>();
            SetRect(searchRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-20f, 30f), new Vector2(0f, -98f));

            var tabs = CreateRegion("SortTabs", header.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(10f, -6f), new Vector2(-10f, 34f));
            ConfigureHorizontal(tabs.gameObject, 3f, new RectOffset(0, 0, 0, 0), true);
            var defaultTab = AddSortTab(tabs, "AllButton", "ALL", 0.8f);
            AddSortTab(tabs, "FavoritesButton", "FAV", 0.8f);
            AddSortTab(tabs, "NameButton", "NAME", 1f);
            AddSortTab(tabs, "QuantityButton", "QTY", 0.8f);
            AddSortTab(tabs, "QualityButton", "QUALITY", 1.15f, 8);
            AddSortTab(tabs, "TypeButton", "TYPE", 0.9f);
            AddSortTab(tabs, "RecentButton", "RECENT", 1.15f);
            accent.transform.SetAsLastSibling();

            var viewport = CreatePanel("SlotViewport", root, new Color32(13, 17, 20, 160),
                _roundedBottomSprite);
            var viewportRect = viewport.GetComponent<RectTransform>();
            SetStretchOffsets(viewportRect, 10f, 10f, 184f, contentBottom);
            viewport.AddComponent<RectMask2D>();
            var grid = CreateRegion("SlotGrid", viewport.transform, Vector2.zero, Vector2.one,
                new Vector2(8f, 8f), new Vector2(-8f, -8f));
            var gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            gridLayout.childAlignment = TextAnchor.UpperCenter;
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = columns;
            gridLayout.cellSize = new Vector2(80f, 102f);
            gridLayout.spacing = new Vector2(8f, 8f);
            gridLayout.padding = new RectOffset(2, 2, 2, 2);

            var footer = CreateRegion("Footer", root, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(14f, contentBottom - 48f), new Vector2(-14f, contentBottom - 8f));
            ConfigureHorizontal(footer.gameObject, 6f, new RectOffset(0, 0, 0, 0), true);
            AddFlexibleButton(footer, "PreviousButton", "<", Control, 0f, 32f);
            var page = CreateText("PageLabel", footer, "PAGE 1/1", 10, FontStyle.Bold,
                TextAnchor.MiddleCenter, SecondaryText);
            AddLayout(page.gameObject, 72f, 32f, 1f);
            AddFlexibleButton(footer, "NextButton", ">", Control, 0f, 32f);
            AddFlexibleButton(footer, "DoneButton", "DONE", Accent, 0f, 72f);
            if (includeBulkTransfer)
            {
                var bulkTransfer = CreateRegion("BulkTransferRow", root, new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(14f, 10f), new Vector2(-14f, contentBottom - 54f));
                ConfigureHorizontal(bulkTransfer.gameObject, 6f, new RectOffset(0, 0, 0, 0), true);
                AddFlexibleButton(bulkTransfer, "BulkSelectorButton", "MOVE: ALL", ControlAlt, 1.2f);
                AddFlexibleButton(bulkTransfer, "MoveToStorageButton", "TO STORAGE", ControlAlt, 1f);
                AddFlexibleButton(bulkTransfer, "MoveToBackpackButton", "TO PACK", ControlAlt, 1f);
            }

            var overlay = CreateRegion("OverlayHost", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            LayoutRebuilder.ForceRebuildLayoutImmediate(tabs);
            CreateActiveTabOverlay(overlay, defaultTab.GetComponent<RectTransform>(), viewportRect, "ALL");
            var dropdown = CreatePanel("Dropdown", overlay, new Color32(12, 21, 30, 255), _roundedSprite);
            SetRect(dropdown.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 1f), new Vector2(180f, 140f), new Vector2(0f, 60f));
            dropdown.SetActive(false);
            if (includeMetricsDrawer)
                CreateMetricsDrawer(overlay);
        }

        private static void CreateSettingsPreviewRows(RectTransform page, string pageName)
        {
            var names = pageName == "GeneralPage"
                ? new[] { "Toggle key", "UI animations", "Reduced motion", "Developer profiler" }
                : new[] { pageName.Replace("Page", string.Empty) + " preset", "Primary option", "Secondary option", "Reset to default" };
            foreach (var name in names)
            {
                var row = CreatePanel("Preview_" + name.Replace(" ", string.Empty), page, ControlAlt, _roundedSprite);
                AddLayout(row, -1f, 42f, 1f);
                var label = CreateText("Label", row.transform, name.ToUpperInvariant(), 11, FontStyle.Bold,
                    TextAnchor.MiddleLeft, PrimaryText);
                SetStretchOffsets(label.GetComponent<RectTransform>(), 12f, 86f, 2f, 2f);
                var value = CreateText("Value", row.transform, "EDIT", 10, FontStyle.Bold,
                    TextAnchor.MiddleCenter, SecondaryText);
                SetRect(value.GetComponent<RectTransform>(), new Vector2(1f, 0f), Vector2.one,
                    new Vector2(1f, 0.5f), new Vector2(74f, 30f), new Vector2(-8f, 0f));
            }
        }

        private static GameObject AddSortTab(RectTransform parent, string name, string label,
            float flexibleWidth, int fontSize = 9)
        {
            return AddFlexibleButton(parent, name, label, Control, flexibleWidth, -1f, fontSize);
        }

        private static void CreateActiveTabOverlay(RectTransform overlayHost, RectTransform sourceTab,
            RectTransform slotViewport, string label)
        {
            var corners = new Vector3[4];
            sourceTab.GetWorldCorners(corners);
            var minimum = overlayHost.InverseTransformPoint(corners[0]);
            var maximum = overlayHost.InverseTransformPoint(corners[2]);
            var viewportCorners = new Vector3[4];
            slotViewport.GetWorldCorners(viewportCorners);
            var dividerY = overlayHost.InverseTransformPoint(viewportCorners[1]).y;
            minimum.y = Mathf.Max(minimum.y, dividerY);

            // The selected tab joins the divider and slot surface, so only its exposed top edge
            // may be rounded. A fully rounded control leaves visible lower arcs above the divider.
            var active = CreatePanel("ActiveFilterTab", overlayHost, Accent, _topControlSprite);
            SetRect(active.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(maximum.x - minimum.x, maximum.y - minimum.y),
                new Vector2((minimum.x + maximum.x) * 0.5f, (minimum.y + maximum.y) * 0.5f));
            active.GetComponent<Image>().raycastTarget = false;
            var activeLabel = CreateText("Label", active.transform, label, 9, FontStyle.Bold,
                TextAnchor.MiddleCenter, PrimaryText);
            Stretch(activeLabel.GetComponent<RectTransform>(), 3f);
        }

        /// <summary>
        /// Authors the main-backpack metrics drawer as a true left extension of the inventory and
        /// footer regions. Its top terminates at the browser's cyan divider and its right ten logical
        /// pixels overlap the card border, so it joins the backpack without recreating the header.
        /// Dynamic product data is supplied at runtime through the inactive row template.
        /// </summary>
        private static void CreateMetricsDrawer(RectTransform overlayHost)
        {
            const float visibleWidth = 190f;
            const float seamOverlap = 10f;
            const float expandedWidth = visibleWidth + seamOverlap;
            const float drawerHeight = 423f;

            var tray = CreateRegion("MetricsTray", overlayHost, new Vector2(0f, 0f),
                new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
            SetRect(tray, Vector2.zero, Vector2.zero, new Vector2(1f, 0f),
                new Vector2(expandedWidth, drawerHeight), new Vector2(seamOverlap, 0f));
            tray.gameObject.AddComponent<RectMask2D>();
            tray.gameObject.AddComponent<CanvasGroup>();

            var panel = CreatePanel("Panel", tray, Card, _bottomLeftPanelSprite);
            Stretch(panel.GetComponent<RectTransform>());
            panel.GetComponent<Image>().raycastTarget = true;

            var accent = CreatePanel("Accent", panel.transform, Accent, null);
            SetRect(accent.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one,
                new Vector2(0.5f, 1f), new Vector2(0f, 3f), Vector2.zero);

            var scrollRoot = CreatePanel("Scroll", panel.transform, new Color32(9, 19, 27, 255), null);
            SetStretchOffsets(scrollRoot.GetComponent<RectTransform>(), 0f, 0f, 3f, 58f);
            var scroll = scrollRoot.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 18f;

            var viewport = CreatePanel("Viewport", scrollRoot.transform, Color.clear, null);
            SetStretchOffsets(viewport.GetComponent<RectTransform>(), 8f, 14f, 8f, 8f);
            viewport.GetComponent<Image>().raycastTarget = true;
            viewport.AddComponent<RectMask2D>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            CreateAutoHidingVerticalScrollbar(scrollRoot.transform, scroll);

            var content = CreateRegion("Content", viewport.transform, new Vector2(0f, 1f),
                new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            SetRect(content, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 1f), Vector2.zero);
            scroll.content = content;

            var rowTemplate = CreatePanel("RowTemplate", content, new Color32(23, 42, 56, 238),
                _controlSprite);
            SetRect(rowTemplate.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one,
                new Vector2(0.5f, 1f), new Vector2(-2f, 68f), new Vector2(0f, -2f));
            rowTemplate.GetComponent<Image>().raycastTarget = false;
            var rowAccent = CreatePanel("Accent", rowTemplate.transform, PrimaryText, null);
            SetRect(rowAccent.GetComponent<RectTransform>(), Vector2.zero, new Vector2(0f, 1f),
                new Vector2(0f, 0.5f), new Vector2(3f, -10f), new Vector2(2f, 0f));
            rowAccent.GetComponent<Image>().raycastTarget = false;

            // ProductDefinition.Icon is the same unpackaged sprite used by the phone's product
            // selectors. The frame is authored here while runtime supplies that game-owned sprite.
            var productImageFrame = CreatePanel("ProductImageFrame", rowTemplate.transform,
                Control, _controlSprite);
            SetRect(productImageFrame.GetComponent<RectTransform>(), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(48f, 48f),
                new Vector2(8f, 0f));
            productImageFrame.GetComponent<Image>().raycastTarget = false;
            var productImage = new GameObject("ProductImage", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            productImage.transform.SetParent(productImageFrame.transform, false);
            Stretch(productImage.GetComponent<RectTransform>(), 3f);
            var productImageGraphic = productImage.GetComponent<Image>();
            productImageGraphic.preserveAspect = true;
            productImageGraphic.raycastTarget = false;
            productImageGraphic.enabled = false;

            var rowName = CreateText("Name", rowTemplate.transform, "PRODUCT NAME", 10, FontStyle.Bold,
                TextAnchor.MiddleLeft, PrimaryText);
            SetRect(rowName.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one,
                new Vector2(0.5f, 1f), new Vector2(-69f, 18f), new Vector2(27.5f, -2f));
            rowName.horizontalOverflow = HorizontalWrapMode.Overflow;
            rowName.verticalOverflow = VerticalWrapMode.Truncate;
            var rowDetails = CreateText("Details", rowTemplate.transform,
                "QTY 0  •  0 UNPACKAGED\nEA $0  •  TOTAL $0", 8,
                FontStyle.Bold, TextAnchor.UpperLeft, SecondaryText);
            SetStretchOffsets(rowDetails.GetComponent<RectTransform>(), 62f, 7f, 21f, 4f);
            rowDetails.horizontalOverflow = HorizontalWrapMode.Wrap;
            rowDetails.verticalOverflow = VerticalWrapMode.Truncate;
            rowTemplate.SetActive(false);

            var empty = CreateText("EmptyLabel", viewport.transform, "NO PRODUCTS IN BACKPACK", 8,
                FontStyle.Bold, TextAnchor.MiddleCenter, SecondaryText);
            Stretch(empty.GetComponent<RectTransform>(), 6f);
            empty.gameObject.SetActive(false);

            var summary = CreateText("Summary", panel.transform, "0 TYPES  •  QTY 0  •  VALUE $0", 8,
                FontStyle.Bold, TextAnchor.MiddleLeft, SecondaryText);
            SetRect(summary.GetComponent<RectTransform>(), Vector2.zero, new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), new Vector2(-24f, 38f), new Vector2(0f, 9f));

            var toggle = CreatePanel("MetricsToggle", overlayHost, ControlAlt, _leftControlSprite);
            SetRect(toggle.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero,
                new Vector2(1f, 0.5f), new Vector2(28f, 48f),
                new Vector2(seamOverlap, drawerHeight * 0.5f));
            var toggleButton = toggle.AddComponent<Button>();
            toggleButton.targetGraphic = toggle.GetComponent<Image>();
            toggleButton.transition = Selectable.Transition.None;
            CreateIcon("OpenIcon", toggle.transform, _collapseIconSprite, new Vector2(17f, 17f));
            CreateIcon("CloseIcon", toggle.transform, _expandIconSprite, new Vector2(17f, 17f));
            toggle.transform.Find("CloseIcon").gameObject.SetActive(false);

            // Runtime begins collapsed and expands the mask from this serialized width contract.
            tray.gameObject.SetActive(false);
        }

        private static Scrollbar CreateAutoHidingVerticalScrollbar(Transform parent, ScrollRect scroll)
        {
            var track = CreatePanel("Scrollbar", parent, new Color32(166, 205, 229, 45), null);
            SetRect(track.GetComponent<RectTransform>(), new Vector2(1f, 0f), Vector2.one,
                new Vector2(1f, 0.5f), new Vector2(4f, -16f), new Vector2(-3f, 0f));
            var trackImage = track.GetComponent<Image>();
            trackImage.raycastTarget = true;

            var slidingArea = CreateRegion("SlidingArea", track.transform, Vector2.zero, Vector2.one,
                new Vector2(0f, 2f), new Vector2(0f, -2f));
            var handle = CreatePanel("Handle", slidingArea, Accent, _roundedSprite);
            Stretch(handle.GetComponent<RectTransform>());
            var handleImage = handle.GetComponent<Image>();
            handleImage.raycastTarget = true;

            var scrollbar = track.AddComponent<Scrollbar>();
            scrollbar.targetGraphic = handleImage;
            scrollbar.handleRect = handle.GetComponent<RectTransform>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.value = 1f;
            scrollbar.size = 0.25f;
            scrollbar.transition = Selectable.Transition.ColorTint;

            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.verticalScrollbarSpacing = 2f;
            return scrollbar;
        }

        private static void CreateSearch(string name, Transform parent, string placeholderText)
        {
            var root = CreatePanel(name, parent, Search, _controlSprite);
            var input = root.AddComponent<InputField>();
            input.lineType = InputField.LineType.SingleLine;
            input.transition = Selectable.Transition.ColorTint;

            var text = CreateText("InputText", root.transform, string.Empty, 12, FontStyle.Italic,
                TextAnchor.MiddleLeft, PrimaryText);
            SetStretchOffsets(text.GetComponent<RectTransform>(), 12f, 12f, 2f, 2f);
            input.textComponent = text;

            var placeholder = CreateText("Placeholder", root.transform, placeholderText, 12, FontStyle.Italic,
                TextAnchor.MiddleLeft, SecondaryText);
            SetStretchOffsets(placeholder.GetComponent<RectTransform>(), 12f, 12f, 2f, 2f);
            input.placeholder = placeholder;
        }

        private static GameObject AddFlexibleButton(RectTransform parent, string name, string label, Color color,
            float flexibleWidth, float preferredWidth = -1f, int fontSize = 9)
        {
            var button = CreateButton(name, parent, label, fontSize, color);
            AddLayout(button, preferredWidth, 30f, flexibleWidth);
            return button;
        }

        private static GameObject CreateButton(string name, Transform parent, string label, int fontSize, Color color)
        {
            var root = CreatePanel(name, parent, color, _controlSprite);
            var image = root.GetComponent<Image>();
            var button = root.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f),
                pressedColor = new Color(0.82f, 0.88f, 0.92f, 1f),
                selectedColor = Color.white,
                disabledColor = new Color(0.45f, 0.48f, 0.50f, 0.65f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };
            var text = CreateText("Label", root.transform, label, fontSize, FontStyle.Bold,
                TextAnchor.MiddleCenter, PrimaryText);
            Stretch(text.GetComponent<RectTransform>(), 3f);
            return root;
        }

        private static void CreateSettingsIcon(Transform parent)
        {
            if (_settingsIconSprite != null)
            {
                var icon = new GameObject("SettingsIcon", typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(Image));
                icon.transform.SetParent(parent, false);
                SetRect(icon.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(21f, 21f), Vector2.zero);
                var image = icon.GetComponent<Image>();
                image.sprite = _settingsIconSprite;
                image.preserveAspect = true;
                image.raycastTarget = false;
                return;
            }

            var label = CreateText("SettingsIcon", parent, "⚙", 16, FontStyle.Bold,
                TextAnchor.MiddleCenter, PrimaryText);
            Stretch(label.GetComponent<RectTransform>());
        }

        private static void CreateCollapseRails(Transform root)
        {
            var expandedRail = CreateRegion("CollapseRail", root, Vector2.zero, Vector2.zero,
                Vector2.zero, Vector2.zero);
            SetRect(expandedRail, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(30f, 64f), new Vector2(8f, 0f));
            var hide = CreateButton("HideButton", expandedRail, string.Empty, 10, Control);
            Stretch(hide.GetComponent<RectTransform>());
            CreateIcon("CollapseIcon", hide.transform, _collapseIconSprite, new Vector2(18f, 18f));
            var hideTooltip = CreateTooltip(expandedRail, "Hide backpack");
            ConfigureTooltipEvents(hide, hideTooltip);
            hideTooltip.SetActive(false);

            var collapsedRail = CreateRegion("CollapsedHandle", root, Vector2.zero, Vector2.zero,
                Vector2.zero, Vector2.zero);
            SetRect(collapsedRail, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(30f, 64f), new Vector2(8f, 0f));
            var show = CreateButton("ShowButton", collapsedRail, string.Empty, 10, Control);
            Stretch(show.GetComponent<RectTransform>());
            CreateIcon("ExpandIcon", show.transform, _expandIconSprite, new Vector2(18f, 18f));
            var showTooltip = CreateTooltip(collapsedRail, "Show backpack");
            ConfigureTooltipEvents(show, showTooltip);
            showTooltip.SetActive(false);
            collapsedRail.gameObject.SetActive(false);
        }

        private static void CreateIcon(string name, Transform parent, Sprite sprite, Vector2 size)
        {
            if (sprite == null)
                throw new InvalidOperationException("PackRat UI icon sprite is missing for " + name + ".");

            var icon = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            icon.transform.SetParent(parent, false);
            SetRect(icon.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), size, Vector2.zero);
            var image = icon.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        private static GameObject CreateTooltip(Transform rail, string copy)
        {
            var tooltip = CreatePanel("Tooltip", rail, new Color32(7, 14, 20, 252), _controlSprite);
            SetRect(tooltip.GetComponent<RectTransform>(), Vector2.one, Vector2.one,
                new Vector2(0f, 0f), new Vector2(104f, 28f), new Vector2(8f, 6f));
            tooltip.GetComponent<Image>().raycastTarget = false;
            var outline = tooltip.AddComponent<Outline>();
            outline.effectColor = new Color32(76, 173, 229, 150);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
            var label = CreateText("Label", tooltip.transform, copy, 10, FontStyle.Bold,
                TextAnchor.MiddleCenter, PrimaryText);
            Stretch(label.GetComponent<RectTransform>(), 6f);
            return tooltip;
        }

        private static void ConfigureTooltipEvents(GameObject button, GameObject tooltip)
        {
            var trigger = button.AddComponent<EventTrigger>();
            trigger.triggers = new List<EventTrigger.Entry>();
            AddTooltipEvent(trigger, EventTriggerType.PointerEnter, tooltip, true);
            AddTooltipEvent(trigger, EventTriggerType.PointerExit, tooltip, false);
            AddTooltipEvent(trigger, EventTriggerType.Select, tooltip, true);
            AddTooltipEvent(trigger, EventTriggerType.Deselect, tooltip, false);
        }

        private static void AddTooltipEvent(EventTrigger trigger, EventTriggerType eventType,
            GameObject tooltip, bool visible)
        {
            var entry = new EventTrigger.Entry { eventID = eventType };
            UnityEventTools.AddBoolPersistentListener(entry.callback, tooltip.SetActive, visible);
            entry.callback.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
            trigger.triggers.Add(entry);
        }

        private static GameObject CreatePanel(string name, Transform parent, Color color, Sprite sprite)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (parent != null)
                root.transform.SetParent(parent, false);
            var image = root.GetComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            image.type = sprite == null ? Image.Type.Simple : Image.Type.Sliced;
            return root;
        }

        private static Text CreateText(string name, Transform parent, string value, int size, FontStyle style,
            TextAnchor alignment, Color color)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            root.transform.SetParent(parent, false);
            var text = root.GetComponent<Text>();
            text.font = _font;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform CreateRegion(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        private static void ConfigureHorizontal(GameObject root, float spacing, RectOffset padding, bool expandHeight)
        {
            var layout = root.AddComponent<HorizontalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = expandHeight;
        }

        private static void ConfigureVertical(GameObject root, float spacing, RectOffset padding)
        {
            var layout = root.AddComponent<VerticalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        private static LayoutElement AddLayout(GameObject root, float preferredWidth, float preferredHeight,
            float flexibleWidth)
        {
            var element = root.AddComponent<LayoutElement>();
            if (preferredWidth >= 0f)
                element.preferredWidth = preferredWidth;
            if (preferredHeight >= 0f)
                element.preferredHeight = preferredHeight;
            element.flexibleWidth = flexibleWidth;
            return element;
        }

        private static void ConfigureFixedCard(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static void SetStretchOffsets(RectTransform rect, float left, float right, float top, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            SetStretchOffsets(rect, inset, inset, inset, inset);
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 size, Vector2 anchoredPosition)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }

        private static void EnsureProjectAssets()
        {
            Directory.CreateDirectory(PrefabDirectory);
            Directory.CreateDirectory(GeneratedDirectory);
            CreateRoundedPng(RoundedSpritePath, 64, 12f);
            CreateCornerRoundedPng(RoundedTopSpritePath, 64, 12f, 12f, 0f, 0f);
            CreateCornerRoundedPng(RoundedBottomSpritePath, 64, 0f, 0f, 12f, 12f);
            CreateRoundedPng(ControlSpritePath, 64, 8f);
            CreateCornerRoundedPng(TopControlSpritePath, 64, 8f, 8f, 0f, 0f);
            CreateCornerRoundedPng(LeftPanelSpritePath, 64, 12f, 0f, 0f, 12f);
            CreateCornerRoundedPng(TopLeftPanelSpritePath, 64, 12f, 0f, 0f, 0f);
            CreateCornerRoundedPng(BottomLeftPanelSpritePath, 64, 0f, 0f, 0f, 12f);
            CreateCornerRoundedPng(LeftControlSpritePath, 64, 8f, 0f, 0f, 8f);
            CreateRoundedPng(PillSpritePath, 64, 30f);
            BakeSettingsIcon();
            BakeCollapseIcons();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureSprite(RoundedSpritePath, new Vector4(14f, 14f, 14f, 14f));
            ConfigureSprite(RoundedTopSpritePath, new Vector4(14f, 14f, 14f, 14f));
            ConfigureSprite(RoundedBottomSpritePath, new Vector4(14f, 14f, 14f, 14f));
            ConfigureSprite(ControlSpritePath, new Vector4(10f, 10f, 10f, 10f));
            ConfigureSprite(TopControlSpritePath, new Vector4(10f, 10f, 10f, 10f));
            ConfigureSprite(LeftPanelSpritePath, new Vector4(14f, 14f, 14f, 14f));
            ConfigureSprite(TopLeftPanelSpritePath, new Vector4(14f, 14f, 14f, 14f));
            ConfigureSprite(BottomLeftPanelSpritePath, new Vector4(14f, 14f, 14f, 14f));
            ConfigureSprite(LeftControlSpritePath, new Vector4(10f, 10f, 10f, 10f));
            ConfigureSprite(PillSpritePath, new Vector4(30f, 30f, 30f, 30f));
            ConfigureIconSprite(SettingsIconSpritePath, 256);
            ConfigureIconSprite(CollapseIconSpritePath, 128);
            ConfigureIconSprite(ExpandIconSpritePath, 128);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null)
                _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _roundedSprite = AssetDatabase.LoadAssetAtPath<Sprite>(RoundedSpritePath);
            _roundedTopSprite = AssetDatabase.LoadAssetAtPath<Sprite>(RoundedTopSpritePath);
            _roundedBottomSprite = AssetDatabase.LoadAssetAtPath<Sprite>(RoundedBottomSpritePath);
            _controlSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ControlSpritePath);
            _topControlSprite = AssetDatabase.LoadAssetAtPath<Sprite>(TopControlSpritePath);
            _leftPanelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(LeftPanelSpritePath);
            _topLeftPanelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(TopLeftPanelSpritePath);
            _bottomLeftPanelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BottomLeftPanelSpritePath);
            _leftControlSprite = AssetDatabase.LoadAssetAtPath<Sprite>(LeftControlSpritePath);
            _settingsIconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SettingsIconSpritePath);
            _collapseIconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CollapseIconSpritePath);
            _expandIconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ExpandIconSpritePath);
            if (_font == null || _roundedSprite == null || _roundedTopSprite == null ||
                _roundedBottomSprite == null || _controlSprite == null || _topControlSprite == null ||
                _leftPanelSprite == null || _topLeftPanelSprite == null || _bottomLeftPanelSprite == null ||
                _leftControlSprite == null ||
                _settingsIconSprite == null || _collapseIconSprite == null || _expandIconSprite == null)
                throw new InvalidOperationException("PackRat UI generated font or sliced sprites could not be loaded.");
        }

        private static void CreateRoundedPng(string assetPath, int size, float radius)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            var half = size * 0.5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = Mathf.Max(Mathf.Abs(x - center.x) - (half - radius), 0f);
                var dy = Mathf.Max(Mathf.Abs(y - center.y) - (half - radius), 0f);
                var alpha = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            texture.Apply();
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private static void CreateCornerRoundedPng(string assetPath, int size, float topLeft,
            float topRight, float bottomRight, float bottomLeft)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var alpha = 1f;
                alpha = Mathf.Min(alpha, GetCornerAlpha(x, y, size, bottomLeft, false, false));
                alpha = Mathf.Min(alpha, GetCornerAlpha(x, y, size, bottomRight, true, false));
                alpha = Mathf.Min(alpha, GetCornerAlpha(x, y, size, topLeft, false, true));
                alpha = Mathf.Min(alpha, GetCornerAlpha(x, y, size, topRight, true, true));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }

            texture.Apply();
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private static float GetCornerAlpha(int x, int y, int size, float radius, bool right, bool top)
        {
            if (radius <= 0f)
                return 1f;

            var inHorizontalCorner = right ? x >= size - radius : x < radius;
            var inVerticalCorner = top ? y >= size - radius : y < radius;
            if (!inHorizontalCorner || !inVerticalCorner)
                return 1f;

            var centerX = right ? size - radius - 0.5f : radius - 0.5f;
            var centerY = top ? size - radius - 0.5f : radius - 0.5f;
            var distance = Vector2.Distance(new Vector2(x, y), new Vector2(centerX, centerY));
            return Mathf.Clamp01(radius + 0.5f - distance);
        }

        private static void BakeSettingsIcon()
        {
            var source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "assets",
                "settings-sliders-ui.svg"));
            if (!File.Exists(source))
                throw new FileNotFoundException("PackRat settings SVG source is missing.", source);

            BakeSvgIcon(source, SettingsIconSpritePath, 256);
        }

        private static void BakeCollapseIcons()
        {
            var sourceDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "assets",
                "ui-icons", "feather"));
            var collapseSource = Path.Combine(sourceDirectory, "chevrons-left.svg");
            var expandSource = Path.Combine(sourceDirectory, "chevrons-right.svg");
            if (!File.Exists(collapseSource) || !File.Exists(expandSource))
                throw new FileNotFoundException("PackRat Feather collapse icon sources are missing.", sourceDirectory);

            BakeSvgIcon(collapseSource, CollapseIconSpritePath, 128);
            BakeSvgIcon(expandSource, ExpandIconSpritePath, 128);
        }

        private static void BakeSvgIcon(string source, string outputPath, int size)
        {
            var document = XDocument.Load(source);
            var root = document.Root;
            if (root == null)
                throw new InvalidOperationException("PackRat SVG has no root element: " + source);

            var viewBox = ParseNumbers(GetRequiredAttribute(root, "viewBox"));
            if (viewBox.Length != 4 || viewBox[2] <= 0f || viewBox[3] <= 0f)
                throw new InvalidOperationException("PackRat SVG requires a positive four-value viewBox: " + source);

            var inheritedStrokeWidth = GetFloat(root, "stroke-width", 0f);
            var inheritedStroke = GetColor(root, "stroke", Accent);

            var lines = root.Descendants().Where(element => element.Name.LocalName == "line")
                .Select(element => new SvgLine(
                    new Vector2(GetFloat(element, "x1"), GetFloat(element, "y1")),
                    new Vector2(GetFloat(element, "x2"), GetFloat(element, "y2")),
                    GetFloat(element, "stroke-width", inheritedStrokeWidth),
                    GetColor(element, "stroke", inheritedStroke)))
                .Concat(root.Descendants().Where(element => element.Name.LocalName == "polyline")
                    .SelectMany(element => ParsePolyline(element, inheritedStrokeWidth, inheritedStroke)))
                .ToArray();
            var circles = root.Descendants().Where(element => element.Name.LocalName == "circle")
                .Select(element => new SvgCircle(
                    new Vector2(GetFloat(element, "cx"), GetFloat(element, "cy")),
                    GetFloat(element, "r"), GetFloat(element, "stroke-width", inheritedStrokeWidth),
                    GetColor(element, "fill", Color.clear), GetColor(element, "stroke", inheritedStroke)))
                .ToArray();
            if (lines.Length == 0 && circles.Length == 0)
                throw new InvalidOperationException("PackRat SVG has no supported line, polyline, or circle geometry: " +
                    source);

            const int samplesPerAxis = 4;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var accumulated = Color.clear;
                for (var sampleY = 0; sampleY < samplesPerAxis; sampleY++)
                for (var sampleX = 0; sampleX < samplesPerAxis; sampleX++)
                {
                    var normalizedX = (x + (sampleX + 0.5f) / samplesPerAxis) / size;
                    var normalizedY = (y + (sampleY + 0.5f) / samplesPerAxis) / size;
                    var point = new Vector2(viewBox[0] + normalizedX * viewBox[2],
                        viewBox[1] + (1f - normalizedY) * viewBox[3]);
                    var sample = SampleSvg(point, lines, circles);
                    accumulated.r += sample.r * sample.a;
                    accumulated.g += sample.g * sample.a;
                    accumulated.b += sample.b * sample.a;
                    accumulated.a += sample.a;
                }

                var sampleCount = samplesPerAxis * samplesPerAxis;
                if (accumulated.a > 0f)
                {
                    accumulated.r /= accumulated.a;
                    accumulated.g /= accumulated.a;
                    accumulated.b /= accumulated.a;
                }
                accumulated.a /= sampleCount;
                texture.SetPixel(x, y, accumulated);
            }

            texture.Apply();
            File.WriteAllBytes(outputPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private static IEnumerable<SvgLine> ParsePolyline(XElement element, float inheritedStrokeWidth,
            Color inheritedStroke)
        {
            var points = ParseNumbers(GetRequiredAttribute(element, "points"));
            if (points.Length < 4 || points.Length % 2 != 0)
                throw new InvalidOperationException("SVG polyline requires at least two coordinate pairs.");

            var strokeWidth = GetFloat(element, "stroke-width", inheritedStrokeWidth);
            var stroke = GetColor(element, "stroke", inheritedStroke);
            for (var index = 0; index <= points.Length - 4; index += 2)
            {
                yield return new SvgLine(new Vector2(points[index], points[index + 1]),
                    new Vector2(points[index + 2], points[index + 3]), strokeWidth, stroke);
            }
        }

        private static Color SampleSvg(Vector2 point, IEnumerable<SvgLine> lines,
            IEnumerable<SvgCircle> circles)
        {
            var sample = Color.clear;
            foreach (var line in lines)
            {
                if (DistanceToSegment(point, line.Start, line.End) <= line.StrokeWidth * 0.5f)
                    sample = line.Stroke;
            }

            foreach (var circle in circles)
            {
                var distance = Vector2.Distance(point, circle.Center);
                if (distance <= circle.Radius + circle.StrokeWidth * 0.5f)
                    sample = distance < circle.Radius - circle.StrokeWidth * 0.5f
                        ? circle.Fill
                        : circle.Stroke;
            }

            return sample;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            var segment = end - start;
            var lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon)
                return Vector2.Distance(point, start);
            var amount = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + segment * amount);
        }

        private static float[] ParseNumbers(string value)
        {
            return value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => float.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        }

        private static float GetFloat(XElement element, string name)
        {
            return float.Parse(GetRequiredAttribute(element, name), CultureInfo.InvariantCulture);
        }

        private static float GetFloat(XElement element, string name, float fallback)
        {
            var attribute = element.Attribute(name);
            return attribute == null || string.IsNullOrWhiteSpace(attribute.Value)
                ? fallback
                : float.Parse(attribute.Value, CultureInfo.InvariantCulture);
        }

        private static Color GetColor(XElement element, string name, Color fallback)
        {
            var attribute = element.Attribute(name);
            if (attribute == null || string.IsNullOrWhiteSpace(attribute.Value))
                return fallback;
            var html = attribute.Value;
            if (string.Equals(html, "currentColor", StringComparison.OrdinalIgnoreCase))
                return Accent;
            if (!ColorUtility.TryParseHtmlString(html, out var color))
                throw new InvalidOperationException("Invalid SVG color '" + html + "'.");
            return color;
        }

        private static string GetRequiredAttribute(XElement element, string name)
        {
            var attribute = element.Attribute(name);
            if (attribute == null || string.IsNullOrWhiteSpace(attribute.Value))
                throw new InvalidOperationException("SVG " + element.Name.LocalName +
                    " is missing required '" + name + "'.");
            return attribute.Value;
        }

        private readonly struct SvgLine
        {
            public readonly Vector2 Start;
            public readonly Vector2 End;
            public readonly float StrokeWidth;
            public readonly Color Stroke;

            public SvgLine(Vector2 start, Vector2 end, float strokeWidth, Color stroke)
            {
                Start = start;
                End = end;
                StrokeWidth = strokeWidth;
                Stroke = stroke;
            }
        }

        private readonly struct SvgCircle
        {
            public readonly Vector2 Center;
            public readonly float Radius;
            public readonly float StrokeWidth;
            public readonly Color Fill;
            public readonly Color Stroke;

            public SvgCircle(Vector2 center, float radius, float strokeWidth, Color fill, Color stroke)
            {
                Center = center;
                Radius = radius;
                StrokeWidth = strokeWidth;
                Fill = fill;
                Stroke = stroke;
            }
        }

        private static void ConfigureSprite(string path, Vector4 border)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Texture importer not found for " + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.spriteBorder = border;
            importer.SaveAndReimport();
        }

        private static void ConfigureIconSprite(string path, int maximumSize)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Texture importer not found for " + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = maximumSize;
            importer.SaveAndReimport();
        }

        private static void SavePrefab(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static void ValidateBrowserPrefab(string path, bool embedded, List<string> failures)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                failures.Add(path + " is missing");
                return;
            }

            var required = new List<string>
            {
                "Header/Title", "Header/Meta", "Header/PrimaryActions/StackButton",
                "Header/PrimaryActions/SettingsButton", "Header/PrimaryActions/SettingsButton/SettingsIcon",
                "Header/FilterRow/TypeButton",
                "Header/FilterRow/QualityButton", "Header/FilterRow/OrderButton",
                "Header/FilterRow/OrganizeButton", "Header/FilterRow/ClearButton",
                "Header/Search/InputText", "Header/Search/Placeholder", "Header/SortTabs/AllButton",
                "Header/SortTabs/FavoritesButton", "Header/SortTabs/NameButton",
                "Header/SortTabs/QuantityButton", "Header/SortTabs/QualityButton",
                "Header/SortTabs/TypeButton", "Header/SortTabs/RecentButton", "SlotViewport/SlotGrid",
                "Footer/PreviousButton", "Footer/PageLabel", "Footer/NextButton", "Footer/DoneButton",
                "OverlayHost/ActiveFilterTab", "OverlayHost/Dropdown"
            };
            if (embedded)
            {
                required.Add("CollapseRail/HideButton");
                required.Add("CollapseRail/HideButton/CollapseIcon");
                required.Add("CollapseRail/Tooltip/Label");
                required.Add("CollapsedHandle/ShowButton");
                required.Add("CollapsedHandle/ShowButton/ExpandIcon");
                required.Add("CollapsedHandle/Tooltip/Label");
                if (string.Equals(path, EmbeddedPrefabPath, StringComparison.Ordinal))
                {
                    required.Add("BulkTransferRow/BulkSelectorButton");
                    required.Add("BulkTransferRow/MoveToStorageButton");
                    required.Add("BulkTransferRow/MoveToBackpackButton");
                }
            }
            else if (string.Equals(path, StandalonePrefabPath, StringComparison.Ordinal))
            {
                required.Add("OverlayHost/MetricsTray/Panel/Accent");
                required.Add("OverlayHost/MetricsTray/Panel/Scroll/Viewport/Content/RowTemplate/Accent");
                required.Add("OverlayHost/MetricsTray/Panel/Scroll/Viewport/Content/RowTemplate/Name");
                required.Add("OverlayHost/MetricsTray/Panel/Scroll/Viewport/Content/RowTemplate/Details");
                required.Add("OverlayHost/MetricsTray/Panel/Scroll/Viewport/EmptyLabel");
                required.Add("OverlayHost/MetricsTray/Panel/Scroll/Scrollbar/SlidingArea/Handle");
                required.Add("OverlayHost/MetricsTray/Panel/Summary");
                required.Add("OverlayHost/MetricsToggle/OpenIcon");
                required.Add("OverlayHost/MetricsToggle/CloseIcon");
            }

            ValidatePaths(prefab, path, required, failures);
            var rootRect = prefab.GetComponent<RectTransform>();
            if (rootRect == null || rootRect.anchorMin != rootRect.anchorMax || rootRect.sizeDelta.x < 320f ||
                rootRect.sizeDelta.y < 420f)
                failures.Add(path + " must be a centered fixed card with usable minimum dimensions");
            ValidateStretch(prefab.transform.Find("OverlayHost") as RectTransform, path + "/OverlayHost", failures);
            ValidateStretch(prefab.transform.Find("SlotViewport/SlotGrid") as RectTransform,
                path + "/SlotViewport/SlotGrid", failures);
            var grid = prefab.transform.Find("SlotViewport/SlotGrid")?.GetComponent<GridLayoutGroup>();
            if (grid == null || grid.constraint != GridLayoutGroup.Constraint.FixedColumnCount ||
                grid.cellSize.x < 64f || grid.cellSize.y < 64f)
                failures.Add(path + " grid must own a readable fixed-column slot layout");
            else if (grid.transform.childCount != 0)
                failures.Add(path + " grid must remain an empty runtime anchor host for game-owned ItemSlotUI assets");
            if (prefab.transform.Find("SlotViewport/EditorPreviewSlots") != null)
                failures.Add(path + " must not serialize authoring-only example storage slots");
            ValidateVerticalSeparation(prefab, path, "Header/Meta", "Header/FilterRow", 4f, failures);
            ValidateVerticalSeparation(prefab, path, "Header/FilterRow", "Header/Search", 6f, failures);
            ValidateVerticalSeparation(prefab, path, "Header/Search", "Header/SortTabs", 8f, failures);
            ValidateVerticalJoin(prefab, path, "Header", "SlotViewport", failures);
            ValidateUnifiedBrowserSeam(prefab, path, failures);
            ValidateSortTabState(prefab, path, failures);
            if (string.Equals(path, StandalonePrefabPath, StringComparison.Ordinal))
                ValidateMetricsDrawer(prefab, path, failures);
            ValidateSettingsIcon(prefab, path, failures);
            ValidateCollapseRails(prefab, path, embedded, failures);
            ValidateSearchShape(prefab, path, failures);
            ValidateRoundedRectangleButtons(prefab, path, failures);
        }

        private static void ValidateHandoverPrefab(List<string> failures)
        {
            ValidateBrowserPrefab(HandoverPrefabPath, true, failures);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandoverPrefabPath);
            if (prefab != null)
                ValidatePaths(prefab, HandoverPrefabPath,
                    new[] { "ModeRow/BackpackButton", "ModeRow/VehicleButton", "TransferRow/AutoFillButton",
                        "TransferRow/StatusLabel" }, failures);
        }

        private static void ValidateSettingsPrefab(List<string> failures)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SettingsPrefabPath);
            if (prefab == null)
            {
                failures.Add(SettingsPrefabPath + " is missing");
                return;
            }

            ValidatePaths(prefab, SettingsPrefabPath, new[]
            {
                "Blocker", "Card/Header/Title", "Card/Header/CloseButton", "Card/SessionStatus/Value",
                "Card/Tabs/GeneralButton", "Card/Tabs/ThemeButton", "Card/Tabs/TiersButton",
                "Card/Tabs/LayoutButton", "Card/Tabs/RoutingButton", "Card/Tabs/MetricsButton",
                "Card/Content/Viewport", "Card/Content/Viewport/GeneralPage",
                "Card/Content/Viewport/ThemePage", "Card/Content/Viewport/TiersPage",
                "Card/Content/Viewport/LayoutPage", "Card/Content/Viewport/RoutingPage",
                "Card/Content/Viewport/MetricsPage"
            }, failures);
            ValidateStretch(prefab.GetComponent<RectTransform>(), SettingsPrefabPath, failures);
            ValidateStretch(prefab.transform.Find("Blocker") as RectTransform,
                SettingsPrefabPath + "/Blocker", failures);
            var card = prefab.transform.Find("Card") as RectTransform;
            if (card == null || card.anchorMin != card.anchorMax || card.sizeDelta.x < 520f || card.sizeDelta.y < 420f)
                failures.Add(SettingsPrefabPath + " card must remain a centered, usable modal surface");
            if (prefab.transform.Find("Blocker")?.GetComponent<Button>() == null)
                failures.Add(SettingsPrefabPath + " blocker must be an interactive full-screen modal dismiss target");
            if (prefab.GetComponent<CanvasGroup>() == null || card?.GetComponent<CanvasGroup>() == null)
                failures.Add(SettingsPrefabPath + " must serialize root and card animation groups");
            ValidateVerticalSeparation(prefab, SettingsPrefabPath, "Card/Header", "Card/SessionStatus", 6f,
                failures);
            ValidateVerticalSeparation(prefab, SettingsPrefabPath, "Card/SessionStatus", "Card/Tabs", 6f,
                failures);
            ValidateVerticalSeparation(prefab, SettingsPrefabPath, "Card/Tabs", "Card/Content", 6f, failures);
            ValidateRoundedRectangleButtons(prefab, SettingsPrefabPath, failures);
        }

        private static void ValidateDedicatedCanvas(List<string> failures)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DedicatedCanvasPrefabPath);
            if (prefab == null)
            {
                failures.Add(DedicatedCanvasPrefabPath + " is missing");
                return;
            }

            ValidatePaths(prefab, DedicatedCanvasPrefabPath,
                new[] { "SafeAreaRoot", "SafeAreaRoot/PaneHost" }, failures);
            var canvas = prefab.GetComponent<Canvas>();
            var scaler = prefab.GetComponent<CanvasScaler>();
            if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                failures.Add(DedicatedCanvasPrefabPath + " must own a screen-space overlay Canvas");
            if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize ||
                scaler.referenceResolution != new Vector2(1920f, 1080f) ||
                scaler.screenMatchMode != CanvasScaler.ScreenMatchMode.MatchWidthOrHeight ||
                !Mathf.Approximately(scaler.matchWidthOrHeight, 1f))
                failures.Add(DedicatedCanvasPrefabPath + " must use the PackRat 1920x1080 height-match policy");
            ValidateStretch(prefab.transform.Find("SafeAreaRoot") as RectTransform,
                DedicatedCanvasPrefabPath + "/SafeAreaRoot", failures);
            ValidateStretch(prefab.transform.Find("SafeAreaRoot/PaneHost") as RectTransform,
                DedicatedCanvasPrefabPath + "/SafeAreaRoot/PaneHost", failures);
        }

        private static void ValidateResolutionMatrix(ICollection<string> failures)
        {
            var resolutions = new[]
            {
                new Vector2(1280f, 720f), new Vector2(1920f, 1080f), new Vector2(2560f, 1440f),
                new Vector2(3840f, 2160f), new Vector2(1920f, 1200f), new Vector2(1280f, 960f),
                new Vector2(3440f, 1440f), new Vector2(5120f, 1440f)
            };
            var cards = new[]
            {
                new Vector2(448f, 604f) * 1.5f,
                new Vector2(448f + 190f + 28f, 604f) * 1.5f,
                new Vector2(420f, 606f) * 1.5f,
                new Vector2(420f, 660f) * 1.5f,
                new Vector2(620f, 480f)
            };
            const float edgeInsets = 24f;
            foreach (var resolution in resolutions)
            {
                var scale = resolution.y / 1080f;
                var logical = resolution / scale;
                foreach (var card in cards)
                {
                    if (card.x > logical.x - edgeInsets * 2f || card.y > logical.y - edgeInsets * 2f)
                    {
                        failures.Add("Resolution matrix overflow at " + resolution.x + "x" + resolution.y +
                            " for card " + card.x + "x" + card.y + " in logical space " +
                            logical.x.ToString("0") + "x" + logical.y.ToString("0"));
                    }
                }
            }
        }

        private static void ValidatePaths(GameObject prefab, string path, IEnumerable<string> required,
            ICollection<string> failures)
        {
            foreach (var childPath in required)
            {
                if (prefab.transform.Find(childPath) == null)
                    failures.Add(path + " is missing binding " + childPath);
            }
        }

        private static void ValidateStretch(RectTransform rect, string path, ICollection<string> failures)
        {
            if (rect == null || rect.anchorMin != Vector2.zero || rect.anchorMax != Vector2.one)
                failures.Add(path + " must stretch to its owner");
        }

        private static void ValidateVerticalSeparation(GameObject prefab, string assetPath, string upperPath,
            string lowerPath, float minimumGap, ICollection<string> failures)
        {
            var root = prefab.GetComponent<RectTransform>();
            var upper = prefab.transform.Find(upperPath) as RectTransform;
            var lower = prefab.transform.Find(lowerPath) as RectTransform;
            if (root == null || upper == null || lower == null)
                return;

            GetVerticalBounds(root, upper, out var upperMin, out _);
            GetVerticalBounds(root, lower, out _, out var lowerMax);
            if (upperMin + 0.01f < lowerMax + minimumGap)
            {
                failures.Add(assetPath + " has overlapping vertical regions " + upperPath + " and " + lowerPath +
                    " (gap " + (upperMin - lowerMax).ToString("0.0") + ", required " +
                    minimumGap.ToString("0.0") + ")");
            }
        }

        private static void ValidateVerticalJoin(GameObject prefab, string assetPath, string upperPath,
            string lowerPath, ICollection<string> failures)
        {
            var root = prefab.GetComponent<RectTransform>();
            var upper = prefab.transform.Find(upperPath) as RectTransform;
            var lower = prefab.transform.Find(lowerPath) as RectTransform;
            if (root == null || upper == null || lower == null)
                return;

            GetVerticalBounds(root, upper, out var upperMin, out _);
            GetVerticalBounds(root, lower, out _, out var lowerMax);
            var gap = upperMin - lowerMax;
            if (Mathf.Abs(gap) > 0.1f)
            {
                failures.Add(assetPath + " must join " + upperPath + " directly to " + lowerPath +
                    " (gap " + gap.ToString("0.0") + ")");
            }
        }

        private static void GetVerticalBounds(RectTransform root, RectTransform rect, out float minimum,
            out float maximum)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            minimum = float.PositiveInfinity;
            maximum = float.NegativeInfinity;
            foreach (var corner in corners)
            {
                var local = root.InverseTransformPoint(corner);
                minimum = Mathf.Min(minimum, local.y);
                maximum = Mathf.Max(maximum, local.y);
            }
        }

        private static void GetHorizontalBounds(RectTransform root, RectTransform rect, out float minimum,
            out float maximum)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            minimum = float.PositiveInfinity;
            maximum = float.NegativeInfinity;
            foreach (var corner in corners)
            {
                var local = root.InverseTransformPoint(corner);
                minimum = Mathf.Min(minimum, local.x);
                maximum = Mathf.Max(maximum, local.x);
            }
        }

        private static void ValidateUnifiedBrowserSeam(GameObject prefab, string assetPath,
            ICollection<string> failures)
        {
            var root = prefab.GetComponent<RectTransform>();
            var header = prefab.transform.Find("Header") as RectTransform;
            var viewport = prefab.transform.Find("SlotViewport") as RectTransform;
            var accent = prefab.transform.Find("Header/Accent");
            var tabs = prefab.transform.Find("Header/SortTabs");
            var overlayHost = prefab.transform.Find("OverlayHost");
            if (root == null || header == null || viewport == null || accent == null || tabs == null ||
                overlayHost == null)
                return;

            GetHorizontalBounds(root, header, out var headerMin, out var headerMax);
            GetHorizontalBounds(root, viewport, out var viewportMin, out var viewportMax);
            if (Mathf.Abs(headerMin - viewportMin) > 0.1f || Mathf.Abs(headerMax - viewportMax) > 0.1f)
                failures.Add(assetPath + " header and slot container must have identical horizontal bounds");

            var headerImage = header.GetComponent<Image>();
            var viewportImage = viewport.GetComponent<Image>();
            if (headerImage?.sprite == null || headerImage.sprite.name != "RoundedTopPanel")
                failures.Add(assetPath + "/Header must round only its outer top corners");
            if (viewportImage?.sprite == null || viewportImage.sprite.name != "RoundedBottomPanel")
                failures.Add(assetPath + "/SlotViewport must round only its outer bottom corners");

            var accentImage = accent.GetComponent<Image>();
            if (accentImage == null || !ColorsMatch(accentImage.color, Accent))
                failures.Add(assetPath + "/Header/Accent must use the canonical cyan divider color");
            if (accent.GetSiblingIndex() <= tabs.GetSiblingIndex())
                failures.Add(assetPath + " divider must render above the background tab row");
            if (header.GetSiblingIndex() >= viewport.GetSiblingIndex() ||
                viewport.GetSiblingIndex() >= overlayHost.GetSiblingIndex())
                failures.Add(assetPath + " must layer header tabs behind the slot panel and active overlay above it");
        }

        private static void ValidateMetricsDrawer(GameObject prefab, string assetPath,
            ICollection<string> failures)
        {
            var root = prefab.GetComponent<RectTransform>();
            var tray = prefab.transform.Find("OverlayHost/MetricsTray") as RectTransform;
            var panel = prefab.transform.Find("OverlayHost/MetricsTray/Panel") as RectTransform;
            var drawerAccent = prefab.transform.Find("OverlayHost/MetricsTray/Panel/Accent") as RectTransform;
            var drawerScroll = prefab.transform.Find("OverlayHost/MetricsTray/Panel/Scroll") as RectTransform;
            var metricsScroll = drawerScroll?.GetComponent<ScrollRect>();
            var rowTemplate = prefab.transform.Find(
                "OverlayHost/MetricsTray/Panel/Scroll/Viewport/Content/RowTemplate");
            var mainAccent = prefab.transform.Find("Header/Accent") as RectTransform;
            var mainViewport = prefab.transform.Find("SlotViewport") as RectTransform;
            var toggle = prefab.transform.Find("OverlayHost/MetricsToggle") as RectTransform;
            if (root == null || tray == null || panel == null || drawerAccent == null || drawerScroll == null ||
                rowTemplate == null || mainAccent == null || mainViewport == null || toggle == null)
                return;

            if (tray.anchorMin != Vector2.zero || tray.anchorMax != Vector2.zero ||
                tray.pivot != new Vector2(1f, 0f) || tray.sizeDelta != new Vector2(200f, 423f) ||
                tray.anchoredPosition != new Vector2(10f, 0f))
                failures.Add(assetPath +
                    " metrics drawer must span from the backpack bottom to the divider with a 10-pixel seam overlap");
            if (tray.gameObject.activeSelf)
                failures.Add(assetPath + " metrics drawer must serialize collapsed while retaining its width contract");
            if (tray.GetComponent<RectMask2D>() == null || tray.GetComponent<CanvasGroup>() == null)
                failures.Add(assetPath + " metrics drawer must own clipping and presentation alpha");

            var panelImage = panel.GetComponent<Image>();
            var scrollImage = drawerScroll.GetComponent<Image>();
            if (panelImage?.sprite == null || panelImage.sprite.name != "RoundedBottomLeftPanel")
                failures.Add(assetPath + " metrics drawer must keep a square divider edge and round only its lower-left corner");
            if (scrollImage == null || !ColorsMatch(scrollImage.color, new Color32(9, 19, 27, 255)))
                failures.Add(assetPath + " metrics content surface must use the canonical drawer color");
            ValidateAutoHidingScrollbar(metricsScroll, assetPath + "/MetricsTray/Scroll", failures);

            GetHorizontalBounds(root, drawerScroll, out _, out var drawerContentRight);
            GetHorizontalBounds(root, mainViewport, out var mainViewportLeft, out _);
            if (Mathf.Abs(drawerContentRight - mainViewportLeft) > 0.1f)
                failures.Add(assetPath + " metrics drawer must terminate at the backpack content edge");

            GetVerticalBounds(root, drawerAccent, out var drawerAccentMin, out var drawerAccentMax);
            GetVerticalBounds(root, mainAccent, out var mainAccentMin, out var mainAccentMax);
            if (Mathf.Abs(drawerAccentMin - mainAccentMin) > 0.1f ||
                Mathf.Abs(drawerAccentMax - mainAccentMax) > 0.1f)
                failures.Add(assetPath + " metrics divider must continue the backpack divider without a step");
            GetHorizontalBounds(root, drawerAccent, out _, out var drawerAccentRight);
            GetHorizontalBounds(root, mainAccent, out var mainAccentLeft, out _);
            var dividerOverlap = drawerAccentRight - mainAccentLeft;
            if (dividerOverlap < 1f || dividerOverlap > 4f)
                failures.Add(assetPath + " metrics divider must overlap the backpack divider by 1-4 pixels");

            var toggleButton = toggle.GetComponent<Button>();
            var toggleImage = toggle.GetComponent<Image>();
            var openIcon = toggle.Find("OpenIcon")?.GetComponent<Image>();
            var closeIcon = toggle.Find("CloseIcon")?.GetComponent<Image>();
            if (toggle.anchorMin != Vector2.zero || toggle.anchorMax != Vector2.zero ||
                toggle.pivot != new Vector2(1f, 0.5f) || toggle.sizeDelta != new Vector2(28f, 48f) ||
                toggle.anchoredPosition != new Vector2(10f, 211.5f))
                failures.Add(assetPath + " metrics toggle must remain attached to the drawer's exposed edge");
            if (toggleButton == null || toggleButton.transition != Selectable.Transition.None ||
                toggleImage?.sprite == null || toggleImage.sprite.name != "RoundedLeftControl")
                failures.Add(assetPath + " metrics toggle must use the left-rounded attached-rail control");
            if (openIcon?.sprite == null || openIcon.sprite.name != "ChevronsLeft" || !openIcon.preserveAspect ||
                openIcon.raycastTarget || closeIcon?.sprite == null || closeIcon.sprite.name != "ChevronsRight" ||
                !closeIcon.preserveAspect || closeIcon.raycastTarget || !openIcon.gameObject.activeSelf ||
                closeIcon.gameObject.activeSelf)
                failures.Add(assetPath + " metrics toggle must serialize the scalable open/close chevron states");
            if (rowTemplate.gameObject.activeSelf ||
                rowTemplate.Find("Accent")?.GetComponent<Image>() == null ||
                rowTemplate.Find("ProductImageFrame")?.GetComponent<Image>() == null ||
                rowTemplate.Find("ProductImageFrame/ProductImage")?.GetComponent<Image>() == null ||
                rowTemplate.Find("Name")?.GetComponent<Text>() == null ||
                rowTemplate.Find("Details")?.GetComponent<Text>() == null)
                failures.Add(assetPath +
                    " metrics row template must remain inactive with accent, unpackaged-product image, name, and detail bindings");

            var productImage = rowTemplate.Find("ProductImageFrame/ProductImage")?.GetComponent<Image>();
            if (productImage != null && (!productImage.preserveAspect || productImage.raycastTarget ||
                productImage.enabled))
                failures.Add(assetPath +
                    " metrics product image must serialize hidden, aspect-preserving, and non-interactive until runtime binds it");
        }

        private static void ValidateRoundedRectangleButtons(GameObject prefab, string assetPath,
            ICollection<string> failures)
        {
            foreach (var button in prefab.GetComponentsInChildren<Button>(true))
            {
                var image = button.targetGraphic as Image;
                if (button.name == "Blocker" && button.transition == Selectable.Transition.None)
                    continue;
                if (button.name == "MetricsToggle" && image?.sprite != null &&
                    image.sprite.name == "RoundedLeftControl")
                    continue;
                if (image == null || image.sprite == null || image.sprite.name != "RoundedControl")
                {
                    failures.Add(assetPath + "/" + button.name +
                        " must use the rounded-rectangle control sprite, not the pill sprite");
                }
            }
        }

        private static void ValidateAutoHidingScrollbar(ScrollRect scroll, string assetPath,
            ICollection<string> failures)
        {
            var scrollbar = scroll?.verticalScrollbar;
            if (scroll == null || scrollbar == null || scrollbar.handleRect == null ||
                scrollbar.direction != Scrollbar.Direction.BottomToTop ||
                scroll.verticalScrollbarVisibility != ScrollRect.ScrollbarVisibility.AutoHide)
            {
                failures.Add(assetPath + " must own a bottom-to-top scrollbar that auto-hides without overflow");
            }
        }

        private static void ValidateSearchShape(GameObject prefab, string assetPath,
            ICollection<string> failures)
        {
            var search = prefab.transform.Find("Header/Search") as RectTransform;
            var image = search?.GetComponent<Image>();
            if (image == null || image.sprite == null || image.sprite.name != "RoundedControl")
            {
                failures.Add(assetPath + "/Header/Search must use the modest rounded-rectangle control sprite");
            }
            if (search != null && !Mathf.Approximately(search.anchoredPosition.x, 0f))
                failures.Add(assetPath + "/Header/Search must keep equal left and right margins");
        }

        private static void ValidateSortTabState(GameObject prefab, string assetPath,
            ICollection<string> failures)
        {
            var tabs = prefab.transform.Find("Header/SortTabs");
            var active = prefab.transform.Find("OverlayHost/ActiveFilterTab") as RectTransform;
            var viewport = prefab.transform.Find("SlotViewport") as RectTransform;
            var root = prefab.GetComponent<RectTransform>();
            if (tabs == null || active == null || viewport == null || root == null)
                return;

            foreach (Transform tab in tabs)
            {
                if (tab.Find("ContentJoin") != null)
                    failures.Add(assetPath + "/" + tab.name + " must not append a separate content-join strip");
            }

            var image = active.GetComponent<Image>();
            if (image == null || image.sprite == null || image.sprite.name != "RoundedTopControl")
                failures.Add(assetPath +
                    " active filter tab must use the top-only rounded control with a square divider edge");
            else if (!ColorsMatch(image.color, Accent))
                failures.Add(assetPath + " active filter tab must exactly match the cyan divider color");
            if (image != null && image.raycastTarget)
                failures.Add(assetPath + " active filter overlay must let input reach the underlying tab buttons");

            GetVerticalBounds(root, active, out var activeMin, out var activeMax);
            GetVerticalBounds(root, viewport, out _, out var viewportMax);
            if (Mathf.Abs(activeMin - viewportMax) > 0.1f)
                failures.Add(assetPath +
                    " active filter tab must terminate exactly at the divider without entering the item area");
            if (activeMax <= viewportMax + 0.5f)
                failures.Add(assetPath + " active filter tab must remain visibly above the divider");
        }

        private static void ValidateSettingsIcon(GameObject prefab, string assetPath,
            ICollection<string> failures)
        {
            var image = prefab.transform.Find("Header/PrimaryActions/SettingsButton/SettingsIcon")
                ?.GetComponent<Image>();
            if (image == null || image.sprite == null || image.sprite.name != "SettingsSliders")
            {
                failures.Add(assetPath + " settings button must use the SVG-authored SettingsSliders sprite");
                return;
            }

            if (!image.preserveAspect || image.raycastTarget)
                failures.Add(assetPath + " settings icon must preserve aspect and defer input to its button");
            if (image.sprite.texture == null || image.sprite.texture.width < 256 || image.sprite.texture.height < 256)
                failures.Add(assetPath + " settings icon must retain its 256px high-density authoring bake");
            var iconPath = AssetDatabase.GetAssetPath(image.sprite);
            var importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
            if (importer == null || !importer.mipmapEnabled)
                failures.Add(assetPath + " settings icon must use mipmaps for clean resolution scaling");
        }

        private static void ValidateCollapseRails(GameObject prefab, string assetPath, bool embedded,
            ICollection<string> failures)
        {
            var expanded = prefab.transform.Find("CollapseRail") as RectTransform;
            var collapsed = prefab.transform.Find("CollapsedHandle") as RectTransform;
            if (!embedded)
            {
                if (expanded != null || collapsed != null)
                    failures.Add(assetPath + " standalone pane must not serialize embedded collapse rails");
                return;
            }

            if (prefab.transform.Find("Header/HideButton") != null)
                failures.Add(assetPath + " must not retain the misleading header back control");
            if (expanded == null || collapsed == null)
                return;

            ValidateRailGeometry(expanded, assetPath + "/CollapseRail", failures);
            ValidateRailGeometry(collapsed, assetPath + "/CollapsedHandle", failures);
            if (!expanded.gameObject.activeSelf || collapsed.gameObject.activeSelf)
                failures.Add(assetPath + " must serialize the expanded rail visible and restore rail hidden");

            var title = prefab.transform.Find("Header/Title") as RectTransform;
            var meta = prefab.transform.Find("Header/Meta") as RectTransform;
            if (title == null || meta == null || !Mathf.Approximately(title.anchoredPosition.x, 12f) ||
                !Mathf.Approximately(meta.anchoredPosition.x, 12f))
                failures.Add(assetPath + " title and metadata must return to the standard left alignment");

            ValidateRailButton(expanded, "HideButton", "CollapseIcon", "ChevronsLeft", "Hide backpack",
                assetPath + "/CollapseRail", failures);
            ValidateRailButton(collapsed, "ShowButton", "ExpandIcon", "ChevronsRight", "Show backpack",
                assetPath + "/CollapsedHandle", failures);
        }

        private static void ValidateRailGeometry(RectTransform rail, string path,
            ICollection<string> failures)
        {
            if (rail.anchorMin != new Vector2(0f, 0.5f) || rail.anchorMax != new Vector2(0f, 0.5f) ||
                rail.pivot != new Vector2(1f, 0.5f) || rail.sizeDelta != new Vector2(30f, 64f) ||
                rail.anchoredPosition != new Vector2(8f, 0f))
                failures.Add(path + " must be the selected 30x64 mid-left edge rail");
        }

        private static void ValidateRailButton(RectTransform rail, string buttonName, string iconName,
            string spriteName, string tooltipCopy, string path, ICollection<string> failures)
        {
            var button = rail.Find(buttonName)?.GetComponent<Button>();
            var icon = rail.Find(buttonName + "/" + iconName)?.GetComponent<Image>();
            var tooltip = rail.Find("Tooltip");
            var tooltipImage = tooltip?.GetComponent<Image>();
            var tooltipLabel = tooltip?.Find("Label")?.GetComponent<Text>();
            if (button == null || icon == null || tooltip == null || tooltipImage == null || tooltipLabel == null)
                return;

            if (icon.sprite == null || icon.sprite.name != spriteName || !icon.preserveAspect || icon.raycastTarget)
                failures.Add(path + " must use the licensed scalable " + spriteName + " icon");
            if (tooltip.gameObject.activeSelf || tooltipImage.raycastTarget || tooltipLabel.raycastTarget ||
                tooltipLabel.text != tooltipCopy)
                failures.Add(path + " tooltip must be hidden, non-interactive, and read '" + tooltipCopy + "'");

            var trigger = button.GetComponent<EventTrigger>();
            var expectedEvents = new HashSet<EventTriggerType>
            {
                EventTriggerType.PointerEnter, EventTriggerType.PointerExit,
                EventTriggerType.Select, EventTriggerType.Deselect
            };
            var actualEvents = trigger?.triggers == null
                ? new HashSet<EventTriggerType>()
                : new HashSet<EventTriggerType>(trigger.triggers.Select(entry => entry.eventID));
            if (!expectedEvents.SetEquals(actualEvents) || trigger.triggers.Any(entry =>
                    entry.callback == null || entry.callback.GetPersistentEventCount() != 1 ||
                    entry.callback.GetPersistentListenerState(0) != UnityEventCallState.EditorAndRuntime))
                failures.Add(path + " tooltip must respond once to hover exit and keyboard/controller focus exit");
        }

        private static bool ColorsMatch(Color actual, Color32 expected)
        {
            var target = (Color)expected;
            return Mathf.Abs(actual.r - target.r) < 0.001f && Mathf.Abs(actual.g - target.g) < 0.001f &&
                   Mathf.Abs(actual.b - target.b) < 0.001f && Mathf.Abs(actual.a - target.a) < 0.001f;
        }

        private static void ValidateBuiltBundle(string path)
        {
            var bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
                throw new InvalidOperationException("Unity could not reopen the built PackRat UI AssetBundle.");
            try
            {
                var expectedNames = new[]
                {
                    StandalonePrefabPath.ToLowerInvariant(), EmbeddedPrefabPath.ToLowerInvariant(),
                    HandoverPrefabPath.ToLowerInvariant(), SettingsPrefabPath.ToLowerInvariant(),
                    DedicatedCanvasPrefabPath.ToLowerInvariant()
                };
                var actual = new HashSet<string>(bundle.GetAllAssetNames(), StringComparer.OrdinalIgnoreCase);
                var missing = expectedNames.Where(name => !actual.Contains(name)).ToArray();
                if (missing.Length > 0)
                    throw new InvalidOperationException("Built AssetBundle is missing: " + string.Join(", ", missing));
            }
            finally
            {
                bundle.Unload(true);
            }
        }
    }
}
