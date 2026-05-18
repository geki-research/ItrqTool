namespace ItrqTool.Tasks.TemplateDiff;

public sealed record SectionDefinition(int SectionRow, int FirstQuestionRow, int LastQuestionRow);

public sealed class ControlLevelQuestionsConfig
{
    public string SheetName { get; init; } = "Control Level Questions";
    public string TextColumn { get; init; } = "C";
    public string InputColumn { get; init; } = "D";
    public IReadOnlyList<int> ChapterRows { get; init; } = [];
    // Each entry: "<sectionRow>:<firstQuestionRow>-<lastQuestionRow>"
    public IReadOnlyList<string> SectionRows { get; init; } = [];

    public IReadOnlyList<SectionDefinition> ParsedSections =>
        SectionRows.Select(ParseEntry).ToList();

    private static SectionDefinition ParseEntry(string entry)
    {
        var colonIdx = entry.IndexOf(':');
        if (colonIdx < 1)
            throw new FormatException(
                $"Section entry '{entry}' must be in format '<sectionRow>:<first>-<last>'.");

        var dashIdx = entry.IndexOf('-', colonIdx + 1);
        if (dashIdx < 0)
            throw new FormatException(
                $"Section entry '{entry}' must be in format '<sectionRow>:<first>-<last>'.");

        if (!int.TryParse(entry[..colonIdx], out int sectionRow) || sectionRow <= 0)
            throw new FormatException(
                $"Section entry '{entry}': sectionRow must be a positive integer.");

        if (!int.TryParse(entry[(colonIdx + 1)..dashIdx], out int first) || first <= 0)
            throw new FormatException(
                $"Section entry '{entry}': firstQuestionRow must be a positive integer.");

        if (!int.TryParse(entry[(dashIdx + 1)..], out int last) || last <= 0)
            throw new FormatException(
                $"Section entry '{entry}': lastQuestionRow must be a positive integer.");

        if (first <= sectionRow)
            throw new FormatException(
                $"Section entry '{entry}': firstQuestionRow ({first}) must be greater than sectionRow ({sectionRow}).");

        if (last < first)
            throw new FormatException(
                $"Section entry '{entry}': lastQuestionRow ({last}) must not be less than firstQuestionRow ({first}).");

        return new SectionDefinition(sectionRow, first, last);
    }
}
