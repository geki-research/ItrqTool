namespace ItrqTool.Tasks.GeneralDataDiff;

public sealed record QuestionDefinition(int FirstRow, int RowSpan);

public sealed record SectionDefinition(int SectionRow, IReadOnlyList<QuestionDefinition> Questions);

/// <summary>
/// Configuration for the General Data sheet parser. Mirrors RiskLevelQuestionsConfig but
/// supports multiple answer columns and a richer SectionRows format that explicitly
/// enumerates each question's first row and rowspan within a section.
/// </summary>
public sealed class GeneralDataConfig
{
    public string SheetName { get; init; } = "General Data";
    public string NumberColumn { get; init; } = "B";
    public string TextColumn { get; init; } = "C";
    public IReadOnlyList<string> AnswerColumns { get; init; } = ["D", "E", "F"];
    public string ExplanationColumn { get; init; } = "G";

    /// <summary>
    /// Each entry: "&lt;sectionRow&gt;:&lt;startRow&gt;(&lt;rowspan&gt;), &lt;startRow&gt;(&lt;rowspan&gt;), ..."
    /// Example: "13:14(1), 15(1), 16(1), 17(1), 18(3), 21(2)"
    ///   → section header on row 13;
    ///     question 1 spans row 14 only;
    ///     question 2 spans row 15 only;
    ///     ...
    ///     question 5 spans rows 18, 19, 20;
    ///     question 6 spans rows 21, 22.
    /// </summary>
    public IReadOnlyList<string> SectionRows { get; init; } = [];

    public IReadOnlyList<SectionDefinition> ParsedSections =>
        SectionRows.Select(ParseEntry).ToList();

    private static SectionDefinition ParseEntry(string entry)
    {
        var colonIdx = entry.IndexOf(':');
        if (colonIdx < 1)
            throw new FormatException(
                $"Section entry '{entry}' must be in format '<sectionRow>:<startRow>(<rowspan>), ...'.");

        if (!int.TryParse(entry[..colonIdx].Trim(), out int sectionRow) || sectionRow <= 0)
            throw new FormatException(
                $"Section entry '{entry}': sectionRow must be a positive integer.");

        var questionsPart = entry[(colonIdx + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(questionsPart))
            throw new FormatException(
                $"Section entry '{entry}': must contain at least one question definition.");

        var questionTokens = questionsPart.Split(
            ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (questionTokens.Length == 0)
            throw new FormatException(
                $"Section entry '{entry}': must contain at least one question definition.");

        var questions = new List<QuestionDefinition>(questionTokens.Length);
        foreach (var token in questionTokens)
        {
            var openIdx = token.IndexOf('(');
            int startRow, rowspan;

            if (openIdx < 0)
            {
                // Bare form: "21" → startRow=21, rowspan=1
                if (!int.TryParse(token, out startRow) || startRow <= 0)
                    throw new FormatException(
                        $"Section entry '{entry}': question startRow must be a positive integer (token '{token}').");
                rowspan = 1;
            }
            else
            {
                var closeIdx = token.IndexOf(')');
                if (openIdx < 1 || closeIdx <= openIdx + 1 || closeIdx != token.Length - 1)
                    throw new FormatException(
                        $"Section entry '{entry}': question token '{token}' must be in format '<startRow>(<rowspan>)'.");

                if (!int.TryParse(token[..openIdx].Trim(), out startRow) || startRow <= 0)
                    throw new FormatException(
                        $"Section entry '{entry}': question startRow must be a positive integer (token '{token}').");

                if (!int.TryParse(token[(openIdx + 1)..closeIdx].Trim(), out rowspan) || rowspan <= 0)
                    throw new FormatException(
                        $"Section entry '{entry}': question rowspan must be a positive integer (token '{token}').");
            }

            if (startRow <= sectionRow)
                throw new FormatException(
                    $"Section entry '{entry}': question startRow ({startRow}) must be greater than sectionRow ({sectionRow}).");

            questions.Add(new QuestionDefinition(startRow, rowspan));
        }

        // Validate no overlap (and no out-of-order) within section.
        for (int i = 0; i < questions.Count - 1; i++)
        {
            int currentLastRow = questions[i].FirstRow + questions[i].RowSpan - 1;
            int nextFirstRow = questions[i + 1].FirstRow;
            if (currentLastRow >= nextFirstRow)
                throw new FormatException(
                    $"Section entry '{entry}': question at row {questions[i].FirstRow} (rowspan {questions[i].RowSpan}, last row {currentLastRow}) " +
                    $"overlaps with or precedes next question at row {nextFirstRow}.");
        }

        return new SectionDefinition(sectionRow, questions);
    }
}
