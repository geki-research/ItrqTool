using ItrqTool.Domain.Validation;

namespace ItrqTool.Tasks.QuestionnaireValidation.Findings;

// ── Sheet-agnostic finding emitter ───────────────────────────────────────────
//
// The generic equivalent of CLQ_v01's private <c>Make</c> helper. Constructed with
// a version's SeverityOverrides plus the <see cref="FindingCatalogue"/>; emits a
// <see cref="ValidationFinding"/> from a finding-id and the per-finding data.
//
// Resolution mirrors v01's Make exactly:
//   Check       ← descriptor.Check
//   Evaluation  ← SeverityOverrides.GetValueOrDefault(id, descriptor.DefaultEvaluation)
// the remaining ValidationFinding fields are the caller-supplied per-finding data.
//
// This core entry point takes a raw string id (for extension findings). A
// sheet-specific convenience entry point taking that sheet's baseline enum lives
// in the sheet's own folder (e.g. ClqFindingEmitterExtensions), keeping this
// sheet-agnostic infrastructure free of any sheet dependency.

public sealed class FindingEmitter
{
    private readonly IReadOnlyDictionary<string, FindingEvaluation> _severityOverrides;
    private readonly FindingCatalogue _catalogue;

    public FindingEmitter(
        IReadOnlyDictionary<string, FindingEvaluation> severityOverrides,
        FindingCatalogue catalogue)
    {
        _severityOverrides = severityOverrides ?? throw new ArgumentNullException(nameof(severityOverrides));
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
    }

    /// <summary>
    /// Emits a <see cref="ValidationFinding"/> for the given finding-id, resolving its
    /// <see cref="ValidationCheck"/> from the catalogue descriptor and its
    /// <see cref="FindingEvaluation"/> from the version's SeverityOverrides (falling back
    /// to the descriptor's default). The remaining fields are the per-finding data.
    /// </summary>
    public ValidationFinding Emit(
        string findingId,
        string cellAddresses,
        string? questionNumber,
        string? questionText,
        string? requestedData,
        string? providedBy,
        string checkResult)
    {
        var descriptor = _catalogue.Descriptor(findingId);
        var evaluation = _severityOverrides.GetValueOrDefault(findingId, descriptor.DefaultEvaluation);
        return new ValidationFinding(
            Check: descriptor.Check,
            Evaluation: evaluation,
            CellAddresses: cellAddresses,
            QuestionNumber: questionNumber,
            QuestionText: questionText,
            RequestedData: requestedData,
            ProvidedBy: providedBy,
            CheckResult: checkResult);
    }
}
