using System;
using System.Collections.Generic;
using System.Linq;

namespace PackRat.Storage;

/// <summary>
/// Owns the local player's definition-level backpack favorites. The collection is kept separate
/// from live item instances so every stack of a favorited definition is treated consistently.
/// </summary>
public static class BackpackFavorites
{
    private static readonly HashSet<string> FavoriteDefinitionIds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static bool IsFavorite(string definitionId)
    {
        return !string.IsNullOrWhiteSpace(definitionId) && FavoriteDefinitionIds.Contains(definitionId.Trim());
    }

    public static bool Toggle(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return false;

        var normalizedId = definitionId.Trim();
        if (!FavoriteDefinitionIds.Add(normalizedId))
        {
            FavoriteDefinitionIds.Remove(normalizedId);
            return false;
        }

        return true;
    }

    public static void SetFavorites(IEnumerable<string> definitionIds)
    {
        FavoriteDefinitionIds.Clear();
        if (definitionIds == null)
            return;

        foreach (var definitionId in definitionIds)
        {
            if (!string.IsNullOrWhiteSpace(definitionId))
                FavoriteDefinitionIds.Add(definitionId.Trim());
        }
    }

    public static List<string> GetSavedFavoriteIds()
    {
        return FavoriteDefinitionIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
