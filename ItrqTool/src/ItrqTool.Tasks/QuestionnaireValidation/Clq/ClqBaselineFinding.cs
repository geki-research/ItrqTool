namespace ItrqTool.Tasks.QuestionnaireValidation.Clq;

// ── CLQ baseline finding set (CLQ specialization) ────────────────────────────
//
// A one-time, independent copy of CLQ_v01's ClqFinding enum (all 18 members,
// names + order verbatim). It does NOT reference v01's ClqFinding — the two are
// independent copies. The CLQ baseline set is a specialization handed to the
// sheet-agnostic finding infrastructure (FindingCatalogue / FindingEmitter); the
// generic infrastructure never references this set.

public enum ClqBaselineFinding
{
    XrefIdEmptyOrDuplicated,
    QuestionRemoved,
    QuestionAdded,
    QuestionRowShifted,
    NumberFormatUnrecognized,
    ReferenceTextAltered,
    PreviousAnswerAltered,
    AnswerValidationRuleChanged,
    AnswerMissing,
    AnswerNotInAllowedSet,
    StrengthsMissing,
    WeaknessesMissing,
    XrefIdConflict,
    NewXrefIdResemblesPrevious,
    SameXrefIdTextDiverged,
    NoPreviousBaseline,
    AnswerDeviation,
    PreviousAnswerUnusable
}
