using MelonLoader;
using PackRat.Helpers;
using UnityEngine;
using System.Text;

#if MONO
using ScheduleOne.Levelling;
#else
using Il2CppScheduleOne.Levelling;
#endif

namespace PackRat.Config;

/// <summary>
/// Singleton configuration class backed by MelonPreferences.
/// Stored in UserData/PackRat.cfg under organized, PackRat-namespaced categories.
/// </summary>
public class Configuration
{
    private static Configuration _instance;
    public static Configuration Instance => _instance ??= new Configuration();

    private readonly string _configFile = Path.Combine("UserData", "PackRat.cfg");

    private readonly MelonPreferences_Category _packratCategory;
    private readonly MelonPreferences_Category _diagnosticsCategory;
    private readonly MelonPreferences_Category _routingCategory;
    private readonly MelonPreferences_Category _metricsCategory;
    private readonly MelonPreferences_Category _themeCategory;
    private readonly MelonPreferences_Category _backpackOverlayCategory;
    private readonly MelonPreferences_Category _storageOverlayCategory;
    private readonly MelonPreferences_Category _stationOverlayCategory;
    private readonly MelonPreferences_Category _handoverOverlayCategory;
    private readonly LegacyPreferenceMigration _legacyPreferenceMigration;
    private readonly MelonPreferences_Entry<KeyCode> _toggleKeyEntry;
    private readonly MelonPreferences_Entry<bool> _enableSearchEntry;
    private readonly MelonPreferences_Entry<bool> _enableDebugLoggingEntry;
    private readonly MelonPreferences_Entry<bool> _enableDeveloperProfilerEntry;
    private readonly MelonPreferences_Entry<bool> _backpackSyncDebugLoggingEntry;
    private readonly MelonPreferences_Entry<bool> _enableUiAnimationsEntry;
    private readonly MelonPreferences_Entry<bool> _reduceUiMotionEntry;
    private readonly MelonPreferences_Entry<bool> _protectFavoritesFromOrganizationEntry;
    private readonly MelonPreferences_Entry<bool> _enableSmartRoutingEntry;
    private readonly MelonPreferences_Entry<bool> _routeProductsEntry;
    private readonly MelonPreferences_Entry<bool> _routeSeedsEntry;
    private readonly MelonPreferences_Entry<bool> _routeMixersEntry;
    private readonly MelonPreferences_Entry<bool> _routeReagentsEntry;
    private readonly MelonPreferences_Entry<bool> _showMetricsTrayEntry;
    private readonly MelonPreferences_Entry<bool> _showProductQuantityMetricEntry;
    private readonly MelonPreferences_Entry<bool> _showProductQuantityTotalMetricEntry;
    private readonly MelonPreferences_Entry<bool> _showProductUnitPriceMetricEntry;
    private readonly MelonPreferences_Entry<bool> _showProductTotalPriceMetricEntry;
    private readonly MelonPreferences_Entry<float> _metricsFontScaleEntry;
    private readonly MelonPreferences_Entry<int> _backpackUiThemeEntry;
    private readonly MelonPreferences_Entry<int> _backpackCustomThemeRedEntry;
    private readonly MelonPreferences_Entry<int> _backpackCustomThemeGreenEntry;
    private readonly MelonPreferences_Entry<int> _backpackCustomThemeBlueEntry;
    private readonly MelonPreferences_Entry<float> _backpackOverlayOffsetXEntry;
    private readonly MelonPreferences_Entry<float> _backpackOverlayOffsetYEntry;
    private readonly MelonPreferences_Entry<float> _backpackOverlayScaleEntry;
    private readonly MelonPreferences_Entry<float> _storageOverlayOffsetXEntry;
    private readonly MelonPreferences_Entry<float> _storageOverlayOffsetYEntry;
    private readonly MelonPreferences_Entry<float> _storageOverlayScaleEntry;
    private readonly MelonPreferences_Entry<float> _stationOverlayOffsetXEntry;
    private readonly MelonPreferences_Entry<float> _stationOverlayOffsetYEntry;
    private readonly MelonPreferences_Entry<float> _stationOverlayScaleEntry;
    private readonly MelonPreferences_Entry<float> _handoverOverlayOffsetXEntry;
    private readonly MelonPreferences_Entry<float> _handoverOverlayOffsetYEntry;
    private readonly MelonPreferences_Entry<float> _handoverOverlayScaleEntry;
    private readonly MelonPreferences_Entry<bool> _embeddedBrowserLayoutDefaultsAppliedEntry;
    private readonly MelonPreferences_Entry<FullRank>[] _tierUnlockRankEntries;
    private readonly MelonPreferences_Entry<int>[] _tierSlotCountEntries;
    private readonly MelonPreferences_Entry<bool>[] _tierEnabledEntries;
    private readonly MelonPreferences_Entry<float>[] _tierPriceEntries;

