using PackRat.Logic;
using Xunit;

namespace PackRat.Logic.Tests;

public sealed class RuntimeCompatibilityTests
{
    [Fact]
    public void ExpandScheduleOneTypeNames_IncludesManagedAndIl2CppNames()
    {
        var names = RuntimeCompatibility.ExpandScheduleOneTypeNames(
            "ScheduleOne.UI.Stations.ChemistryStationInterface");

        Assert.Equal(
            new[]
            {
                "ScheduleOne.UI.Stations.ChemistryStationInterface",
                "Il2CppScheduleOne.UI.Stations.ChemistryStationInterface"
            },
            names);
    }

    [Fact]
    public void ExpandScheduleOneTypeNames_PreservesOrderAndRemovesDuplicates()
    {
        var names = RuntimeCompatibility.ExpandScheduleOneTypeNames(
            "ScheduleOne.UI.Stations.CauldronInterface",
            "Il2CppScheduleOne.UI.Stations.CauldronInterface",
            "ScheduleOne.UI.Stations.CauldronInterface");

        Assert.Equal(
            new[]
            {
                "ScheduleOne.UI.Stations.CauldronInterface",
                "Il2CppScheduleOne.UI.Stations.CauldronInterface"
            },
            names);
    }
}
