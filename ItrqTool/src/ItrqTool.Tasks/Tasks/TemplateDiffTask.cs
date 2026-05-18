using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;
using ItrqTool.Tasks.TemplateDiff;

namespace ItrqTool.Tasks;

public sealed class TemplateDiffTask : IWorkflowTask
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IExcelStructureReader _structureReader;
    private readonly IExcelWriter _excelWriter;
    private readonly ILogger<TemplateDiffTask> _logger;

    public TemplateDiffTask(
        IExcelStructureReader structureReader,
        IExcelWriter excelWriter,
        ILogger<TemplateDiffTask> logger)
    {
        _structureReader = structureReader;
        _excelWriter = excelWriter;
        _logger = logger;
    }

    public string TaskType => "TemplateDiff";

    public async Task<TaskResult> ExecuteAsync(TaskExecutionContext ctx, CancellationToken ct)
    {
        var messages = new List<TaskMessage>();
        var sw = Stopwatch.StartNew();

        try
        {
            // 1. Validate parameters
            if (!TryGetParam(ctx, "oldWorkbookPath", out var oldPath) ||
                !TryGetParam(ctx, "newWorkbookPath", out var newPath) ||
                !TryGetParam(ctx, "configPath", out var configPath))
            {
                messages.Add(new(MessageSeverity.Error,
                    "Required parameters missing. Expected: oldWorkbookPath, newWorkbookPath, configPath.",
                    DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            // 2. Validate file existence
            var missing = new List<string>();
            if (!File.Exists(oldPath)) missing.Add($"oldWorkbookPath: {oldPath}");
            if (!File.Exists(newPath)) missing.Add($"newWorkbookPath: {newPath}");
            if (!File.Exists(configPath)) missing.Add($"configPath: {configPath}");

            if (missing.Count > 0)
            {
                messages.Add(new(MessageSeverity.Error,
                    "File(s) not found: " + string.Join("; ", missing),
                    DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            // 3. Deserialize config
            var configJson = await File.ReadAllTextAsync(configPath, ct);
            var config = JsonSerializer.Deserialize<ControlLevelQuestionsConfig>(configJson, JsonOptions)
                ?? new ControlLevelQuestionsConfig();

            // 4. Read old workbook rows
            _logger.LogInformation("Reading old workbook: {Path}", oldPath);
            var oldRows = _structureReader.ReadRows(oldPath, config.SheetName);

            // 5. Read new workbook rows
            _logger.LogInformation("Reading new workbook: {Path}", newPath);
            var newRows = _structureReader.ReadRows(newPath, config.SheetName);

            ct.ThrowIfCancellationRequested();

            // 6. Parse rows into AuditQuestion lists
            var oldQuestions = ParseQuestions(oldRows, config);
            var newQuestions = ParseQuestions(newRows, config);

            _logger.LogInformation("Parsed {OldCount} questions from old workbook.", oldQuestions.Count);
            _logger.LogInformation("Parsed {NewCount} questions from new workbook.", newQuestions.Count);

            // 7. Diff
            _logger.LogInformation("Diff complete. Running QuestionDiffEngine.");
            var diff = QuestionDiffEngine.Diff(oldQuestions, newQuestions);

            ct.ThrowIfCancellationRequested();

            // 8. Build report workbook
            var reportPath = ctx.OutputPaths["report"];
            var workbookData = BuildReport(config, diff, oldQuestions.Count + newQuestions.Count);

            // 9. Write report
            _excelWriter.WriteWorkbook(workbookData, reportPath);

            // 10. Curated messages
            int totalCompared = oldQuestions.Count;
            messages.Add(new(MessageSeverity.Info,
                $"Compared {totalCompared} questions across 1 sheet.",
                DateTimeOffset.Now));
            messages.Add(new(MessageSeverity.Info,
                $"Found {diff.Added.Count} added, {diff.Removed.Count} removed, " +
                $"{diff.Changed.Count} changed, {diff.ValidationChanges.Count} validation changes.",
                DateTimeOffset.Now));
            messages.Add(new(MessageSeverity.Info,
                $"Report written to {Path.GetFileName(reportPath)}",
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

    private static IReadOnlyList<AuditQuestion> ParseQuestions(
        IReadOnlyList<ExcelRowStructure> rows,
        ControlLevelQuestionsConfig config)
    {
        var chapterSet = new HashSet<int>(config.ChapterRows);
        var sectionSet = new HashSet<int>(config.SectionRows);

        var questions = new List<AuditQuestion>();
        string currentChapter = string.Empty;
        string currentSection = string.Empty;

        foreach (var row in rows)
        {
            if (chapterSet.Contains(row.RowNumber))
            {
                currentChapter = row.CellsByColumn.TryGetValue(
                    config.TextColumn.ToUpperInvariant(), out var c) ? c.TextValue ?? "" : "";
                currentSection = string.Empty;
                continue;
            }

            if (sectionSet.Contains(row.RowNumber))
            {
                currentSection = row.CellsByColumn.TryGetValue(
                    config.TextColumn.ToUpperInvariant(), out var c) ? c.TextValue ?? "" : "";
                continue;
            }

            // Question row
            if (!row.CellsByColumn.TryGetValue(config.TextColumn.ToUpperInvariant(), out var textCell))
                continue;

            var originalText = textCell.TextValue ?? "";
            if (string.IsNullOrWhiteSpace(originalText))
                continue;

            var inputCol = config.InputColumn.ToUpperInvariant();
            string? dvType = row.CellsByColumn.TryGetValue(inputCol, out var inputCell)
                ? inputCell.DataValidationType
                : null;
            string? cfOperator = row.CellsByColumn.TryGetValue(inputCol, out var inputCell2)
                ? inputCell2.ConditionalFormattingOperator
                : null;

            questions.Add(new AuditQuestion(
                currentChapter,
                currentSection,
                AuditQuestion.StripPrefix(originalText),
                originalText,
                row.RowNumber,
                dvType,
                cfOperator));
        }

        return questions;
    }

    private static ExcelWorkbookData BuildReport(
        ControlLevelQuestionsConfig config,
        DiffResult diff,
        int totalQuestionsConsidered)
    {
        var sheets = new List<ExcelSheetData>
        {
            // Summary
            new("Summary",
                ["SheetName", "Added", "Removed", "Changed", "ValidationChanges"],
                [[
                    config.SheetName,
                    diff.Added.Count.ToString(),
                    diff.Removed.Count.ToString(),
                    diff.Changed.Count.ToString(),
                    diff.ValidationChanges.Count.ToString()
                ]]),

            // Added
            new("Added",
                ["Chapter", "Section", "QuestionText", "DvType", "CfOperator"],
                diff.Added.Select(a => (IReadOnlyList<string>)[
                    a.Question.ChapterName,
                    a.Question.SectionName,
                    a.Question.QuestionText,
                    a.Question.DvType ?? "",
                    a.Question.CfOperator ?? ""
                ]).ToList()),

            // Removed
            new("Removed",
                ["Chapter", "Section", "QuestionText", "DvType", "CfOperator"],
                diff.Removed.Select(r => (IReadOnlyList<string>)[
                    r.Question.ChapterName,
                    r.Question.SectionName,
                    r.Question.QuestionText,
                    r.Question.DvType ?? "",
                    r.Question.CfOperator ?? ""
                ]).ToList()),

            // Changed
            new("Changed",
                ["Chapter", "Section", "OldText", "NewText", "SimilarityScore",
                 "DvTypeChanged", "CfOperatorChanged"],
                diff.Changed.Select(c =>
                {
                    bool dvChanged = c.OldQuestion.DvType != c.NewQuestion.DvType;
                    bool cfChanged = c.OldQuestion.CfOperator != c.NewQuestion.CfOperator;
                    return (IReadOnlyList<string>)[
                        c.OldQuestion.ChapterName,
                        c.OldQuestion.SectionName,
                        c.OldQuestion.QuestionText,
                        c.NewQuestion.QuestionText,
                        c.SimilarityScore.ToString("F2"),
                        dvChanged ? "Yes" : "No",
                        cfChanged ? "Yes" : "No"
                    ];
                }).ToList()),

            // Validation Changes
            new("Validation Changes",
                ["Chapter", "Section", "QuestionText", "OldDvType", "NewDvType",
                 "OldCfOperator", "NewCfOperator"],
                diff.ValidationChanges.Select(v => (IReadOnlyList<string>)[
                    v.OldQuestion.ChapterName,
                    v.OldQuestion.SectionName,
                    v.OldQuestion.QuestionText,
                    v.OldDvType ?? "",
                    v.NewDvType ?? "",
                    v.OldCfOperator ?? "",
                    v.NewCfOperator ?? ""
                ]).ToList())
        };

        return new ExcelWorkbookData(sheets);
    }
}
