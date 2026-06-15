namespace ItrqTool.Tasks.QuestionnaireValidation.Alignment;

/// <summary>
/// The six identity fields the generic <see cref="AlignmentEngine"/> reads from a
/// question. Mirrors the nullability of the corresponding fields on CLQ_v01's
/// <c>InternalClqQuestion</c> exactly:
/// <list type="bullet">
///   <item><see cref="RowNumber"/>      — int (within-year row-shift detection)</item>
///   <item><see cref="XrefId"/>         — string? (cross-year key; null/blank ⇒ malformed)</item>
///   <item><see cref="OriginalText"/>   — string (within-year text-mismatch, exact compare)</item>
///   <item><see cref="QuestionText"/>   — string (cross-year matcher similarity)</item>
///   <item><see cref="SectionName"/>    — string (section bonus)</item>
///   <item><see cref="QuestionNumber"/> — string? (number bonus)</item>
/// </list>
/// </summary>
public interface IAlignmentIdentity
{
    int     RowNumber      { get; }
    string? XrefId         { get; }
    string  OriginalText   { get; }
    string  QuestionText   { get; }
    string  SectionName    { get; }
    string? QuestionNumber { get; }
}
