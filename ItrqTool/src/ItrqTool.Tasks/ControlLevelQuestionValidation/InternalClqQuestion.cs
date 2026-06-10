namespace ItrqTool.Tasks.ControlLevelQuestionValidation;

public record InternalClqQuestion(
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
    bool NumberFormatUnrecognized
);
