using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

namespace ItrqTool.Tasks.QuestionnaireValidation.Clq;

// ── CLQ baseline-enum entry point for the sheet-agnostic emitter ──────────────
//
// The sheet-agnostic FindingEmitter (in QuestionnaireValidation/Findings) takes a
// raw string finding-id. This CLQ specialization adds the convenience entry point
// that takes a ClqBaselineFinding, resolving it to its id via ClqBaselineFindings
// and delegating to the same string-id core — so both entry points produce the
// identical ValidationFinding for the same finding and the same data.
//
// This lives in the Clq folder (not in Findings) so the sheet-agnostic
// infrastructure stays free of any CLQ dependency.

public static class ClqFindingEmitterExtensions
{
    /// <summary>
    /// Emits a <see cref="ValidationFinding"/> for a CLQ baseline finding by resolving the
    /// enum to its config-stable id and delegating to <see cref="FindingEmitter.Emit"/>.
    /// </summary>
    public static ValidationFinding Emit(
        this FindingEmitter emitter,
        ClqBaselineFinding finding,
        string cellAddresses,
        string? questionNumber,
        string? questionText,
        string? requestedData,
        string? providedBy,
        string checkResult)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        return emitter.Emit(
            ClqBaselineFindings.Descriptor(finding).Id,
            cellAddresses,
            questionNumber,
            questionText,
            requestedData,
            providedBy,
            checkResult);
    }
}
