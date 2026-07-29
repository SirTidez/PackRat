using MelonLoader;
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
/// Stored in UserData/PackRat.cfg under the "PackRat" category.
/// </summary>
public class Configuration
{
    private static Configuration _instance;
    public static Configuration Instance => _instance ??= new Configuration();

    private readonly string _configFile = Path.Combine("UserData", "PackRat.cfg");

    private readonly MelonPreferences_Category _category;
    private readonly MelonPreferences_Entry<KeyCode> _toggleKeyEntry;
    private readonly MelonPreferences_Entry<bool> _enableSearchEntry;
    private readonly MelonPreferences_Entry<bool> _backpackSyncDebugLoggingEntry;
    private readonly MelonPreferences_Entry<bool> _enableUiAnimationsEntry;
    private readonly MelonPreferences_Entry<bool> _reduceUiMotionEntry;
    private readonly MelonPreferences_Entry<bool> _protectFavoritesFromOrganizationEntry;
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
        _category = MelonPreferences.CreateCategory("PackRat");
        _category.SetFilePath(_configFile, false);
        _toggleKeyEntry = _category.CreateEntry("ToggleKey", KeyCode.B, "Key to toggle backpack");
        _enableSearchEntry = _category.CreateEntry(
            "EnableSearch",
            true,
            "Allow police body searches to include searchable backpack tiers"
        );
        _backpackSyncDebugLoggingEntry = _category.CreateEntry(
            "BackpackSyncDebugLogging",
            false,
            "Enable verbose backpack sync debug logging (host/client save sync diagnostics)"
        );
        _enableUiAnimationsEntry = _category.CreateEntry(
            "EnableUiAnimations",
            true,
            "Enable PackRat backpack UI transitions"
        );
        _reduceUiMotionEntry = _category.CreateEntry(
            "ReduceUiMotion",
            false,
            "Use fade-only PackRat backpack UI transitions"
        );
        _protectFavoritesFromOrganizationEntry = _category.CreateEntry(
            "ProtectFavoritesFromOrganization",
            true,
            "Keep favorited backpack items fixed when using PackRat's organize action"
        );
        _backpackOverlayOffsetXEntry = _category.CreateEntry(
            "BackpackOverlayOffsetX",
            0f,
            "Horizontal offset for the hotkey backpack display"
        );
        _backpackOverlayOffsetYEntry = _category.CreateEntry(
            "BackpackOverlayOffsetY",
            0f,
            "Vertical offset for the hotkey backpack display"
        );
        _backpackOverlayScaleEntry = _category.CreateEntry(
            "BackpackOverlayScale",
            1f,
            "Scale for the hotkey backpack display"
        );
        _storageOverlayOffsetXEntry = _category.CreateEntry(
            "StorageOverlayOffsetX",
            0f,
            "Horizontal offset for backpack overlay in storage container menus"
        );
        _storageOverlayOffsetYEntry = _category.CreateEntry(
            "StorageOverlayOffsetY",
            0f,
            "Vertical offset for backpack overlay in storage container menus"
        );
        _storageOverlayScaleEntry = _category.CreateEntry(
            "StorageOverlayScale",
            1f,
            "Scale for backpack overlay in storage container menus"
        );
        _stationOverlayOffsetXEntry = _category.CreateEntry(
            "StationOverlayOffsetX",
            0f,
            "Horizontal offset for backpack overlay in station menus"
        );
        _stationOverlayOffsetYEntry = _category.CreateEntry(
            "StationOverlayOffsetY",
            0f,
            "Vertical offset for backpack overlay in station menus"
        );
        _stationOverlayScaleEntry = _category.CreateEntry(
            "StationOverlayScale",
            1f,
            "Scale for backpack overlay in station menus"
        );
        _handoverOverlayOffsetXEntry = _category.CreateEntry(
            "HandoverOverlayOffsetX",
            0f,
            "Horizontal offset for backpack overlay in deal handover menus"
        );
        _handoverOverlayOffsetYEntry = _category.CreateEntry(
            "HandoverOverlayOffsetY",
            0f,
            "Vertical offset for backpack overlay in deal handover menus"
        );
        _handoverOverlayScaleEntry = _category.CreateEntry(
            "HandoverOverlayScale",
            1f,
            "Scale for backpack overlay in deal handover menus"
        );
        _embeddedBrowserLayoutDefaultsAppliedEntry = _category.CreateEntry(
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
            _tierUnlockRankEntries[i] = _category.CreateEntry(
                $"Tier{i}_UnlockRank",
                BackpackTiers[i].DefaultUnlockRank,
                $"Required rank to unlock tier {i} ({BackpackTiers[i].Name})"
            );
            _tierSlotCountEntries[i] = _category.CreateEntry(
                $"Tier{i}_SlotCount",
                BackpackTiers[i].DefaultSlotCount,
                $"Number of storage slots for tier {i} ({BackpackTiers[i].Name})"
            );
            _tierEnabledEntries[i] = _category.CreateEntry(
                $"Tier{i}_Enabled",
                true,
                $"Enable tier {i} ({BackpackTiers[i].Name}) - when disabled, this tier is not available"
            );
            _tierPriceEntries[i] = _category.CreateEntry(
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
    public bool BackpackSyncDebugLogging { get; set; }
    public bool EnableUiAnimations { get; set; }
    public bool ReduceUiMotion { get; set; }
    public bool ProtectFavoritesFromOrganization { get; set; }
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
    /// Per-tier enable flags. When false, that tier is not available (not shown as unlockable, not used for current tier).
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
        Reset();
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
        BackpackSyncDebugLogging = _backpackSyncDebugLoggingEntry.Value;
        EnableUiAnimations = _enableUiAnimationsEntry.Value;
        ReduceUiMotion = _reduceUiMotionEntry.Value;
        ProtectFavoritesFromOrganization = _protectFavoritesFromOrganizationEntry.Value;
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
            TierSlotCounts[i] = Math.Clamp(_tierSlotCountEntries[i].Value, 1, PlayerBackpack.MaxStorageSlots);
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
        _backpackSyncDebugLoggingEntry.Value = BackpackSyncDebugLogging;
        _enableUiAnimationsEntry.Value = EnableUiAnimations;
        _reduceUiMotionEntry.Value = ReduceUiMotion;
        _protectFavoritesFromOrganizationEntry.Value = ProtectFavoritesFromOrganization;
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
            _tierUnlockRankEntries[i].Value = new FullRank(TierUnlockRanks[i].Rank, Math.Clamp(TierUnlockRanks[i].Tier, 1, 5));
            _tierSlotCountEntries[i].Value = Math.Clamp(TierSlotCounts[i], 1, PlayerBackpack.MaxStorageSlots);
            _tierEnabledEntries[i].Value = TierEnabled[i];
            _tierPriceEntries[i].Value = Math.Max(0f, TierPrices[i]);
        }
        MelonPreferences.Save();
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
        sb.AppendLine($"EnableSearch = {EnableSearch.ToString().ToLowerInvariant()}");
        sb.AppendLine($"BackpackOverlayOffsetX = {BackpackOverlayOffsetX.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}");
        sb.AppendLine($"BackpackOverlayOffsetY = {BackpackOverlayOffsetY.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}");
        sb.AppendLine($"BackpackOverlayScale = {ClampOverlayScale(BackpackOverlayScale).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
        sb.AppendLine($"StorageOverlayOffsetX = {StorageOverlayOffsetX.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}");
        sb.AppendLine($"StorageOverlayOffsetY = {StorageOverlayOffsetY.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}");
        sb.AppendLine($"StorageOverlayScale = {ClampOverlayScale(StorageOverlayScale).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
        sb.AppendLine($"StationOverlayOffsetX = {StationOverlayOffsetX.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}");
        sb.AppendLine($"StationOverlayOffsetY = {StationOverlayOffsetY.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}");
        sb.AppendLine($"StationOverlayScale = {ClampOverlayScale(StationOverlayScale).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
        sb.AppendLine($"HandoverOverlayOffsetX = {HandoverOverlayOffsetX.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}");
        sb.AppendLine($"HandoverOverlayOffsetY = {HandoverOverlayOffsetY.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}");
        sb.AppendLine($"HandoverOverlayScale = {ClampOverlayScale(HandoverOverlayScale).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
        sb.AppendLine($"EmbeddedBrowserLayoutDefaultsApplied = {EmbeddedBrowserLayoutDefaultsApplied.ToString().ToLowerInvariant()}");

        for (var i = 0; i < BackpackTiers.Length; i++)
        {
            var rank = TierUnlockRanks[i];
            sb.AppendLine($"Tier{i}_UnlockRank = {{ Rank = \"{rank.Rank}\", Tier = {Math.Clamp(rank.Tier, 1, 5)} }}");
            sb.AppendLine($"Tier{i}_SlotCount = {Math.Clamp(TierSlotCounts[i], 1, PlayerBackpack.MaxStorageSlots)}");
        }

        for (var i = 0; i < BackpackTiers.Length; i++)
            sb.AppendLine($"Tier{i}_Enabled = {TierEnabled[i].ToString().ToLowerInvariant()}");

        for (var i = 0; i < BackpackTiers.Length; i++)
            sb.AppendLine($"Tier{i}_Price = {Math.Max(0f, TierPrices[i]).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}");

        sb.AppendLine($"BackpackSyncDebugLogging = {BackpackSyncDebugLogging.ToString().ToLowerInvariant()}");
        sb.AppendLine($"EnableUiAnimations = {EnableUiAnimations.ToString().ToLowerInvariant()}");
        sb.AppendLine($"ReduceUiMotion = {ReduceUiMotion.ToString().ToLowerInvariant()}");
        sb.AppendLine($"ProtectFavoritesFromOrganization = {ProtectFavoritesFromOrganization.ToString().ToLowerInvariant()}");
        return sb.ToString();
    }

    private static float ClampOverlayScale(float value)
    {
        return Math.Clamp(value, 0.5f, 1.5f);
    }
}
