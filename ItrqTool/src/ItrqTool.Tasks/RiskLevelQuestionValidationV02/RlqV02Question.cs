using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;

namespace ItrqTool.Tasks.RiskLevelQuestionValidationV02;

// RLQ_v02 question record. Implements the 6-field IAlignmentIdentity (same member names /
// types / nullability as the interface) plus the RLQ payload and a per-row explanation
// list. Differences from v01:
//   - the once-per-question fields (number C, text D, guidance E, requested-type F,
//     previous-answer G, answer H, material-change L, how-explanation M, provided-by P) are
//     read from the question group's FIRST (top) row — they are merged cells that read blank
//     on continuation rows;
//   - provided-by maps to column P (was O in v01);
//   - xref-id maps to column R (was Q in v01);
//   - HowExplanation (column M) is new: free-text, once-per-question, no DV.
// DV fields are null at parse time and patched later (DvPatcher), mirroring the CLQ record.
public sealed record RlqV02Question(
    // IAlignmentIdentity (6)
    int RowNumber,            // first row of the question group
    string? XrefId,           // column R (grouping key)
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
    string? ProvidedBy,       // column P
    IReadOnlyList<RlqExplanationRow> ExplanationRows,
    string? HowExplanation,   // column M (free-text how-explanation, no DV)
    // Resolved DV List allowed-values for finding 5 (DV-conformance). Null at parse time;
    // populated in the patch phase (5a-ii/5b) for List-typed answer / material-change cells whose
    // source has been resolved (inline now; range-ref / named-range later). Null ⇒ unresolved ⇒
    // the conformance evaluator treats the List as NotCheckable rather than false-positiving.
    // Trailing-optional with defaults so no existing construction site changes.
    IReadOnlyList<string>? AnswerDvListValues = null,
    IReadOnlyList<string>? MaterialChangeDvListValues = null,
    // Locale-safe native CLR value of the ANSWER cell (H) — a boxed double for numeric cells.
    // Null at parse time; stamped in the patch phase from ExcelCellStructure.NativeValue by the
    // answer DV-role, alongside the four answer-DV strings. Threaded to DvConformanceEvaluator as
    // sourceNative so a comma-decimal answer is compared numerically instead of being parsed as
    // invariant text (a false NotConformant). NEVER re-stringified back into the text value.
    // Trailing-optional with a default so no existing construction site changes.
    object? AnswerNativeValue = null
) : IAlignmentIdentity;
