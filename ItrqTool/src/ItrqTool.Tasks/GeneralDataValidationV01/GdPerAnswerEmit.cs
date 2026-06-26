namespace ItrqTool.Tasks.GeneralDataValidationV01;

// ── GdPerAnswerEmit — the shared per-ANSWER emit + section-gate mechanism (C1 lock) ──
//
// The five C2 per-answer checks all share ONE emit shape so they don't each invent their own.
// A GD per-answer check:
//   1. iterates alignment.Aligned (qid grain),
//   2. applies the qid-grain gate it needs (WithinYear == JoinedByXrefId for template-needing
//      checks; CrossYear == Agree for deviation; "skip NotEvaluatedMalformedKey" for input-only),
//   3. applies the optional SECTION gate (this file) to the QUESTION,
//   4. iterates aq.Current.Answers (directly, or paired via GdAnswerJoin for template/previous),
//   5. emits ONE finding per offending answer-cell at CellAddress(column, answer) —
//      $"{column}{answer.AnchorRow}" for the merged once-per-answer cells (H answer / L
//      material-change), or $"{column}{explanationRow.RowNumber}" for the per-row I/J/K cells.
//
// ── BL-037 — the section gate (mechanism only; names deferred to C2) ──
// One C2 check needs more than the qid gate: the per-answer REQUIRED-INPUT check on the
// material-change column (L) must fire only for answers in a SUBSET of sections ("G-ST" onward,
// NOT "G-CO" — recon ⚠3). There is no existing section filter in the core primitives. The
// mechanism locked here is a SectionGate = Func<GdV01Question,bool> ctor parameter on the
// relevant C2 check, built from an explicit set of applicable section names. The CONCRETE set
// ("which sections are in scope") is deliberately NOT bound here — C2 pins it against the real
// GdV01Config / template (lesson 123) and decides whether it is a membership set or an ordinal
// "at/after G-ST" predicate. C1 only freezes the SHAPE: a Func<GdV01Question,bool> the check
// applies to aq.Current after the qid gate and before iterating answers.
//   OPEN NAME-BINDING for C2 (BL-037): the applicable-sections set + its source (config vs layout).

public static class GdPerAnswerEmit
{
    /// <summary>
    /// The per-answer cell address: the column letter joined to the answer's own anchor row,
    /// where the merged once-per-answer cells (G/H/L/O) sit. Per-row explanation cells (I/J/K)
    /// address off their <c>GdExplanationRow.RowNumber</c> instead and do not use this helper.
    /// </summary>
    public static string CellAddress(string column, GdAnswer answer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        ArgumentNullException.ThrowIfNull(answer);
        return $"{column}{answer.AnchorRow}";
    }

    /// <summary>
    /// The no-op section gate: every question is in scope. The default for checks that have no
    /// section restriction (e.g. the per-answer frozen-constraint / conformance / deviation
    /// checks, which gate at qid + answer grain only).
    /// </summary>
    public static readonly Func<GdV01Question, bool> AllSections = static _ => true;

    /// <summary>
    /// Builds a section gate that admits a question iff its <see cref="GdV01Question.SectionName"/>
    /// is in <paramref name="applicableSections"/>. The mechanism C2's material-change (L)
    /// required-input check uses; C2 supplies the concrete section set (BL-037).
    /// </summary>
    public static Func<GdV01Question, bool> SectionsIn(IReadOnlySet<string> applicableSections)
    {
        ArgumentNullException.ThrowIfNull(applicableSections);
        return q => applicableSections.Contains(q.SectionName);
    }
}
