using PackRat.Config;
using PackRat.Patches;

#if MONO
using ScheduleOne.ItemFramework;
#else
using Il2CppScheduleOne.ItemFramework;
#endif

namespace PackRat.Routing;

/// <summary>
/// Decides whether a quick-moved item should prefer the local player's backpack. Routing is
/// intentionally limited to safe resource categories and only changes target selection; the
/// game still performs its native quick-move transaction.
/// </summary>
public static class SmartRoutingManager
{
    /// <summary>
    /// Whether the local player has opted into category-aware quick-move routing.
    /// </summary>
    public static bool IsEnabled => Configuration.Instance.EnableSmartRouting;

    /// <summary>
    /// Returns whether a quick-moved item should be directed into the backpack first.
    /// </summary>
    public static bool ShouldPreferBackpack(ItemInstance item)
    {
        if (item == null || !IsEnabled)
            return false;

        return StorageMenuPatch.GetItemCategory(item) switch
        {
            "Products" => Configuration.Instance.RouteProducts,
            "Seeds" => Configuration.Instance.RouteSeeds,
            "Mixers" => Configuration.Instance.RouteMixers,
            "Reagents" => Configuration.Instance.RouteReagents,
            _ => false
        };
    }
}
