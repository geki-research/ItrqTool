namespace ItrqTool.Tasks.QuestionnaireValidation.Checks;

using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

// ── ConditionalRequirement<T> — intra-year conditional-presence primitive ────
//
// Generic, format-independent extension check: the TARGET cell must not be blank
// if the TRIGGER cell holds any value from a CONFIG-SUPPLIED set. Both cells are
// CURRENT-YEAR cells on the same current-year question (intra-year; no template
// or cross-year baseline is consulted).
//
// Construct-bound: the profile supplies the selectors, column letters, and trigger
// set from config fields (never literals), mirroring the config-values principle
// of RequiredInputCell<T> (allowed) and CrossYearDeviationCell<T> (threshold).
//
// Gate: mirrors RequiredInputCell<T> — runs on every aligned row except
// NotEvaluatedMalformedKey (covered by the structure sweep). JoinedByXrefId AND
// AddedInResponse rows are evaluated; no template match is required or consulted.
//
// Trigger comparison: trim-both, StringComparison.Ordinal, case-sensitive (F1).
// A blank trigger never fires. A non-blank trigger is trimmed and compared against
// each trimmed configured token (Ordinal); if any matches, the target gate applies.
//
// Target gate: string.IsNullOrWhiteSpace — blank (including whitespace-only) counts
// as missing; a non-blank target passes silently.
//
// Id is role-templated: input-cell.{role}.conditionally-required-missing. Each
// instantiated role must produce ids unique across the assembled catalogue (the
// catalogue throws on duplicate ids).
//
// CellAddresses: the TARGET cell ({targetColumn}{row}) — the offending blank.
// The trigger column and its surfaced value appear in CheckResult for visibility.

public sealed class ConditionalRequirement<T> : IExtensionCheck<T> where T : class, IAlignmentIdentity
{
    private readonly Func<T, string?> _target;
    private readonly Func<T, string?> _trigger;
    private readonly Func<T, string?> _providedBy;
    private readonly string _targetColumn;
    private readonly string _triggerColumn;
    private readonly IReadOnlyList<string> _triggerValues;
    private readonly string _missingId;
    private readonly IReadOnlyList<FindingDescriptor> _descriptors;

    public ConditionalRequirement(
        Func<T, string?> targetValueSelector,
        Func<T, string?> triggerValueSelector,
        Func<T, string?> providedBySelector,
        string role,
        string targetColumn,
        string triggerColumn,
        IReadOnlyList<string> triggerValues,
        FindingEvaluation missingDefault = FindingEvaluation.Error)
    {
        _target     = targetValueSelector  ?? throw new ArgumentNullException(nameof(targetValueSelector));
        _trigger    = triggerValueSelector ?? throw new ArgumentNullException(nameof(triggerValueSelector));
        _providedBy = providedBySelector   ?? throw new ArgumentNullException(nameof(providedBySelector));
        if (string.IsNullOrWhiteSpace(role))          throw new ArgumentException("role must be non-empty.",          nameof(role));
        if (string.IsNullOrWhiteSpace(targetColumn))  throw new ArgumentException("targetColumn must be non-empty.",  nameof(targetColumn));
        if (string.IsNullOrWhiteSpace(triggerColumn)) throw new ArgumentException("triggerColumn must be non-empty.", nameof(triggerColumn));
        _triggerValues  = triggerValues ?? throw new ArgumentNullException(nameof(triggerValues));
        _targetColumn   = targetColumn;
        _triggerColumn  = triggerColumn;
        _missingId      = $"input-cell.{role}.conditionally-required-missing";
        _descriptors    = new[]
        {
            new FindingDescriptor(_missingId, missingDefault, ValidationCheck.ConditionalRequirement,
                "A cell that is required when a related trigger cell holds a configured value is empty."),
        };
    }

    public IReadOnlyList<FindingDescriptor> Descriptors => _descriptors;

    public IReadOnlyList<ValidationFinding> Run(AlignmentResult<T> alignment, FindingEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(alignment);
        ArgumentNullException.ThrowIfNull(emitter);

        var findings = new List<ValidationFinding>();
        foreach (var aq in alignment.Aligned)
        {
            // Malformed-key rows are covered by the structure sweep; skip all dependent
            // checks (mirrors RequiredInputCell<T>). Both cells are current-year, so the
            // check runs on JoinedByXrefId AND AddedInResponse rows.
            if (aq.WithinYear == WithinYearJoin.NotEvaluatedMalformedKey)
                continue;

            var cur     = aq.Current;
            int row     = cur.RowNumber;
            var trigger = _trigger(cur);

            // Blank trigger never fires — no configured token can match empty.
            if (string.IsNullOrWhiteSpace(trigger))
                continue;

            // Trigger gate: trim-both, Ordinal, case-sensitive (F1).
            var triggerTrimmed = trigger.Trim();
            var triggered = _triggerValues.Any(v =>
                string.Equals(v?.Trim(), triggerTrimmed, StringComparison.Ordinal));
            if (!triggered)
                continue;

            // Target requirement: blank (whitespace-only counts as blank) → emit.
            var target = _target(cur);
            if (string.IsNullOrWhiteSpace(target))
            {
                findings.Add(emitter.Emit(_missingId,
                    $"{_targetColumn}{row}", cur.QuestionNumber, cur.QuestionText,
                    requestedData: null, providedBy: _providedBy(cur),
                    $"{_targetColumn}{row} is required because {_triggerColumn}{row} is '{trigger}', but it is blank."));
            }
        }
        return findings;
    }
}
