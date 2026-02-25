#if MONO
using ScheduleOne.Levelling;
#else
using Il2CppScheduleOne.Levelling;
#endif

namespace PackRat.Config;

/// <summary>
/// Immutable data record describing one backpack tier.
/// Slot count and unlock rank are configurable by the host; <see cref="HasPoliceSearch"/> is hardcoded.
/// </summary>
public sealed class BackpackTierDefinition
{
    /// <summary>The display name for this backpack tier.</summary>
    public string Name { get; }

    /// <summary>The default number of storage slots for this tier.</summary>
    public int DefaultSlotCount { get; }

    /// <summary>
    /// Whether police body searches include the backpack at this tier.
    /// This is hardcoded per tier and is not configurable.
    /// </summary>
    public bool HasPoliceSearch { get; }

    /// <summary>The default rank required to unlock this tier.</summary>
    public FullRank DefaultUnlockRank { get; }

    public BackpackTierDefinition(string name, int defaultSlotCount, bool hasPoliceSearch, FullRank defaultUnlockRank)
    {
        Name = name;
        DefaultSlotCount = defaultSlotCount;
        HasPoliceSearch = hasPoliceSearch;
        DefaultUnlockRank = defaultUnlockRank;
    }
}
