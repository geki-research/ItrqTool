namespace ItrqTool.Tasks.QuestionnaireValidation.Checks;

using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

// ── DvConformanceCell<T> — DV-conformance primitive for an input column (finding 5) ──
//
// Sheet-agnostic extension check: each PRESENT value in the role's column must CONFORM to the
// data-validation rule defined on the EMPTY TEMPLATE for that cell. The typed dispatch +
// operator semantics live in the pure DvConformanceEvaluator; this wrapper is the alignment
// gate + present-gate + emit shell, mirroring FrozenConstraintCell<T>'s structure.
//
// Gate: conformance is judged against the TEMPLATE DV, so only JoinedByXrefId rows are evaluated
// (template side present) — the same single gate that also skips AddedInResponse and
// NotEvaluatedMalformedKey. The DV-field and resolved-list selectors therefore read the TEMPLATE
// MATCH; the value selector reads the CURRENT question.
//
// Present-gate: a blank current value is skipped (that is finding 1's territory —
// RequiredInputCell*); conformance never double-reports an empty cell.
//
// Emits on DvConformanceResult.NotConformant and UnresolvableList (BL-025). Conformant and
// NotCheckable (no constraint, Custom formula, missing bound, unknown type) emit nothing — the
// evaluator never false-positives a value it cannot judge.
//
// Id is role-templated: input-cell.{role}.not-conformant (ValidationCheck.InputConformance).
// Each instantiated role must produce ids unique across the assembled catalogue.

public sealed class DvConformanceCell<T> : IExtensionCheck<T> where T : class, IAlignmentIdentity
{
    private readonly Func<T, string?> _value;
    private readonly Func<T, string?> _dvType;
    private readonly Func<T, string?> _dvOp;
    private readonly Func<T, string?> _dvFormula;
    private readonly Func<T, string?> _dvFormula2;
    private readonly Func<T, IReadOnlyList<string>?> _listValues;
    private readonly Func<T, string?> _providedBy;
    private readonly string _column;
    private readonly string _notConformantId;
    private readonly string _unresolvableId;
    private readonly IReadOnlyList<FindingDescriptor> _descriptors;

    public DvConformanceCell(
        Func<T, string?> valueSelector,
        Func<T, string?> dvTypeSelector,
        Func<T, string?> dvOperatorSelector,
        Func<T, string?> dvFormulaSelector,
        Func<T, string?> dvFormula2Selector,
        Func<T, IReadOnlyList<string>?> listValuesSelector,
        Func<T, string?> providedBySelector,
        string role,
        string column,
        FindingEvaluation notConformantDefault = FindingEvaluation.Error,
        FindingEvaluation unresolvableDefault = FindingEvaluation.Error)
    {
        _value = valueSelector ?? throw new ArgumentNullException(nameof(valueSelector));
        _dvType = dvTypeSelector ?? throw new ArgumentNullException(nameof(dvTypeSelector));
        _dvOp = dvOperatorSelector ?? throw new ArgumentNullException(nameof(dvOperatorSelector));
        _dvFormula = dvFormulaSelector ?? throw new ArgumentNullException(nameof(dvFormulaSelector));
        _dvFormula2 = dvFormula2Selector ?? throw new ArgumentNullException(nameof(dvFormula2Selector));
        _listValues = listValuesSelector ?? throw new ArgumentNullException(nameof(listValuesSelector));
        _providedBy = providedBySelector ?? throw new ArgumentNullException(nameof(providedBySelector));
        if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException("role must be non-empty.", nameof(role));
        if (string.IsNullOrWhiteSpace(column)) throw new ArgumentException("column must be non-empty.", nameof(column));
        _column = column;
        _notConformantId = $"input-cell.{role}.not-conformant";
        _unresolvableId = $"input-cell.{role}.dv-vocabulary-unresolvable";
        _descriptors = new[]
        {
            new FindingDescriptor(_notConformantId, notConformantDefault, ValidationCheck.InputConformance,
                "The provided value does not conform to the data-validation rule defined on the empty template."),
            new FindingDescriptor(_unresolvableId, unresolvableDefault, ValidationCheck.InputConformance,
                "The data-validation controlled vocabulary defined on the empty template could not be resolved; conformance was not checked."),
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
            // Conformance is against the TEMPLATE DV → only JoinedByXrefId rows.
            // (This single gate also skips AddedInResponse and NotEvaluatedMalformedKey.)
            if (aq.WithinYear != WithinYearJoin.JoinedByXrefId)
                continue;

            var cur = aq.Current;
            var tmpl = aq.TemplateMatch!; // non-null IFF JoinedByXrefId
            var value = _value(cur);

            // Present-gate: blank is finding 1's concern, never double-reported here.
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var result = DvConformanceEvaluator.Evaluate(
                value!,
                _dvType(tmpl), _dvOp(tmpl), _dvFormula(tmpl), _dvFormula2(tmpl),
                _listValues(tmpl));

            if (result == DvConformanceResult.NotConformant)
            {
                int row = cur.RowNumber;
                findings.Add(emitter.Emit(_notConformantId,
                    $"{_column}{row}", cur.QuestionNumber, cur.QuestionText,
                    requestedData: null, providedBy: _providedBy(cur),
                    $"Value '{value}' at {_column}{row} does not conform to the data-validation rule defined on the empty template."));
            }
            else if (result == DvConformanceResult.UnresolvableList)
            {
                int row = cur.RowNumber;
                findings.Add(emitter.Emit(_unresolvableId,
                    $"{_column}{row}", cur.QuestionNumber, cur.QuestionText,
                    requestedData: null, providedBy: _providedBy(cur),
                    $"The data-validation controlled vocabulary for {_column}{row} could not be resolved from the empty template; conformance was not checked."));
            }
        }
        return findings;
    }
}
