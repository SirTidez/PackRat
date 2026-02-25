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
    private readonly MelonPreferences_Entry<FullRank>[] _tierUnlockRankEntries;
    private readonly MelonPreferences_Entry<int>[] _tierSlotCountEntries;

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

        _tierUnlockRankEntries = new MelonPreferences_Entry<FullRank>[BackpackTiers.Length];
        _tierSlotCountEntries = new MelonPreferences_Entry<int>[BackpackTiers.Length];
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
        }

        TierUnlockRanks = new FullRank[BackpackTiers.Length];
        TierSlotCounts = new int[BackpackTiers.Length];
    }

    public KeyCode ToggleKey { get; set; }
    public FullRank[] TierUnlockRanks { get; internal set; }
    public int[] TierSlotCounts { get; internal set; }

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
        for (var i = 0; i < BackpackTiers.Length; i++)
        {
            var rank = _tierUnlockRankEntries[i].Value;
            TierUnlockRanks[i] = new FullRank(rank.Rank, Math.Clamp(rank.Tier, 1, 5));
            TierSlotCounts[i] = Math.Clamp(_tierSlotCountEntries[i].Value, 1, PlayerBackpack.MaxStorageSlots);
        }
    }

    /// <summary>
    /// Persists current property values back to the preferences file.
    /// </summary>
    public void Save()
    {
        _toggleKeyEntry.Value = ToggleKey;
        for (var i = 0; i < BackpackTiers.Length; i++)
        {
            _tierUnlockRankEntries[i].Value = new FullRank(TierUnlockRanks[i].Rank, Math.Clamp(TierUnlockRanks[i].Tier, 1, 5));
            _tierSlotCountEntries[i].Value = Math.Clamp(TierSlotCounts[i], 1, PlayerBackpack.MaxStorageSlots);
        }
        MelonPreferences.Save();
    }
}
