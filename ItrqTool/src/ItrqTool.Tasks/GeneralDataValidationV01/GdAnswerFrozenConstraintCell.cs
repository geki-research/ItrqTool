using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using ItrqTool.Tasks.Shared;   // DvComparer

namespace ItrqTool.Tasks.GeneralDataValidationV01;

// ── GdAnswerFrozenConstraintCell — the C1 reference per-ANSWER check ──
//
// The GD per-answer analogue of the core FrozenConstraintCell<T> (which is per-QUESTION and cannot
// emit N findings per question). It exists in C1 to PROVE the per-answer surface compiles and to
// pin the pattern the five C2 checks copy:
//   - it is an IExtensionCheck<GdV01Question> (qid grain, like every check),
//   - it gates JoinedByXrefId only (a frozen-constraint needs the template side),
//   - it pairs current↔template answers by AnswerId via GdAnswerJoin (the load-bearing join),
//   - it iterates per-answer and emits ONE finding per offending answer-cell at
//     GdPerAnswerEmit.CellAddress (column + answer.AnchorRow),
//   - it reuses the pure kernel DvComparer.IsDvChangedFull (template-first / current-second) —
//     the same comparison FrozenConstraintCell<T> uses, so the logic is shared at the kernel.
//
// Role/column/selectors are ctor params (not literals) so C2 instantiates the same class for BOTH
// the answer DV (H, role "answer-dv") and the material-change DV (L, role "material-change-dv"),
// exactly as RlqV01Profile instantiates FrozenConstraintCell<T> twice. providedBy is read off the
// answer directly — GD's provided-by is per-answer (GdAnswer.ProvidedBy), unlike RLQ's
// per-question field.

public sealed class GdAnswerFrozenConstraintCell : IExtensionCheck<GdV01Question>
{
    private readonly Func<GdAnswer, string?> _dvType;
    private readonly Func<GdAnswer, string?> _dvOp;
    private readonly Func<GdAnswer, string?> _dvFormula;
    private readonly Func<GdAnswer, string?> _dvFormula2;
    private readonly string _column;
    private readonly string _ruleChangedId;
    private readonly IReadOnlyList<FindingDescriptor> _descriptors;

    public GdAnswerFrozenConstraintCell(
        Func<GdAnswer, string?> dvTypeSelector,
        Func<GdAnswer, string?> dvOperatorSelector,
        Func<GdAnswer, string?> dvFormulaSelector,
        Func<GdAnswer, string?> dvFormula2Selector,
        string role,
        string column,
        FindingEvaluation ruleChangedDefault = FindingEvaluation.Error)
    {
        _dvType = dvTypeSelector ?? throw new ArgumentNullException(nameof(dvTypeSelector));
        _dvOp = dvOperatorSelector ?? throw new ArgumentNullException(nameof(dvOperatorSelector));
        _dvFormula = dvFormulaSelector ?? throw new ArgumentNullException(nameof(dvFormulaSelector));
        _dvFormula2 = dvFormula2Selector ?? throw new ArgumentNullException(nameof(dvFormula2Selector));
        if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException("role must be non-empty.", nameof(role));
        if (string.IsNullOrWhiteSpace(column)) throw new ArgumentException("column must be non-empty.", nameof(column));
        _column = column;
        _ruleChangedId = $"constraint.{role}.validation-rule-changed";
        _descriptors = new[]
        {
            new FindingDescriptor(_ruleChangedId, ruleChangedDefault, ValidationCheck.FrozenConstraint,
                "The data-validation rule on the answer cell differs from the empty template; the constraint was altered."),
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
            // Frozen-constraint needs a template side → only JoinedByXrefId rows.
            // (This single gate also skips AddedInResponse and NotEvaluatedMalformedKey.)
            if (aq.WithinYear != WithinYearJoin.JoinedByXrefId)
                continue;

            var cur = aq.Current;
            foreach (var pair in GdAnswerJoin.ToTemplate(aq))
            {
                var tmpl = pair.Counterpart;
                if (tmpl is null) continue;   // current answer with no template counterpart by AnswerId

                var ans = pair.Current;
                if (DvComparer.IsDvChangedFull(
                        _dvType(tmpl), _dvOp(tmpl), _dvFormula(tmpl), _dvFormula2(tmpl),
                        _dvType(ans),  _dvOp(ans),  _dvFormula(ans),  _dvFormula2(ans)))
                {
                    var cell = GdPerAnswerEmit.CellAddress(_column, ans);
                    findings.Add(emitter.Emit(_ruleChangedId,
                        cell, cur.QuestionNumber, cur.QuestionText,
                        requestedData: null, providedBy: ans.ProvidedBy,
                        $"Data-validation rule at {cell} differs from the template."));
                }
            }
        }
        return findings;
    }
}
