namespace ItrqTool.Tasks.TemplateDiff;

/// <summary>
/// Deserialized from a JSON config file. Tells the parser which
/// rows in the sheet are chapter headers, section headers, and
/// (by exclusion) question rows.
/// </summary>
public sealed class ControlLevelQuestionsConfig
{
    public string SheetName { get; init; } = "Control Level Questions";
    public string TextColumn { get; init; } = "C";
    public string InputColumn { get; init; } = "D";
    // 1-based row numbers of chapter header rows
    public IReadOnlyList<int> ChapterRows { get; init; } = [];
    // 1-based row numbers of section header rows
    public IReadOnlyList<int> SectionRows { get; init; } = [];
}
