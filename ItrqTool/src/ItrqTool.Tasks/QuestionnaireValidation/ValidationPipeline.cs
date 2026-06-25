namespace ItrqTool.Tasks.QuestionnaireValidation;

using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;  // AlignmentEngine, IAlignmentIdentity
using ItrqTool.Tasks.QuestionnaireValidation.Config;     // CatalogueValidation, ConfigException
using ItrqTool.Tasks.QuestionnaireValidation.Findings;   // FindingCatalogue, FindingEmitter
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;    // QuestionParser, DvPatcher

public static class ValidationPipeline
{
    public static IReadOnlyList<ValidationFinding> Run<T>(
        IExcelStructureReader reader,
        string currentPath,
        string templatePath,
        string previousPath,
        ValidationPipelineProfile<T> profile,
        IReadOnlyDictionary<string, FindingEvaluation> severityOverrides,
        ICollection<TaskMessage> messages,
        CancellationToken ct)
        where T : class, IAlignmentIdentity
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(currentPath);
        ArgumentNullException.ThrowIfNull(templatePath);
        ArgumentNullException.ThrowIfNull(previousPath);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(severityOverrides);
        ArgumentNullException.ThrowIfNull(messages);

        // 1. Read rows (×3). Reader exceptions propagate (the task converts to Succeeded:false).
        var currentRows  = reader.ReadRows(currentPath,  profile.SheetName);
        var templateRows = reader.ReadRows(templatePath, profile.SheetName);
        var previousRows = reader.ReadRows(previousPath, profile.SheetName);
        ct.ThrowIfCancellationRequested();

        // 2. Parse (×3).
        var current  = QuestionParser.Parse(currentRows,  profile.Layout, profile.RecordFactory, messages);
        var template = QuestionParser.Parse(templateRows, profile.Layout, profile.RecordFactory, messages);
        var previous = QuestionParser.Parse(previousRows, profile.Layout, profile.RecordFactory, messages);

        // 3. Patch DV per role per workbook. Patch returns a NEW list — REASSIGN each time.
        foreach (var (column, applyDv) in profile.DvRoles)
        {
            current  = DvPatcher.Patch(reader, currentPath,  profile.SheetName, column, current,  applyDv);
            template = DvPatcher.Patch(reader, templatePath, profile.SheetName, column, template, applyDv);
            previous = DvPatcher.Patch(reader, previousPath, profile.SheetName, column, previous, applyDv);
        }

