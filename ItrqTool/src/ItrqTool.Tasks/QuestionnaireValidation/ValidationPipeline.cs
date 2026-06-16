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
        ct.ThrowIfCancellationRequested();

        // 4. Align.
        var alignment = AlignmentEngine.Align(current, template, previous);

        // 5. Catalogue (baseline + extension descriptors) → validate overrides → emitter.
        var allDescriptors = profile.BaselineDescriptors
            .Concat(profile.Extensions.SelectMany(e => e.Descriptors));
        var catalogue = new FindingCatalogue(allDescriptors);   // throws on duplicate ids

        var unknownKeys = CatalogueValidation.UnknownOverrideKeys(severityOverrides, catalogue);
        if (unknownKeys.Count > 0)
            throw new ConfigException("Unknown SeverityOverrides keys: " + string.Join(", ", unknownKeys));

        var emitter = new FindingEmitter(severityOverrides, catalogue);

        // 6. Baseline findings first, then each extension's findings in profile order. No dedup.
        var findings = new List<ValidationFinding>(profile.RunBaseline(alignment, emitter));
        foreach (var ext in profile.Extensions)
            findings.AddRange(ext.Run(alignment, emitter));

        return findings;
    }
}
