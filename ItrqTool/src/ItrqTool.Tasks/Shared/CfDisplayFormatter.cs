namespace ItrqTool.Tasks.Shared;

public static class CfDisplayFormatter
{
    private static readonly Dictionary<string, string> CfOperatorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Equal"]             = "equal to",
        ["NotEqual"]          = "not equal to",
        ["GreaterThan"]       = "greater than",
        ["LessThan"]          = "less than",
        ["EqualOrGreaterThan"] = "greater than or equal to",
        ["EqualOrLessThan"]   = "less than or equal to",
        ["Between"]           = "between",
        ["NotBetween"]        = "not between",
    };

    public static string Format(string? cfType, string? cfOperator, string? cfValue, string? cfValue2)
    {
        if (cfType is null) return "—";

        if (string.Equals(cfType, "CellIs", StringComparison.OrdinalIgnoreCase))
        {
            var opWords = cfOperator is not null && CfOperatorWords.TryGetValue(cfOperator, out var ow)
                          ? ow : (cfOperator ?? string.Empty);

            bool rangeable = string.Equals(cfOperator, "Between", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(cfOperator, "NotBetween", StringComparison.OrdinalIgnoreCase);

            var valuePart = rangeable
                ? $"{cfValue} and {cfValue2}"
                : cfValue ?? string.Empty;

            return $"{opWords} {valuePart}";
        }

        if (string.Equals(cfType, "Expression", StringComparison.OrdinalIgnoreCase))
            return "Formula: " + cfValue;

        return cfType;
    }
}
