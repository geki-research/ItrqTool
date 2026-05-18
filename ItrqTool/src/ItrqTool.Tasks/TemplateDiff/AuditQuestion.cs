using System.Text.RegularExpressions;

namespace ItrqTool.Tasks.TemplateDiff;

public sealed record AuditQuestion(
    string ChapterName,
    string SectionName,
    string QuestionText,   // question-number prefix stripped
    string OriginalText,   // raw cell text including prefix
    int RowNumber,
    string? DvType,
    string? CfOperator
)
{
    private static readonly Regex PrefixPattern =
        new(@"^\d+\.\d+\)\s*", RegexOptions.Compiled);

    public static string StripPrefix(string text)
    {
        text = text.Trim();
        var m = PrefixPattern.Match(text);
        return m.Success ? text[m.Length..].Trim() : text;
    }
}
