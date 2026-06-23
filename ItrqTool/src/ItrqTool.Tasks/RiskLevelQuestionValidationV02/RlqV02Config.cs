using System.Text.RegularExpressions;
using ItrqTool.Domain.Validation;

namespace ItrqTool.Tasks.RiskLevelQuestionValidationV02;

// RLQ_v02 config. Sheet name + the 13 column letters + SectionRows, mirroring the shape /
// strictness of the CLQ v01/v02 config classes (init properties defaulting to ""/[], a
// Validate() returning the list of errors). Differences from v01:
//   - sections-only: NO ChapterRows;
//   - HowExplanationColumn (M) is new;
//   - ProvidedByColumn maps to P (was O in v01);
//   - XrefIdColumn maps to R (was Q in v01);
//   - MaterialChangeExplanationTriggers: the configured trigger values that decide whether M
//     is conditionally-required (empty [] is invalid).
//   - SeverityOverrides kept (empty) for forward-compat with the findings infra.
// Like CLQ v01/v02 there is NO ParsedSections property: SectionRows is NOT pre-parsed here.
// It parses at RUN via LayoutParser.Parse, so a malformed range surfaces later as a
// FormatException at parse time, not at config load.
public sealed class RlqV02Config
{
    public string QuestionNumberColumn { get; init; } = "";       // C
    public string TextColumn { get; init; } = "";                 // D (section name / question text dual-purpose)
    public string GuidanceColumn { get; init; } = "";             // E
    public string RequestedTypeColumn { get; init; } = "";        // F
    public string PreviousAnswerColumn { get; init; } = "";       // G
    public string AnswerColumn { get; init; } = "";               // H
    public string RequestedExplanationColumn { get; init; } = ""; // I
    public string PreviousExplanationColumn { get; init; } = "";  // J
    public string CurrentExplanationColumn { get; init; } = "";   // K
    public string MaterialChangeColumn { get; init; } = "";       // L
    public string HowExplanationColumn { get; init; } = "";       // M
    public string ProvidedByColumn { get; init; } = "";           // P
    public string XrefIdColumn { get; init; } = "";               // R

    public string SheetName { get; init; } = "";
    public IReadOnlyList<string> SectionRows { get; init; } = [];

    // Cross-year RELATIVE deviation threshold (finding 6a), expressed as a FRACTION: 0.25 = 25%.
    // A confidently-matched answer whose value moved from the previous year by >= this fraction of
    // the previous value (|cur - prev| / |prev|, WholeNumber/Decimal answers only; prev == 0 skipped)
    // is flagged. Required, no code default: a negative value is a config error (see Validate()).
    public double DeviationThreshold { get; init; }

    // The set of MaterialChange cell values that make the HowExplanation (M) column conditionally
    // required. Empty [] is invalid: an auditor-supplied config must always declare at least one
    // trigger value.
    public IReadOnlyList<string> MaterialChangeExplanationTriggers { get; init; } = [];

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

        if (SectionRows.Count == 0)
            errors.Add("SectionRows must not be empty.");

        if (DeviationThreshold < 0)
            errors.Add("DeviationThreshold must be >= 0.");

        if (MaterialChangeExplanationTriggers.Count == 0)
            errors.Add("MaterialChangeExplanationTriggers must not be empty.");

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
