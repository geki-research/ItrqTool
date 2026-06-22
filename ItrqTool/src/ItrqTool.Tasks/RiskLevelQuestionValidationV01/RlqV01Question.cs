using ItrqTool.Tasks.QuestionnaireValidation.Alignment;

namespace ItrqTool.Tasks.RiskLevelQuestionValidationV01;

// RLQ_v01 question record. Implements the 6-field IAlignmentIdentity (same member names /
// types / nullability as the interface) plus the RLQ payload and a per-row explanation
// list. Differences from the CLQ record:
//   - NO ChapterName (RLQ is sections-only, no chapters);
//   - QuestionText == OriginalText verbatim — RLQ has NO number prefix to strip, so the
//     QuestionNumberParser scheme is NOT used. The question number lives in its own
//     column C (QuestionNumber), not embedded in the text;
//   - the once-per-question fields (number C, text D, guidance E, requested-type F,
//     previous-answer G, answer H, material-change L, provided-by O) are read from the
//     question group's FIRST (top) row — they are merged cells that read blank on
//     continuation rows;
//   - the per-row explanation triplets (I / J / K) are accumulated across the group in
//     ExplanationRows.
// DV fields are null at parse time and patched later (DvPatcher), mirroring the CLQ record.
public sealed record RlqV01Question(
    // IAlignmentIdentity (6)
    int RowNumber,            // first row of the question group
    string? XrefId,           // column Q (grouping key)
    string OriginalText,      // column D text, verbatim (first row)
    string QuestionText,      // = OriginalText (no number prefix to strip)
    string SectionName,       // from column D at the section-header row
    string? QuestionNumber,   // column C (first row)
    // payload — all once-per-question (first-row; merged cells)
    string? Guidance,         // column E
    string? RequestedType,    // column F (human-readable text)
    string? PreviousAnswer,   // column G
    string? Answer,           // column H
    string? AnswerDvType,     // null at parse; patched later
    string? AnswerDvFormula,
    string? AnswerDvOperator,
    string? AnswerDvFormula2,
    string? MaterialChange,   // column L (DV yes/no cell; value read as text here)
    string? MaterialChangeDvType,     // null at parse; patched later
    string? MaterialChangeDvFormula,
    string? MaterialChangeDvOperator,
    string? MaterialChangeDvFormula2,
    string? ProvidedBy,       // column O
    IReadOnlyList<RlqExplanationRow> ExplanationRows,
    // Resolved DV List allowed-values for finding 5 (DV-conformance). Null at parse time;
    // populated in the patch phase (5a-ii/5b) for List-typed answer / material-change cells whose
    // source has been resolved (inline now; range-ref / named-range later). Null ⇒ unresolved ⇒
    // the conformance evaluator treats the List as NotCheckable rather than false-positiving.
    // Trailing-optional with defaults so no existing construction site changes.
    IReadOnlyList<string>? AnswerDvListValues = null,
    IReadOnlyList<string>? MaterialChangeDvListValues = null
) : IAlignmentIdentity;
