namespace ItrqTool.Tasks.QuestionnaireValidation.Checks;

using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

// ── Sheet-agnostic extension-check contract ──────────────────────────────────
//
// An extension check is a config-bound primitive over a single new/added column.
// It is constructed with its selectors, role, column, allowed-set, and default
// severities (chunk G's profile supplies these from configuration, never literals),
// and it runs over an alignment to emit zero-or-more findings.
//
// A primitive exposes the role-templated descriptors it emits via <see cref="Descriptors"/>.
// The assembly site (test harness now, the chunk-F pipeline later) concatenates a version's
// baseline descriptor set with every extension's descriptors into the single
// <see cref="FindingCatalogue"/> handed to the <see cref="FindingEmitter"/> — there is no
// registration/merge seam. The catalogue throws on duplicate ids, so every instantiated
// role must produce ids unique across the assembled set.
//
// <see cref="Run"/> mirrors ClqBaselineChecks.Run's run shape: it consumes an
// <see cref="AlignmentResult{T}"/> and an already-constructed <see cref="FindingEmitter"/>
// (carrying the assembled catalogue + the version's severity overrides) and returns the
// findings it produced, in row order.

public interface IExtensionCheck<T> where T : class, IAlignmentIdentity
{
    IReadOnlyList<FindingDescriptor> Descriptors { get; }
    IReadOnlyList<ValidationFinding> Run(AlignmentResult<T> alignment, FindingEmitter emitter);
}
