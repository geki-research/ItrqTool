using ItrqTool.Tasks.QuestionnaireValidation;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;

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

        // L-gate set: the sections in which column L (material-change) is a required input — derived
        // from the per-section MaterialChangeRequired flag (replaces the old flat MaterialChangeSections
        // membership). Keyed on ExpectedName: on the clean path the actual SectionName equals
        // ExpectedName, so SectionsIn matches; a drifted header is caught fail-loud by the
        // GdSectionHeaderGate (which halts) before any L check runs.
        var materialChangeSections = config.Sections
            .Where(s => s.MaterialChangeRequired)
            .Select(s => s.ExpectedName)
            .ToHashSet(StringComparer.Ordinal);

        return new ValidationPipelineProfile<GdV01Question>(
            SheetName: config.SheetName,
            // Built DIRECTLY from config.Sections (no LayoutParser "<h>:<f>-<l>" string round-trip).
            // GD has no chapters — sections only; every section name is read from column D.
            Layout: new QuestionnaireLayout(
                QuestionTextColumn: config.TextColumn,
                Chapters: [],
                Sections: config.Sections
                    .Select(s => new LayoutSection(s.HeaderRow, s.FirstDataRow, s.LastDataRow, config.TextColumn))
                    .ToList()),
            RecordFactory: _ => throw new InvalidOperationException(
                "GD uses GdV01QuestionParser; RecordFactory is not used"),
            DvRoles: [],
            // The section-header gate runs GD-locally pre-align (GdSectionHeaderGate), NOT as a pipeline
            // baseline/extension. Its descriptor is registered here only so the catalogue knows the id
            // and SeverityOverrides can target it on the clean path; RunBaseline stays a no-op stub.
            BaselineDescriptors: [GdSectionHeaderGate.Descriptor],
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
                    sectionGate:        GdPerAnswerEmit.SectionsIn(materialChangeSections)),

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
                    column:              config.AnswerColumn,
                    // Numeric answer (H) native → compared directly, no invariant text-parse locale
                    // reject (BLG-0022/decimal-conformance). Material-change L below stays native-free
                    // (List DV → native never fires).
                    nativeSelector:      a => a.AnswerNativeValue),
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
                    threshold:             config.DeviationThreshold,
                    // Native (H) preferred per side → a comma-decimal answer compares numerically,
                    // not consumed as a thousands separator (BLG-0022/decimal-deviation).
                    currentNativeSelector:  a => a.AnswerNativeValue,
                    previousNativeSelector: a => a.AnswerNativeValue),

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
