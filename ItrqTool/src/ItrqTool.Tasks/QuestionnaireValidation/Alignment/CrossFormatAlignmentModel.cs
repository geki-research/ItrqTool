namespace ItrqTool.Tasks.QuestionnaireValidation.Alignment;

// ── Generic CROSS-FORMAT alignment output model ───────────────────────────────
//
// Output of CrossFormatAligner.Align<TCur, TPrev>. The cross-format sibling of
// AlignmentResult<T>: it carries ONLY the cross-year reconciliation (no within-year
// / template arm — inject has no template). A current question of type TCur is
// reconciled against a previous question of a DIFFERENT concrete type TPrev,
// reusing the exact same matcher + locked reconciliation table as AlignmentEngine.
//
// The downstream inject task injects only on the confident Agree outcome (where
// Previous is non-null) and warns on the ambiguous ones. The extra context fields
// (XrefIdCounterpart, MatcherCandidate, MatcherBaseScore) are for those warnings —
// they are NEVER auto-used as an injection source.
//
// Default outcome -> severity guidance mirrors AlignmentModel:
//   Agree                  -> confident; Previous IS the injection source
//   XrefIdConflict         -> Error      (key and text disagree about identity)
//   NewXrefIdWithLookalike -> Warning    (rescope vs fat-finger)
//   SameXrefIdTextDiverged -> Warning    (heavy rewrite vs reused key)
//   Neither                -> Information (ordinary new/orphan)
//   NotEvaluatedMalformedKey -> current's own XrefId blank/duplicated

public sealed record CrossFormatMatch<TCur, TPrev>(
    TCur Current,
    CrossYearOutcome Outcome,
    TPrev? Previous,            // CONFIDENT match (injection source); non-null IFF Outcome == Agree
    TPrev? XrefIdCounterpart,   // valid-key previous sharing current's valid XrefId; messaging only — never injected
    TPrev? MatcherCandidate,    // matcher's textual Hungarian pick (informational; may equal Previous on Agree)
    double? MatcherBaseScore)   // BASE similarity of MatcherCandidate (NOT bonus-adjusted); null if no candidate
    where TCur : class, IAlignmentIdentity
    where TPrev : class, IAlignmentIdentity;

public sealed record CrossFormatAlignmentResult<TCur, TPrev>(
    IReadOnlyList<CrossFormatMatch<TCur, TPrev>> Matches,   // one per current question, input order preserved
    IReadOnlyList<MalformedKey> MalformedKeys)              // current + previous malformed XrefIds
    where TCur : class, IAlignmentIdentity
    where TPrev : class, IAlignmentIdentity;
