using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.QuestionnaireValidation;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.Validation;
using ItrqTool.Tasks.WorksheetStructure;

namespace ItrqTool.Tasks;

/// <summary>
/// GD_v01 validation. Reads three General-Data workbooks (the current response, the empty template,
/// and the previous response), parses each with <see cref="GdV01QuestionParser"/>, patches DV with
/// <see cref="GdDvPatcher"/>, aligns with <see cref="GdV01Aligner"/>, and writes a
/// <see cref="ValidationReport"/> JSON to the <c>report</c> output.
/// </summary>
/// <remarks>
/// Mirrors <c>RiskLevelQuestionValidationV01Task</c>, differing in that GD owns its own aligner and
/// calls <see cref="ValidationPipeline.RunFromAlignedGated{T}"/> (not <c>RunFromParsedGated</c>).
/// A GD-local section-header gate (<see cref="GdSectionHeaderGate"/>) runs pre-align; if it fires,
/// the task still Succeeds (section-header mismatch is a data finding, not a task failure).
/// <para>
/// Parameters: <c>configurationFullFilename</c> (full path).
/// Inputs: <c>currentResponse</c>, <c>emptyTemplate</c>, <c>previousResponse</c>.
/// Output: <c>report</c>.
/// Succeeded semantics: false only on unreadable/missing files or invalid config.
/// A finding is data, not a failure.
/// </para>
/// </remarks>
public sealed class GeneralDataValidationV01Task : IWorkflowTask
{
    private readonly IExcelStructureReader _structureReader;
    private readonly IWorksheetStructureMediator _mediator;
    private readonly ILogger<GeneralDataValidationV01Task> _logger;

    public GeneralDataValidationV01Task(
        IExcelStructureReader structureReader,
        IWorksheetStructureMediator mediator,
        ILogger<GeneralDataValidationV01Task> logger)
    {
        _structureReader = structureReader;
        _mediator = mediator;
        _logger = logger;
    }

    public string TaskType => "GeneralDataValidation_v01";

