using System.Globalization;

namespace ItrqTool.Tasks.Shared;

public static class DvDisplayFormatter
{
    private static readonly Dictionary<string, string> DvTypeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WholeNumber"] = "Whole number",
        ["Decimal"]     = "Decimal",
        ["Date"]        = "Date",
        ["Time"]        = "Time",
        ["TextLength"]  = "Text length",
    };

    private static readonly Dictionary<string, string> DvOperatorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EqualTo"]           = "equal to",
        ["NotEqualTo"]        = "not equal to",
        ["GreaterThan"]       = "greater than",
        ["LessThan"]          = "less than",
        ["EqualOrGreaterThan"] = "greater than or equal to",
        ["EqualOrLessThan"]   = "less than or equal to",
        ["Between"]           = "between",
        ["NotBetween"]        = "not between",
    };

    public static string FormatFull(string? dvType, string? dvOperator, string? dvFormula, string? dvFormula2)
    {
        if (dvType is null || string.Equals(dvType, "AnyValue", StringComparison.OrdinalIgnoreCase))
            return "—";

        if (string.Equals(dvType, "List", StringComparison.OrdinalIgnoreCase))
            return FormatList(dvFormula);

        if (string.Equals(dvType, "Custom", StringComparison.OrdinalIgnoreCase))
            return "Custom: " + dvFormula;

        var typeWords = DvTypeWords.TryGetValue(dvType, out var tw) ? tw : dvType;
        var opWords   = dvOperator is not null && DvOperatorWords.TryGetValue(dvOperator, out var ow)
                        ? ow : (dvOperator ?? string.Empty);

        bool isDate = string.Equals(dvType, "Date", StringComparison.OrdinalIgnoreCase);
        bool isTime = string.Equals(dvType, "Time", StringComparison.OrdinalIgnoreCase);
        bool rangeable = string.Equals(dvOperator, "Between", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(dvOperator, "NotBetween", StringComparison.OrdinalIgnoreCase);

        string valuePart = rangeable
            ? FormatValue(dvFormula, isDate, isTime) + " and " + FormatValue(dvFormula2, isDate, isTime)
            : FormatValue(dvFormula, isDate, isTime);

        return $"{typeWords}, {opWords}, {valuePart}";
    }

    // Renders a List DV: "List" when no formula, "List: A | B | C" for inline lists,
    // "List: <ref>" for range references. Shared by the List branch of FormatFull.
    private static string FormatList(string? dvFormula)
    {
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

    private static string FormatValue(string? raw, bool isDate, bool isTime)
    {
        if (raw is null) return string.Empty;
        if (!isDate && !isTime) return raw;

        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return raw;

        var dt = DateTime.FromOADate(d);
        if (isTime)
            return dt.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
