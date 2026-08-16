namespace PackRat.Logic;

/// <summary>
/// Builds candidate runtime type names without depending on either Schedule I assembly.
/// </summary>
public static class RuntimeCompatibility
{
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
