using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;
using ItrqTool.Domain.Reporting;
using ItrqTool.Tasks.ControlLevelQuestionDiff;
using ItrqTool.Tasks.Shared;

namespace ItrqTool.Tasks;

public sealed class ControlLevelQuestionDiffTask : IWorkflowTask
{
    private const string DefaultReportTitle = "Control Level Questions Diff Report";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IExcelStructureReader _structureReader;
    private readonly IHtmlReportWriter _htmlReportWriter;
    private readonly ILogger<ControlLevelQuestionDiffTask> _logger;

    public ControlLevelQuestionDiffTask(
        IExcelStructureReader structureReader,
        IHtmlReportWriter htmlReportWriter,
        ILogger<ControlLevelQuestionDiffTask> logger)
    {
        _structureReader = structureReader;
        _htmlReportWriter = htmlReportWriter;
        _logger = logger;
    }

    public string TaskType => "ControlLevelQuestionDiff";

    public async Task<TaskResult> ExecuteAsync(TaskExecutionContext ctx, CancellationToken ct)
    {
        var messages = new List<TaskMessage>();
        var sw = Stopwatch.StartNew();

        try
        {
            // 1. Validate required parameters
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

            // 2. Read optional reportTitle parameter
            ctx.Parameters.TryGetValue("reportTitle", out var reportTitleRaw);
            var reportTitle = string.IsNullOrWhiteSpace(reportTitleRaw)
                ? DefaultReportTitle
                : reportTitleRaw;

            // 3. Validate file existence
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

            // 4. Deserialize configs
            var previousConfigJson = await File.ReadAllTextAsync(previousConfigPath, ct);
            var previousConfig = JsonSerializer.Deserialize<ControlLevelQuestionsConfig>(previousConfigJson, JsonOptions)
                ?? new ControlLevelQuestionsConfig();

            var currentConfigJson = await File.ReadAllTextAsync(currentConfigPath, ct);
            var currentConfig = JsonSerializer.Deserialize<ControlLevelQuestionsConfig>(currentConfigJson, JsonOptions)
                ?? new ControlLevelQuestionsConfig();

            // 5. Read workbook rows
            _logger.LogInformation("Reading previous workbook: {Path}", previousPath);
            var previousRows = _structureReader.ReadRows(previousPath, previousConfig.SheetName);

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

            // 8. Build and write report
            var reportPath = ctx.OutputPaths["report"];
            var reportData = BuildReportData(reportTitle, previousPath, currentPath, diff);
            _htmlReportWriter.WriteReport(reportData, reportPath);

            // 9. Curated messages
            messages.Add(new(MessageSeverity.Info,
                $"Compared {previousQuestions.Count} questions across 1 sheet.",
                DateTimeOffset.Now));
            messages.Add(new(MessageSeverity.Info,
                $"Found {diff.Added.Count} added, {diff.Removed.Count} removed, " +
                $"{diff.Changed.Count} changed, {diff.Unchanged.Count} unchanged.",
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

            row.CellsByColumn.TryGetValue(inputCol, out var inputCell);
            string? dvType     = inputCell?.DataValidationType;
            string? dvFormula  = inputCell?.DataValidationFormula;
            string? cfOperator = inputCell?.ConditionalFormattingOperator;
            string? dvOperator = inputCell?.DataValidationOperator;
            string? dvFormula2 = inputCell?.DataValidationFormula2;
            string? cfType     = inputCell?.ConditionalFormattingType;
            string? cfValue    = inputCell?.ConditionalFormattingValue;
            string? cfValue2   = inputCell?.ConditionalFormattingValue2;

            questions.Add(new AuditQuestion(
                chapterText,
                sectionText,
                AuditQuestion.StripPrefix(originalText),
                originalText,
                AuditQuestion.ExtractNumber(originalText),
                row.RowNumber,
                dvType,
                dvFormula,
                cfOperator,
                dvOperator,
                dvFormula2,
                cfType,
                cfValue,
                cfValue2));
        }

        return questions;
    }

    private static HtmlDiffReportData BuildReportData(
        string title,
        string previousWorkbookPath,
        string currentWorkbookPath,
        DiffResult diff)
    {
        var added = diff.Added
            .Select(a => new HtmlDiffQuestion(
                a.Question.QuestionNumber,
                a.Question.ChapterName,
                a.Question.SectionName,
                a.Question.RowNumber,
                a.Question.QuestionText,
                DvDisplayFormatter.FormatFull(a.Question.DvType, a.Question.DvOperator, a.Question.DvFormula, a.Question.DvFormula2),
                CfDisplayFormatter.Format(a.Question.CfType, a.Question.CfOperator, a.Question.CfValue, a.Question.CfValue2)))
            .ToList();

        var removed = diff.Removed
            .Select(r => new HtmlDiffQuestion(
                r.Question.QuestionNumber,
                r.Question.ChapterName,
                r.Question.SectionName,
                r.Question.RowNumber,
                r.Question.QuestionText,
                DvDisplayFormatter.FormatFull(r.Question.DvType, r.Question.DvOperator, r.Question.DvFormula, r.Question.DvFormula2),
                CfDisplayFormatter.Format(r.Question.CfType, r.Question.CfOperator, r.Question.CfValue, r.Question.CfValue2)))
            .ToList();

        var changed = diff.Changed
            .Select(c => new HtmlDiffChangedQuestion(
                c.OldQuestion.ChapterName,
                c.OldQuestion.SectionName,
                PreviousRowNumber:    c.OldQuestion.RowNumber,
                CurrentRowNumber:     c.NewQuestion.RowNumber,
                PreviousNumber:       c.OldQuestion.QuestionNumber,
                CurrentNumber:        c.NewQuestion.QuestionNumber,
                c.OldQuestion.QuestionText,
                c.NewQuestion.QuestionText,
                c.SimilarityScore,
                SecondBestSimilarity: c.SecondBestSimilarity,
                OldDvDisplay: DvDisplayFormatter.FormatFull(c.OldQuestion.DvType, c.OldQuestion.DvOperator, c.OldQuestion.DvFormula, c.OldQuestion.DvFormula2),
                NewDvDisplay: DvDisplayFormatter.FormatFull(c.NewQuestion.DvType, c.NewQuestion.DvOperator, c.NewQuestion.DvFormula, c.NewQuestion.DvFormula2),
                OldCfDisplay: CfDisplayFormatter.Format(c.OldQuestion.CfType, c.OldQuestion.CfOperator, c.OldQuestion.CfValue, c.OldQuestion.CfValue2),
                NewCfDisplay: CfDisplayFormatter.Format(c.NewQuestion.CfType, c.NewQuestion.CfOperator, c.NewQuestion.CfValue, c.NewQuestion.CfValue2),
                TextChanged:   c.TextChanged,
                NumberChanged: c.NumberChanged,
                DvChanged:     c.DvChanged,
                CfChanged:     c.CfChanged))
            .ToList();

        var unchanged = diff.Unchanged
            .Select(u => new HtmlDiffUnchangedQuestion(
                u.Question.ChapterName,
                u.Question.SectionName,
                u.PreviousRowNumber,
                u.Question.RowNumber,
                u.Question.QuestionNumber,
                u.Question.QuestionText,
                DvDisplayFormatter.FormatFull(u.Question.DvType, u.Question.DvOperator, u.Question.DvFormula, u.Question.DvFormula2),
                CfDisplayFormatter.Format(u.Question.CfType, u.Question.CfOperator, u.Question.CfValue, u.Question.CfValue2),
                SimilarityScore:      1.0,
                SecondBestSimilarity: u.SecondBestSimilarity))
            .ToList();

        return new HtmlDiffReportData(
            title,
            previousWorkbookPath,
            currentWorkbookPath,
            DateTimeOffset.Now,
            added,
            removed,
            changed,
            unchanged);
    }
}
