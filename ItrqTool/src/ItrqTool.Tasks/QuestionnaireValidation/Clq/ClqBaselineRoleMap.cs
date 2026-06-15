using ItrqTool.Tasks.QuestionnaireValidation.Alignment;

namespace ItrqTool.Tasks.QuestionnaireValidation.Clq;

// ── CLQ baseline role map (payload selectors) ────────────────────────────────
//
// Version-neutral binding between a concrete question type T and the NON-identity
// payload the CLQ baseline checks read. The six identity fields (RowNumber, XrefId,
// OriginalText, QuestionText, SectionName, QuestionNumber) come from
// IAlignmentIdentity directly and are NOT selectors here.
//
// The FULL baseline selector set is declared now so D1b/D1c need not widen it.
// D1a exercises only NumberFormatUnrecognized and ProvidedBy; the rest are present
// for the within-year structure and input-validity checks that follow.

public sealed record ClqBaselineRoleMap<T>(
    Func<T, string?> Guidance,
    Func<T, string?> ChapterName,
    Func<T, string?> Answer,
    Func<T, string?> Strengths,
    Func<T, string?> Weaknesses,
    Func<T, string?> PreviousAnswer,
    Func<T, string?> ProvidedBy,
    Func<T, bool> NumberFormatUnrecognized,
    Func<T, string?> AnswerDvType,
    Func<T, string?> AnswerDvOperator,
    Func<T, string?> AnswerDvFormula,
    Func<T, string?> AnswerDvFormula2)
    where T : IAlignmentIdentity;
