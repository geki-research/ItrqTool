namespace ItrqTool.Tasks.ControlLevelQuestionValidation;

/// <summary>
/// Pure reconciliation logic for CLQ_v01. Aligns the current-year response
/// against the empty template (within-year) and the previous-year response
/// (cross-year, Option B). No config, no I/O, no cancellation — a pure
/// function over three already-parsed question lists.
///
/// Matrix constants mirror phase-1 (<c>ControlLevelQuestionDiff.QuestionDiffEngine</c>):
/// section bonus 0.10, number bonus 0.10, match threshold 0.50. There is NO
/// XrefId bonus in the matrix — XrefId independence from the textual matcher is
/// the whole point of Option B.
/// </summary>
public static class ClqAlignmentEngine
{
    private const double SectionBonus   = 0.10;
    private const double NumberBonus    = 0.10;
    private const double MatchThreshold = 0.50;

    public static ClqAlignmentResult Align(
        IReadOnlyList<InternalClqQuestion> currentResponse,
        IReadOnlyList<InternalClqQuestion> emptyTemplate,
        IReadOnlyList<InternalClqQuestion> previousResponse)
    {
        var malformed = new List<MalformedKey>();

        // ── Step A — malformed-key detection (independently per workbook) ──────
        var current  = ClassifyKeys(currentResponse,  ClqWorkbook.CurrentResponse,  malformed);
        var template = ClassifyKeys(emptyTemplate,    ClqWorkbook.EmptyTemplate,    malformed);
        var previous = ClassifyKeys(previousResponse, ClqWorkbook.PreviousResponse, malformed);

        // ── Step C (matrix part) — cross-year matcher, computed up front so the
        //    final assembly loop can pull each current's reconciliation result ──
        var crossYearByCurrent = ComputeCrossYear(current, previous);

        // ── Step B + Step D — assemble one AlignedQuestion per current question
        //    in input order ──────────────────────────────────────────────────
        var aligned = new List<AlignedQuestion>(currentResponse.Count);

        foreach (var q in currentResponse)
        {
            bool ownKeyMalformed = current.MalformedRowNumbers.Contains(q.RowNumber);

            // within-year (currentResponse vs emptyTemplate)
            WithinYearJoin withinYear;
            InternalClqQuestion? templateMatch = null;
            bool rowShifted = false;
            bool textMismatched = false;

            if (ownKeyMalformed)
            {
                withinYear = WithinYearJoin.NotEvaluatedMalformedKey;
            }
            else if (template.ValidByXref.TryGetValue(q.XrefId!, out var tmpl))
            {
                withinYear = WithinYearJoin.JoinedByXrefId;
                templateMatch = tmpl;
                rowShifted = tmpl.RowNumber != q.RowNumber;
                textMismatched = !string.Equals(q.OriginalText, tmpl.OriginalText, StringComparison.Ordinal);
            }
            else
            {
                withinYear = WithinYearJoin.AddedInResponse;
            }

            // cross-year (currentResponse vs previousResponse) — Option B
            var cy = ownKeyMalformed
                ? CrossYearData.NotEvaluated
                : crossYearByCurrent[q];

            aligned.Add(new AlignedQuestion(
                Current: q,
                WithinYear: withinYear,
                TemplateMatch: templateMatch,
                RowShifted: rowShifted,
                TextMismatched: textMismatched,
                CrossYear: cy.Outcome,
                PreviousMatch: cy.PreviousMatch,
                XrefIdCounterpart: cy.XrefIdCounterpart,
                MatcherCandidate: cy.MatcherCandidate,
                MatcherBaseScore: cy.MatcherBaseScore));
        }

        // WithinYearRemoved — valid-key template questions absent from current's valid keys.
        // Iterate emptyTemplate in input order; each valid key appears exactly once.
        var withinYearRemoved = new List<InternalClqQuestion>();
        foreach (var t in emptyTemplate)
        {
            if (string.IsNullOrWhiteSpace(t.XrefId)) continue;
            if (!template.ValidByXref.TryGetValue(t.XrefId, out var vt) || !ReferenceEquals(vt, t)) continue;
            if (!current.ValidByXref.ContainsKey(t.XrefId))
                withinYearRemoved.Add(t);
        }

        return new ClqAlignmentResult(aligned, withinYearRemoved, malformed);
    }

