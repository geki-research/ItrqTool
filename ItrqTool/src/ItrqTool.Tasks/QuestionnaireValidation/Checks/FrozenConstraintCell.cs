namespace ItrqTool.Tasks.QuestionnaireValidation.Checks;

using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using ItrqTool.Tasks.Shared;   // DvComparer

// ── FrozenConstraintCell<T> — frozen-constraint primitive for a new/added column ──
//
// Sheet-agnostic generalization of CLQ_v01's frozen-constraint block
// (AnswerValidationRuleChanged), lifted from the answer column to any role/column.
// Compares current-vs-template DV via DvComparer.IsDvChangedFull (template-first,
// current-second) and emits a finding when the DV rule changed.
//
// Requires a template side: only JoinedByXrefId rows are evaluated (same gate as the
// baseline block in ClqBaselineChecks). AddedInResponse rows have no template DV to
// compare against and are silently skipped.
//
// Id is role-templated: constraint.{role}.validation-rule-changed. The baseline owns
// constraint.answer-validation-rule-changed (hyphenated, single-segment role); v02
// instantiates this primitive only for answer-stability (chunk G). Each instantiated
// role must produce ids unique across the assembled catalogue.

public sealed class FrozenConstraintCell<T> : IExtensionCheck<T> where T : class, IAlignmentIdentity
{
    private readonly Func<T, string?> _dvType;
    private readonly Func<T, string?> _dvOp;
    private readonly Func<T, string?> _dvFormula;
    private readonly Func<T, string?> _dvFormula2;
    private readonly Func<T, string?> _providedBy;
    private readonly string _column;
    private readonly string _ruleChangedId;
    private readonly IReadOnlyList<FindingDescriptor> _descriptors;

    public FrozenConstraintCell(
        Func<T, string?> dvTypeSelector,
        Func<T, string?> dvOperatorSelector,
        Func<T, string?> dvFormulaSelector,
        Func<T, string?> dvFormula2Selector,
        Func<T, string?> providedBySelector,
        string role,
        string column,
        FindingEvaluation ruleChangedDefault = FindingEvaluation.Error)
    {
        _dvType = dvTypeSelector ?? throw new ArgumentNullException(nameof(dvTypeSelector));
        _dvOp = dvOperatorSelector ?? throw new ArgumentNullException(nameof(dvOperatorSelector));
        _dvFormula = dvFormulaSelector ?? throw new ArgumentNullException(nameof(dvFormulaSelector));
        _dvFormula2 = dvFormula2Selector ?? throw new ArgumentNullException(nameof(dvFormula2Selector));
        _providedBy = providedBySelector ?? throw new ArgumentNullException(nameof(providedBySelector));
        if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException("role must be non-empty.", nameof(role));
        if (string.IsNullOrWhiteSpace(column)) throw new ArgumentException("column must be non-empty.", nameof(column));
        _column = column;
        _ruleChangedId = $"constraint.{role}.validation-rule-changed";
        _descriptors = new[]
        {
            new FindingDescriptor(_ruleChangedId, ruleChangedDefault, ValidationCheck.FrozenConstraint,
                "The data-validation rule on the cell differs from the empty template; the constraint was altered."),
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
            // Frozen-constraint needs a template side → only JoinedByXrefId rows.
            // (This single gate also skips AddedInResponse and NotEvaluatedMalformedKey.)
            if (aq.WithinYear != WithinYearJoin.JoinedByXrefId)
                continue;

            var cur = aq.Current;
            var tmpl = aq.TemplateMatch!; // non-null IFF JoinedByXrefId
            int row = cur.RowNumber;

            if (DvComparer.IsDvChangedFull(
                    _dvType(tmpl), _dvOp(tmpl), _dvFormula(tmpl), _dvFormula2(tmpl),
                    _dvType(cur),  _dvOp(cur),  _dvFormula(cur),  _dvFormula2(cur)))
            {
                findings.Add(emitter.Emit(_ruleChangedId,
                    $"{_column}{row}", cur.QuestionNumber, cur.QuestionText,
                    requestedData: null, providedBy: _providedBy(cur),
                    $"Data-validation rule at {_column}{row} differs from the template."));
            }
        }
        return findings;
    }
}
