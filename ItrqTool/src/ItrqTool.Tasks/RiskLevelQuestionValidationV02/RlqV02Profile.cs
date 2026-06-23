using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.Shared;

namespace ItrqTool.Tasks.RiskLevelQuestionValidationV02;

/// <summary>
/// Builds the RLQ_v02 <see cref="ValidationPipelineProfile{T}"/>. Mirrors
/// <c>RlqV01Profile.Build</c> with the v02 additions:
///   - M (HowExplanation) is conditionally required when L (MaterialChange) holds a
///     value from the configured trigger set (<see cref="ConditionalRequirement{T}"/>);
///   - the configured trigger values must appear in L's template DV vocabulary
///     (<see cref="ConfiguredTriggerInDvList{T}"/>);
///   - provided-by maps to P (was O in v01); xref-id maps to R (was Q in v01).
/// M has NO data-validation — no DV-role is added for M. The DvRangeRefResolver
/// post-patch pass for H and L is preserved in the task's ReadParsePatch.
/// </summary>
public static class RlqV02Profile
{
    public static ValidationPipelineProfile<RlqV02Question> Build(RlqV02Config config)
    {
        var gateCheck = new MalformedKeyCheck<RlqV02Question>(config.XrefIdColumn);

        return new ValidationPipelineProfile<RlqV02Question>(
            SheetName: config.SheetName,
            Layout: LayoutParser.Parse(
                [],                  // RLQ has no chapters — sections only
                config.SectionRows,
                config.TextColumn,   // chapter-name column (unused — no chapters)
                config.TextColumn,   // section-name column = D
                config.TextColumn),  // question-text column = D
            RecordFactory: _ => throw new InvalidOperationException(
                "RLQ uses RlqV02QuestionParser; RecordFactory is not used"),
            DvRoles:
            [
                (Column: config.AnswerColumn,
                 ApplyDv: (RlqV02Question q, ExcelCellStructure cell) => q with
                 {
                     AnswerDvType     = cell.DataValidationType,
                     AnswerDvFormula  = cell.DataValidationFormula,
                     AnswerDvOperator = cell.DataValidationOperator,
                     AnswerDvFormula2 = cell.DataValidationFormula2,
                     AnswerDvListValues =
                         string.Equals(cell.DataValidationType, "List", StringComparison.OrdinalIgnoreCase)
                         && DvListParser.ClassifySource(cell.DataValidationFormula ?? "") == DvListSourceKind.Inline
                             ? DvListParser.ParseInline(cell.DataValidationFormula ?? "")
                             : null,
                 }),
                (Column: config.MaterialChangeColumn,
                 ApplyDv: (RlqV02Question q, ExcelCellStructure cell) => q with
                 {
                     MaterialChangeDvType     = cell.DataValidationType,
                     MaterialChangeDvFormula  = cell.DataValidationFormula,
                     MaterialChangeDvOperator = cell.DataValidationOperator,
                     MaterialChangeDvFormula2 = cell.DataValidationFormula2,
                     MaterialChangeDvListValues =
                         string.Equals(cell.DataValidationType, "List", StringComparison.OrdinalIgnoreCase)
                         && DvListParser.ClassifySource(cell.DataValidationFormula ?? "") == DvListSourceKind.Inline
                             ? DvListParser.ParseInline(cell.DataValidationFormula ?? "")
                             : null,
                 }),
            ],
            BaselineDescriptors: [],
            RunBaseline: (alignment, emitter) => [],
            Extensions:
            [
                new RequiredInputCellAnyValue<RlqV02Question>(
                    valueSelector:      q => q.MaterialChange,
                    providedBySelector: q => q.ProvidedBy,
                    role:               "material-change",
                    column:             config.MaterialChangeColumn),
                new RequiredInputCellAnyValue<RlqV02Question>(
                    valueSelector:      q => q.Answer,
                    providedBySelector: q => q.ProvidedBy,
                    role:               "answer",
                    column:             config.AnswerColumn),
                new WithinYearStructureCheck<RlqV02Question>(
                    providedBySelector: q => q.ProvidedBy,
                    column:             config.XrefIdColumn),
                new FrozenConstraintCell<RlqV02Question>(
                    dvTypeSelector:     q => q.AnswerDvType,
                    dvOperatorSelector: q => q.AnswerDvOperator,
                    dvFormulaSelector:  q => q.AnswerDvFormula,
                    dvFormula2Selector: q => q.AnswerDvFormula2,
                    providedBySelector: q => q.ProvidedBy,
                    role:   "answer-dv",
                    column: config.AnswerColumn),
                new FrozenConstraintCell<RlqV02Question>(
                    dvTypeSelector:     q => q.MaterialChangeDvType,
                    dvOperatorSelector: q => q.MaterialChangeDvOperator,
                    dvFormulaSelector:  q => q.MaterialChangeDvFormula,
                    dvFormula2Selector: q => q.MaterialChangeDvFormula2,
                    providedBySelector: q => q.ProvidedBy,
                    role:   "material-change-dv",
                    column: config.MaterialChangeColumn),
                new DvConformanceCell<RlqV02Question>(
                    valueSelector:      q => q.Answer,
                    dvTypeSelector:     q => q.AnswerDvType,
                    dvOperatorSelector: q => q.AnswerDvOperator,
                    dvFormulaSelector:  q => q.AnswerDvFormula,
                    dvFormula2Selector: q => q.AnswerDvFormula2,
                    listValuesSelector: q => q.AnswerDvListValues,
                    providedBySelector: q => q.ProvidedBy,
                    role:   "answer",
                    column: config.AnswerColumn),
                new DvConformanceCell<RlqV02Question>(
                    valueSelector:      q => q.MaterialChange,
                    dvTypeSelector:     q => q.MaterialChangeDvType,
                    dvOperatorSelector: q => q.MaterialChangeDvOperator,
                    dvFormulaSelector:  q => q.MaterialChangeDvFormula,
                    dvFormula2Selector: q => q.MaterialChangeDvFormula2,
                    listValuesSelector: q => q.MaterialChangeDvListValues,
                    providedBySelector: q => q.ProvidedBy,
                    role:   "material-change",
                    column: config.MaterialChangeColumn),
                new CrossYearDeviationCell<RlqV02Question>(
                    answerSelector:         q => q.Answer,
                    templateDvTypeSelector: q => q.AnswerDvType,
                    currentDvTypeSelector:  q => q.AnswerDvType,
                    providedBySelector:     q => q.ProvidedBy,
                    role:      "answer",
                    column:    config.AnswerColumn,
                    threshold: config.DeviationThreshold),
                new ExplanationCompletenessCellV02(config.CurrentExplanationColumn),
                // Rule 1 — M is conditionally required when L is in the configured trigger set.
                new ConditionalRequirement<RlqV02Question>(
                    targetValueSelector:  q => q.HowExplanation,
                    triggerValueSelector: q => q.MaterialChange,
                    providedBySelector:   q => q.ProvidedBy,
                    role:          "material-change-explanation",
                    targetColumn:  config.HowExplanationColumn,
                    triggerColumn: config.MaterialChangeColumn,
                    triggerValues: config.MaterialChangeExplanationTriggers),
                // Rule 2 — the configured trigger(s) must be in L's template DV vocabulary.
                new ConfiguredTriggerInDvList<RlqV02Question>(
                    triggerDvListValuesSelector: q => q.MaterialChangeDvListValues,
                    role:                    "material-change-explanation",
                    triggerColumn:           config.MaterialChangeColumn,
                    configuredTriggerValues: config.MaterialChangeExplanationTriggers),
            ],
            HaltOnMalformedKeys: true,
            IdentityGateCheck: gateCheck);
    }
}
