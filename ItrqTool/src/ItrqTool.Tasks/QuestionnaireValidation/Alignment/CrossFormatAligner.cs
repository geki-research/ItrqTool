namespace ItrqTool.Tasks.QuestionnaireValidation.Alignment;

/// <summary>
/// Generic, sheet-agnostic CROSS-FORMAT aligner. A faithful verbatim lift of
/// <see cref="AlignmentEngine"/>'s cross-year computation (the similarity-matrix
/// build, the <see cref="HungarianAlgorithm"/> assignment, and the locked
/// reconciliation table), generalized to TWO type parameters so a current question
/// set of type <typeparamref name="TCur"/> can be reconciled against a previous
/// question set of a DIFFERENT concrete type <typeparamref name="TPrev"/>.
///
/// The within-year / template arm of <see cref="AlignmentEngine"/> is intentionally
/// DROPPED: inject has no empty template, only current and previous. Everything that
/// drives the cross-year outcome — matrix constants (section bonus 0.10, number bonus
/// 0.10, match threshold 0.50), the XrefId key-join, the confident-match rule, and the
/// malformed-key collection — is identical to the engine.
///
/// The <c>class</c> constraints preserve the engine's reference-identity semantics
/// (<see cref="object.ReferenceEquals"/> / <c>ReferenceEqualityComparer</c>): the
/// Agree outcome compares the matcher pick and the XrefId counterpart by object
/// identity. Both arms reach those previous objects through the SAME
/// <typeparamref name="TPrev"/> instances, so identity comparison is well-defined
/// across the two type parameters.
/// </summary>
public static class CrossFormatAligner
{
    private const double SectionBonus   = 0.10;
    private const double NumberBonus    = 0.10;
    private const double MatchThreshold = 0.50;

    public static CrossFormatAlignmentResult<TCur, TPrev> Align<TCur, TPrev>(
        IReadOnlyList<TCur> currentResponse,
        IReadOnlyList<TPrev> previousResponse)
        where TCur : class, IAlignmentIdentity
        where TPrev : class, IAlignmentIdentity
    {
        var malformed = new List<MalformedKey>();

        // ── Step A — malformed-key detection (independently per workbook) ──────
        var current  = ClassifyKeys(currentResponse,  ValidationWorkbook.CurrentResponse,  malformed);
        var previous = ClassifyKeys(previousResponse, ValidationWorkbook.PreviousResponse, malformed);

        // ── Step C (matrix part) — cross-year matcher, computed up front so the
        //    final assembly loop can pull each current's reconciliation result ──
        var crossYearByCurrent = ComputeCrossYear(current, previous);

        // ── Step D — assemble one CrossFormatMatch per current question, input order ──
        var matches = new List<CrossFormatMatch<TCur, TPrev>>(currentResponse.Count);

        foreach (var q in currentResponse)
        {
            bool ownKeyMalformed = current.MalformedRowNumbers.Contains(q.RowNumber);

            var cy = ownKeyMalformed
                ? CrossYearData<TPrev>.NotEvaluated
                : crossYearByCurrent[q];

            matches.Add(new CrossFormatMatch<TCur, TPrev>(
                Current: q,
                Outcome: cy.Outcome,
                Previous: cy.PreviousMatch,
                XrefIdCounterpart: cy.XrefIdCounterpart,
                MatcherCandidate: cy.MatcherCandidate,
                MatcherBaseScore: cy.MatcherBaseScore));
        }

        return new CrossFormatAlignmentResult<TCur, TPrev>(matches, malformed);
    }

    // ── Step C — cross-year reconciliation for every valid-key current question ─
    private static Dictionary<TCur, CrossYearData<TPrev>> ComputeCrossYear<TCur, TPrev>(
        KeyClassification<TCur> current,
        KeyClassification<TPrev> previous)
        where TCur : class, IAlignmentIdentity
        where TPrev : class, IAlignmentIdentity
    {
        // Matcher inputs are XrefId-BLIND (similarity uses QuestionText only); we
        // merely exclude malformed-key rows so un-evaluable rows stay out of
        // identity reasoning. Input order preserved.
        var matcherCurrents   = current.ValidInInputOrder;
        var matcherPreviouses = previous.ValidInInputOrder;

        int m = matcherCurrents.Count;
        int n = matcherPreviouses.Count;

        var result = new Dictionary<TCur, CrossYearData<TPrev>>(ReferenceEqualityComparer.Instance);

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

                TPrev? matcherCandidate   = j >= 0 ? matcherPreviouses[j] : null;
                double? matcherBaseScore  = j >= 0 ? baseSim[i, j] : null;

                // M = confident textual match (adjusted score over threshold).
                TPrev? m_ = (j >= 0 && adjSim[i, j] >= MatchThreshold)
                    ? matcherCandidate
                    : null;

                // X = valid-key previous question sharing current's valid XrefId (key-based).
                TPrev? x_ =
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
                TPrev? x_ =
                    previous.ValidByXref.TryGetValue(matcherCurrents[i].XrefId!, out var xq) ? xq : null;
                result[matcherCurrents[i]] = Reconcile<TPrev>(null, x_, null, null);
            }
        }

        return result;
    }

    // The locked reconciliation table. "M==X" compares by question IDENTITY
    // (same previousResponse object), not by text.
    private static CrossYearData<TPrev> Reconcile<TPrev>(
        TPrev? m,
        TPrev? x,
        TPrev? matcherCandidate,
        double? matcherBaseScore)
        where TPrev : class, IAlignmentIdentity
    {
        CrossYearOutcome outcome;
        TPrev? previousMatch = null;

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

        return new CrossYearData<TPrev>(outcome, previousMatch, x, matcherCandidate, matcherBaseScore);
    }

    // ── Step A helper — classify one workbook's keys, emitting malformed entries ─
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

        var validByXref = new Dictionary<string, T>(StringComparer.Ordinal);
        var validInOrder = new List<T>();
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

        return new KeyClassification<T>(validByXref, validInOrder, malformedRows);
    }

    private sealed record KeyClassification<T>(
        Dictionary<string, T> ValidByXref,
        IReadOnlyList<T> ValidInInputOrder,
        HashSet<int> MalformedRowNumbers)
        where T : class, IAlignmentIdentity;

    private readonly record struct CrossYearData<TPrev>(
        CrossYearOutcome Outcome,
        TPrev? PreviousMatch,
        TPrev? XrefIdCounterpart,
        TPrev? MatcherCandidate,
        double? MatcherBaseScore)
        where TPrev : class, IAlignmentIdentity
    {
        public static readonly CrossYearData<TPrev> NotEvaluated =
            new(CrossYearOutcome.NotEvaluatedMalformedKey, null, null, null, null);
    }
}
