using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Clq;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;

namespace ItrqTool.Tasks.ControlLevelQuestionValidationV02;

public static class ClqV02Profile
{
    public static ValidationPipelineProfile<ClqV02Question> Build(
        ControlLevelQuestionValidationV02Config config)
    {
        var roleMap = new ClqBaselineRoleMap<ClqV02Question>(
            Guidance:                 q => q.Guidance,
            ChapterName:              q => q.ChapterName,
            Answer:                   q => q.Answer,
            Strengths:                q => q.Strengths,
            Weaknesses:               q => q.Weaknesses,
            PreviousAnswer:           q => q.PreviousAnswer,
            ProvidedBy:               q => q.ProvidedBy,
            NumberFormatUnrecognized: q => q.NumberFormatUnrecognized,
            AnswerDvType:             q => q.AnswerDvType,
            AnswerDvOperator:         q => q.AnswerDvOperator,
            AnswerDvFormula:          q => q.AnswerDvFormula,
            AnswerDvFormula2:         q => q.AnswerDvFormula2);

        return new ValidationPipelineProfile<ClqV02Question>(
            SheetName: config.SheetName,
            Layout: LayoutParser.Parse(
                config.ChapterRows,
                config.SectionRows,
                config.TextColumn,
                config.TextColumn,
                config.TextColumn),
            RecordFactory: ctx =>
            {
                var originalText = GetCellText(ctx.Row, config.TextColumn) ?? "";
                return new ClqV02Question(
                    RowNumber:                  ctx.RowNumber,
                    XrefId:                     GetCellText(ctx.Row, config.XrefIdColumn),
                    QuestionNumber:             QuestionNumberParser.ExtractNumber(originalText),
                    QuestionText:               QuestionNumberParser.StripPrefix(originalText),
                    OriginalText:               originalText,
                    ChapterName:                ctx.ChapterName,
                    SectionName:                ctx.SectionName,
                    Guidance:                   GetCellText(ctx.Row, config.GuidanceColumn),
                    PreviousAnswer:             GetCellText(ctx.Row, config.PreviousAnswerColumn),
                    Answer:                     GetCellText(ctx.Row, config.AnswerColumn),
                    Strengths:                  GetCellText(ctx.Row, config.StrengthsColumn),
                    Weaknesses:                 GetCellText(ctx.Row, config.WeaknessesColumn),
                    ProvidedBy:                 GetCellText(ctx.Row, config.ProvidedByColumn),
                    AnswerDvType:               null,
                    AnswerDvFormula:            null,
                    AnswerDvOperator:           null,
                    AnswerDvFormula2:           null,
                    NumberFormatUnrecognized:   QuestionNumberParser.HasUnsupportedDeeperPrefix(originalText),
                    AnswerStability:            GetCellText(ctx.Row, config.AnswerStabilityColumn),
                    AnswerStabilityDvType:      null,
                    AnswerStabilityDvFormula:   null,
                    AnswerStabilityDvOperator:  null,
                    AnswerStabilityDvFormula2:  null);
            },
            DvRoles:
            [
                (Column: config.AnswerColumn,
                 ApplyDv: (ClqV02Question q, ExcelCellStructure cell) => q with
                 {
                     AnswerDvType     = cell.DataValidationType,
                     AnswerDvFormula  = cell.DataValidationFormula,
                     AnswerDvOperator = cell.DataValidationOperator,
                     AnswerDvFormula2 = cell.DataValidationFormula2,
                 }),
                (Column: config.AnswerStabilityColumn,
                 ApplyDv: (ClqV02Question q, ExcelCellStructure cell) => q with
                 {
                     AnswerStabilityDvType     = cell.DataValidationType,
                     AnswerStabilityDvFormula  = cell.DataValidationFormula,
                     AnswerStabilityDvOperator = cell.DataValidationOperator,
                     AnswerStabilityDvFormula2 = cell.DataValidationFormula2,
                 }),
            ],
            BaselineDescriptors: ClqBaselineFindings.All,
            RunBaseline: (alignment, emitter) => ClqBaselineChecks.Run(alignment, roleMap, config, emitter),
            Extensions:
            [
                new RequiredInputCell<ClqV02Question>(
                    valueSelector:      q => q.AnswerStability,
                    providedBySelector: q => q.ProvidedBy,
                    role:               "answer-stability",
                    column:             config.AnswerStabilityColumn,
                    allowed:            config.AllowedStabilityAnswers),
                new FrozenConstraintCell<ClqV02Question>(
                    dvTypeSelector:     q => q.AnswerStabilityDvType,
                    dvOperatorSelector: q => q.AnswerStabilityDvOperator,
                    dvFormulaSelector:  q => q.AnswerStabilityDvFormula,
                    dvFormula2Selector: q => q.AnswerStabilityDvFormula2,
                    providedBySelector: q => q.ProvidedBy,
                    role:               "answer-stability",
                    column:             config.AnswerStabilityColumn),
            ]);
    }

    private static string? GetCellText(ExcelRowStructure row, string column)
        => row.CellsByColumn.TryGetValue(column.ToUpperInvariant(), out var cell) ? cell.TextValue : null;
}
