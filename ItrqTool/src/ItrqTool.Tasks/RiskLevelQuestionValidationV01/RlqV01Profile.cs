using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Config;

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
///   - two extensions (chunk 2): RequiredInputCellAnyValue for column L
///     (material-change, role "material-change") and column H (answer, role "answer"),
///     both emitting input-cell.{role}.missing (Error) when blank;
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
            ],
            HaltOnMalformedKeys: true,
            IdentityGateCheck: gateCheck);
    }
}
