using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;
using ItrqTool.Domain.Reporting;
using ItrqTool.Tasks.RiskLevelQuestionDiff;
using ItrqTool.Tasks.Shared;

namespace ItrqTool.Tasks;

public sealed class RiskLevelQuestionDiffTask : IWorkflowTask
{
    private const string DefaultReportTitle = "Risk Level Questions Diff Report";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IExcelStructureReader _structureReader;
    private readonly IHtmlReportWriter _htmlReportWriter;
    private readonly ILogger<RiskLevelQuestionDiffTask> _logger;

    public RiskLevelQuestionDiffTask(
        IExcelStructureReader structureReader,
        IHtmlReportWriter htmlReportWriter,
        ILogger<RiskLevelQuestionDiffTask> logger)
    {
        _structureReader = structureReader;
        _htmlReportWriter = htmlReportWriter;
        _logger = logger;
    }

    public string TaskType => "RiskLevelQuestionDiff";

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
            var previousConfig = JsonSerializer.Deserialize<RiskLevelQuestionsConfig>(previousConfigJson, JsonOptions)
                ?? new RiskLevelQuestionsConfig();

            var currentConfigJson = await File.ReadAllTextAsync(currentConfigPath, ct);
            var currentConfig = JsonSerializer.Deserialize<RiskLevelQuestionsConfig>(currentConfigJson, JsonOptions)
                ?? new RiskLevelQuestionsConfig();

            // 5. Read workbook rows
            _logger.LogInformation("Reading previous workbook: {Path}", previousPath);
            var previousRows = _structureReader.ReadRows(previousPath, previousConfig.SheetName);

            _logger.LogInformation("Reading current workbook: {Path}", currentPath);
            var currentRows = _structureReader.ReadRows(currentPath, currentConfig.SheetName);

            ct.ThrowIfCancellationRequested();

            // 6. Parse rows into RiskLevelQuestion lists
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

    private static IReadOnlyList<RiskLevelQuestion> ParseQuestions(
        IReadOnlyList<ExcelRowStructure> rows,
        RiskLevelQuestionsConfig config)
    {
        var parsedSections = config.ParsedSections; // throws FormatException on bad config
        var textCol = config.TextColumn.ToUpperInvariant();
        var numberCol = config.NumberColumn.ToUpperInvariant();
        var answerCol = config.AnswerColumn.ToUpperInvariant();
        var explanationCol = config.ExplanationColumn.ToUpperInvariant();

        var rowsByNumber = rows.ToDictionary(r => r.RowNumber);
        var questions = new List<RiskLevelQuestion>();

        foreach (var section in parsedSections)
        {
            rowsByNumber.TryGetValue(section.SectionRow, out var sectionStructure);
            var sectionText = sectionStructure is not null &&
                              sectionStructure.CellsByColumn.TryGetValue(textCol, out var sectionCell)
                ? sectionCell.TextValue ?? "" : "";

            for (int rowNum = section.FirstQuestionRow; rowNum <= section.LastQuestionRow; rowNum++)
            {
                if (!rowsByNumber.TryGetValue(rowNum, out var row))
                    continue;

                if (!row.CellsByColumn.TryGetValue(textCol, out var textCell))
                    continue;

                var questionText = textCell.TextValue ?? "";
                if (string.IsNullOrWhiteSpace(questionText))
                    continue;

                string? questionNumber = null;
                if (row.CellsByColumn.TryGetValue(numberCol, out var numberCell))
                {
                    var numText = numberCell.TextValue?.Trim();
                    if (!string.IsNullOrEmpty(numText))
                        questionNumber = numText;
                }

                row.CellsByColumn.TryGetValue(explanationCol, out var explanationCell);
                var explanationPrompt = explanationCell?.TextValue ?? "";

                row.CellsByColumn.TryGetValue(answerCol, out var answerCell);
                string? dvType     = answerCell?.DataValidationType;
                string? dvFormula  = answerCell?.DataValidationFormula;
                string? cfOperator = answerCell?.ConditionalFormattingOperator;
                string? dvOperator = answerCell?.DataValidationOperator;
                string? dvFormula2 = answerCell?.DataValidationFormula2;
                string? cfType     = answerCell?.ConditionalFormattingType;
                string? cfValue    = answerCell?.ConditionalFormattingValue;
                string? cfValue2   = answerCell?.ConditionalFormattingValue2;

                questions.Add(new RiskLevelQuestion(
                    sectionText,
                    questionText,
                    explanationPrompt,
                    questionNumber,
                    rowNum,
                    dvType,
                    dvFormula,
                    cfOperator,
                    dvOperator,
                    dvFormula2,
                    cfType,
                    cfValue,
                    cfValue2));
            }
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
                Chapter: "",
                a.Question.SectionName,
                a.Question.RowNumber,
                a.Question.QuestionText,
                DvDisplayFormatter.FormatFull(a.Question.DvType, a.Question.DvOperator, a.Question.DvFormula, a.Question.DvFormula2),
                CfDisplayFormatter.Format(a.Question.CfType, a.Question.CfOperator, a.Question.CfValue, a.Question.CfValue2)))
            .ToList();

        var removed = diff.Removed
            .Select(r => new HtmlDiffQuestion(
                r.Question.QuestionNumber,
                Chapter: "",
                r.Question.SectionName,
                r.Question.RowNumber,
                r.Question.QuestionText,
                DvDisplayFormatter.FormatFull(r.Question.DvType, r.Question.DvOperator, r.Question.DvFormula, r.Question.DvFormula2),
                CfDisplayFormatter.Format(r.Question.CfType, r.Question.CfOperator, r.Question.CfValue, r.Question.CfValue2)))
            .ToList();

        var changed = diff.Changed
            .Select(c => new HtmlDiffChangedQuestion(
                Chapter:              "",
                Section:              c.OldQuestion.SectionName,
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
                CfChanged:     c.CfChanged,
                OldExplanation:     c.OldQuestion.ExplanationPrompt,
                NewExplanation:     c.NewQuestion.ExplanationPrompt,
                ExplanationChanged: c.ExplanationChanged))
            .ToList();

        var unchanged = diff.Unchanged
            .Select(u => new HtmlDiffUnchangedQuestion(
                Chapter: "",
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
