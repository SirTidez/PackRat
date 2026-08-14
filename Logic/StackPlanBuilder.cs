namespace PackRat.Logic;

public sealed class StackSlot
{
    public StackSlot(int slotIndex, string compatibilityKey, int quantity, int capacity, bool isProtected)
    {
        SlotIndex = slotIndex;
        CompatibilityKey = compatibilityKey;
        Quantity = quantity;
        Capacity = capacity;
        IsProtected = isProtected;
    }

    public int SlotIndex { get; }
    public string CompatibilityKey { get; }
    public int Quantity { get; }
    public int Capacity { get; }
    public bool IsProtected { get; }
}

public sealed class StackPair
{
    public StackPair(int left, int right) { Left = left; Right = right; }
    public int Left { get; }
    public int Right { get; }
}

public sealed class StackTransfer
{
    public StackTransfer(int sourceSlotIndex, int targetSlotIndex, int quantity)
    {
        SourceSlotIndex = sourceSlotIndex;
        TargetSlotIndex = targetSlotIndex;
        Quantity = quantity;
    }

    public int SourceSlotIndex { get; }
    public int TargetSlotIndex { get; }
    public int Quantity { get; }
}

public sealed class StackAssignment
{
    public StackAssignment(int slotIndex, int? sourceSlotIndex)
    {
        SlotIndex = slotIndex;
        SourceSlotIndex = sourceSlotIndex;
    }

    public int SlotIndex { get; }
    public int? SourceSlotIndex { get; }
}

public sealed class StackPlan
{
    public StackPlan(IReadOnlyList<StackTransfer> transfers,
        IReadOnlyList<StackAssignment> compaction, IReadOnlyList<StackPair> comparisons)
    {
        Transfers = transfers;
        Compaction = compaction;
        Comparisons = comparisons;
    }

    public IReadOnlyList<StackTransfer> Transfers { get; }
    public IReadOnlyList<StackAssignment> Compaction { get; }
    public IReadOnlyList<StackPair> Comparisons { get; }
}

public static class StackPlanBuilder
{
    public static StackPlan Build(IReadOnlyList<StackSlot> slots)
    {
        var comparisons = new List<StackPair>();
        var transfers = new List<StackTransfer>();
        var remaining = slots.ToDictionary(slot => slot.SlotIndex, slot => slot.Quantity);

        foreach (var bucket in slots.Where(slot => !slot.IsProtected && slot.Quantity > 0)
                     .GroupBy(slot => slot.CompatibilityKey, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var ordered = bucket.OrderBy(slot => slot.SlotIndex).ToArray();
            for (var targetIndex = 0; targetIndex < ordered.Length; targetIndex++)
            {
                var target = ordered[targetIndex];
                for (var sourceIndex = targetIndex + 1; sourceIndex < ordered.Length; sourceIndex++)
                {
                    var capacity = target.Capacity - remaining[target.SlotIndex];
                    if (capacity <= 0)
                        break;

                    var source = ordered[sourceIndex];
                    if (remaining[source.SlotIndex] <= 0)
                        continue;

                    comparisons.Add(new StackPair(target.SlotIndex, source.SlotIndex));
                    var amount = Math.Min(capacity, remaining[source.SlotIndex]);
                    if (amount <= 0)
                        continue;

                    transfers.Add(new StackTransfer(source.SlotIndex, target.SlotIndex, amount));
                    remaining[target.SlotIndex] += amount;
                    remaining[source.SlotIndex] -= amount;
                }
            }
        }

        var movable = slots.Where(slot => !slot.IsProtected)
            .OrderBy(slot => slot.SlotIndex).ToArray();
        var survivors = movable.Where(slot => remaining[slot.SlotIndex] > 0)
            .Select(slot => slot.SlotIndex).ToArray();
        var compaction = new List<StackAssignment>();
        for (var index = 0; index < movable.Length; index++)
        {
            int? source = index < survivors.Length ? survivors[index] : null;
            if (source != movable[index].SlotIndex)
                compaction.Add(new StackAssignment(movable[index].SlotIndex, source));
        }

        return new StackPlan(transfers, compaction, comparisons);
    }
}
