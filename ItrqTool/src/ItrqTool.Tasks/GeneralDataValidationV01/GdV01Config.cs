using System.Text.RegularExpressions;
using ItrqTool.Domain.Validation;

namespace ItrqTool.Tasks.GeneralDataValidationV01;

/// <summary>
/// One processed GD section, declared explicitly. Replaces the old
/// <c>"&lt;header&gt;:&lt;first&gt;-&lt;last&gt;"</c> string encoding (range only) PLUS the flat
/// <c>MaterialChangeSections</c> name set with a single per-section declaration that ALSO carries:
///   - <see cref="ExpectedName"/> — the column-D section header text the workbook MUST carry at
///     <see cref="HeaderRow"/>. The parser cross-checks the actual header against this (Ordinal);
///     a mismatch is surfaced fail-loud as <c>structure.section-header-mismatch</c> (was a silent
///     no-op of the L check under the old flat name-membership). See <see cref="GdSectionHeaderGate"/>.
///   - <see cref="MaterialChangeRequired"/> — whether column L (material-change) is a required input
///     in this section. Replaces the flat MaterialChangeSections membership: the L required-input
///     check fires only for sections whose flag is true.
/// </summary>
public sealed record GdSectionSpec(
    int HeaderRow,
    int FirstDataRow,
    int LastDataRow,
    string ExpectedName,
    bool MaterialChangeRequired);

// GD_v01 config. Sheet name + the 12 column letters + Sections, mirroring the shape / strictness of
// the RLQ v01 config (init properties defaulting to ""/[], a Validate() returning the list of errors).
// Differences / notes vs RLQ:
//   - sections-only: NO ChapterRows (GD, like RLQ, has no chapters);
//   - the column map is the recon-confirmed C/D/E/F/G/H/I/J/K/L + ProvidedBy O + XrefId Q.
//     Columns M (Answer Due Date internal) and N (Answer Status) ARE present in the GD-v01 template
//     but are auditor/internal columns OUT of v01 validation scope, so they are deliberately NOT
//     config fields here (recon §2.2 / §3 deviation 3).
//   - Sections is the STRUCTURED section declaration (see GdSectionSpec): it folds in both the
//     row geometry (header + data range) AND the per-section material-change requirement AND the
//     expected header name. Unlike the LayoutParser string form, the row geometry is validated at
//     config-load time here (the invariants ported from LayoutParser), not deferred to parse.
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

    // The explicit, per-section declaration. Replaces the old SectionRows string list + the flat
    // MaterialChangeSections name set (G1: explicit per-section, fail-loud L requirement; G2: the
    // "General comments" section is simply not declared, so the engine never touches its rows).
    public IReadOnlyList<GdSectionSpec> Sections { get; init; } = [];

    // Cross-year RELATIVE deviation threshold, expressed as a FRACTION: 0.25 = 25%. A
    // confidently-matched numeric answer whose value moved from the previous year by >= this
    // fraction of the previous value is flagged. Required, no code default: a negative value is a
    // config error (see Validate()).
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

        ValidateSections(errors);

        if (DeviationThreshold < 0)
            errors.Add("DeviationThreshold must be >= 0.");

        return errors;
    }

    // Section invariants ported from LayoutParser (which the profile no longer routes through —
    // it builds the LayoutSection list directly from Sections), so a malformed range surfaces at
    // config-load time rather than as a parse-time exception. Plus the two declarations the string
    // form could not carry: a non-blank ExpectedName, and distinct HeaderRows (the parser keys
    // sections by header row, so a duplicate would otherwise throw at runtime).
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