    /// <summary>Default prices per tier (rucksack cheapest, hiking most expensive).</summary>
    private static readonly float[] DefaultTierPrices = [25f, 75f, 150f, 300f, 500f];

    /// <summary>
    /// Default tier definitions. Slot count and unlock rank are configurable by the host;
    /// <see cref="BackpackTierDefinition.HasPoliceSearch"/> is hardcoded per tier.
    /// </summary>
    public static readonly BackpackTierDefinition[] BackpackTiers =
    [
        new BackpackTierDefinition("Rucksack",        8,  false, new FullRank(ERank.Hoodlum,   1)),
        new BackpackTierDefinition("Small Pack",      16, false, new FullRank(ERank.Peddler,   1)),
        new BackpackTierDefinition("Duffel Bag",      24, true,  new FullRank(ERank.Hustler,   1)),
        new BackpackTierDefinition("Tactical Pack",   32, true,  new FullRank(ERank.Enforcer,  1)),
        new BackpackTierDefinition("Hiking Backpack", 40, true,  new FullRank(ERank.Block_Boss, 1)),
    ];

    public Configuration()
    {
        _packratCategory = CreateCategory("PackRat", "PackRat - General");
        _diagnosticsCategory = CreateCategory("PackRat_Diagnostics", "PackRat - Diagnostics");
        _routingCategory = CreateCategory("PackRat_Routing", "PackRat - Routing");
        _metricsCategory = CreateCategory("PackRat_Metrics", "PackRat - Metrics");
        _themeCategory = CreateCategory("PackRat_Theme", "PackRat - Theme");
        _backpackOverlayCategory = CreateCategory("PackRat_BackpackOverlay", "PackRat - Backpack Overlay");
        _storageOverlayCategory = CreateCategory("PackRat_StorageOverlay", "PackRat - Storage Overlay");
        _stationOverlayCategory = CreateCategory("PackRat_StationOverlay", "PackRat - Station Overlay");
        _handoverOverlayCategory = CreateCategory("PackRat_HandoverOverlay", "PackRat - Handover Overlay");
        _legacyPreferenceMigration = new LegacyPreferenceMigration(_packratCategory);

        _toggleKeyEntry = _packratCategory.CreateEntry("ToggleKey", KeyCode.B, "Key to toggle backpack");
        _enableSearchEntry = _packratCategory.CreateEntry(
            "EnableSearch",
            true,
            "Allow police body searches to include searchable backpack tiers"
        );
        _backpackSyncDebugLoggingEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _diagnosticsCategory,
            "BackpackSyncDebugLogging",
            false,
            "Enable verbose backpack network synchronization diagnostics in release builds"
        );
        _enableDebugLoggingEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _diagnosticsCategory,
            "EnableDebugLogging",
            false,
            "Enable verbose PackRat UI, lifecycle, and shop diagnostics in release builds"
        );
        _enableDeveloperProfilerEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _diagnosticsCategory,
            "EnableDeveloperProfiler",
            false,
            "Developer Profiler"
        );
        _enableUiAnimationsEntry = _packratCategory.CreateEntry(
            "EnableUiAnimations",
            true,
            "Enable PackRat backpack UI transitions"
        );
        _reduceUiMotionEntry = _packratCategory.CreateEntry(
            "ReduceUiMotion",
            false,
            "Use fade-only PackRat backpack UI transitions"
        );
        _protectFavoritesFromOrganizationEntry = _packratCategory.CreateEntry(
            "ProtectFavoritesFromOrganization",
            true,
            "Keep favorited backpack items unchanged by PackRat's organize and stack actions"
        );
        _enableSmartRoutingEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _routingCategory,
            "EnableSmartRouting",
            false,
            "Prefer routing configured quick-move categories into the backpack"
        );
        _routeProductsEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _routingCategory,
            "RouteProducts",
            true,
            "Route drug products into the backpack during quick move when Smart Routing is enabled"
        );
        _routeSeedsEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _routingCategory,
            "RouteSeeds",
            true,
            "Route seeds into the backpack during quick move when Smart Routing is enabled"
        );
        _routeMixersEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _routingCategory,
            "RouteMixers",
            false,
            "Route mixers into the backpack during quick move when Smart Routing is enabled"
        );
        _routeReagentsEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _routingCategory,
            "RouteReagents",
            false,
            "Route reagents into the backpack during quick move when Smart Routing is enabled"
        );
        _showMetricsTrayEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _metricsCategory,
            "ShowMetricsTray",
            true,
            "Show the expandable product metrics tray beside the hotkey backpack"
        );
        _showProductQuantityMetricEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _metricsCategory,
            "ShowProductQuantityMetric",
            true,
            "Show each product's saleable unit quantity after accounting for package capacity"
        );
        _showProductQuantityTotalMetricEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _metricsCategory,
            "ShowProductQuantityTotalMetric",
            true,
            "Show the total saleable product units in the backpack metrics tray"
        );
        _showProductUnitPriceMetricEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _metricsCategory,
            "ShowProductUnitPriceMetric",
            true,
            "Show the game's current product unit price in the backpack metrics tray"
        );
        _showProductTotalPriceMetricEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _metricsCategory,
            "ShowProductTotalPriceMetric",
            true,
            "Show total product value in the backpack metrics tray"
        );
        _metricsFontScaleEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _metricsCategory,
            "MetricsFontScale",
            1f,
            "Text scale for the backpack product metrics tray"
        );
        _backpackUiThemeEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _themeCategory,
            "BackpackUiTheme",
            (int)BackpackUiTheme.S1Blue,
            "PackRat backpack UI color theme"
        );
        _backpackCustomThemeRedEntry = _legacyPreferenceMigration.CreateMovedEntry(_themeCategory,
            "BackpackCustomThemeRed", 35,
            "Red channel for PackRat's custom backpack UI theme");
        _backpackCustomThemeGreenEntry = _legacyPreferenceMigration.CreateMovedEntry(_themeCategory,
            "BackpackCustomThemeGreen", 61,
            "Green channel for PackRat's custom backpack UI theme");
        _backpackCustomThemeBlueEntry = _legacyPreferenceMigration.CreateMovedEntry(_themeCategory,
            "BackpackCustomThemeBlue", 86,
            "Blue channel for PackRat's custom backpack UI theme");
        _backpackOverlayOffsetXEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _backpackOverlayCategory,
            "BackpackOverlayOffsetX",
            0f,
            "Horizontal offset for the hotkey backpack display"
        );
        _backpackOverlayOffsetYEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _backpackOverlayCategory,
            "BackpackOverlayOffsetY",
            0f,
            "Vertical offset for the hotkey backpack display"
        );
        _backpackOverlayScaleEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _backpackOverlayCategory,
            "BackpackOverlayScale",
            1f,
            "Scale for the hotkey backpack display"
        );
        _storageOverlayOffsetXEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _storageOverlayCategory,
            "StorageOverlayOffsetX",
            0f,
            "Horizontal offset for backpack overlay in storage container menus"
        );
        _storageOverlayOffsetYEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _storageOverlayCategory,
            "StorageOverlayOffsetY",
            0f,
            "Vertical offset for backpack overlay in storage container menus"
        );
        _storageOverlayScaleEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _storageOverlayCategory,
            "StorageOverlayScale",
            1f,
            "Scale for backpack overlay in storage container menus"
        );
        _stationOverlayOffsetXEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _stationOverlayCategory,
            "StationOverlayOffsetX",
            0f,
            "Horizontal offset for backpack overlay in station menus"
        );
        _stationOverlayOffsetYEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _stationOverlayCategory,
            "StationOverlayOffsetY",
            0f,
            "Vertical offset for backpack overlay in station menus"
        );
        _stationOverlayScaleEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _stationOverlayCategory,
            "StationOverlayScale",
            1f,
            "Scale for backpack overlay in station menus"
        );
        _handoverOverlayOffsetXEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _handoverOverlayCategory,
            "HandoverOverlayOffsetX",
            0f,
            "Horizontal offset for backpack overlay in deal handover menus"
        );
        _handoverOverlayOffsetYEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _handoverOverlayCategory,
            "HandoverOverlayOffsetY",
            0f,
            "Vertical offset for backpack overlay in deal handover menus"
        );
        _handoverOverlayScaleEntry = _legacyPreferenceMigration.CreateMovedEntry(
            _handoverOverlayCategory,
            "HandoverOverlayScale",
            1f,
            "Scale for backpack overlay in deal handover menus"
        );
        _embeddedBrowserLayoutDefaultsAppliedEntry = _packratCategory.CreateEntry(
            "EmbeddedBrowserLayoutDefaultsApplied",
            false,
            "Tracks the one-time upgrade to full embedded backpack browser layouts"
        );

        _tierUnlockRankEntries = new MelonPreferences_Entry<FullRank>[BackpackTiers.Length];
        _tierSlotCountEntries = new MelonPreferences_Entry<int>[BackpackTiers.Length];
        _tierEnabledEntries = new MelonPreferences_Entry<bool>[BackpackTiers.Length];
        _tierPriceEntries = new MelonPreferences_Entry<float>[BackpackTiers.Length];
        for (var i = 0; i < BackpackTiers.Length; i++)
        {
            var tierCategory = CreateCategory(
                $"PackRat_Tier{i}",
                $"PackRat - Tier {i}: {BackpackTiers[i].Name}"
            );
            _tierUnlockRankEntries[i] = _legacyPreferenceMigration.CreateMovedEntry(
                tierCategory,
                $"Tier{i}_UnlockRank",
                BackpackTiers[i].DefaultUnlockRank,
                $"Required rank to unlock tier {i} ({BackpackTiers[i].Name})"
            );
            _tierSlotCountEntries[i] = _legacyPreferenceMigration.CreateMovedEntry(
                tierCategory,
                $"Tier{i}_SlotCount",
                BackpackTiers[i].DefaultSlotCount,
                $"Number of storage slots for tier {i} ({BackpackTiers[i].Name})"
            );
            _tierEnabledEntries[i] = _legacyPreferenceMigration.CreateMovedEntry(
                tierCategory,
                $"Tier{i}_Enabled",
                true,
                $"Enable tier {i} ({BackpackTiers[i].Name}) - when disabled, this tier is not available"
            );
            _tierPriceEntries[i] = _legacyPreferenceMigration.CreateMovedEntry(
                tierCategory,
                $"Tier{i}_Price",
                DefaultTierPrices[i],
                $"Price at hardware store for tier {i} ({BackpackTiers[i].Name})"
            );
        }

        TierUnlockRanks = new FullRank[BackpackTiers.Length];
        TierSlotCounts = new int[BackpackTiers.Length];
        TierEnabled = new bool[BackpackTiers.Length];
        TierPrices = new float[BackpackTiers.Length];
    }

    public KeyCode ToggleKey { get; set; }
    public bool EnableSearch { get; set; }
    public bool EnableDebugLogging { get; set; }
    public bool EnableDeveloperProfiler { get; set; }
    public bool BackpackSyncDebugLogging { get; set; }
    public bool EnableUiAnimations { get; set; }
    public bool ReduceUiMotion { get; set; }
    public bool ProtectFavoritesFromOrganization { get; set; }
    public bool EnableSmartRouting { get; set; }
    public bool RouteProducts { get; set; }
    public bool RouteSeeds { get; set; }
    public bool RouteMixers { get; set; }
    public bool RouteReagents { get; set; }
    public bool ShowMetricsTray { get; set; }
    public bool ShowProductQuantityMetric { get; set; }
    public bool ShowProductQuantityTotalMetric { get; set; }
    public bool ShowProductUnitPriceMetric { get; set; }
    public bool ShowProductTotalPriceMetric { get; set; }
    public float MetricsFontScale { get; set; }
    public BackpackUiTheme BackpackUiTheme { get; set; }
    public Color CustomBackpackUiPrimaryColor { get; set; }
    public float BackpackOverlayOffsetX { get; set; }
    public float BackpackOverlayOffsetY { get; set; }
    public float BackpackOverlayScale { get; set; }
    public float StorageOverlayOffsetX { get; set; }
    public float StorageOverlayOffsetY { get; set; }
    public float StorageOverlayScale { get; set; }
    public float StationOverlayOffsetX { get; set; }
    public float StationOverlayOffsetY { get; set; }
    public float StationOverlayScale { get; set; }
    public float HandoverOverlayOffsetX { get; set; }
    public float HandoverOverlayOffsetY { get; set; }
    public float HandoverOverlayScale { get; set; }
    public bool EmbeddedBrowserLayoutDefaultsApplied { get; private set; }
    public FullRank[] TierUnlockRanks { get; internal set; }
    public int[] TierSlotCounts { get; internal set; }

    /// <summary>
    /// Per-tier enable flags. When false, that tier is not available
    /// (not shown as unlockable and not used for the current tier).
    /// </summary>
    public bool[] TierEnabled { get; internal set; }

    /// <summary>
    /// Per-tier price at the hardware store (rucksack cheapest, hiking most expensive). Synced from host.
    /// </summary>
    public float[] TierPrices { get; internal set; }

    /// <summary>
    /// Loads preferences from disk and resets cached values.
    /// </summary>
    public void Load()
    {
        MelonPreferences.Load();
        var migratedPreferenceCount = _legacyPreferenceMigration.Apply();
        Reset();
        if (migratedPreferenceCount > 0)
        {
            MelonPreferences.Save();
            ModLogger.Info($"Migrated {migratedPreferenceCount} preferences into organized categories.");
        }
        ApplyEmbeddedBrowserLayoutDefaults();
        EnsureConfigFileExists();
    }

    /// <summary>
    /// Resets cached property values from loaded preferences.
    /// </summary>
    public void Reset()
    {
        ToggleKey = _toggleKeyEntry.Value;
        EnableSearch = _enableSearchEntry.Value;
        EnableDebugLogging = _enableDebugLoggingEntry.Value;
        EnableDeveloperProfiler = _enableDeveloperProfilerEntry.Value;
        BackpackSyncDebugLogging = _backpackSyncDebugLoggingEntry.Value;
        ModLogger.SetDebugLoggingEnabled(EnableDebugLogging);
        ModLogger.SetSyncDebugLoggingEnabled(BackpackSyncDebugLogging);
        EnableUiAnimations = _enableUiAnimationsEntry.Value;
        ReduceUiMotion = _reduceUiMotionEntry.Value;
        ProtectFavoritesFromOrganization = _protectFavoritesFromOrganizationEntry.Value;
        EnableSmartRouting = _enableSmartRoutingEntry.Value;
        RouteProducts = _routeProductsEntry.Value;
        RouteSeeds = _routeSeedsEntry.Value;
        RouteMixers = _routeMixersEntry.Value;
        RouteReagents = _routeReagentsEntry.Value;
        ShowMetricsTray = _showMetricsTrayEntry.Value;
        ShowProductQuantityMetric = _showProductQuantityMetricEntry.Value;
        ShowProductQuantityTotalMetric = _showProductQuantityTotalMetricEntry.Value;
        ShowProductUnitPriceMetric = _showProductUnitPriceMetricEntry.Value;
        ShowProductTotalPriceMetric = _showProductTotalPriceMetricEntry.Value;
        MetricsFontScale = ClampMetricsFontScale(_metricsFontScaleEntry.Value);
        BackpackUiTheme = BackpackUiThemes.Clamp(_backpackUiThemeEntry.Value);
        CustomBackpackUiPrimaryColor = new Color32(
            (byte)Mathf.Clamp(_backpackCustomThemeRedEntry.Value, 0, 255),
            (byte)Mathf.Clamp(_backpackCustomThemeGreenEntry.Value, 0, 255),
            (byte)Mathf.Clamp(_backpackCustomThemeBlueEntry.Value, 0, 255),
            255
        );
        BackpackOverlayOffsetX = _backpackOverlayOffsetXEntry.Value;
        BackpackOverlayOffsetY = _backpackOverlayOffsetYEntry.Value;
        BackpackOverlayScale = ClampOverlayScale(_backpackOverlayScaleEntry.Value);
        StorageOverlayOffsetX = _storageOverlayOffsetXEntry.Value;
        StorageOverlayOffsetY = _storageOverlayOffsetYEntry.Value;
        StorageOverlayScale = ClampOverlayScale(_storageOverlayScaleEntry.Value);
        StationOverlayOffsetX = _stationOverlayOffsetXEntry.Value;
        StationOverlayOffsetY = _stationOverlayOffsetYEntry.Value;
        StationOverlayScale = ClampOverlayScale(_stationOverlayScaleEntry.Value);
        HandoverOverlayOffsetX = _handoverOverlayOffsetXEntry.Value;
        HandoverOverlayOffsetY = _handoverOverlayOffsetYEntry.Value;
        HandoverOverlayScale = ClampOverlayScale(_handoverOverlayScaleEntry.Value);
        EmbeddedBrowserLayoutDefaultsApplied = _embeddedBrowserLayoutDefaultsAppliedEntry.Value;
        for (var i = 0; i < BackpackTiers.Length; i++)
        {
            var rank = _tierUnlockRankEntries[i].Value;
            TierUnlockRanks[i] = new FullRank(rank.Rank, Math.Clamp(rank.Tier, 1, 5));
            TierSlotCounts[i] = Math.Max(PlayerBackpack.MinimumStorageSlots, _tierSlotCountEntries[i].Value);
            TierEnabled[i] = _tierEnabledEntries[i].Value;
            TierPrices[i] = Math.Max(0f, _tierPriceEntries[i].Value);
        }
    }

    /// <summary>
    /// Persists current property values back to the preferences file.
    /// </summary>
    public void Save()
    {
        _toggleKeyEntry.Value = ToggleKey;
        _enableSearchEntry.Value = EnableSearch;
        _enableDebugLoggingEntry.Value = EnableDebugLogging;
        _enableDeveloperProfilerEntry.Value = EnableDeveloperProfiler;
        _backpackSyncDebugLoggingEntry.Value = BackpackSyncDebugLogging;
        ModLogger.SetDebugLoggingEnabled(EnableDebugLogging);
        ModLogger.SetSyncDebugLoggingEnabled(BackpackSyncDebugLogging);
        _enableUiAnimationsEntry.Value = EnableUiAnimations;
        _reduceUiMotionEntry.Value = ReduceUiMotion;
        _protectFavoritesFromOrganizationEntry.Value = ProtectFavoritesFromOrganization;
        _enableSmartRoutingEntry.Value = EnableSmartRouting;
        _routeProductsEntry.Value = RouteProducts;
        _routeSeedsEntry.Value = RouteSeeds;
        _routeMixersEntry.Value = RouteMixers;
        _routeReagentsEntry.Value = RouteReagents;
        _showMetricsTrayEntry.Value = ShowMetricsTray;
        _showProductQuantityMetricEntry.Value = ShowProductQuantityMetric;
        _showProductQuantityTotalMetricEntry.Value = ShowProductQuantityTotalMetric;
        _showProductUnitPriceMetricEntry.Value = ShowProductUnitPriceMetric;
        _showProductTotalPriceMetricEntry.Value = ShowProductTotalPriceMetric;
        _metricsFontScaleEntry.Value = ClampMetricsFontScale(MetricsFontScale);
        _backpackUiThemeEntry.Value = (int)BackpackUiThemes.Clamp((int)BackpackUiTheme);
        var customPrimary = (Color32)CustomBackpackUiPrimaryColor;
        _backpackCustomThemeRedEntry.Value = customPrimary.r;
        _backpackCustomThemeGreenEntry.Value = customPrimary.g;
        _backpackCustomThemeBlueEntry.Value = customPrimary.b;
        _backpackOverlayOffsetXEntry.Value = BackpackOverlayOffsetX;
        _backpackOverlayOffsetYEntry.Value = BackpackOverlayOffsetY;
        _backpackOverlayScaleEntry.Value = ClampOverlayScale(BackpackOverlayScale);
        _storageOverlayOffsetXEntry.Value = StorageOverlayOffsetX;
        _storageOverlayOffsetYEntry.Value = StorageOverlayOffsetY;
        _storageOverlayScaleEntry.Value = ClampOverlayScale(StorageOverlayScale);
        _stationOverlayOffsetXEntry.Value = StationOverlayOffsetX;
        _stationOverlayOffsetYEntry.Value = StationOverlayOffsetY;
        _stationOverlayScaleEntry.Value = ClampOverlayScale(StationOverlayScale);
        _handoverOverlayOffsetXEntry.Value = HandoverOverlayOffsetX;
        _handoverOverlayOffsetYEntry.Value = HandoverOverlayOffsetY;
        _handoverOverlayScaleEntry.Value = ClampOverlayScale(HandoverOverlayScale);
        _embeddedBrowserLayoutDefaultsAppliedEntry.Value = EmbeddedBrowserLayoutDefaultsApplied;
        for (var i = 0; i < BackpackTiers.Length; i++)
        {
            _tierUnlockRankEntries[i].Value = new FullRank(
                TierUnlockRanks[i].Rank,
                Math.Clamp(TierUnlockRanks[i].Tier, 1, 5)
            );
            _tierSlotCountEntries[i].Value = Math.Max(PlayerBackpack.MinimumStorageSlots, TierSlotCounts[i]);
            _tierEnabledEntries[i].Value = TierEnabled[i];
            _tierPriceEntries[i].Value = Math.Max(0f, TierPrices[i]);
        }
        MelonPreferences.Save();
    }

    private MelonPreferences_Category CreateCategory(string identifier, string displayName)
    {
        var category = MelonPreferences.CreateCategory(identifier, displayName);
        category.SetFilePath(_configFile, false);
        return category;
    }

    private void EnsureConfigFileExists()
    {
        try
        {
            if (File.Exists(_configFile))
                return;

            var directory = Path.GetDirectoryName(_configFile);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(_configFile, BuildConfigFileContents());
        }
        catch
        {
            // Ignore bootstrap file creation failures; the mod can still run with in-memory defaults.
        }
    }

    /// <summary>
    /// Moves the newly shared embedded browser into the open left-side workspace once for
    /// existing installs. The old compact cards were centered by default; retaining those values
    /// would place the full main browser over the deal and station controls.
    /// </summary>
    private void ApplyEmbeddedBrowserLayoutDefaults()
    {
        if (EmbeddedBrowserLayoutDefaultsApplied)
            return;

        const float leftCenterX = -430f;
        const float embeddedScale = 0.85f;
        StorageOverlayOffsetX = leftCenterX;
        StorageOverlayOffsetY = 0f;
        StorageOverlayScale = embeddedScale;
        StationOverlayOffsetX = leftCenterX;
        StationOverlayOffsetY = 0f;
        StationOverlayScale = embeddedScale;
        HandoverOverlayOffsetX = leftCenterX;
        HandoverOverlayOffsetY = 0f;
        HandoverOverlayScale = embeddedScale;
        EmbeddedBrowserLayoutDefaultsApplied = true;
        Save();
    }

    private string BuildConfigFileContents()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[PackRat]");
        sb.AppendLine($"ToggleKey = \"{ToggleKey}\"");
        sb.AppendLine($"EnableSearch = {FormatConfigBool(EnableSearch)}");
        sb.AppendLine($"EnableUiAnimations = {FormatConfigBool(EnableUiAnimations)}");
        sb.AppendLine($"ReduceUiMotion = {FormatConfigBool(ReduceUiMotion)}");
        sb.AppendLine($"ProtectFavoritesFromOrganization = {FormatConfigBool(ProtectFavoritesFromOrganization)}");
        sb.Append("EmbeddedBrowserLayoutDefaultsApplied = ");
        sb.AppendLine(FormatConfigBool(EmbeddedBrowserLayoutDefaultsApplied));

        AppendConfigSection(sb, "PackRat_Diagnostics");
        sb.AppendLine($"EnableDebugLogging = {FormatConfigBool(EnableDebugLogging)}");
        sb.AppendLine($"EnableDeveloperProfiler = {FormatConfigBool(EnableDeveloperProfiler)}");
        sb.AppendLine($"BackpackSyncDebugLogging = {FormatConfigBool(BackpackSyncDebugLogging)}");

        AppendConfigSection(sb, "PackRat_Routing");
        sb.AppendLine($"EnableSmartRouting = {FormatConfigBool(EnableSmartRouting)}");
        sb.AppendLine($"RouteProducts = {FormatConfigBool(RouteProducts)}");
        sb.AppendLine($"RouteSeeds = {FormatConfigBool(RouteSeeds)}");
        sb.AppendLine($"RouteMixers = {FormatConfigBool(RouteMixers)}");
        sb.AppendLine($"RouteReagents = {FormatConfigBool(RouteReagents)}");

        AppendConfigSection(sb, "PackRat_Metrics");
        sb.AppendLine($"ShowMetricsTray = {FormatConfigBool(ShowMetricsTray)}");
        sb.AppendLine($"ShowProductQuantityMetric = {FormatConfigBool(ShowProductQuantityMetric)}");
        sb.AppendLine($"ShowProductQuantityTotalMetric = {FormatConfigBool(ShowProductQuantityTotalMetric)}");
        sb.AppendLine($"ShowProductUnitPriceMetric = {FormatConfigBool(ShowProductUnitPriceMetric)}");
        sb.AppendLine($"ShowProductTotalPriceMetric = {FormatConfigBool(ShowProductTotalPriceMetric)}");
        sb.AppendLine($"MetricsFontScale = {FormatConfigFloat(ClampMetricsFontScale(MetricsFontScale), "0.00")}");

        AppendConfigSection(sb, "PackRat_Theme");
        sb.AppendLine($"BackpackUiTheme = {(int)BackpackUiThemes.Clamp((int)BackpackUiTheme)}");
        var customPrimary = (Color32)CustomBackpackUiPrimaryColor;
        sb.AppendLine($"BackpackCustomThemeRed = {customPrimary.r}");
        sb.AppendLine($"BackpackCustomThemeGreen = {customPrimary.g}");
        sb.AppendLine($"BackpackCustomThemeBlue = {customPrimary.b}");

        AppendConfigSection(sb, "PackRat_BackpackOverlay");
        AppendOverlayConfig(sb, "Backpack", BackpackOverlayOffsetX, BackpackOverlayOffsetY, BackpackOverlayScale);

        AppendConfigSection(sb, "PackRat_StorageOverlay");
        AppendOverlayConfig(sb, "Storage", StorageOverlayOffsetX, StorageOverlayOffsetY, StorageOverlayScale);

        AppendConfigSection(sb, "PackRat_StationOverlay");
        AppendOverlayConfig(sb, "Station", StationOverlayOffsetX, StationOverlayOffsetY, StationOverlayScale);

        AppendConfigSection(sb, "PackRat_HandoverOverlay");
        AppendOverlayConfig(sb, "Handover", HandoverOverlayOffsetX, HandoverOverlayOffsetY, HandoverOverlayScale);

        for (var i = 0; i < BackpackTiers.Length; i++)
        {
            AppendConfigSection(sb, $"PackRat_Tier{i}");
            var rank = TierUnlockRanks[i];
            sb.AppendLine($"Tier{i}_UnlockRank = {{ Rank = \"{rank.Rank}\", Tier = {Math.Clamp(rank.Tier, 1, 5)} }}");
            sb.AppendLine($"Tier{i}_SlotCount = {Math.Max(PlayerBackpack.MinimumStorageSlots, TierSlotCounts[i])}");
            sb.AppendLine($"Tier{i}_Enabled = {FormatConfigBool(TierEnabled[i])}");
            sb.AppendLine($"Tier{i}_Price = {FormatConfigFloat(Math.Max(0f, TierPrices[i]), "0.0")}");
        }
        return sb.ToString();
    }

    private static void AppendConfigSection(StringBuilder sb, string category)
    {
        sb.AppendLine();
        sb.AppendLine($"[{category}]");
    }

    private static void AppendOverlayConfig(StringBuilder sb, string prefix, float offsetX, float offsetY, float scale)
    {
        sb.AppendLine($"{prefix}OverlayOffsetX = {FormatConfigFloat(offsetX, "0.0")}");
        sb.AppendLine($"{prefix}OverlayOffsetY = {FormatConfigFloat(offsetY, "0.0")}");
        sb.AppendLine($"{prefix}OverlayScale = {FormatConfigFloat(ClampOverlayScale(scale), "0.00")}");
    }

    private static string FormatConfigFloat(float value, string format)
    {
        return value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FormatConfigBool(bool value)
    {
        return value.ToString().ToLowerInvariant();
    }

    private static float ClampOverlayScale(float value)
    {
        return Math.Clamp(value, 0.5f, 1.5f);
    }

    /// <summary>
    /// Keeps the metrics tray legible without allowing its compact rows to become unusably large.
    /// </summary>
    public static float ClampMetricsFontScale(float value)
    {
        return Math.Clamp(value, 0.75f, 1.5f);
    }
}
