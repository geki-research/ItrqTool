namespace ItrqTool.Tasks.GeneralDataDiff;

public static class DvDisplayFormatter
{
    public static string FormatDv(string? dvType, string? dvFormula)
    {
        if (dvType is null) return "—";

        if (!string.Equals(dvType, "List", StringComparison.OrdinalIgnoreCase))
            return dvType;

        if (dvFormula is null) return "List";

        // Inline list: doesn't start with "=" and doesn't contain "$"
        if (!dvFormula.StartsWith("=") && !dvFormula.Contains('$'))
        {
            var stripped = dvFormula.Trim().Trim('"');
            var items = stripped.Split(',')
                                .Select(x => x.Trim())
                                .Where(x => x.Length > 0);
            return "List: " + string.Join(" | ", items);
        }

        return "List: " + dvFormula;
    }
}
