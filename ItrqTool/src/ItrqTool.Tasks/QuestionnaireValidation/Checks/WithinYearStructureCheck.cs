namespace ItrqTool.Tasks.QuestionnaireValidation.Checks;

using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

// ── WithinYearStructureCheck<T> — within-year structure: removed + added + row-shifted ──
//
// Generic extension reproducing ClqBaselineChecks Phase 2 (within-year removed) and the
// Phase 3 AddedInResponse + RowShifted branches. Three descriptors:
//   structure.question-removed      — a valid-key template question with no current counterpart
//   structure.question-added        — a current question whose valid XrefId is absent from the template
//   structure.question-row-shifted  — a matched question out of its expected position in the
//                                      template question SEQUENCE (order-based — see below)
// All Error, all ValidationCheck.Structure. Descriptor descriptions and the removed/added
// check-result format strings are lifted VERBATIM from CLQ (parity by reproduction, not
// shared code — ClqBaselineChecks is NOT imported).
//
// ORDER-BASED ROW-SHIFT (the one place RLQ deliberately diverges from CLQ's computation).
// CLQ compares ABSOLUTE rows (template row != current row); RLQ surfaces the SAME user-facing
// concept (id structure.question-row-shifted is reused) but computes it from SEQUENCE order so
// that an upstream height change (a multi-row question gaining/losing rows) does not spuriously
// shift everything below it. Algorithm: among matched (JoinedByXrefId) questions taken in
// CURRENT order, assign each its template RANK (1..k by ascending template-match RowNumber;
// template anchor rows are unique ⇒ no ties). Compute a rank-minimal Longest Increasing
// Subsequence of that rank sequence (O(k²) DP; ties broken toward keeping the SMALLER ranks).
// Questions whose rank is NOT in the kept LIS are flagged — they are the ones out of order.
//
// FILTER-FREE by design. The identity-integrity gate (MalformedKeyCheck in the profile's
// IdentityGateCheck slot, HaltOnMalformedKeys=true) halts the chain BEFORE any extension
// runs whenever a malformed XrefId key exists, so this check only ever sees clean keys.
// It therefore performs NO malformed-key handling and applies NO suppression filter on
// alignment.MalformedKeys — the parked 3a branch added a row-based filter that
// over-suppressed genuine removals; that filter is deliberately NOT reintroduced. The
// matched-only scope of the row-shift pass already excludes malformed and added/removed rows.

