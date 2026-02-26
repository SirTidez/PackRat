namespace PackRat.Storage;

/// <summary>
/// Persisted backpack state: item contents and highest purchased tier index.
/// Serialized to the Backpack subfile and network payload.
/// </summary>
public sealed class BackpackSaveData
{
    /// <summary>JSON string from <see cref="ItemSet"/> for backpack slot contents.</summary>
    public string Contents { get; set; }

    /// <summary>Highest backpack tier index the player has purchased (0-4), or -1 if none.</summary>
    public int HighestPurchasedTierIndex { get; set; } = -1;
}
