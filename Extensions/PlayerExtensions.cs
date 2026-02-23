#if MONO
using ScheduleOne.PlayerScripts;
using ScheduleOne.Storage;
#else
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Storage;
#endif

namespace PackRat.Extensions;

/// <summary>
/// Extension methods for the <see cref="Player"/> class.
/// </summary>
public static class PlayerExtensions
{
    /// <summary>
    /// Retrieves the <see cref="StorageEntity"/> component used as the player's backpack.
    /// </summary>
    /// <param name="player">The player instance.</param>
    /// <returns>The backpack <see cref="StorageEntity"/> attached to the player's GameObject.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="player"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if no <see cref="StorageEntity"/> component is found.</exception>
    public static StorageEntity GetBackpackStorage(this Player player)
    {
        if (player == null)
            throw new ArgumentNullException(nameof(player));

        var backpackStorage = player.gameObject.GetComponent<StorageEntity>();
        if (backpackStorage == null)
            throw new InvalidOperationException("Player does not have a BackpackStorage component.");

        return backpackStorage;
    }
}
