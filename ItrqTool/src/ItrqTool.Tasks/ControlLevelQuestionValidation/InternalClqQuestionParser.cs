using ItrqTool.Domain;

namespace ItrqTool.Tasks.ControlLevelQuestionValidation;

public static class InternalClqQuestionParser
{
    public static IReadOnlyList<InternalClqQuestion> Parse(
        IReadOnlyList<ExcelRowStructure> rows,
        ControlLevelQuestionValidationV01Config config,
        ICollection<TaskMessage> messages)
    {
        var parsedSections = config.ParsedSections; // may throw FormatException on bad config

        var chapterRowSet = new HashSet<int>(config.ChapterRows);
        var sectionRowMap = parsedSections.ToDictionary(s => s.SectionRow);

        var questions = new List<InternalClqQuestion>();
        string currentChapter = "";
        string currentSection = "";
        SectionDefinition? currentSectionDef = null;

        foreach (var row in rows) // IExcelStructureReader guarantees ascending row-number order
        {
            if (chapterRowSet.Contains(row.RowNumber))
            {
                currentChapter = GetCellText(row, config.TextColumn) ?? "";
                currentSection = "";
                currentSectionDef = null;
                continue;
            }

            if (sectionRowMap.TryGetValue(row.RowNumber, out var secDef))
            {
                currentSection = GetCellText(row, config.TextColumn) ?? "";
                currentSectionDef = secDef;
                continue;
            }

            if (currentSectionDef is null) continue;
            if (row.RowNumber < currentSectionDef.FirstQuestionRow) continue;
            if (row.RowNumber > currentSectionDef.LastQuestionRow) continue;

            var originalText = GetCellText(row, config.TextColumn) ?? "";

            if (string.IsNullOrWhiteSpace(originalText))
            {
                messages.Add(new TaskMessage(MessageSeverity.Warning,
                    $"Row {row.RowNumber}: text column ({config.TextColumn}) is blank — row skipped.",
                    DateTimeOffset.Now));
                continue;
            }

            var answerCell = GetCell(row, config.AnswerColumn);

            questions.Add(new InternalClqQuestion(
                RowNumber: row.RowNumber,
                XrefId: GetCellText(row, config.XrefIdColumn),
                QuestionNumber: InternalClqPrefixParser.ExtractNumber(originalText),
                QuestionText: InternalClqPrefixParser.StripPrefix(originalText),
                OriginalText: originalText,
                ChapterName: currentChapter,
                SectionName: currentSection,
                Guidance: GetCellText(row, config.GuidanceColumn),
                PreviousAnswer: GetCellText(row, config.PreviousAnswerColumn),
                Answer: GetCellText(row, config.AnswerColumn),
                Strengths: GetCellText(row, config.StrengthsColumn),
                Weaknesses: GetCellText(row, config.WeaknessesColumn),
                ProvidedBy: GetCellText(row, config.ProvidedByColumn),
                AnswerDvType: answerCell?.DataValidationType,
                AnswerDvFormula: answerCell?.DataValidationFormula,
                AnswerDvOperator: answerCell?.DataValidationOperator,
                AnswerDvFormula2: answerCell?.DataValidationFormula2,
                NumberFormatUnrecognized: InternalClqPrefixParser.HasUnsupportedDeeperPrefix(originalText)
            ));
        }

        return questions;
    }

    private static string? GetCellText(ExcelRowStructure row, string column)
        => row.CellsByColumn.TryGetValue(column.ToUpperInvariant(), out var cell) ? cell.TextValue : null;

    private static ExcelCellStructure? GetCell(ExcelRowStructure row, string column)
        => row.CellsByColumn.TryGetValue(column.ToUpperInvariant(), out var cell) ? cell : null;
}
