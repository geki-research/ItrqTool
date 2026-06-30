using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.Configuration;
using ItrqTool.Tasks.QuestionnaireValidation;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using ItrqTool.Tasks.Validation;
using ItrqTool.Tasks.WorksheetStructure;

namespace ItrqTool.Tasks;

/// <summary>
/// RLQ_v01 validation. Reads three Risk-Level-Questions workbooks (the current response,
/// the empty template, and the previous response), parses each with the multi-row
/// <see cref="RlqV01QuestionParser"/>, patches the answer DV, aligns them, and writes a
/// <see cref="ValidationReport"/> JSON to the <c>report</c> output.
/// </summary>
/// <remarks>
/// Mirrors <c>ControlLevelQuestionValidationV02Task</c>, differing only in: it parses with
/// <see cref="RlqV01QuestionParser"/> (NOT <c>QuestionParser.Parse</c>) and runs the
/// IO-free align-and-check half via <see cref="ValidationPipeline.RunFromParsed{T}"/>
/// (NOT <c>ValidationPipeline.Run</c>). This chunk wires no findings (the profile carries an
/// empty catalogue), so a successful run always writes an empty findings list.
/// <para>
/// Parameters: <c>configurationFullFilename</c> (absolute path, or relative to the application directory (AppContext.BaseDirectory)).
/// Inputs: <c>currentResponse</c>, <c>emptyTemplate</c>, <c>previousResponse</c>.
/// Output: <c>report</c>.
/// Succeeded semantics: false only on unreadable/missing files or invalid config.
/// A finding is data, not a failure.
/// </para>
/// </remarks>
public sealed class RiskLevelQuestionValidationV01Task : IWorkflowTask
{
    private readonly IExcelStructureReader _structureReader;
    private readonly IWorksheetStructureMediator _mediator;
    private readonly ILogger<RiskLevelQuestionValidationV01Task> _logger;

    public RiskLevelQuestionValidationV01Task(
        IExcelStructureReader structureReader,
        IWorksheetStructureMediator mediator,
        ILogger<RiskLevelQuestionValidationV01Task> logger)
    {
        _structureReader = structureReader;
        _mediator = mediator;
        _logger = logger;
    }

    public string TaskType => "RiskLevelQuestionValidation_v01";

    public async Task<TaskResult> ExecuteAsync(TaskExecutionContext ctx, CancellationToken ct)
    {
        var messages = new List<TaskMessage>();
        var sw = Stopwatch.StartNew();

        try
        {
            if (!TryGetParam(ctx, "configurationFullFilename", out var configPathRaw))
            {
                messages.Add(new(MessageSeverity.Error,
                    "Required parameter missing or empty: configurationFullFilename.",
                    DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }
            var configPath = ConfigPathResolver.Resolve(configPathRaw);

            if (!TryGetInput(ctx, "currentResponse", out var currentPath, messages) ||
                !TryGetInput(ctx, "emptyTemplate", out var templatePath, messages) ||
                !TryGetInput(ctx, "previousResponse", out var previousPath, messages))
            {
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            var reportPath = ctx.OutputPaths["report"];

            var missing = new List<string>();
            if (!File.Exists(configPath))  missing.Add($"configurationFullFilename: {configPath}");
            if (!File.Exists(currentPath)) missing.Add($"currentResponse: {currentPath}");
            if (!File.Exists(templatePath)) missing.Add($"emptyTemplate: {templatePath}");
            if (!File.Exists(previousPath)) missing.Add($"previousResponse: {previousPath}");

            if (missing.Count > 0)
            {
                messages.Add(new(MessageSeverity.Error,
                    "File(s) not found: " + string.Join("; ", missing), DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            RlqV01Config config;
            try
            {
                var configJson = await File.ReadAllTextAsync(configPath, ct);
                config = ConfigLoader.Load<RlqV01Config>(configJson, c => c.Validate());
            }
            catch (ConfigException ex)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Configuration error ({configPath}): {ex.Message}", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            // Malformed SectionRows surface here as a FormatException (LayoutParser.Parse runs
            // inside Build) → caught by the outer handler → Succeeded:false, mirroring CLQ_v02.
            var profile = RlqV01Profile.Build(config);

            var gate = StructureGate.VerifyAll(_mediator, new[] {
                (currentPath,  new WorksheetSchemaRef("rlq","v01")),
                (templatePath, new WorksheetSchemaRef("rlq","v01")),
                (previousPath, new WorksheetSchemaRef("rlq","v01")),
            });
            if (gate.AssetFailed)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Worksheet-structure schema asset error: {gate.AssetErrorReason}", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            var current  = ReadParsePatch(currentPath,  profile, config, messages);
            var template = ReadParsePatch(templatePath, profile, config, messages);
            var previous = ReadParsePatch(previousPath, profile, config, messages);

            ValidationRunResult runResult;
            try
            {
                // Gated entry: RLQ opts into the identity-integrity gate (HaltOnMalformedKeys),
                // so a malformed XrefId in any workbook yields ONLY the gate findings + Halted=true.
                runResult = ValidationPipeline.RunFromParsedGated(
                    current, template, previous,
                    profile, config.SeverityOverrides, messages, ct);
            }
            catch (ConfigException ex)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Configuration error ({configPath}): {ex.Message}", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            var findings = gate.StructureFindings.Count == 0
                ? runResult.Findings
                : gate.StructureFindings.Concat(runResult.Findings).ToList();
            // bool? Halted: true only on a deliberate gate halt; null otherwise so the serializer
            // (WhenWritingNull) omits the key on a normal run — the report stays byte-identical.
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
            ctx.Logger.LogError(ex, "RLQ v01 validation failed: {Message}", ex.Message);
            messages.Add(new(MessageSeverity.Error, ex.Message, DateTimeOffset.Now));
            return new TaskResult(Succeeded: false, messages, sw.Elapsed);
        }
    }

    // Read rows, parse with the RLQ multi-row parser, then patch each DV role. Patch returns
    // a NEW list — reassign each time, exactly as ValidationPipeline.Run does.
    private IReadOnlyList<RlqV01Question> ReadParsePatch(
        string path,
        ValidationPipelineProfile<RlqV01Question> profile,
        RlqV01Config config,
        ICollection<TaskMessage> messages)
    {
        var rows = _structureReader.ReadRows(path, profile.SheetName);
        var parsed = RlqV01QuestionParser.Parse(rows, profile.Layout, config, messages);
        foreach (var (column, applyDv) in profile.DvRoles)
            parsed = DvPatcher.Patch(_structureReader, path, profile.SheetName, column, parsed, applyDv);

        // 5a-iv: resolve range-ref List DVs (inline already resolved in the profile ApplyDv; named-range deferred to 5b).
        parsed = DvRangeRefResolver.Resolve(
            _structureReader, path, profile.SheetName, parsed,
            dvTypeSelector:            q => q.AnswerDvType,
            dvFormulaSelector:         q => q.AnswerDvFormula,
            currentListValuesSelector: q => q.AnswerDvListValues,
            stampListValues:           (q, vals) => q with { AnswerDvListValues = vals });
        parsed = DvRangeRefResolver.Resolve(
            _structureReader, path, profile.SheetName, parsed,
            q => q.MaterialChangeDvType,
            q => q.MaterialChangeDvFormula,
            q => q.MaterialChangeDvListValues,
            (q, vals) => q with { MaterialChangeDvListValues = vals });

        return parsed;
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
