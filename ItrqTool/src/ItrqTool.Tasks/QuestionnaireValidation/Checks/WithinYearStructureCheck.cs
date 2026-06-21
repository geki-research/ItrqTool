namespace ItrqTool.Tasks.QuestionnaireValidation.Checks;

using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

// ── WithinYearStructureCheck<T> — within-year structure: question removed + added ──
//
// Generic extension reproducing ClqBaselineChecks Phase 2 (within-year removed) and the
// Phase 3 AddedInResponse branch. Two descriptors:
//   structure.question-removed — a valid-key template question with no current counterpart
//   structure.question-added   — a current question whose valid XrefId is absent from the template
// Both Error, both ValidationCheck.Structure. Descriptor descriptions and check-result
// format strings are lifted VERBATIM from CLQ (parity by reproduction, not shared code —
// ClqBaselineChecks is NOT imported).
//
// FILTER-FREE by design. The identity-integrity gate (MalformedKeyCheck in the profile's
// IdentityGateCheck slot, HaltOnMalformedKeys=true) halts the chain BEFORE any extension
// runs whenever a malformed XrefId key exists, so this check only ever sees clean keys.
// It therefore performs NO malformed-key handling and applies NO suppression filter on
// alignment.MalformedKeys — the parked 3a branch added a row-based filter that
// over-suppressed genuine removals; that filter is deliberately NOT reintroduced.
//
// Row-shift (Phase 3 JoinedByXrefId + RowShifted) is intentionally OUT OF SCOPE here — it
// is finding 3b. JoinedByXrefId rows (shifted or not) and NotEvaluatedMalformedKey rows
// produce no finding.

public sealed class WithinYearStructureCheck<T> : IExtensionCheck<T>
    where T : class, IAlignmentIdentity
{
    private const string RemovedId = "structure.question-removed";
    private const string AddedId   = "structure.question-added";
    private readonly Func<T, string?> _providedBy;
    private readonly string _column;
    private readonly IReadOnlyList<FindingDescriptor> _descriptors;

    public WithinYearStructureCheck(
        Func<T, string?> providedBySelector,
        string column,
        FindingEvaluation removedDefault = FindingEvaluation.Error,
        FindingEvaluation addedDefault   = FindingEvaluation.Error)
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

        // ── Phase 3 (AddedInResponse branch only — row-shift is 3b) ──
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

        return findings;
    }
}
