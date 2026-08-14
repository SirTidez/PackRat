using PackRat.Logic;
using Xunit;

namespace PackRat.Logic.Tests;

public sealed class StackPlanBuilderTests
{
    [Fact]
    public void DoesNotCompareSlotsAcrossCompatibilityBuckets()
    {
        var slots = new[]
        {
            new StackSlot(0, "weed|standard|baggie", 10, 20, false),
            new StackSlot(1, "weed|standard|baggie", 5, 20, false),
            new StackSlot(2, "coke|standard|baggie", 5, 20, false)
        };

        var plan = StackPlanBuilder.Build(slots);

        var transfer = Assert.Single(plan.Transfers);
        Assert.Equal(1, transfer.SourceSlotIndex);
        Assert.Equal(0, transfer.TargetSlotIndex);
        Assert.Equal(5, transfer.Quantity);
        Assert.DoesNotContain(plan.Comparisons, pair => pair.Left == 2 || pair.Right == 2);
    }

    [Fact]
    public void ProtectedSlotsNeverMoveOrReceiveItems()
    {
        var slots = new[]
        {
            new StackSlot(0, "weed|standard|baggie", 10, 20, true),
            new StackSlot(1, "weed|standard|baggie", 5, 20, false)
        };

        var plan = StackPlanBuilder.Build(slots);

        Assert.Empty(plan.Transfers);
        Assert.Empty(plan.Compaction);
    }

    [Fact]
    public void MoveOrderIsDeterministicByTargetThenSourceSlot()
    {
        var slots = new[]
        {
            new StackSlot(0, "weed|standard|baggie", 10, 20, false),
            new StackSlot(1, "weed|standard|baggie", 4, 20, false),
            new StackSlot(2, "weed|standard|baggie", 3, 20, false)
        };

        var first = StackPlanBuilder.Build(slots);
        var second = StackPlanBuilder.Build(slots);

        Assert.Equal(
            first.Transfers.Select(x => (x.SourceSlotIndex, x.TargetSlotIndex, x.Quantity)),
            second.Transfers.Select(x => (x.SourceSlotIndex, x.TargetSlotIndex, x.Quantity)));
    }
}
