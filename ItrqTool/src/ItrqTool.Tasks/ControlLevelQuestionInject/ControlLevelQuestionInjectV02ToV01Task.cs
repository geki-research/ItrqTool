using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;
using ItrqTool.Tasks.Configuration;
using ItrqTool.Tasks.ControlLevelQuestionValidationV01;
using ItrqTool.Tasks.ControlLevelQuestionValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;
using ItrqTool.Tasks.WorksheetStructure;

namespace ItrqTool.Tasks.ControlLevelQuestionInject;

/// <summary>
/// CLQ inject (v02 → v01). Reads the previous-year v02 response and the current-year v01
/// template, aligns them with <see cref="CrossFormatAligner"/>, maps the confident matches
/// to reference / carry-forward cell writes via <see cref="ClqInjectMapper"/>, and writes
/// the populated workbook to the working-dir <c>output</c> path. Final placement is the
/// downstream StaticFileSink's job (chunk 5b) — this task performs NO File.* / SaveAs (R1).
/// </summary>
/// <remarks>
/// Parameters: <c>configurationFullFilename</c> (absolute path, or relative to the application
/// directory (AppContext.BaseDirectory); the two referenced validation configs resolve relative to ITS directory).
/// Inputs: <c>previousResponse</c> (v02), <c>currentTemplate</c> (v01).
/// Output: <c>output</c>.
/// Succeeded semantics: false only on missing inputs/params, missing/invalid config files,
/// or parse errors. Ambiguous matches surface as Warning messages, not failure.
/// </remarks>
public sealed class ControlLevelQuestionInjectV02ToV01Task : IWorkflowTask
{
    private readonly IExcelStructureReader _reader;
    private readonly IExcelTemplateWriter _writer;
    private readonly IWorksheetStructureMediator _mediator;

    public ControlLevelQuestionInjectV02ToV01Task(
        IExcelStructureReader reader,
        IExcelTemplateWriter writer,
        IWorksheetStructureMediator mediator)
    {
        _reader = reader;
        _writer = writer;
        _mediator = mediator;
    }

    public string TaskType => "ControlLevelQuestionInject_v02_to_v01";

