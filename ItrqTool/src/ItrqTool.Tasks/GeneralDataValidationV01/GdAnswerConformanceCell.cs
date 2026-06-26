using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

namespace ItrqTool.Tasks.GeneralDataValidationV01;

// ── GdAnswerConformanceCell — per-ANSWER DV conformance check ──
//
// The GD per-answer analogue of DvConformanceCell<T> (which emits at question grain and reads DV
// from TemplateMatch). In GD the template DV is stamped directly onto each GdAnswer by
// GdDvPatcher, so this check reads the value and DV fields from the CURRENT answer's own DV
// fields (no TemplateMatch look-through needed) and emits one finding per offending answer-cell
// at GdPerAnswerEmit.CellAddress.
//
// Ungated: no WithinYear filter. DvConformanceEvaluator.Evaluate naturally returns NotCheckable
// (→ no finding) when the answer carries no DV type or when the constraint cannot be evaluated.
// Present-gate: a blank value skips conformance (that is RequiredInputCell's territory).
//
// Finding ids mirror DvConformanceCell<T> (lesson 107):
//   input-cell.{role}.not-conformant        — ValidationCheck.InputConformance
//   input-cell.{role}.dv-vocabulary-unresolvable — ValidationCheck.InputConformance (BL-025)
// Each role must produce unique ids across the assembled catalogue.

public sealed class GdAnswerConformanceCell : IExtensionCheck<GdV01Question>
{
    private readonly Func<GdAnswer, string?> _value;
    private readonly Func<GdAnswer, string?> _dvType;
    private readonly Func<GdAnswer, string?> _dvOp;
    private readonly Func<GdAnswer, string?> _dvFormula;
    private readonly Func<GdAnswer, string?> _dvFormula2;
    private readonly Func<GdAnswer, IReadOnlyList<string>?> _listValues;
    private readonly Func<GdAnswer, string?> _providedBy;
    private readonly string _column;
    private readonly string _notConformantId;
    private readonly string _unresolvableId;
    private readonly IReadOnlyList<FindingDescriptor> _descriptors;

    public GdAnswerConformanceCell(
        Func<GdAnswer, string?> valueSelector,
        Func<GdAnswer, string?> dvTypeSelector,
        Func<GdAnswer, string?> dvOperatorSelector,
        Func<GdAnswer, string?> dvFormulaSelector,
        Func<GdAnswer, string?> dvFormula2Selector,
        Func<GdAnswer, IReadOnlyList<string>?> listValuesSelector,
        Func<GdAnswer, string?> providedBySelector,
        string role,
        string column,
        FindingEvaluation notConformantDefault = FindingEvaluation.Error,
        FindingEvaluation unresolvableDefault  = FindingEvaluation.Error)
    {
        _value      = valueSelector      ?? throw new ArgumentNullException(nameof(valueSelector));
        _dvType     = dvTypeSelector     ?? throw new ArgumentNullException(nameof(dvTypeSelector));
        _dvOp       = dvOperatorSelector ?? throw new ArgumentNullException(nameof(dvOperatorSelector));
        _dvFormula  = dvFormulaSelector  ?? throw new ArgumentNullException(nameof(dvFormulaSelector));
        _dvFormula2 = dvFormula2Selector ?? throw new ArgumentNullException(nameof(dvFormula2Selector));
        _listValues = listValuesSelector ?? throw new ArgumentNullException(nameof(listValuesSelector));
        _providedBy = providedBySelector ?? throw new ArgumentNullException(nameof(providedBySelector));
        if (string.IsNullOrWhiteSpace(role))   throw new ArgumentException("role must be non-empty.",   nameof(role));
        if (string.IsNullOrWhiteSpace(column)) throw new ArgumentException("column must be non-empty.", nameof(column));
        _column           = column;
        _notConformantId  = $"input-cell.{role}.not-conformant";
        _unresolvableId   = $"input-cell.{role}.dv-vocabulary-unresolvable";
        _descriptors = new[]
        {
            new FindingDescriptor(_notConformantId, notConformantDefault, ValidationCheck.InputConformance,
                "The provided value does not conform to the data-validation rule defined on the empty template."),
            new FindingDescriptor(_unresolvableId, unresolvableDefault, ValidationCheck.InputConformance,
                "The data-validation controlled vocabulary defined on the empty template could not be resolved; conformance was not checked."),
        };
    }

    public IReadOnlyList<FindingDescriptor> Descriptors => _descriptors;

    public IReadOnlyList<ValidationFinding> Run(AlignmentResult<GdV01Question> alignment, FindingEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(alignment);
        ArgumentNullException.ThrowIfNull(emitter);

        var findings = new List<ValidationFinding>();
        foreach (var aq in alignment.Aligned)
        {
            var cur = aq.Current;
            foreach (var answer in cur.Answers)
            {
                var value = _value(answer);
                // Present-gate: blank is RequiredInputCell's territory, never double-reported here.
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                var result = DvConformanceEvaluator.Evaluate(
                    value!,
                    _dvType(answer), _dvOp(answer), _dvFormula(answer), _dvFormula2(answer),
                    _listValues(answer));

                if (result == DvConformanceResult.NotConformant)
                {
                    var cell = GdPerAnswerEmit.CellAddress(_column, answer);
                    findings.Add(emitter.Emit(_notConformantId,
                        cell, cur.QuestionNumber, cur.QuestionText,
                        requestedData: null, providedBy: _providedBy(answer),
                        $"Value '{value}' at {cell} does not conform to the data-validation rule defined on the empty template."));
                }
                else if (result == DvConformanceResult.UnresolvableList)
                {
                    var cell = GdPerAnswerEmit.CellAddress(_column, answer);
                    findings.Add(emitter.Emit(_unresolvableId,
                        cell, cur.QuestionNumber, cur.QuestionText,
                        requestedData: null, providedBy: _providedBy(answer),
                        $"The data-validation controlled vocabulary for {cell} could not be resolved from the empty template; conformance was not checked."));
                }
            }
        }
        return findings;
    }
}
