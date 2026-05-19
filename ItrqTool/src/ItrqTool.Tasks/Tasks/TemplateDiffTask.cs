using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;
using ItrqTool.Domain.Reporting;
using ItrqTool.Tasks.TemplateDiff;

namespace ItrqTool.Tasks;

public sealed class TemplateDiffTask : IWorkflowTask
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IExcelStructureReader _structureReader;
    private readonly IHtmlReportWriter _htmlReportWriter;
    private readonly ILogger<TemplateDiffTask> _logger;

    public TemplateDiffTask(
        IExcelStructureReader structureReader,
        IHtmlReportWriter htmlReportWriter,
        ILogger<TemplateDiffTask> logger)
    {
        _structureReader = structureReader;
        _htmlReportWriter = htmlReportWriter;
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
            var missingParams = new List<string>();
            if (!TryGetParam(ctx, "previousWorkbookFullFilename", out var previousPath))
                missingParams.Add("previousWorkbookFullFilename");
            if (!TryGetParam(ctx, "currentWorkbookFullFilename", out var currentPath))
                missingParams.Add("currentWorkbookFullFilename");
            if (!TryGetParam(ctx, "previousConfigurationFullFilename", out var previousConfigPath))
                missingParams.Add("previousConfigurationFullFilename");
            if (!TryGetParam(ctx, "currentConfigurationFullFilename", out var currentConfigPath))
                missingParams.Add("currentConfigurationFullFilename");

            if (missingParams.Count > 0)
            {
                messages.Add(new(MessageSeverity.Error,
                    $"Required parameter(s) missing or empty: {string.Join(", ", missingParams)}.",
                    DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            // 2. Validate file existence
            var missing = new List<string>();
            if (!File.Exists(previousPath)) missing.Add($"previousWorkbookFullFilename: {previousPath}");
            if (!File.Exists(currentPath)) missing.Add($"currentWorkbookFullFilename: {currentPath}");
            if (!File.Exists(previousConfigPath)) missing.Add($"previousConfigurationFullFilename: {previousConfigPath}");
            if (!File.Exists(currentConfigPath)) missing.Add($"currentConfigurationFullFilename: {currentConfigPath}");

            if (missing.Count > 0)
            {
                messages.Add(new(MessageSeverity.Error,
                    "File(s) not found: " + string.Join("; ", missing),
                    DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            // 3. Deserialize configs
            var previousConfigJson = await File.ReadAllTextAsync(previousConfigPath, ct);
            var previousConfig = JsonSerializer.Deserialize<ControlLevelQuestionsConfig>(previousConfigJson, JsonOptions)
                ?? new ControlLevelQuestionsConfig();

            var currentConfigJson = await File.ReadAllTextAsync(currentConfigPath, ct);
            var currentConfig = JsonSerializer.Deserialize<ControlLevelQuestionsConfig>(currentConfigJson, JsonOptions)
                ?? new ControlLevelQuestionsConfig();

            // 4. Read previous workbook rows
            _logger.LogInformation("Reading previous workbook: {Path}", previousPath);
            var previousRows = _structureReader.ReadRows(previousPath, previousConfig.SheetName);

            // 5. Read current workbook rows
            _logger.LogInformation("Reading current workbook: {Path}", currentPath);
            var currentRows = _structureReader.ReadRows(currentPath, currentConfig.SheetName);

            ct.ThrowIfCancellationRequested();

            // 6. Parse rows into AuditQuestion lists
            var previousQuestions = ParseQuestions(previousRows, previousConfig);
            var currentQuestions = ParseQuestions(currentRows, currentConfig);

            _logger.LogInformation("Parsed {PreviousCount} questions from previous workbook.", previousQuestions.Count);
            _logger.LogInformation("Parsed {CurrentCount} questions from current workbook.", currentQuestions.Count);

            // 7. Diff
            _logger.LogInformation("Running QuestionDiffEngine.");
            var diff = QuestionDiffEngine.Diff(previousQuestions, currentQuestions);

            ct.ThrowIfCancellationRequested();

            // 8. Build report data
            var reportPath = ctx.OutputPaths["report"];
            var reportData = BuildReportData(previousPath, currentPath, diff);

            // 9. Write HTML report
            _htmlReportWriter.WriteReport(reportData, reportPath);

            // 10. Curated messages
            int totalCompared = previousQuestions.Count;
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
        var parsedSections = config.ParsedSections; // throws FormatException on bad config
        var chapterSet = new HashSet<int>(config.ChapterRows);
        var sectionRowSet = new HashSet<int>(parsedSections.Select(s => s.SectionRow));
        var textCol = config.TextColumn.ToUpperInvariant();
        var inputCol = config.InputColumn.ToUpperInvariant();

        // Pre-pass: collect text for all header rows
        var headerText = new Dictionary<int, string>();
        foreach (var row in rows)
        {
            if (chapterSet.Contains(row.RowNumber) || sectionRowSet.Contains(row.RowNumber))
            {
                headerText[row.RowNumber] = row.CellsByColumn.TryGetValue(textCol, out var c)
                    ? c.TextValue ?? "" : "";
            }
        }

        var sortedChapterRows = config.ChapterRows.OrderBy(r => r).ToList();
        var questions = new List<AuditQuestion>();

        foreach (var row in rows)
        {
            if (chapterSet.Contains(row.RowNumber) || sectionRowSet.Contains(row.RowNumber))
                continue;

            var section = parsedSections.FirstOrDefault(s =>
                row.RowNumber >= s.FirstQuestionRow && row.RowNumber <= s.LastQuestionRow);

            if (section is null)
                continue;

            if (!row.CellsByColumn.TryGetValue(textCol, out var textCell))
                continue;

            var originalText = textCell.TextValue ?? "";
            if (string.IsNullOrWhiteSpace(originalText))
                continue;

            var chapterRow = sortedChapterRows.LastOrDefault(cr => cr <= row.RowNumber);
            var chapterText = chapterRow > 0 && headerText.TryGetValue(chapterRow, out var ct) ? ct : "";
            var sectionText = headerText.TryGetValue(section.SectionRow, out var st) ? st : "";

            string? dvType = row.CellsByColumn.TryGetValue(inputCol, out var inputCell)
                ? inputCell.DataValidationType : null;
            string? cfOperator = row.CellsByColumn.TryGetValue(inputCol, out var inputCell2)
                ? inputCell2.ConditionalFormattingOperator : null;

            questions.Add(new AuditQuestion(
                chapterText,
                sectionText,
                AuditQuestion.StripPrefix(originalText),
                originalText,
                row.RowNumber,
                dvType,
                cfOperator));
        }

        return questions;
    }

    private static HtmlDiffReportData BuildReportData(
        string previousWorkbookPath,
        string currentWorkbookPath,
        DiffResult diff)
    {
        var added = diff.Added
            .Select(a => new HtmlDiffQuestion(
                a.Question.ChapterName,
                a.Question.SectionName,
                a.Question.QuestionText,
                a.Question.DvType,
                a.Question.CfOperator))
            .ToList();

        var removed = diff.Removed
            .Select(r => new HtmlDiffQuestion(
                r.Question.ChapterName,
                r.Question.SectionName,
                r.Question.QuestionText,
                r.Question.DvType,
                r.Question.CfOperator))
            .ToList();

        var changed = diff.Changed
            .Select(c => new HtmlDiffChangedQuestion(
                c.OldQuestion.ChapterName,
                c.OldQuestion.SectionName,
                c.OldQuestion.QuestionText,
                c.NewQuestion.QuestionText,
                c.SimilarityScore,
                DvTypeChanged: c.OldQuestion.DvType != c.NewQuestion.DvType,
                CfOperatorChanged: c.OldQuestion.CfOperator != c.NewQuestion.CfOperator))
            .ToList();

        var validationChanges = diff.ValidationChanges
            .Select(v => new HtmlDiffValidationChange(
                v.OldQuestion.ChapterName,
                v.OldQuestion.SectionName,
                v.OldQuestion.QuestionText,
                v.OldQuestion.DvType,
                v.NewQuestion.DvType,
                v.OldQuestion.CfOperator,
                v.NewQuestion.CfOperator))
            .ToList();

        return new HtmlDiffReportData(
            previousWorkbookPath,
            currentWorkbookPath,
            DateTimeOffset.Now,
            added,
            removed,
            changed,
            validationChanges);
    }
}
