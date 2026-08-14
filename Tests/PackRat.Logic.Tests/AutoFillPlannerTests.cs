using PackRat.Logic;
using Xunit;

namespace PackRat.Logic.Tests;

public sealed class AutoFillPlannerTests
{
    [Fact]
    public void FiveUnitsPreferFiveBaggiesOverFiveJars()
    {
        var candidates = new[]
        {
            new AutoFillCandidate("PACK", 0, "product", 2, 5, 5, true, true),
            new AutoFillCandidate("PACK", 1, "product", 2, 1, 5, true, true)
        };

        var plan = AutoFillPlanner.Plan(new AutoFillRequirement("product", 2, 5), candidates);

        var move = Assert.Single(plan.Moves);
        Assert.Equal(1, move.SourceSlotIndex);
        Assert.Equal(5, move.PackageCount);
        Assert.Equal(5, move.ProductUnits);
        Assert.Equal(0, plan.OversuppliedUnits);
    }

    [Fact]
    public void FiveUnitsUseOneJarWhenNoBaggiesExist()
    {
        var candidates = new[]
        {
            new AutoFillCandidate("PACK", 0, "product", 2, 5, 5, true, true)
        };

        var plan = AutoFillPlanner.Plan(new AutoFillRequirement("product", 2, 5), candidates);

        var move = Assert.Single(plan.Moves);
        Assert.Equal(1, move.PackageCount);
        Assert.Equal(5, move.ProductUnits);
    }

    [Fact]
    public void RejectsUnpackagedLockedWrongQualityAndNativeRejectedCandidates()
    {
        var candidates = new[]
        {
            new AutoFillCandidate("PACK", 0, "product", 2, 1, 5, false, true),
            new AutoFillCandidate("PACK", 1, "product", 2, 1, 5, true, false),
            new AutoFillCandidate("PACK", 2, "product", 1, 1, 5, true, true),
            new AutoFillCandidate("PACK", 3, "product", 2, 1, 5, true, true)
        };

        var plan = AutoFillPlanner.Plan(new AutoFillRequirement("product", 2, 5), candidates);

        Assert.All(plan.Moves, move => Assert.Equal(3, move.SourceSlotIndex));
    }
}
