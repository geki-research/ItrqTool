namespace ItrqTool.Tasks.QuestionnaireValidation.Checks;

using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

// ── RequiredInputCellAnyValue<T> — presence-only input primitive (any value accepted) ─
//
// Faithful subset of RequiredInputCell<T>: one descriptor, no allowed-set, no
// not-in-allowed-set branch. Fires solely on string.IsNullOrWhiteSpace (presence check).
//
// Name mirrors DV's "Any value" setting — value-validity/DV-conformance is a separate
// primitive. This one checks only that the cell is non-blank.
//
// Id is role-templated: input-cell.{role}.missing. Each instantiated role must produce
// ids unique across the assembled catalogue (the catalogue throws on duplicate ids).
//
// <see cref="Run"/> mirrors <see cref="RequiredInputCell{T}.Run"/>: iterates
// <see cref="AlignmentResult{T}.Aligned"/>, skips NotEvaluatedMalformedKey rows,
// emits once per blank cell. providedBySelector is attribution into the finding only.

public sealed class RequiredInputCellAnyValue<T> : IExtensionCheck<T>
    where T : class, IAlignmentIdentity
{
    private readonly Func<T, string?> _value;
    private readonly Func<T, string?> _providedBy;
    private readonly string _column;
    private readonly string _missingId;
    private readonly IReadOnlyList<FindingDescriptor> _descriptors;

    public RequiredInputCellAnyValue(
        Func<T, string?> valueSelector,
        Func<T, string?> providedBySelector,
        string role,
        string column,
        FindingEvaluation missingDefault = FindingEvaluation.Error)
    {
        _value = valueSelector ?? throw new ArgumentNullException(nameof(valueSelector));
        _providedBy = providedBySelector ?? throw new ArgumentNullException(nameof(providedBySelector));
        if (string.IsNullOrWhiteSpace(role))   throw new ArgumentException("role must be non-empty.",   nameof(role));
        if (string.IsNullOrWhiteSpace(column)) throw new ArgumentException("column must be non-empty.", nameof(column));
        _column = column;
        _missingId = $"input-cell.{role}.missing";
        _descriptors = new[]
        {
            new FindingDescriptor(_missingId, missingDefault, ValidationCheck.MissingResponse,
                "A required input cell is empty; the value was not provided."),
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
            // checks (mirrors ClqBaselineChecks and RequiredInputCell). Input-validity
            // needs no template, so it runs on every other row (both JoinedByXrefId
            // and AddedInResponse).
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
        }
        return findings;
    }
}
