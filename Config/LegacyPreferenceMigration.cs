using MelonLoader;

namespace PackRat.Config;

/// <summary>
/// Moves preferences out of PackRat's original all-in-one category after MelonPreferences loads.
/// Legacy entries are registered only long enough to deserialize their existing values.
/// </summary>
internal sealed class LegacyPreferenceMigration
{
    private readonly MelonPreferences_Category _legacyCategory;
    private readonly List<Func<bool>> _pendingMoves = [];

    public LegacyPreferenceMigration(MelonPreferences_Category legacyCategory)
    {
        _legacyCategory = legacyCategory;
    }

    /// <summary>
    /// Creates a preference in its organized category and registers a hidden probe for the same
    /// identifier in the original PackRat category. The probe is removed before preferences save.
    /// </summary>
    public MelonPreferences_Entry<T> CreateMovedEntry<T>(
        MelonPreferences_Category targetCategory,
        string identifier,
        T defaultValue,
        string displayName = null,
        string description = null,
        bool isHidden = false,
        bool dontSaveDefault = false)
    {
        var legacyEntry = _legacyCategory.CreateEntry(
            identifier,
            defaultValue,
            displayName,
            description,
            true,
            true
        );
        var targetEntry = targetCategory.CreateEntry(
            identifier,
            defaultValue,
            displayName,
            description,
            isHidden,
            dontSaveDefault
        );

        _pendingMoves.Add(() =>
        {
            var legacyValue = legacyEntry.Value;
            if (!_legacyCategory.DeleteEntry(identifier))
                return false;

            targetEntry.Value = legacyValue;
            return true;
        });

        return targetEntry;
    }

    /// <summary>
    /// Applies all registered moves. Re-running against an already migrated file is a no-op because
    /// the original PackRat entries no longer exist in the TOML document.
    /// </summary>
    public int Apply()
    {
        var migratedCount = 0;
        foreach (var move in _pendingMoves)
        {
            if (move())
                migratedCount++;
        }

        _pendingMoves.Clear();
        return migratedCount;
    }
}
