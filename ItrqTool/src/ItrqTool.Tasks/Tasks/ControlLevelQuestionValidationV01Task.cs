using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.ControlLevelQuestionValidationV01;
using ItrqTool.Tasks.QuestionnaireValidation;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.Validation;

namespace ItrqTool.Tasks;

/// <summary>
/// CLQ_v01 validation. Reads three Control-Level-Questions workbooks (the current response,
/// the empty template, and the previous response), aligns them, runs the version-neutral
/// baseline check catalogue (no extensions — v01 has no answer-stability column), and writes
/// a <see cref="ValidationReport"/> JSON to the <c>report</c> output.
/// </summary>
/// <remarks>
/// Parameters: <c>configurationFullFilename</c> (full path — not CWD-relative).
/// Inputs: <c>currentResponse</c>, <c>emptyTemplate</c>, <c>previousResponse</c>.
/// Output: <c>report</c>.
/// Succeeded semantics: false only on unreadable/missing files or invalid config.
/// A Fatal finding does NOT fail the task.
/// </remarks>
public sealed class ControlLevelQuestionValidationV01Task : IWorkflowTask
{
    private readonly IExcelStructureReader _structureReader;
    private readonly ILogger<ControlLevelQuestionValidationV01Task> _logger;

    public ControlLevelQuestionValidationV01Task(
        IExcelStructureReader structureReader,
        ILogger<ControlLevelQuestionValidationV01Task> logger)
    {
        _structureReader = structureReader;
        _logger = logger;
    }

    public string TaskType => "ControlLevelQuestionValidation_v01";

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

            ClqV01Config config;
            try
            {
                var configJson = await File.ReadAllTextAsync(configPath, ct);
                config = ConfigLoader.Load<ClqV01Config>(configJson, c => c.Validate());
            }
            catch (ConfigException ex)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Configuration error ({configPath}): {ex.Message}", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            var profile = ClqV01Profile.Build(config);

            IReadOnlyList<ValidationFinding> findings;
            try
            {
                findings = ValidationPipeline.Run<ClqV01Question>(
                    _structureReader,
                    currentPath,
                    templatePath,
                    previousPath,
                    profile,
                    config.SeverityOverrides,
                    messages,
                    ct);
            }
            catch (ConfigException ex)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Configuration error ({configPath}): {ex.Message}", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            var report = new ValidationReport(config.SheetName, TaskType, findings);
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
