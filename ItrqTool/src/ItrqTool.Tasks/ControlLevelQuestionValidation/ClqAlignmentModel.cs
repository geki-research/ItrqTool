namespace ItrqTool.Tasks.ControlLevelQuestionValidation;

// ── Alignment output model ───────────────────────────────────────────────────
//
// Produced by ClqAlignmentEngine. Consumed by chunk 3, which turns these
// structures into findings. This chunk emits NO severities — the default
// outcome -> severity map lives in chunk 3's (overridable) config:
//   Agree                  -> clean, or Warning if |answer delta| >= 2
//   XrefIdConflict         -> Error
//   NewXrefIdWithLookalike -> Warning   (attention: rescope vs fat-finger)
//   SameXrefIdTextDiverged -> Warning   (attention: heavy rewrite vs reused key)
//   Neither                -> Information
// For the two attention-raising outcomes (NewXrefIdWithLookalike,
// SameXrefIdTextDiverged) Option B holds: the previous is NEVER auto-used as a
// deviation / F-integrity baseline (PreviousMatch stays null) — the finding
// asks the human to verify.

public enum ClqWorkbook { CurrentResponse, EmptyTemplate, PreviousResponse }

public enum MalformedKeyReason { Blank, Duplicate }

public enum WithinYearJoin
{
    JoinedByXrefId,            // a valid-key template question shares this XrefId
    AddedInResponse,           // current's (valid) XrefId is absent from the template
    NotEvaluatedMalformedKey   // current's OWN XrefId is blank or duplicated within currentResponse
}

public enum CrossYearOutcome
{
    Agree,                     // case 1: key + text agree on the SAME previous question → confident; previous IS the baseline
    XrefIdConflict,            // case 2: matcher confidently matches prevA but current.XrefId points to prevB (A!=B) →
                               //   the key and the text disagree about identity. (chunk 3 default: Error)
    NewXrefIdWithLookalike,    // case 3: current has a NEW XrefId (absent from previous) BUT a confident textual twin
                               //   exists in previous → legit rescope OR fat-fingered/mistyped key. RAISE ATTENTION.
                               //   (chunk 3 default: Warning)
    SameXrefIdTextDiverged,    // case 4: current shares an XrefId with a previous question (key asserts SAME identity)
                               //   BUT the text diverged below the match threshold → legit heavy rewrite OR reused key
                               //   that should have been retired. RAISE ATTENTION. (chunk 3 default: Warning)
    Neither,                   // case 5: NO XrefId counterpart AND no textual match → ordinary new/orphan. (default: Information)
    NotEvaluatedMalformedKey   // current's OWN XrefId is malformed → cross-year not evaluated
}

public sealed record MalformedKey(
    ClqWorkbook Workbook,
    int RowNumber,
    string? XrefId,            // the offending value: null/empty for Blank; the duplicated value for Duplicate
    MalformedKeyReason Reason);

public sealed record AlignedQuestion(
    InternalClqQuestion Current,
    // within-year (currentResponse vs emptyTemplate)
    WithinYearJoin WithinYear,
    InternalClqQuestion? TemplateMatch,   // non-null IFF WithinYear == JoinedByXrefId
    bool RowShifted,                      // meaningful only when JoinedByXrefId: template row != current row
    bool TextMismatched,                  // meaningful only when JoinedByXrefId: raw OriginalText differs (exact)
    // cross-year (currentResponse vs previousResponse) — Option B
    CrossYearOutcome CrossYear,
    InternalClqQuestion? PreviousMatch,   // CONFIDENT baseline (deviation/F-integrity); non-null IFF CrossYear == Agree
    InternalClqQuestion? XrefIdCounterpart,// valid-key previous question sharing current's valid XrefId; non-null when
                                          //   one exists (Agree / XrefIdConflict / SameXrefIdTextDiverged). For messaging
                                          //   only — NEVER auto-used as a baseline (it surfaces as PreviousMatch only on Agree).
    InternalClqQuestion? MatcherCandidate,// matcher's textual Hungarian pick (informational; may equal PreviousMatch on Agree); null if none
    double? MatcherBaseScore);            // BASE similarity of MatcherCandidate (NOT adjusted); null if no candidate

public sealed record ClqAlignmentResult(
    IReadOnlyList<AlignedQuestion> Aligned,               // one per currentResponse question, input order preserved
    IReadOnlyList<InternalClqQuestion> WithinYearRemoved, // valid-key template questions with no currentResponse counterpart
    IReadOnlyList<MalformedKey> MalformedKeys);           // across all three workbooks
