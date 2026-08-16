namespace PackRat.Logic;

public sealed class AutoFillRequirement
{
    public AutoFillRequirement(string productId, int minimumQuality, int remainingUnits)
    {
        ProductId = productId;
        MinimumQuality = minimumQuality;
        RemainingUnits = remainingUnits;
    }

    public string ProductId { get; }
    public int MinimumQuality { get; }
    public int RemainingUnits { get; }
}

public sealed class AutoFillCandidate
{
    public AutoFillCandidate(string source, int sourceSlotIndex, string productId, int quality,
        int packageAmount, int availablePackages, bool isPackaged, bool isNativeAcceptable)
    {
        Source = source;
        SourceSlotIndex = sourceSlotIndex;
        ProductId = productId;
        Quality = quality;
        PackageAmount = packageAmount;
        AvailablePackages = availablePackages;
        IsPackaged = isPackaged;
        IsNativeAcceptable = isNativeAcceptable;
    }

    public string Source { get; }
    public int SourceSlotIndex { get; }
    public string ProductId { get; }
    public int Quality { get; }
    public int PackageAmount { get; }
    public int AvailablePackages { get; }
    public bool IsPackaged { get; }
    public bool IsNativeAcceptable { get; }
}

public sealed class AutoFillMove
{
    public AutoFillMove(string source, int sourceSlotIndex, int packageCount, int productUnits)
    {
        Source = source;
        SourceSlotIndex = sourceSlotIndex;
        PackageCount = packageCount;
        ProductUnits = productUnits;
    }

    public string Source { get; }
    public int SourceSlotIndex { get; }
    public int PackageCount { get; }
    public int ProductUnits { get; }
}

public sealed class AutoFillPlan
{
    public AutoFillPlan(IReadOnlyList<AutoFillMove> moves, int filledUnits, int oversuppliedUnits)
    {
        Moves = moves;
        FilledUnits = filledUnits;
        OversuppliedUnits = oversuppliedUnits;
    }

    public IReadOnlyList<AutoFillMove> Moves { get; }
    public int FilledUnits { get; }
    public int OversuppliedUnits { get; }
}

public static class AutoFillPlanner
{
    private static readonly Dictionary<string, int> SourcePriority = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PACK"] = 0,
        ["VEHICLE"] = 1,
        ["INVENTORY"] = 2
    };

    public static AutoFillPlan Plan(AutoFillRequirement requirement,
        IReadOnlyList<AutoFillCandidate> candidates)
    {
        var eligible = candidates
            .Where(candidate => candidate.IsPackaged && candidate.IsNativeAcceptable &&
                candidate.AvailablePackages > 0 && candidate.PackageAmount > 0 &&
                candidate.Quality >= requirement.MinimumQuality &&
                string.Equals(candidate.ProductId, requirement.ProductId,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.PackageAmount)
            .ThenBy(candidate => SourcePriority.TryGetValue(candidate.Source, out var priority)
                ? priority
                : int.MaxValue)
            .ThenBy(candidate => candidate.SourceSlotIndex)
            .ToArray();

        if (requirement.RemainingUnits <= 0 || eligible.Length == 0)
            return new AutoFillPlan(Array.Empty<AutoFillMove>(), 0, 0);

        // A package is only eligible when the selected packages exactly satisfy the
        // remaining requirement.  Trying larger targets here would permit an
        // oversized package to be moved just because it was the closest available
        // match, which can overfill the customer's order and mutate the UI before
        // the game can reject the transfer.
        var counts = new int[eligible.Length];
        var failedStates = new HashSet<long>();
        if (TryFillExact(eligible, 0, requirement.RemainingUnits, counts, failedStates))
        {
            var moves = eligible.Select((candidate, index) => new { candidate, count = counts[index] })
                .Where(entry => entry.count > 0)
                .Select(entry => new AutoFillMove(entry.candidate.Source,
                    entry.candidate.SourceSlotIndex, entry.count,
                    entry.count * entry.candidate.PackageAmount))
                .ToArray();
            var filled = moves.Sum(move => move.ProductUnits);
            return new AutoFillPlan(moves, filled, 0);
        }

        return new AutoFillPlan(Array.Empty<AutoFillMove>(), 0, 0);
    }

    private static bool TryFillExact(IReadOnlyList<AutoFillCandidate> candidates, int index,
        int remainingUnits, int[] counts, HashSet<long> failedStates)
    {
        if (remainingUnits == 0)
        {
            if (index < counts.Length)
                Array.Clear(counts, index, counts.Length - index);
            return true;
        }
        if (index >= candidates.Count || remainingUnits < 0)
            return false;

        var stateKey = ((long)index << 32) | (uint)remainingUnits;
        if (failedStates.Contains(stateKey))
            return false;

        var candidate = candidates[index];
        var maxCount = Math.Min(candidate.AvailablePackages,
            remainingUnits / candidate.PackageAmount);
        for (var count = maxCount; count >= 0; count--)
        {
            counts[index] = count;
            if (TryFillExact(candidates, index + 1,
                    remainingUnits - count * candidate.PackageAmount, counts, failedStates))
                return true;
        }

        counts[index] = 0;
        failedStates.Add(stateKey);
        return false;
    }
}
