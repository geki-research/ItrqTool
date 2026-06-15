using ItrqTool.Domain.Validation;

namespace ItrqTool.Tasks.QuestionnaireValidation.Findings;

// ── Sheet-agnostic finding descriptor ────────────────────────────────────────
//
// Faithful generalization of CLQ_v01's <c>ClqFindingDescriptor</c>: the single
// record that carries a finding's config-stable id string, its default severity,
// the <see cref="ValidationCheck"/> it maps to, and a human-readable description.
//
// A finding-id is a semantic, config-stable string: it must NEVER embed anything
// configurable (no column letters, sheet names, thresholds, or algorithm
// internals). The id is the stable key used by a version's SeverityOverrides.
//
// The Check is stored here (not derived by id prefix) because cross-year.*
// findings split across two ValidationCheck values, so a prefix rule would be wrong.
//
// Description is carried for parity/docs; it is NOT part of the emitted
// <see cref="ValidationFinding"/> output.

public sealed record FindingDescriptor(
    string Id,
    FindingEvaluation DefaultEvaluation,
    ValidationCheck Check,
    string Description);
