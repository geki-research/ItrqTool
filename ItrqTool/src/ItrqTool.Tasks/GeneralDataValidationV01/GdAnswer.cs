namespace ItrqTool.Tasks.GeneralDataValidationV01;

// One answer within a GD question. A GD question may have MORE THAN ONE answer (XrefId
// segment <answer-id>, e.g. "A-01", "A-02"); the collapsed bare-qid case (single-row
// question, no <answer-id> segment) is modelled as ONE implicit answer with AnswerId == null.
//
// The once-per-answer cells (previous-answer G, answer H, material-change L, provided-by O)
// are MERGED across the answer's explanation rows: their value sits on the answer's anchor
// (top) row and reads blank on continuation rows, so they are read from AnchorRow. The per-row
// explanation triplet (I / J / K) is accumulated across the answer's rows in Explanations.
//
// The DV fields are null at parse time and stamped later by a per-answer DV-patch pass (a
// later chunk), mirroring the RLQ record. They are trailing-optional with null defaults so no
// existing construction site changes when the patcher reassigns them.
public sealed record GdAnswer(
    string? AnswerId,            // XrefId <answer-id> segment, e.g. "A-01"; null for bare-qid collapsed
    int AnchorRow,               // min row of this answer (where merged G/H/L/O live)
    string? PreviousAnswer,      // column G
    string? Answer,              // column H
    string? MaterialChange,      // column L
    string? ProvidedBy,          // column O
    IReadOnlyList<GdExplanationRow> Explanations,
    // DV fields — null at parse; stamped later by the per-answer DV patcher (later chunk).
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
    IReadOnlyList<string>? MaterialChangeDvListValues = null,
    // Native CLR value of the answer cell H (a boxed double for numeric cells), stamped alongside
    // the answer DV fields by GdDvPatcher.StampInline. Threaded into DV conformance as sourceNative
    // so a comma-decimal numeric answer is compared numerically, not text-parsed under an invariant
    // locale (BLG-0022/decimal-conformance — the GD twin of the RLQ AnswerNativeValue). Trailing-
    // optional; null (the default) keeps the invariant text-parse path byte-for-byte. Only the H
    // (answer) role carries it — material-change L is a List DV, where native never fires.
    object? AnswerNativeValue = null);
