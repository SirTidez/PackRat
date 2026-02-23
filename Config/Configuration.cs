using MelonLoader;
using UnityEngine;

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
    private readonly MelonPreferences_Entry<FullRank> _unlockLevelEntry;
    private readonly MelonPreferences_Entry<int> _storageSlotsEntry;

    public Configuration()
    {
        _category = MelonPreferences.CreateCategory("PackRat");
        _category.SetFilePath(_configFile, false);
        _toggleKeyEntry = _category.CreateEntry("ToggleKey", KeyCode.B, "Key to toggle backpack");
        _enableSearchEntry = _category.CreateEntry("EnableSearch", false, "Enable police search for backpack items");
        _unlockLevelEntry = _category.CreateEntry("UnlockLevel", new FullRank(ERank.Hoodlum, 1), "Required level to unlock backpack");
        _storageSlotsEntry = _category.CreateEntry("StorageSlots", 12, "Number of total storage slots");
    }

    public KeyCode ToggleKey { get; set; }
    public bool EnableSearch { get; set; }
    public FullRank UnlockLevel { get; internal set; }
    public int StorageSlots { get; internal set; }

    /// <summary>
    /// Loads preferences from disk and resets cached values.
    /// </summary>
    public void Load()
    {
        MelonPreferences.Load();
        Reset();
    }

    /// <summary>
    /// Resets cached property values from loaded preferences.
    /// </summary>
    public void Reset()
    {
        ToggleKey = _toggleKeyEntry.Value;
        EnableSearch = _enableSearchEntry.Value;
        UnlockLevel = new FullRank(_unlockLevelEntry.Value.Rank, Math.Clamp(_unlockLevelEntry.Value.Tier, 1, 5));
        StorageSlots = Math.Clamp(_storageSlotsEntry.Value, 1, PlayerBackpack.MaxStorageSlots);
    }

    /// <summary>
    /// Persists current property values back to the preferences file.
    /// </summary>
    public void Save()
    {
        _toggleKeyEntry.Value = ToggleKey;
        _enableSearchEntry.Value = EnableSearch;
        _unlockLevelEntry.Value = new FullRank(UnlockLevel.Rank, Math.Clamp(UnlockLevel.Tier, 1, 5));
        _storageSlotsEntry.Value = Math.Clamp(StorageSlots, 1, PlayerBackpack.MaxStorageSlots);
        MelonPreferences.Save();
    }
}
