using System.Text.RegularExpressions;

namespace ItrqTool.Tasks.ControlLevelQuestionValidation;

public static class InternalClqPrefixParser
{
    private static readonly Regex PrefixPattern =
        new(@"^\d+\.\d+\)?\s*", RegexOptions.Compiled);

    private static readonly Regex DeeperPrefixPattern =
        new(@"^\d+(\.\d+){2,}", RegexOptions.Compiled);

    public static string? ExtractNumber(string text)
    {
        text = text.Trim();
        var m = PrefixPattern.Match(text);
        if (!m.Success) return null;
        return m.Value.TrimEnd().TrimEnd(')').Trim();
    }

    public static string StripPrefix(string text)
    {
        text = text.Trim();
        var m = PrefixPattern.Match(text);
        return m.Success ? text[m.Length..].Trim() : text;
    }

    public static bool HasUnsupportedDeeperPrefix(string text) =>
        DeeperPrefixPattern.IsMatch(text.Trim());
}
