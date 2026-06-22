using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.Shared;

namespace ItrqTool.Tasks.RiskLevelQuestionValidationV01;

/// <summary>
/// Builds the RLQ_v01 <see cref="ValidationPipelineProfile{T}"/>. Mirrors
/// <c>ClqV02Profile.Build</c> with the RLQ differences:
///   - sections only (empty chapterRows) — section name + question text both read from
///     column D, so the layout's section-name and question-text columns are both
///     <c>config.TextColumn</c>;
///   - the RLQ path parses with <c>RlqV01QuestionParser</c> and runs
///     <see cref="ValidationPipeline.RunFromParsed{T}"/>, so <see cref="RecordFactory"/> is
///     never reached — it is a documenting throw rather than a real factory;
///   - one DV-role: the answer column (H), stamping the four answer-DV fields exactly as the
///     CLQ answer DV-role does;
///   - three extensions (chunk 2): RequiredInputCellAnyValue for column L
///     (material-change, role "material-change") and column H (answer, role "answer"),
///     both emitting input-cell.{role}.missing (Error) when blank; plus
///     WithinYearStructureCheck for the XrefId column (Q), emitting
///     structure.question-removed / structure.question-added (Error) — filter-free,
///     since the gate guarantees clean keys before any extension runs;
///   - the identity-integrity gate (opt-in): MalformedKeyCheck for column Q (XrefId),
///     emitting structure.xrefid-empty-or-duplicated (Fatal) for blank or duplicate keys,
///     carried in IdentityGateCheck with HaltOnMalformedKeys=true so any malformed key
///     halts the chain (emits ONLY the gate findings) before the input checks run.
/// </summary>
public static class RlqV01Profile
{
    public static ValidationPipelineProfile<RlqV01Question> Build(RlqV01Config config)
    {
        // Identity-integrity gate check: surfaces blank/duplicate XrefId keys (column Q) as
        // structure.xrefid-empty-or-duplicated (Fatal). Lifted out of Extensions into the gate
        // slot so that any malformed key halts the chain before the input/structure checks run.
        var gateCheck = new MalformedKeyCheck<RlqV01Question>(config.XrefIdColumn);

        return new ValidationPipelineProfile<RlqV01Question>(
            SheetName: config.SheetName,
            Layout: LayoutParser.Parse(
                [],                  // RLQ has no chapters — sections only
                config.SectionRows,
                config.TextColumn,   // chapter-name column (unused — no chapters)
                config.TextColumn,   // section-name column = D
                config.TextColumn),  // question-text column = D
            RecordFactory: _ => throw new InvalidOperationException(
                "RLQ uses RlqV01QuestionParser; RecordFactory is not used"),
            DvRoles:
            [
                (Column: config.AnswerColumn,
                 ApplyDv: (RlqV01Question q, ExcelCellStructure cell) => q with
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
                 ApplyDv: (RlqV01Question q, ExcelCellStructure cell) => q with
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
                new RequiredInputCellAnyValue<RlqV01Question>(
                    valueSelector:      q => q.MaterialChange,
                    providedBySelector: q => q.ProvidedBy,
                    role:               "material-change",
                    column:             config.MaterialChangeColumn),
                new RequiredInputCellAnyValue<RlqV01Question>(
                    valueSelector:      q => q.Answer,
                    providedBySelector: q => q.ProvidedBy,
                    role:               "answer",
                    column:             config.AnswerColumn),
                new WithinYearStructureCheck<RlqV01Question>(
                    providedBySelector: q => q.ProvidedBy,
                    column:             config.XrefIdColumn),
                new FrozenConstraintCell<RlqV01Question>(
                    dvTypeSelector:     q => q.AnswerDvType,
                    dvOperatorSelector: q => q.AnswerDvOperator,
                    dvFormulaSelector:  q => q.AnswerDvFormula,
                    dvFormula2Selector: q => q.AnswerDvFormula2,
                    providedBySelector: q => q.ProvidedBy,
                    role:   "answer-dv",
                    column: config.AnswerColumn),
                new FrozenConstraintCell<RlqV01Question>(
                    dvTypeSelector:     q => q.MaterialChangeDvType,
                    dvOperatorSelector: q => q.MaterialChangeDvOperator,
                    dvFormulaSelector:  q => q.MaterialChangeDvFormula,
                    dvFormula2Selector: q => q.MaterialChangeDvFormula2,
                    providedBySelector: q => q.ProvidedBy,
                    role:   "material-change-dv",
                    column: config.MaterialChangeColumn),
                new DvConformanceCell<RlqV01Question>(
                    valueSelector:      q => q.Answer,
                    dvTypeSelector:     q => q.AnswerDvType,
                    dvOperatorSelector: q => q.AnswerDvOperator,
                    dvFormulaSelector:  q => q.AnswerDvFormula,
                    dvFormula2Selector: q => q.AnswerDvFormula2,
                    listValuesSelector: q => q.AnswerDvListValues,
                    providedBySelector: q => q.ProvidedBy,
                    role:   "answer",
                    column: config.AnswerColumn),
                new DvConformanceCell<RlqV01Question>(
                    valueSelector:      q => q.MaterialChange,
                    dvTypeSelector:     q => q.MaterialChangeDvType,
                    dvOperatorSelector: q => q.MaterialChangeDvOperator,
                    dvFormulaSelector:  q => q.MaterialChangeDvFormula,
                    dvFormula2Selector: q => q.MaterialChangeDvFormula2,
                    listValuesSelector: q => q.MaterialChangeDvListValues,
                    providedBySelector: q => q.ProvidedBy,
                    role:   "material-change",
                    column: config.MaterialChangeColumn),
                // Cross-year numeric deviation (finding 6a): a confidently-matched (Agree) answer
                // that moved from the previous year by >= DeviationThreshold, for WholeNumber/Decimal
                // answer DVs only. Reads the answer (H) value and its frozen DV type (template, current
                // fallback). Emits cross-year.answer-deviation (Warning).
                new CrossYearDeviationCell<RlqV01Question>(
                    answerSelector:         q => q.Answer,
                    templateDvTypeSelector: q => q.AnswerDvType,
                    currentDvTypeSelector:  q => q.AnswerDvType,
                    providedBySelector:     q => q.ProvidedBy,
                    role:      "answer",
                    column:    config.AnswerColumn,
                    threshold: config.DeviationThreshold),
            ],
            HaltOnMalformedKeys: true,
            IdentityGateCheck: gateCheck);
    }
}
