using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation;
using ItrqTool.Tasks.QuestionnaireValidation.Clq;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;

namespace ItrqTool.Tasks.ControlLevelQuestionValidationV01;

// CLQ_v01 profile. This is ClqV02Profile.Build MINUS answer-stability:
//   - the SAME 12-selector ClqBaselineRoleMap (field names match v02's first 18 fields);
//   - the SAME LayoutParser.Parse call (TextColumn for chapter / section / blank-test);
//   - a RecordFactory building the 18-field ClqV01Question (DV fields null at parse;
//     NumberFormatUnrecognized via QuestionNumberParser.HasUnsupportedDeeperPrefix);
//   - DvRoles = [answer → config.AnswerColumn] ONLY (no stability/K DV-role);
//   - Extensions = [] (no RequiredInputCell / FrozenConstraintCell).
// The column-map difference (v01's provided-by M / xref-id N vs v02's N/O) lives ONLY in
// the JSON config — the profile CODE is v02's minus stability.
public static class ClqV01Profile
{
    public static ValidationPipelineProfile<ClqV01Question> Build(ClqV01Config config)
    {
        var roleMap = new ClqBaselineRoleMap<ClqV01Question>(
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

        return new ValidationPipelineProfile<ClqV01Question>(
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
                return new ClqV01Question(
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
                    NumberFormatUnrecognized:   QuestionNumberParser.HasUnsupportedDeeperPrefix(originalText));
            },
            DvRoles:
            [
                (Column: config.AnswerColumn,
                 ApplyDv: (ClqV01Question q, ExcelCellStructure cell) => q with
                 {
                     AnswerDvType     = cell.DataValidationType,
                     AnswerDvFormula  = cell.DataValidationFormula,
                     AnswerDvOperator = cell.DataValidationOperator,
                     AnswerDvFormula2 = cell.DataValidationFormula2,
                 }),
            ],
            BaselineDescriptors: ClqBaselineFindings.All,
            RunBaseline: (alignment, emitter) => ClqBaselineChecks.Run(alignment, roleMap, config, emitter),
            Extensions: []);
    }

    private static string? GetCellText(ExcelRowStructure row, string column)
        => row.CellsByColumn.TryGetValue(column.ToUpperInvariant(), out var cell) ? cell.TextValue : null;
}
