namespace ItrqTool.Tasks.Shared;

public static class DvComparer
{
    public static bool IsDvChanged(
        string? oldType, string? oldFormula,
        string? newType, string? newFormula)
    {
        if (!string.Equals(oldType, newType, StringComparison.Ordinal))
            return true;

        if (oldType is null) return false;

        if (IsDvList(oldType))
            return !ListValuesEqual(oldFormula, newFormula);

        return false;
    }

    private static bool IsDvList(string? dvType)
        => string.Equals(dvType, "List", StringComparison.OrdinalIgnoreCase);

    private static bool ListValuesEqual(string? oldFormula, string? newFormula)
    {
        if (oldFormula is null && newFormula is null) return true;
        if (oldFormula is null || newFormula is null) return false;

        bool oldInline = IsInlineList(oldFormula);
        bool newInline = IsInlineList(newFormula);

        if (oldInline && newInline)
        {
            var oldItems = ParseInlineList(oldFormula);
            var newItems = ParseInlineList(newFormula);
            return oldItems.SequenceEqual(newItems);
        }

        return string.Equals(oldFormula, newFormula, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInlineList(string formula)
        => !formula.StartsWith("=") && !formula.Contains('$');

    private static IReadOnlyList<string> ParseInlineList(string formula)
    {
        var s = formula.Trim().Trim('"');
        return s.Split(',')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
    }
}
