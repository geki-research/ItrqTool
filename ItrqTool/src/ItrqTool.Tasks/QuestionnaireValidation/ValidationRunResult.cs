namespace ItrqTool.Tasks.QuestionnaireValidation;

using ItrqTool.Domain.Validation;

// ── Gated align-and-check result ─────────────────────────────────────────────
//
// The richer return shape of ValidationPipeline.RunFromParsedGated<T>: the combined
// finding list PLUS the identity-integrity gate's halt flag. Halted is true only when
// the profile opted in (HaltOnMalformedKeys) AND malformed XrefId keys were present, in
// which case Findings carries ONLY the gate-check's malformed-key findings and the
// baseline/extension chain did not run. The non-gated RunFromParsed<T> wrapper projects
// this to its Findings, so callers that do not care about the halt marker are unchanged.
public sealed record ValidationRunResult(
    IReadOnlyList<ValidationFinding> Findings,
    bool Halted);
