using System.Text.RegularExpressions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Clq;

namespace ItrqTool.Tasks.ControlLevelQuestionValidationV01;

// CLQ_v01 config. Two notable shapes:
//   (a) ChapterRows is IReadOnlyList<string> (not int) — LayoutParser.Parse parses each
//       entry with int.TryParse; the production JSON carries the rows as strings.
//   (b) it implements IClqBaselineConfig (the version-neutral baseline read-surface).
// There is NO ParsedSections property: sections derive at run via LayoutParser, exactly
// like v02. Validate() covers the v01 structural rules EXCEPT the section-format check
// (now surfaced at run by LayoutParser.Parse → FormatException → task catch) and the
// override-key check (now in ValidationPipeline.Run) — i.e. the v02 config's Validate()
// shape minus the answer-stability rules.
public sealed class ClqV01Config : IClqBaselineConfig
{
    public string TextColumn { get; init; } = "";
    public string GuidanceColumn { get; init; } = "";
    public string PreviousAnswerColumn { get; init; } = "";
    public string PreviousExplanationColumn { get; init; } = "";
    public string AnswerColumn { get; init; } = "";
    public string StrengthsColumn { get; init; } = "";
    public string WeaknessesColumn { get; init; } = "";
    public string ProvidedByColumn { get; init; } = "";
    public string XrefIdColumn { get; init; } = "";

    public string SheetName { get; init; } = "";
    public IReadOnlyList<string> ChapterRows { get; init; } = [];
    public IReadOnlyList<string> SectionRows { get; init; } = [];

    public IReadOnlyList<string> AllowedAnswers { get; init; } = [];
    public int DeviationThreshold { get; init; }

    public IReadOnlyDictionary<string, FindingEvaluation> SeverityOverrides { get; init; }
        = new Dictionary<string, FindingEvaluation>();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        ValidateColumnLetter(nameof(TextColumn), TextColumn, errors);
        ValidateColumnLetter(nameof(GuidanceColumn), GuidanceColumn, errors);
        ValidateColumnLetter(nameof(PreviousAnswerColumn), PreviousAnswerColumn, errors);
        ValidateColumnLetter(nameof(PreviousExplanationColumn), PreviousExplanationColumn, errors);
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
}
