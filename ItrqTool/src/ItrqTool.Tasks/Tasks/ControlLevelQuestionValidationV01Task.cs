using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.ControlLevelQuestionValidation;
using ItrqTool.Tasks.Validation;

namespace ItrqTool.Tasks;

/// <summary>
/// CLQ_v01 validation. Reads three Control-Level-Questions workbooks (the current
/// response, the empty template, and the previous response — all in the same internal
/// format, governed by one config), aligns them, runs the check catalogue, and writes a
/// <see cref="ValidationReport"/> JSON to the <c>report</c> output.
/// </summary>
/// <remarks>
/// Wiring contract (mirrors <see cref="FeedbackChecklistAssemblerTask"/> /
/// <see cref="ControlLevelQuestionDiffTask"/>):
/// <list type="bullet">
/// <item>Parameter <c>configurationFullFilename</c>: path to the CLQ_v01 config JSON.</item>
/// <item>Inputs <c>currentResponse</c>, <c>emptyTemplate</c>, <c>previousResponse</c>:
///   the three workbook paths.</item>
/// <item>Output <c>report</c>: the validation-findings JSON path.</item>
/// </list>
/// Succeeded semantics (locked): Succeeded:false ONLY on an unreadable input file or an
/// invalid / under-specified config (the fail-loud set). A Fatal *finding* does NOT fail
/// the task — it is data in the report.
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
            // 1. Config-file parameter
            if (!TryGetParam(ctx, "configurationFullFilename", out var configPath))
            {
                messages.Add(new(MessageSeverity.Error,
                    "Required parameter missing or empty: configurationFullFilename.",
                    DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            // 2. Resolve input/output paths
            if (!TryGetInput(ctx, "currentResponse", out var currentPath, messages) ||
                !TryGetInput(ctx, "emptyTemplate", out var templatePath, messages) ||
                !TryGetInput(ctx, "previousResponse", out var previousPath, messages))
            {
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            var reportPath = ctx.OutputPaths["report"];

            // 3. Positive disk pre-check — every input must exist before we read anything.
            var missing = new List<string>();
            if (!File.Exists(configPath)) missing.Add($"configurationFullFilename: {configPath}");
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

            // 4. Load + validate config (fail-loud, §4).
            ControlLevelQuestionValidationV01Config config;
            try
            {
                var configJson = await File.ReadAllTextAsync(configPath, ct);
                config = ControlLevelQuestionValidationV01ConfigLoader.Load(configJson);
            }
            catch (ClqConfigException ex)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Configuration error ({configPath}): {ex.Message}", DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            // 5. Read each workbook's configured sheet. One config governs all three.
            //    A missing/unreadable sheet → precise, sheet-named Error (CLAUDE.md posture),
            //    rather than letting the raw exception fall through to the general catch.
            if (!TryReadRows(currentPath, "currentResponse", config.SheetName, messages, out var currentRows) ||
                !TryReadRows(templatePath, "emptyTemplate", config.SheetName, messages, out var templateRows) ||
                !TryReadRows(previousPath, "previousResponse", config.SheetName, messages, out var previousRows))
            {
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            // 6. Parse each workbook (same internal format for all three).
            var currentQuestions = InternalClqQuestionParser.Parse(currentRows, config, messages);
            var templateQuestions = InternalClqQuestionParser.Parse(templateRows, config, messages);
            var previousQuestions = InternalClqQuestionParser.Parse(previousRows, config, messages);

            // 6a. Patch answer-column DV from address-driven ReadCells so blank-but-DV'd
            //     template H cells (skipped by ReadRows/CellsUsed) are captured correctly.
            currentQuestions  = PatchAnswerDv(currentPath,  config.SheetName, config.AnswerColumn, currentQuestions);
            templateQuestions = PatchAnswerDv(templatePath, config.SheetName, config.AnswerColumn, templateQuestions);
            previousQuestions = PatchAnswerDv(previousPath, config.SheetName, config.AnswerColumn, previousQuestions);

            ct.ThrowIfCancellationRequested();

            // 7. Align + check.
            var alignment = ClqAlignmentEngine.Align(currentQuestions, templateQuestions, previousQuestions);
            var findings = ClqValidationChecks.Build(alignment, config);

            ct.ThrowIfCancellationRequested();

            // 8. Serialize + write the report (zero findings → still a well-formed report).
            var report = new ValidationReport(config.SheetName, TaskType, findings);
            await File.WriteAllTextAsync(reportPath, ValidationReportSerializer.Serialize(report), ct);

            // 9. Curated summary.
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

    // ── helpers ──────────────────────────────────────────────────────────────────

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

    private IReadOnlyList<InternalClqQuestion> PatchAnswerDv(
        string path, string sheetName, string answerColumn,
        IReadOnlyList<InternalClqQuestion> questions)
    {
        if (questions.Count == 0) return questions;
        var col = answerColumn.ToUpperInvariant();
        int firstRow = questions.Min(q => q.RowNumber);
        int lastRow  = questions.Max(q => q.RowNumber);
        var cells = _structureReader.ReadCells(path, sheetName, [$"{col}{firstRow}:{col}{lastRow}"]);
        return questions
            .Select(q =>
            {
                var addr = $"{col}{q.RowNumber}";
                var cell = cells.TryGetValue(addr, out var c) ? c : null;
                return q with
                {
                    AnswerDvType     = cell?.DataValidationType,
                    AnswerDvFormula  = cell?.DataValidationFormula,
                    AnswerDvOperator = cell?.DataValidationOperator,
                    AnswerDvFormula2 = cell?.DataValidationFormula2
                };
            })
            .ToList();
    }

    private bool TryReadRows(
        string path, string inputKey, string sheetName,
        List<TaskMessage> messages, out IReadOnlyList<ExcelRowStructure> rows)
    {
        // ReadRows throws for a missing sheet (ClosedXML). Convert any read failure into a
        // precise, sheet-named Error rather than a raw exception.
        try
        {
            _logger.LogInformation("Reading {Key} sheet '{Sheet}': {Path}", inputKey, sheetName, path);
            rows = _structureReader.ReadRows(path, sheetName);
            return true;
        }
        catch (Exception ex)
        {
            messages.Add(new(MessageSeverity.Error,
                $"Could not read sheet '{sheetName}' from {inputKey} ({path}): {ex.Message}",
                DateTimeOffset.Now));
            rows = [];
            return false;
        }
    }
}
