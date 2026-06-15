namespace ItrqTool.Integration.Tests.ClqV01;

/// <summary>
/// Per-question data for one workbook in the baseline trio.
/// The writer maps each field to the column declared in the production config:
/// D=OriginalText, E=Guidance, F=PreviousAnswer, H=Answer, I=Strengths,
/// J=Weaknesses, M=ProvidedBy, N=XrefId.
/// Null fields are written as empty cells (template has most fields null).
/// 4c mutates specific rows using <c>with</c> expressions before writing.
/// </summary>
public sealed record ClqV01QuestionSpec(
    int RowNumber,
    string XrefId,
    string OriginalText,
    string? Guidance,
    string? PreviousAnswer,
    string? Answer,
    string? Strengths,
    string? Weaknesses,
    string? ProvidedBy
);
