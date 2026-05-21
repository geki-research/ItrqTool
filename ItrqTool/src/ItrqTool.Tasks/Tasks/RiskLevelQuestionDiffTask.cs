using Microsoft.Extensions.Logging;
using ItrqTool.Domain;
using ItrqTool.Domain.Reporting;
using ItrqTool.Tasks.RiskLevelQuestionDiff;

namespace ItrqTool.Tasks;

public sealed class RiskLevelQuestionDiffTask : IWorkflowTask
{
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
        _ = _structureReader;
        _ = _htmlReportWriter;
        _ = _logger;
    }

    public string TaskType => "RiskLevelQuestionDiff";

    public Task<TaskResult> ExecuteAsync(TaskExecutionContext context, CancellationToken ct)
    {
        throw new NotImplementedException(
            "RiskLevelQuestionDiffTask is scaffolded; ExecuteAsync wiring lands in Phase D.");
    }

    // ── helpers ────────────────────────────────────────────────────────────────

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

                questions.Add(new RiskLevelQuestion(
                    sectionText,
                    questionText,
                    explanationPrompt,
                    questionNumber,
                    rowNum,
                    dvType,
                    dvFormula,
                    cfOperator));
            }
        }

        return questions;
    }
}
