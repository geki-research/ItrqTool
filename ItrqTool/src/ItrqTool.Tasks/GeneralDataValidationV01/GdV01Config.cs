using System.Text.RegularExpressions;
using ItrqTool.Domain.Validation;

namespace ItrqTool.Tasks.GeneralDataValidationV01;

// GD_v01 config. Sheet name + the 12 column letters + SectionRows, mirroring the shape /
// strictness of the RLQ v01 config (init properties defaulting to ""/[], a Validate()
// returning the list of errors). Differences / notes vs RLQ:
//   - sections-only: NO ChapterRows (GD, like RLQ, has no chapters);
//   - the column map is the recon-confirmed C/D/E/F/G/H/I/J/K/L + ProvidedBy O + XrefId Q.
//     Columns M (Answer Due Date internal) and N (Answer Status) ARE present in the GD-v01
//     template but are auditor/internal columns OUT of v01 validation scope, so they are
//     deliberately NOT config fields here (recon §2.2 / §3 deviation 3).
// Like the CLQ/RLQ configs there is NO ParsedSections property: SectionRows is NOT pre-parsed
// here. It parses at RUN via LayoutParser.Parse, so a malformed range surfaces later as a
// FormatException at parse time, not at config load.
public sealed class GdV01Config
{
    public string QuestionNumberColumn { get; init; } = "";       // C
    public string TextColumn { get; init; } = "";                 // D (section name / question text dual-purpose)
    public string GuidanceColumn { get; init; } = "";             // E
    public string RequestedTypeColumn { get; init; } = "";        // F (supplemental — no check keys off F)
    public string PreviousAnswerColumn { get; init; } = "";       // G
    public string AnswerColumn { get; init; } = "";               // H
    public string RequestedExplanationColumn { get; init; } = ""; // I
    public string PreviousExplanationColumn { get; init; } = "";  // J
    public string CurrentExplanationColumn { get; init; } = "";   // K
    public string MaterialChangeColumn { get; init; } = "";       // L
    public string ProvidedByColumn { get; init; } = "";           // O
    public string XrefIdColumn { get; init; } = "";               // Q

    public string SheetName { get; init; } = "";
    public IReadOnlyList<string> SectionRows { get; init; } = [];

    // Cross-year RELATIVE deviation threshold, expressed as a FRACTION: 0.25 = 25%. A
    // confidently-matched numeric answer whose value moved from the previous year by >= this
    // fraction of the previous value is flagged (consumed in a later chunk). Required, no code
    // default: a negative value is a config error (see Validate()).
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