    public async Task<TaskResult> ExecuteAsync(TaskExecutionContext ctx, CancellationToken ct)
    {
        var messages = new List<TaskMessage>();
        var sw = Stopwatch.StartNew();

        try
        {
            if (!TryGetParam(ctx, "configurationFullFilename", out var configPath))
            {
                messages.Add(new(MessageSeverity.Error,
                    "Required parameter missing or empty: configurationFullFilename.",
                    DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            if (!TryGetInput(ctx, "currentResponse", out var currentPath, messages) ||
                !TryGetInput(ctx, "emptyTemplate", out var templatePath, messages) ||
                !TryGetInput(ctx, "previousResponse", out var previousPath, messages))
            {
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            var reportPath = ctx.OutputPaths["report"];

            var missing = new List<string>();
            if (!File.Exists(configPath))   missing.Add($"configurationFullFilename: {configPath}");
            if (!File.Exists(currentPath))  missing.Add($"currentResponse: {currentPath}");
            if (!File.Exists(templatePath)) missing.Add($"emptyTemplate: {templatePath}");
            if (!File.Exists(previousPath)) missing.Add($"previousResponse: {previousPath}");

            if (missing.Count > 0)
            {
                messages.Add(new(MessageSeverity.Error,
                    "File(s) not found: " + string.Join("; ", missing), DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            GdV01Config config;
            try
            {
                var configJson = await File.ReadAllTextAsync(configPath, ct);
                config = ConfigLoader.Load<GdV01Config>(configJson, c => c.Validate());
            }
            catch (ConfigException ex)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Configuration error ({configPath}): {ex.Message}", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            var profile = GdV01Profile.Build(config);

            var gate = StructureGate.VerifyAll(_mediator, new[] {
                (currentPath,  new WorksheetSchemaRef("gd", "v01")),
                (templatePath, new WorksheetSchemaRef("gd", "v01")),
                (previousPath, new WorksheetSchemaRef("gd", "v01")),
            });
            if (gate.AssetFailed)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Worksheet-structure schema asset error: {gate.AssetErrorReason}",
                    DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            var severityOverrides = config.SeverityOverrides;

            // Parse each workbook.
            var parseMessages = new List<TaskMessage>();
            var cur = GdV01QuestionParser.Parse(
                _structureReader.ReadRows(currentPath,  config.SheetName), profile.Layout, config, parseMessages);
            var tmp = GdV01QuestionParser.Parse(
                _structureReader.ReadRows(templatePath, config.SheetName), profile.Layout, config, parseMessages);
            var prv = GdV01QuestionParser.Parse(
                _structureReader.ReadRows(previousPath, config.SheetName), profile.Layout, config, parseMessages);
            messages.AddRange(parseMessages);

            // GD-local section-header gate (pre-align, fail-loud). A section-header mismatch in any
            // workbook halts further processing — a wrong header invalidates section-anchored semantics.
            // The task still Succeeds: this is a data/structure finding, not an unrecoverable error.
            var headerFindings = GdSectionHeaderGate.Verify(cur, tmp, prv, severityOverrides);
            ValidationRunResult runResult;
            if (headerFindings.Count > 0)
            {
                runResult = new ValidationRunResult(headerFindings, Halted: true);
            }
            else
            {
                // Patch DV fields, then align, then run the gated pipeline.
                var patchedCurrentQs  = GdDvPatcher.Patch(_structureReader, currentPath,  config.SheetName, config, cur.Questions);
                var patchedTemplateQs = GdDvPatcher.Patch(_structureReader, templatePath, config.SheetName, config, tmp.Questions);
                var patchedPreviousQs = GdDvPatcher.Patch(_structureReader, previousPath, config.SheetName, config, prv.Questions);

                var patchedCurrent  = new GdV01ParseResult(patchedCurrentQs,  cur.Malformed, cur.SectionHeaderMismatches);
                var patchedTemplate = new GdV01ParseResult(patchedTemplateQs, tmp.Malformed, tmp.SectionHeaderMismatches);
                var patchedPrevious = new GdV01ParseResult(patchedPreviousQs, prv.Malformed, prv.SectionHeaderMismatches);

                var alignment = GdV01Aligner.Align(patchedCurrent, patchedTemplate, patchedPrevious);

                runResult = ValidationPipeline.RunFromAlignedGated(
                    alignment, profile, severityOverrides, messages, ct);
            }

            ct.ThrowIfCancellationRequested();

            var findings = gate.StructureFindings.Count == 0
                ? runResult.Findings
                : gate.StructureFindings.Concat(runResult.Findings).ToList();

            var report = new ValidationReport(
                config.SheetName, TaskType, findings,
                runResult.Halted ? true : (bool?)null);
            await File.WriteAllTextAsync(reportPath, ValidationReportSerializer.Serialize(report), ct);

            if (findings.Count == 0)
            {
                messages.Add(new(MessageSeverity.Info,
                    "No findings; validation report written with an empty findings list.",
                    DateTimeOffset.Now));
            }
            else
            {
                var byEval = findings
                    .GroupBy(f => f.Evaluation)
                    .OrderBy(g => g.Key)
                    .Select(g => $"{g.Count()} {g.Key}");
                messages.Add(new(MessageSeverity.Info,
                    $"{findings.Count} finding(s): {string.Join(", ", byEval)}.", DateTimeOffset.Now));
            }

            messages.Add(new(MessageSeverity.Info,
                $"Report written to {Path.GetFileName(reportPath)}.", DateTimeOffset.Now));

            return new TaskResult(Succeeded: true, messages, sw.Elapsed);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            messages.Add(new(MessageSeverity.Error, ex.Message, DateTimeOffset.Now));
            return new TaskResult(Succeeded: false, messages, sw.Elapsed);
        }
    }

    private static bool TryGetParam(TaskExecutionContext ctx, string key, out string value)
    {
        if (ctx.Parameters.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
        {
            value = v;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static bool TryGetInput(
        TaskExecutionContext ctx, string key, out string value, List<TaskMessage> messages)
    {
        if (ctx.InputPaths.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
        {
            value = v;
            return true;
        }
        messages.Add(new(MessageSeverity.Error,
            $"Required input missing or empty: {key}.", DateTimeOffset.Now));
        value = string.Empty;
        return false;
    }
}
