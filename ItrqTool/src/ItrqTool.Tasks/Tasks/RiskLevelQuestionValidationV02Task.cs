using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.Configuration;
using ItrqTool.Tasks.QuestionnaireValidation;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;
using ItrqTool.Tasks.RiskLevelQuestionValidationV02;
using ItrqTool.Tasks.Validation;
using ItrqTool.Tasks.WorksheetStructure;

namespace ItrqTool.Tasks;

/// <summary>
/// RLQ_v02 validation. Reads three Risk-Level-Questions workbooks (the current response,
/// the empty template, and the previous response), parses each with the multi-row
/// <see cref="RlqV02QuestionParser"/>, patches the answer and material-change DVs,
/// resolves range-ref List DVs, aligns them, and writes a <see cref="ValidationReport"/>
/// JSON to the <c>report</c> output.
/// </summary>
/// <remarks>
/// Mirrors <c>RiskLevelQuestionValidationV01Task</c>, differing only in: config type
/// (<see cref="RlqV02Config"/>), parser (<see cref="RlqV02QuestionParser"/>), profile
/// (<see cref="RlqV02Profile"/>), and record type parameter (<see cref="RlqV02Question"/>).
/// The DvRangeRefResolver post-patch pass for H (answer) and L (material-change) is
/// preserved verbatim — this resolves range-ref and named-range List DVs into
/// AnswerDvListValues / MaterialChangeDvListValues so that Rule 2's
/// ConfiguredTriggerInDvList check can access the resolved vocabulary.
/// <para>
/// Parameters: <c>configurationFullFilename</c> (absolute path, or relative to the application directory (AppContext.BaseDirectory)).
/// Inputs: <c>currentResponse</c>, <c>emptyTemplate</c>, <c>previousResponse</c>.
/// Output: <c>report</c>.
/// Succeeded semantics: false only on unreadable/missing files or invalid config.
/// A finding is data, not a failure.
/// </para>
/// </remarks>
public sealed class RiskLevelQuestionValidationV02Task : IWorkflowTask
{
    private readonly IExcelStructureReader _structureReader;
    private readonly IWorksheetStructureMediator _mediator;
    private readonly ILogger<RiskLevelQuestionValidationV02Task> _logger;

    public RiskLevelQuestionValidationV02Task(
        IExcelStructureReader structureReader,
        IWorksheetStructureMediator mediator,
        ILogger<RiskLevelQuestionValidationV02Task> logger)
    {
        _structureReader = structureReader;
        _mediator = mediator;
        _logger = logger;
    }

    public string TaskType => "RiskLevelQuestionValidation_v02";

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

            RlqV02Config config;
            try
            {
                var configJson = await File.ReadAllTextAsync(configPath, ct);
                config = ConfigLoader.Load<RlqV02Config>(configJson, c => c.Validate());
            }
            catch (ConfigException ex)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Configuration error ({configPath}): {ex.Message}", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            // Malformed SectionRows surface here as a FormatException (LayoutParser.Parse runs
            // inside Build) → caught by the outer handler → Succeeded:false, mirroring v01.
            var profile = RlqV02Profile.Build(config);

            var gate = StructureGate.VerifyAll(_mediator, new[] {
                (currentPath,  new WorksheetSchemaRef("rlq","v02")),
                (templatePath, new WorksheetSchemaRef("rlq","v02")),
                (previousPath, new WorksheetSchemaRef("rlq","v02")),
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

    // Read rows, parse with the RLQ v02 multi-row parser, patch each DV role, then
    // resolve range-ref List DVs for H (answer) and L (material-change). The resolver
    // pass fills AnswerDvListValues / MaterialChangeDvListValues for cells whose source
    // is a range-ref or named-range (inline was already resolved in the profile ApplyDv).
    // Without this pass, a non-inline L DV stays null and Rule 2's dv-list-unresolvable
    // Fatal would fire spuriously for all non-inline L vocabularies.
    private IReadOnlyList<RlqV02Question> ReadParsePatch(
        string path,
        ValidationPipelineProfile<RlqV02Question> profile,
        RlqV02Config config,
        ICollection<TaskMessage> messages)
    {
        var rows = _structureReader.ReadRows(path, profile.SheetName);
        var parsed = RlqV02QuestionParser.Parse(rows, profile.Layout, config, messages);
        foreach (var (column, applyDv) in profile.DvRoles)
            parsed = DvPatcher.Patch(_structureReader, path, profile.SheetName, column, parsed, applyDv);

        // 5a-iv: resolve range-ref List DVs (inline already resolved in the profile ApplyDv).
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
