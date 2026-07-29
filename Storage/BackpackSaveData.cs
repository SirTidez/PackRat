using System.Collections.Generic;

namespace PackRat.Storage;

/// <summary>
/// Persisted backpack state: item contents and the equipped backpack tier.
/// Serialized to the Backpack subfile and network payload.
/// </summary>
public sealed class BackpackSaveData
{
    /// <summary>JSON string from <see cref="ItemSet"/> for backpack slot contents.</summary>
    public string Contents { get; set; }

    /// <summary>Currently equipped backpack tier index (0-4), or -1 if none.</summary>
    public int EquippedTierIndex { get; set; } = -1;

    /// <summary>
    /// Legacy field kept for backward compatibility with older saves. When <see cref="EquippedTierIndex"/> is unset,
    /// this value is treated as the equipped tier on load.
    /// </summary>
    public int HighestPurchasedTierIndex { get; set; } = -1;

    /// <summary>
    /// Definition IDs marked as favorites by this player. Missing data from older saves is
    /// intentionally treated as an empty collection.
    /// </summary>
    public List<string> FavoriteDefinitionIds { get; set; } = new List<string>();
}
