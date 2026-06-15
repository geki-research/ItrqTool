using System.Text.RegularExpressions;

namespace ItrqTool.Tasks.QuestionnaireValidation.Parsing;

/// <summary>
/// Verbatim port of CLQ_v01's <c>InternalClqPrefixParser</c> — the
/// "number embedded in the text-column prefix" scheme. This is a
/// per-version concern: the shared <see cref="QuestionParser"/> does NOT
/// call it; a per-version record factory does (RLQ reads the number from
/// its own column; GD is multi-row). Behaviour is unchanged from v01.
/// </summary>
public static class QuestionNumberParser
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
