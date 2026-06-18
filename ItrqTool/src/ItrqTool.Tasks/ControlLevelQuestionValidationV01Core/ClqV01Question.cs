using ItrqTool.Tasks.QuestionnaireValidation.Alignment;

namespace ItrqTool.Tasks.ControlLevelQuestionValidationV01Core;

// v01-on-core question record. EXACTLY the 18 fields of the frozen bespoke
// InternalClqQuestion (recon §A4) — the same names / types / nullability as the first
// 18 fields of ClqV02Question, so the ClqBaselineRoleMap selectors are identical — but
// WITHOUT v02's 5 answer-stability fields. v01 predates v02's column-K insert.
public record ClqV01Question(
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
) : IAlignmentIdentity;
