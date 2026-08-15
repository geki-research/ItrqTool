using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.GeneralDataValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;

namespace ItrqTool.Tasks.GeneralDataInject;

// ── GdInjectAligner ───────────────────────────────────────────────────────────
//
// Cross-VERSION alignment for the GD inject (current v02 ⟷ previous v01): a pure
// qid-join FORK — no Hungarian matcher, no similarity MATRIX, no bonus constants, no
// Reconcile table. It is GdV01Aligner's cross-year arm, lifted to two concrete type
// parameters (GdV02Question current, GdV01Question previous) and emitting the generic
// CrossFormat model so the mapper reuses RLQ's consumption shape.
//
// The qid is the alignment unit (IAlignmentIdentity.XrefId = the substring before the
// first ':'). Per current question, by qid lookup then a THREE-way text decision
// (BLG-0077):
//   • qid present + text identical (Ordinal)      → Agree  (Previous = the v01 question)
//   • qid present + text differs, score >= thresh → Agree  (Previous = the v01 question);
//                                                   the mapper raises one Warning per such
//                                                   question — an accepted-but-changed text
//                                                   is never carried forward silently.
//   • qid present + text differs, score <  thresh → SameXrefIdTextDiverged (Previous = null)
//   • qid absent                                  → Neither                (Previous = null)
//   • current qid blank/duplicate → NotEvaluatedMalformedKey (+ a MalformedKey)
//
// WHY the threshold exists: the qid carries identity; the text carries only resemblance.
// Judging sameness by exact string equality misclassified a question whose wording merely
// drifted between years (a rolling "as of" date, a reworded clause) as a DIFFERENT question,
// and so silently failed to carry its previous-year values forward. The threshold restores
// the intended contract; it is supplied per run from GdInjectConfig.QidJoinSimilarityThreshold
// (default 0.50) and passed in as a scalar — this aligner reads no constant of its own.
//
// XrefIdConflict / NewXrefIdWithLookalike remain structurally impossible without the
// Hungarian matcher and are NEVER emitted — matching GdV01Aligner. Nothing here ranks or
// re-pairs questions: the qid alone decides WHICH previous question is compared, and the
// score only decides whether that one pairing is kept.
//
// REUSE boundary (MOVED by BLG-0077 — it previously read "does NOT reference
// TextSimilarity"): the fork still reuses the generic model + enums + IAlignmentIdentity
// only (CrossFormatMatch / CrossFormatAlignmentResult / CrossYearOutcome / MalformedKey /
// MalformedKeyReason), and still does NOT reference CrossFormatAligner.Align or
// HungarianAlgorithm. It NOW scores text similarity, via GdInjectTextComparison, which
// adapts the ItrqTool.Tasks.GeneralDataDiff copy of TextSimilarity — the same-domain copy,
// not the QuestionnaireValidation.Alignment one, so the inject path still does not reach
// into the validation core. The boundary that moved is "no similarity scoring at all";
// the boundary that held is "no validation-core machinery, no matcher".
// Key classification mirrors CrossFormatAligner.ClassifyKeys (blank → Blank, in-workbook
// duplicate → Duplicate) on BOTH workbooks, so the previous lookup is duplicate-safe and
// both sides contribute malformed keys.
public static class GdInjectAligner
{
    /// <param name="similarityThreshold">
    /// Minimum <see cref="GdInjectTextComparison.Score"/> at which a NON-identical text pair under
    /// one qid is still treated as the same question. Comparison is <c>&gt;=</c>: a score exactly
    /// equal to the threshold agrees. Supplied by the caller from
    /// <see cref="GdInjectConfig.QidJoinSimilarityThreshold"/>.
    /// <para>
    /// A scalar rather than the whole <see cref="GdInjectConfig"/>, deliberately: alignment needs
    /// this one number and nothing else the config carries (the two validation-config filenames are
    /// meaningless here), so a scalar keeps the signature honest about its real dependency and
    /// keeps the unit tests free of config construction. This mirrors the established precedent at
    /// <c>GdV01Profile.cs:111</c>, which likewise passes <c>threshold: config.DeviationThreshold</c>
    /// rather than the config object.
    /// </para>
    /// </param>
    public static CrossFormatAlignmentResult<GdV02Question, GdV01Question> Align(
        IReadOnlyList<GdV02Question> current,    // v02
        IReadOnlyList<GdV01Question> previous,   // v01
        double similarityThreshold)
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
            double? baseScore;

            if (previousClass.ValidByXref.TryGetValue(c.XrefId!, out var pv))
            {
                xrefIdCounterpart = pv;

                // The RAW similarity of the one pairing the qid selected — computed for every
                // counterpart, agreeing or not, so the score is reported on both sides of the
                // boundary. Never bonus-adjusted and never rounded: reproducible from the texts.
                var score = GdInjectTextComparison.Score(c.OriginalText, pv.OriginalText);
                baseScore = score;

                // Three-way. Identity is tested FIRST and unconditionally, so an identical pair
                // agrees whatever the threshold is; only a NON-identical pair is put to the score.
                if (GdInjectTextComparison.AreIdentical(c.OriginalText, pv.OriginalText) ||
                    score >= similarityThreshold)
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
                baseScore         = null;
            }

            // MatcherCandidate / MatcherBaseScore now carry the qid-selected previous question and
            // its raw score (BLG-0077). They travel together so the model's own documented pairing
            // — "MatcherBaseScore = BASE similarity of MatcherCandidate; null if no candidate" —
            // stays true; both are null exactly when no qid counterpart was found. "Candidate"
            // here means the question the qid chose to compare against, NOT a matcher's pick:
            // there is still no matcher, and adding one is explicitly out of scope.
            matches.Add(new CrossFormatMatch<GdV02Question, GdV01Question>(
                Current:            c,
                Outcome:            outcome,
                Previous:           previousMatch,
                XrefIdCounterpart:  xrefIdCounterpart,
                MatcherCandidate:   xrefIdCounterpart,
                MatcherBaseScore:   baseScore));
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
