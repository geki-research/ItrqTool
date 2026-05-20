using System.Text.RegularExpressions;

namespace ItrqTool.Tasks.TemplateDiff;

public sealed record AuditQuestion(
    string  ChapterName,
    string  SectionName,
    string  QuestionText,      // prefix stripped
    string  OriginalText,      // raw cell text
    string? QuestionNumber,    // e.g. "1.2", null if no prefix
    int     RowNumber,
    string? DvType,
    string? DvFormula,         // raw DV formula from ExcelCellStructure.DataValidationFormula
    string? CfOperator
)
{
    private static readonly Regex PrefixPattern =
        new(@"^\d+\.\d+\)?\s*", RegexOptions.Compiled);

    public static string StripPrefix(string text)
    {
        text = text.Trim();
        var m = PrefixPattern.Match(text);
        return m.Success ? text[m.Length..].Trim() : text;
    }

    // Returns "1.2" from "1.2) Some text", or null if no prefix.
    public static string? ExtractNumber(string text)
    {
        text = text.Trim();
        var m = PrefixPattern.Match(text);
        if (!m.Success) return null;
        // match is e.g. "1.2) " — strip the closing ")" and whitespace
        return m.Value.TrimEnd().TrimEnd(')').Trim();
    }
}
