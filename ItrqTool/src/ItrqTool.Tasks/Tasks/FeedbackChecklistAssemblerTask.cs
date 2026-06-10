using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.FeedbackChecklist;
using ItrqTool.Tasks.Validation;

namespace ItrqTool.Tasks;

/// <summary>
/// Assembles one feedback checklist from N per-task validation-findings JSON files.
/// Reads each findings input (in input-key order), flattens every finding into a
/// <see cref="FeedbackChecklistRow"/> with a running 1-based counter, and hands the
/// rows to the injected <see cref="IFeedbackChecklistWriter"/> to populate a template.
/// </summary>
/// <remarks>
/// Wiring contract (for the workflow JSON, Chunk B):
/// <list type="bullet">
/// <item>Parameter <c>configurationFullFilename</c>: path to the assembler config JSON
///   (<see cref="FeedbackChecklistConfig"/>).</item>
/// <item>Inputs: each declared input is a findings JSON file. They are consumed in
///   <em>ordinal order of the input key</em>, so name them with an order-significant
///   scheme (e.g. <c>findings1</c>, <c>findings2</c>, …) to control emission order.</item>
/// <item>Output <c>checklist</c>: the populated workbook path.</item>
/// </list>
/// Posture (matches the diff tasks' #10 contract): a missing / null / unparseable config
/// or a missing / malformed input findings file is a hard <c>Error</c> with
/// <c>Succeeded:false</c> — these are our own pipeline artifacts, so a bad one is a
/// pipeline bug and we fail loudly. Zero findings across all inputs is NOT a failure:
/// the checklist is still produced (template populated with no data rows) plus an Info
/// message.
/// </remarks>
public sealed class FeedbackChecklistAssemblerTask : IWorkflowTask
{
    private static readonly JsonSerializerOptions ConfigJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IFeedbackChecklistWriter _writer;
    private readonly ILogger<FeedbackChecklistAssemblerTask> _logger;

    public FeedbackChecklistAssemblerTask(
        IFeedbackChecklistWriter writer,
        ILogger<FeedbackChecklistAssemblerTask> logger)
    {
        _writer = writer;
        _logger = logger;
    }

    public string TaskType => "FeedbackChecklistAssembler";

    public async Task<TaskResult> ExecuteAsync(TaskExecutionContext ctx, CancellationToken ct)
    {
        var messages = new List<TaskMessage>();
        var sw = Stopwatch.StartNew();

        try
        {
            // 1. Config-file parameter
            if (!TryGetParam(ctx, "configurationFullFilename", out var configPath))
            {
                messages.Add(new(MessageSeverity.Error,
                    "Required parameter missing or empty: configurationFullFilename.",
                    DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            if (!File.Exists(configPath))
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Configuration file not found: {configPath}", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            // 2. Load + deserialize config. Null (empty / literal 'null') or unparseable → Error.
            var configJson = await File.ReadAllTextAsync(configPath, ct);
            FeedbackChecklistConfig? config;
            try
            {
                config = JsonSerializer.Deserialize<FeedbackChecklistConfig>(configJson, ConfigJsonOptions);
            }
            catch (JsonException ex)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Configuration could not be parsed ({configPath}): {ex.Message}",
                    DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            if (config is null)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Configuration is null (file is empty or contains literal 'null'): {configPath}",
                    DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            // 3. Validate config essentials — an under-specified config writes nothing useful,
            //    so surface it rather than silently emitting an empty/garbage checklist.
            var configErrors = new List<string>();
            if (string.IsNullOrWhiteSpace(config.TemplatePath)) configErrors.Add("templatePath is blank");
            if (string.IsNullOrWhiteSpace(config.SheetName))     configErrors.Add("sheetName is blank");
            if (config.DataStartRow <= 0)                        configErrors.Add("dataStartRow must be a positive integer");
            if (config.ColumnMap is null || config.ColumnMap.Count == 0) configErrors.Add("columnMap is empty");

            if (configErrors.Count > 0)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Configuration is invalid ({configPath}): {string.Join("; ", configErrors)}.",
                    DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            // 4. Resolve template path (relative → AppContext.BaseDirectory; absolute as-is).
            var templatePath = ResolvePath(config.TemplatePath);
            if (!File.Exists(templatePath))
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Checklist template not found: {templatePath}", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            var outputPath = ctx.OutputPaths["checklist"];

            // 5. Read each findings input (ordinal key order) and flatten into rows with a
            //    running 1-based counter, preserving input order then within-report order.
            var orderedInputs = ctx.InputPaths
                .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                .ToList();

            var rows = new List<FeedbackChecklistRow>();
            int counter = 1;

            foreach (var (key, path) in orderedInputs)
            {
                ct.ThrowIfCancellationRequested();

                if (!File.Exists(path))
                {
                    messages.Add(new(MessageSeverity.Error,
                        $"Input findings file not found ('{key}'): {path}", DateTimeOffset.Now));
                    return new TaskResult(Succeeded: false, messages, sw.Elapsed);
                }

                var json = await File.ReadAllTextAsync(path, ct);

                ValidationReport report;
                try
                {
                    report = ValidationReportSerializer.Deserialize(json);
                }
                catch (JsonException ex)
                {
                    messages.Add(new(MessageSeverity.Error,
                        $"Input findings file '{key}' could not be parsed ({path}): {ex.Message}",
                        DateTimeOffset.Now));
                    return new TaskResult(Succeeded: false, messages, sw.Elapsed);
                }

                _logger.LogInformation(
                    "Input '{Key}' ({Sheet}): {Count} finding(s).", key, report.Sheet, report.Findings.Count);

                foreach (var finding in report.Findings)
                {
                    rows.Add(new FeedbackChecklistRow(
                        Counter:        counter++,
                        Worksheet:      report.Sheet,
                        QuestionNumber: finding.QuestionNumber,
                        CellAddresses:  finding.CellAddresses,
                        QuestionText:   finding.QuestionText,
                        RequestedData:  finding.RequestedData,
                        ProvidedBy:     finding.ProvidedBy,
                        Evaluation:     finding.Evaluation,
                        CheckResult:    finding.CheckResult));
                }
            }

            // 6. Populate the checklist (zero rows still produces the workbook).
            ct.ThrowIfCancellationRequested();

            var options = new FeedbackChecklistWriterOptions(
                config.SheetName, config.DataStartRow, config.ColumnMap!); // non-null: validated above

            _logger.LogInformation(
                "Populating checklist '{Sheet}' from {Inputs} input(s) with {Rows} row(s).",
                config.SheetName, orderedInputs.Count, rows.Count);

            _writer.Populate(rows, templatePath, outputPath, options);

            // 7. Curated outcome messages.
            if (rows.Count == 0)
                messages.Add(new(MessageSeverity.Info,
                    "No findings across all inputs; checklist produced with no data rows.",
                    DateTimeOffset.Now));

            messages.Add(new(MessageSeverity.Info,
                $"Assembled {rows.Count} finding(s) from {orderedInputs.Count} input(s) into " +
                $"{Path.GetFileName(outputPath)}.",
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

    // ── helpers ────────────────────────────────────────────────────────────────

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

    private static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
