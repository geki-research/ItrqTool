namespace ItrqTool.Tasks.QuestionnaireValidation.Checks;

using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

// ── RequiredInputCell<T> — input-validity primitive for a new/added column ────
//
// Sheet-agnostic generalization of CLQ_v01's input-validity block
// (AnswerMissing / AnswerNotInAllowedSet), lifted from the answer column to any
// role/column. Construct-bound: chunk G's profile supplies the selectors and the
// allowed-set from a config field (never a literal).
//
// Ids are role-templated, dot-segmented: input-cell.{role}.missing and
// input-cell.{role}.not-in-allowed-set. The baseline owns the answer column's ids
// (input-cell.answer-missing, hyphenated single-segment); this primitive is for
// *new* columns and must NEVER be instantiated with role = "answer" in CLQ — v02
// instantiates it only for answer-stability (chunk G). Each instantiated role must
// be unique across the assembled catalogue (it throws on duplicate ids).
//
// The baseline's default-evaluation split is preserved per role: missing defaults
// to Error, not-in-set defaults to Fatal (dependent checks cannot proceed). Both
// map to ValidationCheck.MissingResponse, mirroring the baseline.

public sealed class RequiredInputCell<T> : IExtensionCheck<T> where T : class, IAlignmentIdentity
{
    private readonly Func<T, string?> _value;
    private readonly Func<T, string?> _providedBy;
    private readonly string _column;
    private readonly IReadOnlyList<string> _allowed;
    private readonly string _missingId;
    private readonly string _notInSetId;
    private readonly IReadOnlyList<FindingDescriptor> _descriptors;

    public RequiredInputCell(
        Func<T, string?> valueSelector,
        Func<T, string?> providedBySelector,
        string role,
        string column,
        IReadOnlyList<string> allowed,
        FindingEvaluation missingDefault = FindingEvaluation.Error,
        FindingEvaluation notInSetDefault = FindingEvaluation.Fatal)
    {
        _value = valueSelector ?? throw new ArgumentNullException(nameof(valueSelector));
        _providedBy = providedBySelector ?? throw new ArgumentNullException(nameof(providedBySelector));
        if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException("role must be non-empty.", nameof(role));
        if (string.IsNullOrWhiteSpace(column)) throw new ArgumentException("column must be non-empty.", nameof(column));
        _column = column;
        _allowed = allowed ?? throw new ArgumentNullException(nameof(allowed));
        _missingId = $"input-cell.{role}.missing";
        _notInSetId = $"input-cell.{role}.not-in-allowed-set";
        _descriptors = new[]
        {
            new FindingDescriptor(_missingId, missingDefault, ValidationCheck.MissingResponse,
                "A required input cell is empty; the value was not provided."),
            new FindingDescriptor(_notInSetId, notInSetDefault, ValidationCheck.MissingResponse,
                "A required input cell holds a value outside the configured allowed set."),
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
            // Malformed-key rows are covered by the structure sweep; skip all dependent
            // checks (mirrors ClqBaselineChecks). Input-validity needs no template, so it
            // runs on every other row (both JoinedByXrefId and AddedInResponse).
            if (aq.WithinYear == WithinYearJoin.NotEvaluatedMalformedKey)
                continue;

            var cur = aq.Current;
            int row = cur.RowNumber;
            var value = _value(cur);

            if (string.IsNullOrWhiteSpace(value))
            {
                findings.Add(emitter.Emit(_missingId,
                    $"{_column}{row}", cur.QuestionNumber, cur.QuestionText,
                    requestedData: null, providedBy: _providedBy(cur),
                    $"Required input cell {_column}{row} is empty; the value was not provided."));
            }
            else if (!_allowed.Contains(value, StringComparer.Ordinal))
            {
                findings.Add(emitter.Emit(_notInSetId,
                    $"{_column}{row}", cur.QuestionNumber, cur.QuestionText,
                    requestedData: null, providedBy: _providedBy(cur),
                    $"Value '{value}' at {_column}{row} is not in the allowed set [{string.Join(", ", _allowed)}]."));
            }
        }
        return findings;
    }
}