        // 4+. Align-and-check half — shared, IO-free, reusable by multi-row validators.
        return RunFromParsed(current, template, previous, profile, severityOverrides, messages, ct);
    }

    /// <summary>
    /// IO-free align-and-check half of the pipeline: the three already-read,
    /// already-parsed, already-DV-patched question lists flow in, alignment and the
    /// full finding catalogue run, and the combined finding list flows out. Carries no
    /// <see cref="IExcelStructureReader"/> and no file paths — purely the post-DV-patch
    /// steps in their original order. Behaviour is identical to the tail of <see cref="Run{T}"/>.
    /// </summary>
    /// <remarks>
    /// Thin projection of <see cref="RunFromParsedGated{T}"/> to its findings, for callers
    /// (CLQ tasks) that do not consume the identity-integrity halt marker. With a profile
    /// that leaves the gate at its defaults this is byte-identical to the pre-gate body.
    /// </remarks>
    public static IReadOnlyList<ValidationFinding> RunFromParsed<T>(
        IReadOnlyList<T> currentResponse,
        IReadOnlyList<T> emptyTemplate,
        IReadOnlyList<T> previousResponse,
        ValidationPipelineProfile<T> profile,
        IReadOnlyDictionary<string, FindingEvaluation> severityOverrides,
        ICollection<TaskMessage> messages,
        CancellationToken ct)
        where T : class, IAlignmentIdentity
        => RunFromParsedGated(
            currentResponse, emptyTemplate, previousResponse,
            profile, severityOverrides, messages, ct).Findings;

    /// <summary>
    /// Gated variant of <see cref="RunFromParsed{T}"/> returning the combined findings PLUS
    /// the identity-integrity halt flag. When the profile opts in
    /// (<see cref="ValidationPipelineProfile{T}.HaltOnMalformedKeys"/>) and alignment surfaces
    /// any malformed XrefId key, only the profile's
    /// <see cref="ValidationPipelineProfile{T}.IdentityGateCheck"/> findings are emitted, the
    /// result is marked <c>Halted</c>, and the baseline/extension chain does NOT run. With the
    /// gate off (the default), the chain runs exactly as before and <c>Halted</c> is false.
    /// </summary>
    public static ValidationRunResult RunFromParsedGated<T>(
        IReadOnlyList<T> currentResponse,
        IReadOnlyList<T> emptyTemplate,
        IReadOnlyList<T> previousResponse,
        ValidationPipelineProfile<T> profile,
        IReadOnlyDictionary<string, FindingEvaluation> severityOverrides,
        ICollection<TaskMessage> messages,
        CancellationToken ct)
        where T : class, IAlignmentIdentity
    {
        ArgumentNullException.ThrowIfNull(currentResponse);
        ArgumentNullException.ThrowIfNull(emptyTemplate);
        ArgumentNullException.ThrowIfNull(previousResponse);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(severityOverrides);
        ArgumentNullException.ThrowIfNull(messages);

        ct.ThrowIfCancellationRequested();

        // 4. Align.
        var alignment = AlignmentEngine.Align(currentResponse, emptyTemplate, previousResponse);

        return RunFromAlignedGated(alignment, profile, severityOverrides, messages, ct);
    }

    /// <summary>
    /// IO-free, pre-aligned check half of the pipeline: a fully-constructed
    /// <see cref="AlignmentResult{T}"/> flows in, the catalogue/gate/baseline/extension
    /// chain runs, and the <see cref="ValidationRunResult"/> flows out. Carries no
    /// question lists and no file paths — purely the post-Align steps in their original
    /// order. Behaviour is identical to the tail of <see cref="RunFromParsedGated{T}"/>.
    /// </summary>
    public static ValidationRunResult RunFromAlignedGated<T>(
        AlignmentResult<T> alignment,
        ValidationPipelineProfile<T> profile,
        IReadOnlyDictionary<string, FindingEvaluation> severityOverrides,
        ICollection<TaskMessage> messages,
        CancellationToken ct)
        where T : class, IAlignmentIdentity
    {
        ArgumentNullException.ThrowIfNull(alignment);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(severityOverrides);
        ArgumentNullException.ThrowIfNull(messages);

        ct.ThrowIfCancellationRequested();

        // 5. Catalogue (baseline + extension + gate-check descriptors) → validate overrides → emitter.
        // The gate check's descriptors MUST join the catalogue: RLQ moves MalformedKeyCheck out of
        // Extensions into the gate slot, so without this concat its id would be unregistered and the
        // gate's Emit would throw. On the clean path the descriptor is registered-but-unused (harmless,
        // and a valid override key). For profiles with no gate check (CLQ) nothing is added.
        var allDescriptors = profile.BaselineDescriptors
            .Concat(profile.Extensions.SelectMany(e => e.Descriptors))
            .Concat(profile.IdentityGateCheck?.Descriptors ?? Enumerable.Empty<FindingDescriptor>());
        var catalogue = new FindingCatalogue(allDescriptors);   // throws on duplicate ids

        var unknownKeys = CatalogueValidation.UnknownOverrideKeys(severityOverrides, catalogue);
        if (unknownKeys.Count > 0)
            throw new ConfigException("Unknown SeverityOverrides keys: " + string.Join(", ", unknownKeys));

        var emitter = new FindingEmitter(severityOverrides, catalogue);

        // ── identity-integrity gate ──
        // Opt-in: when enabled and any malformed key exists in ANY of the three workbooks, emit ONLY
        // the gate-check's malformed-key findings, mark the run halted, and return without running the
        // baseline/extension chain. alignment.MalformedKeys already spans all three workbooks.
        if (profile.HaltOnMalformedKeys && alignment.MalformedKeys.Count > 0)
        {
            var gateFindings = profile.IdentityGateCheck?.Run(alignment, emitter)
                               ?? (IReadOnlyList<ValidationFinding>)[];
            return new ValidationRunResult(gateFindings, Halted: true);
        }

        // 6. Baseline findings first, then each extension's findings in profile order. No dedup.
        var findings = new List<ValidationFinding>(profile.RunBaseline(alignment, emitter));
        foreach (var ext in profile.Extensions)
            findings.AddRange(ext.Run(alignment, emitter));

        return new ValidationRunResult(findings, Halted: false);
    }
}
