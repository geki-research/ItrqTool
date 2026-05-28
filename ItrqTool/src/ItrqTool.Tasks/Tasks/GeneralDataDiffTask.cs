using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;
using ItrqTool.Domain.Reporting;
using ItrqTool.Tasks.GeneralDataDiff;

namespace ItrqTool.Tasks;

public sealed class GeneralDataDiffTask : IWorkflowTask
{
    private const string DefaultReportTitle = "General Data Diff Report";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IExcelStructureReader _structureReader;
    private readonly IHtmlGeneralDataDiffReportWriter _htmlReportWriter;
    private readonly ILogger<GeneralDataDiffTask> _logger;

    public GeneralDataDiffTask(
        IExcelStructureReader structureReader,
        IHtmlGeneralDataDiffReportWriter htmlReportWriter,
        ILogger<GeneralDataDiffTask> logger)
    {
        _structureReader = structureReader;
        _htmlReportWriter = htmlReportWriter;
        _logger = logger;
    }

    public string TaskType => "GeneralDataDiff";

    public async Task<TaskResult> ExecuteAsync(TaskExecutionContext ctx, CancellationToken ct)
    {
        var messages = new List<TaskMessage>();
        var sw = Stopwatch.StartNew();

        try
        {
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

            ctx.Parameters.TryGetValue("reportTitle", out var reportTitleRaw);
            var reportTitle = string.IsNullOrWhiteSpace(reportTitleRaw)
                ? DefaultReportTitle
                : reportTitleRaw;

            var missing = new List<string>();
            if (!File.Exists(previousPath))       missing.Add($"previousWorkbookFullFilename: {previousPath}");
            if (!File.Exists(currentPath))        missing.Add($"currentWorkbookFullFilename: {currentPath}");
            if (!File.Exists(previousConfigPath)) missing.Add($"previousConfigurationFullFilename: {previousConfigPath}");
            if (!File.Exists(currentConfigPath))  missing.Add($"currentConfigurationFullFilename: {currentConfigPath}");

            if (missing.Count > 0)
            {
                messages.Add(new(MessageSeverity.Error,
                    "File(s) not found: " + string.Join("; ", missing),
                    DateTimeOffset.Now));
                return new TaskResult(Succeeded: false, messages, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            var previousConfigJson = await File.ReadAllTextAsync(previousConfigPath, ct);
            var previousConfig = JsonSerializer.Deserialize<GeneralDataConfig>(previousConfigJson, JsonOptions)
                ?? new GeneralDataConfig();

            var currentConfigJson = await File.ReadAllTextAsync(currentConfigPath, ct);
            var currentConfig = JsonSerializer.Deserialize<GeneralDataConfig>(currentConfigJson, JsonOptions)
                ?? new GeneralDataConfig();

            _logger.LogInformation("Reading previous workbook: {Path}", previousPath);
            var previousRows = _structureReader.ReadRows(previousPath, previousConfig.SheetName);

            _logger.LogInformation("Reading current workbook: {Path}", currentPath);
            var currentRows = _structureReader.ReadRows(currentPath, currentConfig.SheetName);

            ct.ThrowIfCancellationRequested();

            var previousQuestions = GeneralDataQuestionParser.Parse(previousRows, previousConfig);
            var currentQuestions  = GeneralDataQuestionParser.Parse(currentRows, currentConfig);

            _logger.LogInformation("Parsed {PreviousCount} questions from previous workbook.", previousQuestions.Count);
            _logger.LogInformation("Parsed {CurrentCount} questions from current workbook.", currentQuestions.Count);

            _logger.LogInformation("Running GeneralDataDiffEngine.");
            var diff = GeneralDataDiffEngine.Diff(previousQuestions, currentQuestions);

            ct.ThrowIfCancellationRequested();

            var reportPath = ctx.OutputPaths["report"];
            var reportData = BuildReportData(reportTitle, previousPath, currentPath, diff);
            _htmlReportWriter.WriteReport(reportData, reportPath);

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

    private static HtmlDiffGeneralDataReportData BuildReportData(
        string title,
        string previousWorkbookPath,
        string currentWorkbookPath,
        DiffResult diff)
    {
        var added = diff.Added
            .Select(a => MapQuestion(a.Question))
            .ToList();

        var removed = diff.Removed
            .Select(r => MapQuestion(r.Question))
            .ToList();

        var changed = diff.Changed
            .Select(c => new HtmlDiffGeneralDataChangedQuestion(
                Section:                  c.OldQuestion.SectionName,
                PreviousRowNumber:        c.OldQuestion.RowNumber,
                CurrentRowNumber:         c.NewQuestion.RowNumber,
                PreviousNumber:           c.OldQuestion.QuestionNumber,
                CurrentNumber:            c.NewQuestion.QuestionNumber,
                OldText:                  c.OldQuestion.QuestionText,
                NewText:                  c.NewQuestion.QuestionText,
                SimilarityScore:          c.SimilarityScore,
                SecondBestSimilarity:     c.SecondBestSimilarity,
                TextChanged:              c.TextChanged,
                NumberChanged:            c.NumberChanged,
                AnswerCellsChanged:       c.AnswerCellsChanged,
                ExplanationCellsChanged:  c.ExplanationCellsChanged,
                AnswerCellChanges:        c.AnswerCellChanges.Select(MapAnswerChange).ToList(),
                ExplanationCellChanges:   c.ExplanationCellChanges.Select(MapExplanationChange).ToList()))
            .ToList();

        var unchanged = diff.Unchanged
            .Select(u => new HtmlDiffGeneralDataUnchangedQuestion(
                Section:               u.Question.SectionName,
                PreviousRowNumber:     u.PreviousRowNumber,
                CurrentRowNumber:      u.Question.RowNumber,
                QuestionNumber:        u.Question.QuestionNumber,
                QuestionText:          u.Question.QuestionText,
                RowNumberLabels:       u.Question.RowNumberLabels,
                AnswerCells:           u.Question.AnswerCells.Select(MapAnswerCell).ToList(),
                ExplanationCells:      u.Question.ExplanationCells.Select(MapExplanationCell).ToList(),
                SimilarityScore:       1.0,
                SecondBestSimilarity:  u.SecondBestSimilarity))
            .ToList();

        return new HtmlDiffGeneralDataReportData(
            title,
            previousWorkbookPath,
            currentWorkbookPath,
            DateTimeOffset.Now,
            added,
            removed,
            changed,
            unchanged);
    }

    private static HtmlDiffGeneralDataQuestion MapQuestion(GeneralDataQuestion q)
        => new(
            QuestionNumber:   q.QuestionNumber,
            Section:          q.SectionName,
            RowNumber:        q.RowNumber,
            QuestionText:     q.QuestionText,
            RowNumberLabels:  q.RowNumberLabels,
            AnswerCells:      q.AnswerCells.Select(MapAnswerCell).ToList(),
            ExplanationCells: q.ExplanationCells.Select(MapExplanationCell).ToList());

    private static HtmlDiffGeneralDataAnswerCell MapAnswerCell(GeneralDataAnswerCell c)
        => new(
            RowOffset:  c.RowOffset,
            Column:     c.Column,
            Text:       c.Text,
            DvDisplay:  DvDisplayFormatter.FormatDv(c.DvType, c.DvFormula),
            CfOperator: c.CfOperator);

    private static HtmlDiffGeneralDataExplanationCell MapExplanationCell(GeneralDataExplanationCell c)
        => new(
            RowOffset:  c.RowOffset,
            Text:       c.Text,
            DvDisplay:  DvDisplayFormatter.FormatDv(c.DvType, c.DvFormula),
            CfOperator: c.CfOperator);

    private static HtmlDiffAnswerCellChange MapAnswerChange(AnswerCellChange ch)
        => new(
            RowOffset:      ch.RowOffset,
            Column:         ch.Column,
            OldText:        ch.OldText,
            NewText:        ch.NewText,
            OldDvDisplay:   DvDisplayFormatter.FormatDv(ch.OldDvType, ch.OldDvFormula),
            NewDvDisplay:   DvDisplayFormatter.FormatDv(ch.NewDvType, ch.NewDvFormula),
            OldCfOperator:  ch.OldCfOperator,
            NewCfOperator:  ch.NewCfOperator,
            TextChanged:    ch.TextChanged,
            DvChanged:      ch.DvChanged,
            CfChanged:      ch.CfChanged);

    private static HtmlDiffExplanationCellChange MapExplanationChange(ExplanationCellChange ch)
        => new(
            RowOffset:     ch.RowOffset,
            OldText:       ch.OldText,
            NewText:       ch.NewText,
            OldDvDisplay:  DvDisplayFormatter.FormatDv(ch.OldDvType, ch.OldDvFormula),
            NewDvDisplay:  DvDisplayFormatter.FormatDv(ch.NewDvType, ch.NewDvFormula),
            OldCfOperator: ch.OldCfOperator,
            NewCfOperator: ch.NewCfOperator,
            TextChanged:   ch.TextChanged,
            DvChanged:     ch.DvChanged,
            CfChanged:     ch.CfChanged);
}