    public async Task<TaskResult> ExecuteAsync(TaskExecutionContext ctx, CancellationToken ct)
    {
        var messages = new List<TaskMessage>();
        var sw = Stopwatch.StartNew();

        try
        {
            if (!TryGetParam(ctx, "configurationFullFilename", out var injectConfigPathRaw))
            {
                messages.Add(new(MessageSeverity.Error,
                    "Required parameter missing or empty: configurationFullFilename.",
                    DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }
            var injectConfigPath = ConfigPathResolver.Resolve(injectConfigPathRaw);

            if (!TryGetInput(ctx, "previousResponse", out var previousPath, messages) ||
                !TryGetInput(ctx, "currentTemplate", out var currentPath, messages))
            {
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            var outputPath = ctx.OutputPaths["output"];

            if (!File.Exists(injectConfigPath))
            {
                messages.Add(new(MessageSeverity.Error,
                    $"File not found: configurationFullFilename: {injectConfigPath}", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            // ── Load the inject config + the two referenced validation configs ──
            ClqInjectConfig injectConfig;
            ClqV01Config currentConfig;
            ControlLevelQuestionValidationV02Config previousConfig;
            try
            {
                var injectJson = await File.ReadAllTextAsync(injectConfigPath, ct);
                injectConfig = ConfigLoader.Load<ClqInjectConfig>(injectJson, c => c.Validate());

                var configDir = Path.GetDirectoryName(injectConfigPath) ?? "";
                var currentConfigPath = Path.Combine(configDir, injectConfig.CurrentConfigFilename);
                var previousConfigPath = Path.Combine(configDir, injectConfig.PreviousConfigFilename);

                var configMissing = new List<string>();
                if (!File.Exists(currentConfigPath)) configMissing.Add($"CurrentConfigFilename: {currentConfigPath}");
                if (!File.Exists(previousConfigPath)) configMissing.Add($"PreviousConfigFilename: {previousConfigPath}");
                if (configMissing.Count > 0)
                {
                    messages.Add(new(MessageSeverity.Error,
                        "Referenced config file(s) not found: " + string.Join("; ", configMissing),
                        DateTimeOffset.Now));
                    return new TaskResult(Succeeded: false, messages, sw.Elapsed);
                }

                var currentJson = await File.ReadAllTextAsync(currentConfigPath, ct);
                currentConfig = ConfigLoader.Load<ClqV01Config>(currentJson, c => c.Validate());

                var previousJson = await File.ReadAllTextAsync(previousConfigPath, ct);
                previousConfig = ConfigLoader.Load<ControlLevelQuestionValidationV02Config>(
                    previousJson, c => c.Validate());
            }
            catch (ConfigException ex)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Configuration error ({injectConfigPath}): {ex.Message}", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            var missingInputs = new List<string>();
            if (!File.Exists(currentPath)) missingInputs.Add($"currentTemplate: {currentPath}");
            if (!File.Exists(previousPath)) missingInputs.Add($"previousResponse: {previousPath}");
            if (missingInputs.Count > 0)
            {
                messages.Add(new(MessageSeverity.Error,
                    "Input file(s) not found: " + string.Join("; ", missingInputs), DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            // ── Parse both workbooks via the minimal profile-reuse path (no DV, no rules) ──
            var currentProfile = ClqV01Profile.Build(currentConfig);
            var previousProfile = ClqV02Profile.Build(previousConfig);

            var gate = StructureGate.VerifyAll(_mediator, new[]
            {
                (previousPath, new WorksheetSchemaRef("clq", "v02")),   // SOURCE
                (currentPath,  new WorksheetSchemaRef("clq", "v01")),   // TARGET
            });
            if (gate.AssetFailed)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Worksheet-structure schema asset error: {gate.AssetErrorReason}", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }
            if (gate.StructureFindings.Count > 0)
            {
                foreach (var f in gate.StructureFindings)
                    messages.Add(new(MessageSeverity.Error, f.CheckResult, DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            var parseMessages = new List<TaskMessage>();

            var currentRows = _reader.ReadRows(currentPath, currentProfile.SheetName);
            var currentQuestions = QuestionParser.Parse(
                currentRows, currentProfile.Layout, currentProfile.RecordFactory, parseMessages);

            ct.ThrowIfCancellationRequested();

            var previousRows = _reader.ReadRows(previousPath, previousProfile.SheetName);
            var previousQuestions = QuestionParser.Parse(
                previousRows, previousProfile.Layout, previousProfile.RecordFactory, parseMessages);

            messages.AddRange(parseMessages);
            if (parseMessages.Any(m => m.Severity == MessageSeverity.Error))
            {
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            // ── Align (cross-format) → map → write ──
            var alignment = CrossFormatAligner.Align<ClqV01Question, ClqV02Question>(
                currentQuestions, previousQuestions);

            var (cells, mapMessages) = ClqInjectMapper.Map(alignment, injectConfig, currentConfig);

            ct.ThrowIfCancellationRequested();

            _writer.Populate(currentPath, currentProfile.SheetName, cells, outputPath);

            messages.AddRange(mapMessages);

            int agreeCount = alignment.Matches.Count(m => m.Outcome == CrossYearOutcome.Agree);
            int carriedForwardCount = injectConfig.CarryForwardEnabled
                ? alignment.Matches.Count(m =>
                    m.Outcome == CrossYearOutcome.Agree &&
                    string.Equals(m.Previous!.AnswerStability, injectConfig.StabilityTriggerToken,
                        StringComparison.Ordinal))
                : 0;
            int ambiguousCount = alignment.Matches.Count(m =>
                m.Outcome is CrossYearOutcome.XrefIdConflict
                          or CrossYearOutcome.NewXrefIdWithLookalike
                          or CrossYearOutcome.SameXrefIdTextDiverged);
            int unmatchedCount = alignment.Matches.Count(m =>
                m.Outcome is CrossYearOutcome.Neither
                          or CrossYearOutcome.NotEvaluatedMalformedKey);

            messages.Add(new(MessageSeverity.Info,
                $"Inject complete: {agreeCount} confident match(es) injected, " +
                $"{carriedForwardCount} carried forward, {ambiguousCount} ambiguous (left untouched), " +
                $"{unmatchedCount} unmatched. Wrote {cells.Count} cell(s) to {Path.GetFileName(outputPath)}.",
                DateTimeOffset.Now));

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