    // ── Step C — cross-year reconciliation for every valid-key current question ─
    private static Dictionary<InternalClqQuestion, CrossYearData> ComputeCrossYear(
        KeyClassification current,
        KeyClassification previous)
    {
        // Matcher inputs are XrefId-BLIND (similarity uses QuestionText only); we
        // merely exclude malformed-key rows so un-evaluable rows stay out of
        // identity reasoning. Input order preserved.
        var matcherCurrents   = current.ValidInInputOrder;
        var matcherPreviouses = previous.ValidInInputOrder;

        int m = matcherCurrents.Count;
        int n = matcherPreviouses.Count;

        var result = new Dictionary<InternalClqQuestion, CrossYearData>(ReferenceEqualityComparer.Instance);

        // Base + adjusted matrices, mirroring phase-1's Diff(old=previous, new=current)
        // orientation: rows = current, cols = previous.
        double[,]? baseSim = null;
        int[]? assignment = null;

        if (m > 0 && n > 0)
        {
            baseSim = new double[m, n];
            for (int i = 0; i < m; i++)
                for (int j = 0; j < n; j++)
                    baseSim[i, j] = TextSimilarity.Score(matcherCurrents[i].QuestionText,
                                                         matcherPreviouses[j].QuestionText);

            var adjSim = new double[m, n];
            for (int i = 0; i < m; i++)
            for (int j = 0; j < n; j++)
            {
                var cur = matcherCurrents[i];
                var prv = matcherPreviouses[j];
                double bonus = 0.0;

                if (!string.IsNullOrEmpty(cur.SectionName) &&
                    string.Equals(cur.SectionName, prv.SectionName, StringComparison.Ordinal))
                    bonus += SectionBonus;

                if (!string.IsNullOrEmpty(cur.QuestionNumber) &&
                    string.Equals(cur.QuestionNumber, prv.QuestionNumber, StringComparison.Ordinal))
                    bonus += NumberBonus;

                adjSim[i, j] = bonus > 0.0
                    ? Math.Min(1.0, baseSim[i, j] + bonus)
                    : baseSim[i, j];
            }

            assignment = HungarianAlgorithm.SolveMaximumAssignment(adjSim);

            for (int i = 0; i < m; i++)
            {
                int j = assignment[i];

                InternalClqQuestion? matcherCandidate = j >= 0 ? matcherPreviouses[j] : null;
                double? matcherBaseScore             = j >= 0 ? baseSim[i, j] : null;

                // M = confident textual match (adjusted score over threshold).
                InternalClqQuestion? m_ = (j >= 0 && adjSim[i, j] >= MatchThreshold)
                    ? matcherCandidate
                    : null;

                // X = valid-key previous question sharing current's valid XrefId (key-based).
                InternalClqQuestion? x_ =
                    previous.ValidByXref.TryGetValue(matcherCurrents[i].XrefId!, out var xq) ? xq : null;

                result[matcherCurrents[i]] = Reconcile(m_, x_, matcherCandidate, matcherBaseScore);
            }
        }
        else
        {
            // No matcher columns (n == 0) — every valid current is matcher-less.
            // X may still exist only if previous had valid keys, but n == 0 means
            // previous.ValidByXref is empty, so X is always null here → Neither.
            for (int i = 0; i < m; i++)
            {
                InternalClqQuestion? x_ =
                    previous.ValidByXref.TryGetValue(matcherCurrents[i].XrefId!, out var xq) ? xq : null;
                result[matcherCurrents[i]] = Reconcile(null, x_, null, null);
            }
        }

        return result;
    }

    // The locked reconciliation table. "M==X" compares by question IDENTITY
    // (same previousResponse object), not by text.
    private static CrossYearData Reconcile(
        InternalClqQuestion? m,
        InternalClqQuestion? x,
        InternalClqQuestion? matcherCandidate,
        double? matcherBaseScore)
    {
        CrossYearOutcome outcome;
        InternalClqQuestion? previousMatch = null;

        if (m is not null && x is not null)
        {
            if (ReferenceEquals(m, x))
            {
                outcome = CrossYearOutcome.Agree;
                previousMatch = m;
            }
            else
            {
                outcome = CrossYearOutcome.XrefIdConflict;
            }
        }
        else if (m is not null && x is null)
        {
            outcome = CrossYearOutcome.NewXrefIdWithLookalike;
        }
        else if (m is null && x is not null)
        {
            outcome = CrossYearOutcome.SameXrefIdTextDiverged;
        }
        else
        {
            outcome = CrossYearOutcome.Neither;
        }

        return new CrossYearData(outcome, previousMatch, x, matcherCandidate, matcherBaseScore);
    }

    // ── Step A helper — classify one workbook's keys, emitting malformed entries ─
    private static KeyClassification ClassifyKeys(
        IReadOnlyList<InternalClqQuestion> questions,
        ClqWorkbook workbook,
        List<MalformedKey> malformed)
    {
        var countByValue = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var q in questions)
            if (!string.IsNullOrWhiteSpace(q.XrefId))
                countByValue[q.XrefId] = countByValue.GetValueOrDefault(q.XrefId) + 1;

        var validByXref = new Dictionary<string, InternalClqQuestion>(StringComparer.Ordinal);
        var validInOrder = new List<InternalClqQuestion>();
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
                validInOrder.Add(q);
            }
        }

        return new KeyClassification(validByXref, validInOrder, malformedRows);
    }

    private sealed record KeyClassification(
        Dictionary<string, InternalClqQuestion> ValidByXref,
        IReadOnlyList<InternalClqQuestion> ValidInInputOrder,
        HashSet<int> MalformedRowNumbers);

    private readonly record struct CrossYearData(
        CrossYearOutcome Outcome,
        InternalClqQuestion? PreviousMatch,
        InternalClqQuestion? XrefIdCounterpart,
        InternalClqQuestion? MatcherCandidate,
        double? MatcherBaseScore)
    {
        public static readonly CrossYearData NotEvaluated =
            new(CrossYearOutcome.NotEvaluatedMalformedKey, null, null, null, null);
    }
}
