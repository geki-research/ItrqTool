using ItrqTool.Tasks.QuestionnaireValidation;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Config;

namespace ItrqTool.Tasks.GeneralDataValidationV01;

/// <summary>
/// Builds the GD_v01 <see cref="ValidationPipelineProfile{T}"/>. Mirrors
/// <c>RlqV01Profile.Build</c> with the GD differences:
///   - DvRoles: empty — GD stamps DV via <c>GdDvPatcher</c>, not the pipeline DvPatcher loop;
///   - RecordFactory: documenting throw — GD parses with <c>GdV01QuestionParser</c> and calls
///     <see cref="ValidationPipeline.RunFromAlignedGated{T}"/> directly;
///   - Extensions: 10 bespoke per-answer or per-question checks (see §3 in C2b spec).
///   - Identity-integrity gate: MalformedKeyCheck for column Q (XrefId), HaltOnMalformedKeys=true.
/// </summary>
public static class GdV01Profile
{
    public static ValidationPipelineProfile<GdV01Question> Build(GdV01Config config)
    {
        var gateCheck = new MalformedKeyCheck<GdV01Question>(config.XrefIdColumn);

        return new ValidationPipelineProfile<GdV01Question>(
            SheetName: config.SheetName,
            Layout: LayoutParser.Parse(
                [],                  // GD has no chapters — sections only
                config.SectionRows,
                config.TextColumn,   // chapter-name column (unused — no chapters)
                config.TextColumn,   // section-name column = D
                config.TextColumn),  // question-text column = D
            RecordFactory: _ => throw new InvalidOperationException(
                "GD uses GdV01QuestionParser; RecordFactory is not used"),
            DvRoles: [],
            BaselineDescriptors: [],
            RunBaseline: (alignment, emitter) => [],
            Extensions:
            [
                // ── required-input (presence-only, per answer) ──────────────────────────
                new GdAnswerRequiredInputCell(
                    valueSelector:      a => a.Answer,
                    providedBySelector: a => a.ProvidedBy,
                    role:               "answer",
                    column:             config.AnswerColumn,
                    sectionGate:        GdPerAnswerEmit.AllSections),
                new GdAnswerRequiredInputCell(
                    valueSelector:      a => a.MaterialChange,
                    providedBySelector: a => a.ProvidedBy,
                    role:               "material-change",
                    column:             config.MaterialChangeColumn,
                    sectionGate:        GdPerAnswerEmit.SectionsIn(
                                            config.MaterialChangeSections.ToHashSet(StringComparer.Ordinal))),

                // ── DV conformance (current value vs current DV, per answer) ───────────
                new GdAnswerConformanceCell(
                    valueSelector:       a => a.Answer,
                    dvTypeSelector:      a => a.AnswerDvType,
                    dvOperatorSelector:  a => a.AnswerDvOperator,
                    dvFormulaSelector:   a => a.AnswerDvFormula,
                    dvFormula2Selector:  a => a.AnswerDvFormula2,
                    listValuesSelector:  a => a.AnswerDvListValues,
                    providedBySelector:  a => a.ProvidedBy,
                    role:                "answer",
                    column:              config.AnswerColumn),
                new GdAnswerConformanceCell(
                    valueSelector:       a => a.MaterialChange,
                    dvTypeSelector:      a => a.MaterialChangeDvType,
                    dvOperatorSelector:  a => a.MaterialChangeDvOperator,
                    dvFormulaSelector:   a => a.MaterialChangeDvFormula,
                    dvFormula2Selector:  a => a.MaterialChangeDvFormula2,
                    listValuesSelector:  a => a.MaterialChangeDvListValues,
                    providedBySelector:  a => a.ProvidedBy,
                    role:                "material-change",
                    column:              config.MaterialChangeColumn),

                // ── frozen-constraint (current DV vs template DV, per answer) ──────────
                new GdAnswerFrozenConstraintCell(
                    a => a.AnswerDvType, a => a.AnswerDvOperator,
                    a => a.AnswerDvFormula, a => a.AnswerDvFormula2,
                    role:   "answer-dv",
                    column: config.AnswerColumn),
                new GdAnswerFrozenConstraintCell(
                    a => a.MaterialChangeDvType, a => a.MaterialChangeDvOperator,
                    a => a.MaterialChangeDvFormula, a => a.MaterialChangeDvFormula2,
                    role:   "material-change-dv",
                    column: config.MaterialChangeColumn),

                // ── cross-year numeric deviation (H only) ─────────────────────────────
                new GdAnswerDeviationCell(
                    currentValueSelector:  a => a.Answer,
                    previousValueSelector: a => a.Answer,
                    dvTypeSelector:        a => a.AnswerDvType,
                    providedBySelector:    a => a.ProvidedBy,
                    column:                config.AnswerColumn,
                    threshold:             config.DeviationThreshold),

                // ── explanation completeness (per explanation row, flattened across answers) ──
                new ExplanationCompletenessCell<GdV01Question>(
                    q => q.Answers.SelectMany(a => a.Explanations.Select(r =>
                             new ExplanationRowView(r.Requested, r.Current, r.RowNumber, a.ProvidedBy))),
                    config.CurrentExplanationColumn),

                // ── within-year structure (qid grain) ────────────────────────────────
                new WithinYearStructureCheck<GdV01Question>(
                    providedBySelector: q => q.Answers.FirstOrDefault()?.ProvidedBy,
                    column:             config.XrefIdColumn),

                // ── SameXrefIdTextDiverged (qid grain) ───────────────────────────────
                new GdAnswerTextDivergedCell(
                    providedBySelector: q => q.Answers.FirstOrDefault()?.ProvidedBy,
                    column:             config.XrefIdColumn),
            ],
            HaltOnMalformedKeys: true,
            IdentityGateCheck: gateCheck);
    }
}
