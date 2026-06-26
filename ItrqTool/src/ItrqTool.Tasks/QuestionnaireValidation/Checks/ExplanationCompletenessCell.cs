namespace ItrqTool.Tasks.QuestionnaireValidation.Checks;

using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

// ── ExplanationCompletenessCell<T> — per-row explanation-completeness check (finding 6b) ──
//
// Generic over any IAlignmentIdentity question type. A neutral selector projects the
// question's per-row explanation data into ExplanationRowViews so the check is decoupled
// from any specific question record (RLQ v01, v02, GD, …).
//
// Per-row rule: if the request (Requested) is non-blank AND the current explanation
// (Current) is blank → emit one finding at the current-explanation column on that row's
// ACTUAL worksheet row (ExplanationRowView.RowNumber). A complete row (Current present),
// a row with no request (Requested blank), and an all-blank triplet all pass silently.
//
// Input-only: like RequiredInputCellAnyValue this is an input-validity check needing no
// template or cross-year baseline, so it mirrors that gate exactly — it runs on every
// aligned row except NotEvaluatedMalformedKey rows (covered by the structure sweep).
//
// Id: input-cell.explanation.incomplete (ValidationCheck.MissingResponse), default Error —
// parity with CLQ's explanation-missing (Strengths/Weaknesses) findings. The single role-fixed
// id must be unique across the assembled catalogue (the catalogue throws on duplicate ids).

public sealed class ExplanationCompletenessCell<T> : IExtensionCheck<T> where T : class, IAlignmentIdentity
{
    private readonly Func<T, IEnumerable<ExplanationRowView>> _explanationRowsSelector;
    private readonly string _column;
    private readonly string _incompleteId;
    private readonly IReadOnlyList<FindingDescriptor> _descriptors;

    public ExplanationCompletenessCell(
        Func<T, IEnumerable<ExplanationRowView>> explanationRowsSelector,
        string column,
        FindingEvaluation incompleteDefault = FindingEvaluation.Error)
    {
        ArgumentNullException.ThrowIfNull(explanationRowsSelector);
        if (string.IsNullOrWhiteSpace(column)) throw new ArgumentException("column must be non-empty.", nameof(column));
        _explanationRowsSelector = explanationRowsSelector;
        _column = column;
        _incompleteId = "input-cell.explanation.incomplete";
        _descriptors = new[]
        {
            new FindingDescriptor(_incompleteId, incompleteDefault, ValidationCheck.MissingResponse,
                "An explanation was requested but the current explanation is missing."),
        };
    }

    public IReadOnlyList<FindingDescriptor> Descriptors => _descriptors;

    public IReadOnlyList<ValidationFinding> Run(AlignmentResult<T> alignment, FindingEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(alignment);
        ArgumentNullException.ThrowIfNull(emitter);

        var findings = new List<ValidationFinding>();
        foreach (var aq in alignment.Aligned)
        {
            // Mirror RequiredInputCellAnyValue's gate: malformed-key rows are covered by the
            // structure sweep; input-validity needs no template, so it runs on every other row.
            if (aq.WithinYear == WithinYearJoin.NotEvaluatedMalformedKey)
                continue;

            var cur = aq.Current;
            foreach (var v in _explanationRowsSelector(cur))
            {
                // A request (Requested) present but the current explanation (Current) blank → one finding at {column}{row}.
                if (!string.IsNullOrWhiteSpace(v.Requested)
                    && string.IsNullOrWhiteSpace(v.Current))
                {
                    findings.Add(emitter.Emit(_incompleteId,
                        $"{_column}{v.RowNumber}", cur.QuestionNumber, cur.QuestionText,
                        requestedData: v.Requested, providedBy: v.ProvidedBy,
                        $"An explanation was requested but the current explanation at " +
                        $"{_column}{v.RowNumber} is missing."));
                }
            }
        }
        return findings;
    }
}
