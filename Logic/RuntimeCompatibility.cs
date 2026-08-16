namespace PackRat.Logic;

/// <summary>
/// Builds candidate runtime type names without depending on either Schedule I assembly.
/// </summary>
public static class RuntimeCompatibility
{
    /// <summary>
    /// Returns whether an optional runtime hook resolved at least one concrete target.
    /// </summary>
    public static bool HasResolvedTargets<T>(IEnumerable<T> targets)
    {
        return targets != null && targets.Any(target => target != null);
    }

    /// <summary>
    /// Expands managed Schedule One type names with their IL2CPP equivalents while preserving order.
    /// </summary>
    public static IReadOnlyList<string> ExpandScheduleOneTypeNames(params string[] typeNames)
    {
        var expanded = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (typeNames == null)
            return expanded;

        foreach (var typeName in typeNames)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                continue;

            Add(typeName);
            if (typeName.StartsWith("ScheduleOne.", StringComparison.Ordinal))
                Add("Il2Cpp" + typeName);
        }

        return expanded;

        void Add(string candidate)
        {
            if (seen.Add(candidate))
                expanded.Add(candidate);
        }
    }
}
