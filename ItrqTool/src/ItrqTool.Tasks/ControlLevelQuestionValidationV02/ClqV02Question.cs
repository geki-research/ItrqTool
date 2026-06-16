using ItrqTool.Tasks.QuestionnaireValidation.Alignment;

namespace ItrqTool.Tasks.ControlLevelQuestionValidationV02;

public record ClqV02Question(
    int RowNumber,
    string? XrefId,
    string? QuestionNumber,
    string QuestionText,
    string OriginalText,
    string ChapterName,
    string SectionName,
    string? Guidance,
    string? PreviousAnswer,
    string? Answer,
    string? Strengths,
    string? Weaknesses,
    string? ProvidedBy,
    string? AnswerDvType,
    string? AnswerDvFormula,
    string? AnswerDvOperator,
    string? AnswerDvFormula2,
    bool NumberFormatUnrecognized,
    // ── v02 additions ──
    string? AnswerStability,
    string? AnswerStabilityDvType,
    string? AnswerStabilityDvFormula,
    string? AnswerStabilityDvOperator,
    string? AnswerStabilityDvFormula2
) : IAlignmentIdentity;