public sealed class WithinYearStructureCheck<T> : IExtensionCheck<T>
    where T : class, IAlignmentIdentity
{
    private const string RemovedId    = "structure.question-removed";
    private const string AddedId      = "structure.question-added";
    private const string RowShiftedId = "structure.question-row-shifted";
    private readonly Func<T, string?> _providedBy;
    private readonly string _column;
    private readonly IReadOnlyList<FindingDescriptor> _descriptors;

    public WithinYearStructureCheck(
        Func<T, string?> providedBySelector,
        string column,
        FindingEvaluation removedDefault   = FindingEvaluation.Error,
        FindingEvaluation addedDefault     = FindingEvaluation.Error,
        FindingEvaluation rowShiftDefault  = FindingEvaluation.Error)
    {
        _providedBy = providedBySelector ?? throw new ArgumentNullException(nameof(providedBySelector));
        if (string.IsNullOrWhiteSpace(column))
            throw new ArgumentException("column must be non-empty.", nameof(column));
        _column = column;
        _descriptors = new[]
        {
            new FindingDescriptor(RemovedId, removedDefault, ValidationCheck.Structure,
                "A question present in the empty template (by identity key) is absent from the organisational unit's response."),
            new FindingDescriptor(AddedId, addedDefault, ValidationCheck.Structure,
                "A question present in the response (by identity key) is absent from the empty template; the response introduced a row the template did not declare."),
            new FindingDescriptor(RowShiftedId, rowShiftDefault, ValidationCheck.Structure,
                "A matched question appears out of its expected position in the template question sequence."),
        };
    }

    public IReadOnlyList<FindingDescriptor> Descriptors => _descriptors;

    public IReadOnlyList<ValidationFinding> Run(AlignmentResult<T> alignment, FindingEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(alignment);
        ArgumentNullException.ThrowIfNull(emitter);

        var findings = new List<ValidationFinding>();

        // ── Phase 2: within-year removed (template questions absent from response) ─
        // NO filter — emit one finding per WithinYearRemoved entry (gate guarantees clean keys).
        foreach (var removed in alignment.WithinYearRemoved)
        {
            findings.Add(emitter.Emit(RemovedId,
                $"{_column}{removed.RowNumber}",
                questionNumber: removed.QuestionNumber, questionText: removed.QuestionText,
                requestedData: null, providedBy: null,   // template-side: no responder
                $"Template question (identity key '{removed.XrefId}', template row {removed.RowNumber}) " +
                "is absent from the response."));
        }

        // ── Phase 3a (AddedInResponse branch) ──
        foreach (var aq in alignment.Aligned)
        {
            if (aq.WithinYear != WithinYearJoin.AddedInResponse) continue;

            var cur = aq.Current;
            int row = cur.RowNumber;
            findings.Add(emitter.Emit(AddedId,
                $"{_column}{row}", cur.QuestionNumber, cur.QuestionText,
                requestedData: null, providedBy: _providedBy(cur),
                $"Response question (identity key '{cur.XrefId}', row {row}) is absent from the empty template."));
        }

        // ── Phase 3b (order-based row-shift — matched questions out of template sequence) ──
        EmitRowShifts(alignment, emitter, findings);

        return findings;
    }

    // Order-based row-shift detection. See the class header for the algorithm rationale.
    private void EmitRowShifts(
        AlignmentResult<T> alignment, FindingEmitter emitter, List<ValidationFinding> findings)
    {
        // Matched questions in CURRENT order (alignment.Aligned preserves current-sheet input order).
        var matched = alignment.Aligned
            .Where(aq => aq.WithinYear == WithinYearJoin.JoinedByXrefId)
            .ToList();
        int k = matched.Count;
        if (k < 2) return; // 0 or 1 matched question can never be out of order

        // Template rank 1..k by ascending template-match RowNumber (unique anchor rows ⇒ no ties).
        // byRank[r-1] is the matched question of rank r; rankOf[i] is the rank of current-order item i.
        var byRank = matched
            .Select((aq, idx) => (aq, idx))
            .OrderBy(t => t.aq.TemplateMatch!.RowNumber)
            .ToList();
        var rankOf = new int[k];
        for (int r = 1; r <= k; r++)
            rankOf[byRank[r - 1].idx] = r;

        // f[i] = length of the longest rank-increasing subsequence STARTING at i (O(k²) DP).
        var f = new int[k];
        for (int i = k - 1; i >= 0; i--)
        {
            int best = 0;
            for (int j = i + 1; j < k; j++)
                if (rankOf[j] > rankOf[i] && f[j] > best) best = f[j];
            f[i] = best + 1;
        }
        int lisLength = 0;
        foreach (var len in f) if (len > lisLength) lisLength = len;

        // Greedy rank-minimal reconstruction: at each step pick the SMALLEST rank that is still
        // reachable (after the previous pick, rank strictly larger) and from which the remaining
        // length completes (f == remaining; with lisLength = global max, no reachable item can
        // exceed the remaining length, so this is exactly the lexicographically-smallest LIS).
        var kept = new bool[k];
        int cursor = -1, lastRank = 0, remaining = lisLength;
        while (remaining > 0)
        {
            int pick = -1;
            for (int j = cursor + 1; j < k; j++)
            {
                if (rankOf[j] <= lastRank) continue;
                if (f[j] != remaining) continue;
                if (pick == -1 || rankOf[j] < rankOf[pick]) pick = j;
            }
            kept[pick] = true;
            cursor = pick;
            lastRank = rankOf[pick];
            remaining--;
        }

        // Flag every matched question NOT in the kept LIS, in current order.
        for (int i = 0; i < k; i++)
        {
            if (kept[i]) continue;

            var cur = matched[i].Current;
            int rank = rankOf[i];
            // Expected predecessor: the matched question one rank earlier in the template sequence.
            string expected = rank > 1
                ? $"'{byRank[rank - 2].aq.Current.XrefId}'"
                : "the start of the sequence";
            // Actual predecessor: the matched question immediately before it in current order.
            string actual = i > 0
                ? $"'{matched[i - 1].Current.XrefId}'"
                : "the start of the sequence";

            findings.Add(emitter.Emit(RowShiftedId,
                $"{_column}{cur.RowNumber}", cur.QuestionNumber, cur.QuestionText,
                requestedData: null, providedBy: _providedBy(cur),
                $"Question (identity key '{cur.XrefId}') is shifted from its expected position in the " +
                $"question sequence (expected to follow {expected}, found following {actual})."));
        }
    }
}
