using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.GeneralDataValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;

namespace ItrqTool.Tasks.GeneralDataInject;

// ── GdInjectAligner ───────────────────────────────────────────────────────────
//
// Cross-VERSION alignment for the GD inject (current v02 ⟷ previous v01): a pure
// qid-join FORK — no Hungarian matcher, no similarity matrix, no bonus/threshold
// constants, no Reconcile table. It is GdV01Aligner's cross-year arm, lifted to two
// concrete type parameters (GdV02Question current, GdV01Question previous) and emitting
// the generic CrossFormat model so the mapper reuses RLQ's consumption shape.
//
// The qid is the alignment unit (IAlignmentIdentity.XrefId = the substring before the
// first ':'). Per current question, by qid lookup + Ordinal OriginalText compare:
//   • qid present + text equal  → Agree                  (Previous = the v01 question)
//   • qid present + text differs → SameXrefIdTextDiverged (Previous = null)
//   • qid absent                 → Neither                (Previous = null)
//   • current qid blank/duplicate → NotEvaluatedMalformedKey (+ a MalformedKey)
//
// XrefIdConflict / NewXrefIdWithLookalike are structurally impossible without the
// Hungarian matcher and are NEVER emitted — matching GdV01Aligner.
//
// REUSE boundary: the fork reuses the generic model + enums + IAlignmentIdentity only
// (CrossFormatMatch / CrossFormatAlignmentResult / CrossYearOutcome / MalformedKey /
// MalformedKeyReason). It does NOT reference CrossFormatAligner.Align, TextSimilarity,
// or HungarianAlgorithm. Key classification mirrors CrossFormatAligner.ClassifyKeys
// (blank → Blank, in-workbook duplicate → Duplicate) on BOTH workbooks, so the previous
// lookup is duplicate-safe and both sides contribute malformed keys.
public static class GdInjectAligner
{
    public static CrossFormatAlignmentResult<GdV02Question, GdV01Question> Align(
        IReadOnlyList<GdV02Question> current,    // v02
        IReadOnlyList<GdV01Question> previous)   // v01
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);

        var malformed = new List<MalformedKey>();

        var currentClass  = ClassifyKeys(current,  ValidationWorkbook.CurrentResponse,  malformed);
        var previousClass = ClassifyKeys(previous, ValidationWorkbook.PreviousResponse, malformed);

        var matches = new List<CrossFormatMatch<GdV02Question, GdV01Question>>(current.Count);

        foreach (var c in current)
        {
            // current's OWN key malformed → cross-year not evaluated, no previous.
            if (currentClass.MalformedRows.Contains(c.RowNumber))
            {
                matches.Add(new CrossFormatMatch<GdV02Question, GdV01Question>(
                    Current:            c,
                    Outcome:            CrossYearOutcome.NotEvaluatedMalformedKey,
                    Previous:           null,
                    XrefIdCounterpart:  null,
                    MatcherCandidate:   null,
                    MatcherBaseScore:   null));
                continue;
            }

            CrossYearOutcome outcome;
            GdV01Question? previousMatch;
            GdV01Question? xrefIdCounterpart;

            if (previousClass.ValidByXref.TryGetValue(c.XrefId!, out var pv))
            {
                xrefIdCounterpart = pv;
                if (string.Equals(c.OriginalText, pv.OriginalText, StringComparison.Ordinal))
                {
                    outcome       = CrossYearOutcome.Agree;
                    previousMatch = pv;
                }
                else
                {
                    outcome       = CrossYearOutcome.SameXrefIdTextDiverged;
                    previousMatch = null;
                }
            }
            else
            {
                outcome           = CrossYearOutcome.Neither;
                previousMatch     = null;
                xrefIdCounterpart = null;
            }

            // Matcher-only fields (candidate / score) at their no-match defaults — the qid-join
            // has no Hungarian candidate. The mapper reads only Current / Outcome / Previous.
            matches.Add(new CrossFormatMatch<GdV02Question, GdV01Question>(
                Current:            c,
                Outcome:            outcome,
                Previous:           previousMatch,
                XrefIdCounterpart:  xrefIdCounterpart,
                MatcherCandidate:   null,
                MatcherBaseScore:   null));
        }

        return new CrossFormatAlignmentResult<GdV02Question, GdV01Question>(matches, malformed);
    }

    // Mirror of CrossFormatAligner.ClassifyKeys: blank → Blank, in-workbook duplicate → Duplicate.
    private static KeyClassification<T> ClassifyKeys<T>(
        IReadOnlyList<T> questions,
        ValidationWorkbook workbook,
        List<MalformedKey> malformed)
        where T : class, IAlignmentIdentity
    {
        var countByValue = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var q in questions)
            if (!string.IsNullOrWhiteSpace(q.XrefId))
                countByValue[q.XrefId] = countByValue.GetValueOrDefault(q.XrefId) + 1;

        var validByXref   = new Dictionary<string, T>(StringComparer.Ordinal);
        var malformedRows = new HashSet<int>();

        foreach (var q in questions) // input order → stable malformed-entry order
        {
            if (string.IsNullOrWhiteSpace(q.XrefId))
            {
                malformed.Add(new MalformedKey(workbook, q.RowNumber, q.XrefId, MalformedKeyReason.Blank));
                malformedRows.Add(q.RowNumber);
            }
            else if (countByValue[q.XrefId] >= 2)
            {
                malformed.Add(new MalformedKey(workbook, q.RowNumber, q.XrefId, MalformedKeyReason.Duplicate));
                malformedRows.Add(q.RowNumber);
            }
            else
            {
                validByXref[q.XrefId] = q;
            }
        }

        return new KeyClassification<T>(validByXref, malformedRows);
    }

    private sealed record KeyClassification<T>(
        Dictionary<string, T> ValidByXref,
        HashSet<int> MalformedRows)
        where T : class, IAlignmentIdentity;
}
