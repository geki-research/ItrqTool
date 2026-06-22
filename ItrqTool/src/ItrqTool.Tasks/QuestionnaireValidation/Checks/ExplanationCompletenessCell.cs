namespace ItrqTool.Tasks.QuestionnaireValidation.Checks;

using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;

// ── ExplanationCompletenessCell — RLQ per-row explanation-completeness check (finding 6b) ──
//
// RLQ-specific (it needs RlqV01Question.ExplanationRows, an RLQ payload not on
// IAlignmentIdentity) and the first PER-ROW emitter: an RLQ question spans one or more sheet
// rows, each carrying its own explanation triplet (I/J/K). A request without a current answer
// is incomplete on a PER-ROW basis, so this check loops ExplanationRows and emits one finding
// per offending row at K{row} — not one per question.
//
// Per-row rule: if the request (I = Requested) is non-blank AND the current explanation
// (K = Current) is blank → emit one finding at the current-explanation column on that row's
// ACTUAL worksheet row (RlqExplanationRow.RowNumber). The previous explanation (J) is not
// involved. A complete row (K present), a row with no request (I blank), and an all-blank
// triplet all pass silently.
//
// Input-only: like RequiredInputCellAnyValue this is an input-validity check needing no
// template or cross-year baseline, so it mirrors that gate exactly — it runs on every aligned
// row except NotEvaluatedMalformedKey rows (covered by the structure sweep).
//
// Id: input-cell.explanation.incomplete (ValidationCheck.MissingResponse), default Error —
// parity with CLQ's explanation-missing (Strengths/Weaknesses) findings. The single role-fixed
// id must be unique across the assembled catalogue (the catalogue throws on duplicate ids).

public sealed class ExplanationCompletenessCell : IExtensionCheck<RlqV01Question>
{
    private readonly string _column;
    private readonly string _incompleteId;
    private readonly IReadOnlyList<FindingDescriptor> _descriptors;

    public ExplanationCompletenessCell(
        string column,
        FindingEvaluation incompleteDefault = FindingEvaluation.Error)
    {
        if (string.IsNullOrWhiteSpace(column)) throw new ArgumentException("column must be non-empty.", nameof(column));
        _column = column;
        _incompleteId = "input-cell.explanation.incomplete";
        _descriptors = new[]
        {
            new FindingDescriptor(_incompleteId, incompleteDefault, ValidationCheck.MissingResponse,
                "An explanation was requested but the current explanation is missing."),
        };
    }

    public IReadOnlyList<FindingDescriptor> Descriptors => _descriptors;

    public IReadOnlyList<ValidationFinding> Run(AlignmentResult<RlqV01Question> alignment, FindingEmitter emitter)
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
            foreach (var row in cur.ExplanationRows)
            {
                // A request (I) present but the current explanation (K) blank → one finding at K{row}.
                if (!string.IsNullOrWhiteSpace(row.Requested)
                    && string.IsNullOrWhiteSpace(row.Current))
                {
                    findings.Add(emitter.Emit(_incompleteId,
                        $"{_column}{row.RowNumber}", cur.QuestionNumber, cur.QuestionText,
                        requestedData: row.Requested, providedBy: cur.ProvidedBy,
                        $"An explanation was requested but the current explanation at " +
                        $"{_column}{row.RowNumber} is missing."));
                }
            }
        }
        return findings;
    }
}
