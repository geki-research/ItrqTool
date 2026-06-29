using ItrqTool.Tasks.GeneralDataValidationV01;

namespace ItrqTool.Tasks.GeneralDataValidationV02;

// GD_v02 answer record. Extends GdAnswer (v01) with HowExplanation (column M), which is merged
// once-per-answer at the AnchorRow — exactly like MaterialChange (L). ProvidedBy is unchanged in
// meaning but reads from column P in v02 (shifted +1 from v01's O). The DV fields and
// Explanations list are identical to GdAnswer. GdExplanationRow (I/J/K triplets, columns
// unshifted) is reused as-is from the v01 namespace.
public sealed record GdV02Answer(
    string? AnswerId,              // XrefId <answer-id> segment, e.g. "A-01"; null for bare-qid
    int AnchorRow,                 // min row of this answer (where merged G/H/L/M/P live)
    string? PreviousAnswer,        // column G
    string? Answer,                // column H
    string? MaterialChange,        // column L
    string? HowExplanation,        // column M (new in v02; merged once-per-answer at AnchorRow)
    string? ProvidedBy,            // column P (shifted from v01's O)
    IReadOnlyList<GdExplanationRow> Explanations,
    // DV fields — null at parse; stamped later by the per-answer DV patcher (deferred to v02 validation).
    string? AnswerDvType = null,
    string? AnswerDvFormula = null,
    string? AnswerDvOperator = null,
    string? AnswerDvFormula2 = null,
    string? MaterialChangeDvType = null,
    string? MaterialChangeDvFormula = null,
    string? MaterialChangeDvOperator = null,
    string? MaterialChangeDvFormula2 = null,
    // Resolved DV List allowed-values (inline / range-ref / named-range), populated in the
    // patch phase for List-typed answer / material-change cells. Null ⇒ unresolved.
    IReadOnlyList<string>? AnswerDvListValues = null,
    IReadOnlyList<string>? MaterialChangeDvListValues = null);
