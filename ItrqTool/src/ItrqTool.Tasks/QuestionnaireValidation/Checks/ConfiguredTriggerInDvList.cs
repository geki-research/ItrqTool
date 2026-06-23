namespace ItrqTool.Tasks.QuestionnaireValidation.Checks;

using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

// Config-validity check (RLQ-v02 Rule 2): every configured trigger value must be a member of the trigger
// column's DV list (the controlled vocabulary, resolved from the TEMPLATE). A configured value that is not a
// member is a CONFIG defect (Fatal), not a response defect. Config-level: emitted ONCE against the first
// resolved template DV list. Silent when no template-aligned question exists at all (the absence of alignment
// is surfaced by structure/identity-gate checks).
//
// Gate: JoinedByXrefId (the DV list lives on the template match).
//
// Cardinality (ONCE):
//   - Scan alignment.Aligned for the first JoinedByXrefId question whose template-match DV list is non-null
//     (the representative list). Also track whether ANY JoinedByXrefId question exists.
//   - If a representative list is found: for each configured trigger NOT a member, emit ONE Fatal
//     config.{role}.trigger-not-in-dv-list (ConfigConsistency).
//   - Else if anyTemplateAligned (template questions exist but the vocabulary resolves nowhere): emit ONE
//     Fatal config.{role}.dv-list-unresolvable (ConfigConsistency). Do NOT stay silent on a real gap.
//   - Else (no JoinedByXrefId question at all): silent — no vocabulary to check; absence surfaced elsewhere.
//
// CellAddresses: the trigger column letter only (e.g. "L") — config-level finding, not a specific cell.
// QuestionNumber/QuestionText/ProvidedBy: null (no per-question attribution).

public sealed class ConfiguredTriggerInDvList<T> : IExtensionCheck<T> where T : class, IAlignmentIdentity
{
    private readonly Func<T, IReadOnlyList<string>?> _triggerDvListValues;
    private readonly string _triggerColumn;
    private readonly IReadOnlyList<string> _configuredTriggerValues;
    private readonly string _notInListId;
    private readonly string _unresolvableId;
    private readonly IReadOnlyList<FindingDescriptor> _descriptors;

    public ConfiguredTriggerInDvList(
        Func<T, IReadOnlyList<string>?> triggerDvListValuesSelector,
        string role,
        string triggerColumn,
        IReadOnlyList<string> configuredTriggerValues,
        FindingEvaluation notInListDefault = FindingEvaluation.Fatal,
        FindingEvaluation unresolvableDefault = FindingEvaluation.Fatal)
    {
        _triggerDvListValues = triggerDvListValuesSelector ?? throw new ArgumentNullException(nameof(triggerDvListValuesSelector));
        if (string.IsNullOrWhiteSpace(role))          throw new ArgumentException("role must be non-empty.", nameof(role));
        if (string.IsNullOrWhiteSpace(triggerColumn)) throw new ArgumentException("triggerColumn must be non-empty.", nameof(triggerColumn));
        _configuredTriggerValues = configuredTriggerValues ?? throw new ArgumentNullException(nameof(configuredTriggerValues));
        _triggerColumn = triggerColumn;
        _notInListId    = $"config.{role}.trigger-not-in-dv-list";
        _unresolvableId = $"config.{role}.dv-list-unresolvable";
        _descriptors = new[]
        {
            new FindingDescriptor(_notInListId, notInListDefault, ValidationCheck.ConfigConsistency,
                "A configured trigger value is not a member of the data-validation list defined on the template."),
            new FindingDescriptor(_unresolvableId, unresolvableDefault, ValidationCheck.ConfigConsistency,
                "The trigger column's controlled vocabulary (data-validation list) could not be resolved from the template, so the configured trigger could not be verified."),
        };
    }

    public IReadOnlyList<FindingDescriptor> Descriptors => _descriptors;

    public IReadOnlyList<ValidationFinding> Run(AlignmentResult<T> alignment, FindingEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(alignment);
        ArgumentNullException.ThrowIfNull(emitter);

        var findings = new List<ValidationFinding>();

        // Representative DV list = the first JoinedByXrefId question with a resolved (non-null) template list.
        // Also track whether ANY JoinedByXrefId (template-aligned) question exists at all.
        bool anyTemplateAligned = false;
        IReadOnlyList<string>? repList = null;
        foreach (var aq in alignment.Aligned)
        {
            if (aq.WithinYear != WithinYearJoin.JoinedByXrefId) continue;
            anyTemplateAligned = true;
            var list = _triggerDvListValues(aq.TemplateMatch!); // TemplateMatch non-null IFF JoinedByXrefId
            if (list is not null) { repList = list; break; }
        }

        if (repList is not null)
        {
            // Verify each configured trigger against the controlled vocabulary.
            foreach (var trigger in _configuredTriggerValues)
            {
                if (DvConformanceEvaluator.Evaluate(trigger, "List", null, null, null, repList)
                        == DvConformanceResult.NotConformant)
                {
                    findings.Add(emitter.Emit(_notInListId,
                        _triggerColumn,
                        questionNumber: null, questionText: null,
                        requestedData: null, providedBy: null,
                        $"Configured trigger value '{trigger}' is not a member of the template data-validation " +
                        $"list [{string.Join(", ", repList)}] on column {_triggerColumn}."));
                }
            }
        }
        else if (anyTemplateAligned)
        {
            // Template-aligned questions exist, but the trigger column's controlled vocabulary resolves
            // NOWHERE → we cannot verify the config. Surface it (do NOT stay silent).
            findings.Add(emitter.Emit(_unresolvableId,
                _triggerColumn,
                questionNumber: null, questionText: null,
                requestedData: null, providedBy: null,
                $"Column {_triggerColumn}'s controlled vocabulary (data-validation list) could not be resolved " +
                $"from the template; the configured trigger value(s) could not be verified."));
        }
        // else: no template-aligned question at all → no vocabulary to check against; absence of alignment
        // is surfaced by the structure / identity-gate checks, so this primitive stays silent here.

        return findings;
    }
}
