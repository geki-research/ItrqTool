using System.Text.RegularExpressions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.GeneralDataValidationV01;

namespace ItrqTool.Tasks.GeneralDataValidationV02;

// GD_v02 config. Faithful copy of GdV01Config's shape with these column-letter values:
//   C=QuestionNumber, D=Text, E=Guidance, F=RequestedType, G=PreviousAnswer, H=Answer,
//   I=RequestedExplanation, J=PreviousExplanation, K=CurrentExplanation, L=MaterialChange,
//   M=HowExplanation (NEW), P=ProvidedBy (shifted from v01's O), R=XrefId (shifted from v01's Q).
// GdSectionSpec and DeviationThreshold/SeverityOverrides carry over verbatim from v01.
// The column insertion at M does NOT shift any ROW — section geometry is unchanged.
public sealed class GdV02Config
{
    public string QuestionNumberColumn { get; init; } = "";        // C
    public string TextColumn { get; init; } = "";                  // D
    public string GuidanceColumn { get; init; } = "";              // E
    public string RequestedTypeColumn { get; init; } = "";         // F
    public string PreviousAnswerColumn { get; init; } = "";        // G
    public string AnswerColumn { get; init; } = "";                // H
    public string RequestedExplanationColumn { get; init; } = "";  // I
    public string PreviousExplanationColumn { get; init; } = "";   // J
    public string CurrentExplanationColumn { get; init; } = "";    // K
    public string MaterialChangeColumn { get; init; } = "";        // L
    public string HowExplanationColumn { get; init; } = "";        // M (new in v02)
    public string ProvidedByColumn { get; init; } = "";            // P (shifted from v01's O)
    public string XrefIdColumn { get; init; } = "";                // R (shifted from v01's Q)

    public string SheetName { get; init; } = "";

    public IReadOnlyList<GdSectionSpec> Sections { get; init; } = [];

    public double DeviationThreshold { get; init; }

    public IReadOnlyDictionary<string, FindingEvaluation> SeverityOverrides { get; init; }
        = new Dictionary<string, FindingEvaluation>();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        var columns = new (string Name, string Value)[]
        {
            (nameof(QuestionNumberColumn), QuestionNumberColumn),
            (nameof(TextColumn), TextColumn),
            (nameof(GuidanceColumn), GuidanceColumn),
            (nameof(RequestedTypeColumn), RequestedTypeColumn),
            (nameof(PreviousAnswerColumn), PreviousAnswerColumn),
            (nameof(AnswerColumn), AnswerColumn),
            (nameof(RequestedExplanationColumn), RequestedExplanationColumn),
            (nameof(PreviousExplanationColumn), PreviousExplanationColumn),
            (nameof(CurrentExplanationColumn), CurrentExplanationColumn),
            (nameof(MaterialChangeColumn), MaterialChangeColumn),
            (nameof(HowExplanationColumn), HowExplanationColumn),
            (nameof(ProvidedByColumn), ProvidedByColumn),
            (nameof(XrefIdColumn), XrefIdColumn),
        };

        foreach (var (name, value) in columns)
            ValidateColumnLetter(name, value, errors);

        if (string.IsNullOrWhiteSpace(SheetName))
            errors.Add("SheetName must not be empty.");

        var valid = columns
            .Where(c => !string.IsNullOrWhiteSpace(c.Value))
            .Select(c => c.Value.ToUpperInvariant())
            .ToList();
        if (valid.Distinct().Count() != valid.Count)
            errors.Add("Column letters must be distinct.");

        ValidateSections(errors);

        if (DeviationThreshold < 0)
            errors.Add("DeviationThreshold must be >= 0.");

        return errors;
    }

    private void ValidateSections(List<string> errors)
    {
        if (Sections.Count == 0)
        {
            errors.Add("Sections must not be empty.");
            return;
        }

        foreach (var s in Sections)
        {
            if (s.HeaderRow <= 0)
                errors.Add($"Section header row ({s.HeaderRow}) must be a positive integer.");
            if (s.FirstDataRow <= s.HeaderRow)
                errors.Add($"Section (header row {s.HeaderRow}): firstDataRow ({s.FirstDataRow}) must be greater than headerRow ({s.HeaderRow}).");
            if (s.LastDataRow < s.FirstDataRow)
                errors.Add($"Section (header row {s.HeaderRow}): lastDataRow ({s.LastDataRow}) must not be less than firstDataRow ({s.FirstDataRow}).");
            if (string.IsNullOrWhiteSpace(s.ExpectedName))
                errors.Add($"Section (header row {s.HeaderRow}): expectedName must not be empty.");
        }

        var headerRows = Sections.Select(s => s.HeaderRow).ToList();
        if (headerRows.Distinct().Count() != headerRows.Count)
            errors.Add("Section header rows must be distinct.");
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
