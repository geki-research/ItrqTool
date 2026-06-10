using System.Text.RegularExpressions;
using ItrqTool.Domain.Validation;

namespace ItrqTool.Tasks.ControlLevelQuestionValidation;

public sealed record SectionDefinition(int SectionRow, int FirstQuestionRow, int LastQuestionRow);

public sealed class ControlLevelQuestionValidationV01Config
{
    public string TextColumn { get; init; } = "";
    public string GuidanceColumn { get; init; } = "";
    public string PreviousAnswerColumn { get; init; } = "";
    public string AnswerColumn { get; init; } = "";
    public string StrengthsColumn { get; init; } = "";
    public string WeaknessesColumn { get; init; } = "";
    public string ProvidedByColumn { get; init; } = "";
    public string XrefIdColumn { get; init; } = "";

    public string SheetName { get; init; } = "";
    public IReadOnlyList<int> ChapterRows { get; init; } = [];
    public IReadOnlyList<string> SectionRows { get; init; } = [];

    public IReadOnlyList<string> AllowedAnswers { get; init; } = [];
    public int DeviationThreshold { get; init; }

    public IReadOnlyDictionary<string, FindingEvaluation> SeverityOverrides { get; init; }
        = new Dictionary<string, FindingEvaluation>();

    public IReadOnlyList<SectionDefinition> ParsedSections =>
        SectionRows.Select(ParseEntry).ToList();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        ValidateColumnLetter(nameof(TextColumn), TextColumn, errors);
        ValidateColumnLetter(nameof(GuidanceColumn), GuidanceColumn, errors);
        ValidateColumnLetter(nameof(PreviousAnswerColumn), PreviousAnswerColumn, errors);
        ValidateColumnLetter(nameof(AnswerColumn), AnswerColumn, errors);
        ValidateColumnLetter(nameof(StrengthsColumn), StrengthsColumn, errors);
        ValidateColumnLetter(nameof(WeaknessesColumn), WeaknessesColumn, errors);
        ValidateColumnLetter(nameof(ProvidedByColumn), ProvidedByColumn, errors);
        ValidateColumnLetter(nameof(XrefIdColumn), XrefIdColumn, errors);

        if (string.IsNullOrWhiteSpace(SheetName))
            errors.Add("SheetName must not be empty.");

        if (AllowedAnswers.Count == 0)
            errors.Add("AllowedAnswers must not be empty.");

        if (DeviationThreshold <= 0)
            errors.Add("DeviationThreshold must be greater than 0.");

        return errors;
    }

    private static readonly Regex ColumnLetterPattern = new(@"^[A-Za-z]+$", RegexOptions.Compiled);

    private static void ValidateColumnLetter(string propertyName, string value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"{propertyName} must not be empty.");
        else if (!ColumnLetterPattern.IsMatch(value))
            errors.Add($"{propertyName} must be a valid column letter (e.g. 'A', 'AA').");
    }

    internal static SectionDefinition ParseEntry(string entry)
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
