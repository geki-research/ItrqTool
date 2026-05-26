using ItrqTool.Domain;

namespace ItrqTool.Tasks.GeneralDataDiff;

/// <summary>
/// Parses a General Data sheet (represented as a list of ExcelRowStructure) into
/// logical questions using a config-driven approach. Question boundaries are taken
/// entirely from the config's SectionRows; no inference from column C content.
/// </summary>
public static class GeneralDataQuestionParser
{
    public static IReadOnlyList<GeneralDataQuestion> Parse(
        IReadOnlyList<ExcelRowStructure> rows,
        GeneralDataConfig config)
    {
        var parsedSections = config.ParsedSections; // throws FormatException on bad per-entry config
        ValidateCrossSection(parsedSections);

        var textCol        = config.TextColumn.ToUpperInvariant();
        var numberCol      = config.NumberColumn.ToUpperInvariant();
        var answerCols     = config.AnswerColumns.Select(c => c.ToUpperInvariant()).ToList();
        var explanationCol = config.ExplanationColumn.ToUpperInvariant();

        var rowsByNumber = rows.ToDictionary(r => r.RowNumber);
        var questions = new List<GeneralDataQuestion>();

        foreach (var section in parsedSections)
        {
            // Section name from the section row's text column.
            rowsByNumber.TryGetValue(section.SectionRow, out var sectionRow);
            var sectionText = sectionRow is not null &&
                              sectionRow.CellsByColumn.TryGetValue(textCol, out var sectionCell)
                ? sectionCell.TextValue?.Trim() ?? ""
                : "";

            foreach (var qDef in section.Questions)
            {
                // First row must exist and have non-empty text in the text column.
                if (!rowsByNumber.TryGetValue(qDef.FirstRow, out var firstRow))
                    continue;
                if (!firstRow.CellsByColumn.TryGetValue(textCol, out var textCell))
                    continue;
                var questionText = textCell.TextValue?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(questionText))
                    continue;

                // Collect per-row data across the question's span.
                var rowNumberLabels = new List<string>(qDef.RowSpan);
                var answerCells = new List<GeneralDataAnswerCell>();
                var explanationCells = new List<GeneralDataExplanationCell>();

                for (int offset = 0; offset < qDef.RowSpan; offset++)
                {
                    int rowNum = qDef.FirstRow + offset;
                    rowsByNumber.TryGetValue(rowNum, out var row);

                    // Number column → RowNumberLabels (always RowSpan entries).
                    string label = "";
                    if (row is not null &&
                        row.CellsByColumn.TryGetValue(numberCol, out var numberCell))
                    {
                        label = numberCell.TextValue?.Trim() ?? "";
                    }
                    rowNumberLabels.Add(label);

                    if (row is null) continue;

                    // Answer columns → AnswerCells (only when text non-empty).
                    foreach (var col in answerCols)
                    {
                        if (row.CellsByColumn.TryGetValue(col, out var cell))
                        {
                            var text = cell.TextValue?.Trim() ?? "";
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                answerCells.Add(new GeneralDataAnswerCell(
                                    offset, col, text,
                                    cell.DataValidationType,
                                    cell.DataValidationFormula,
                                    cell.ConditionalFormattingOperator));
                            }
                        }
                    }

                    // Explanation column → ExplanationCells (only when text non-empty).
                    if (row.CellsByColumn.TryGetValue(explanationCol, out var expCell))
                    {
                        var text = expCell.TextValue?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            explanationCells.Add(new GeneralDataExplanationCell(
                                offset, text,
                                expCell.DataValidationType,
                                expCell.DataValidationFormula,
                                expCell.ConditionalFormattingOperator));
                        }
                    }
                }

                string? questionNumber = string.IsNullOrEmpty(rowNumberLabels[0])
                    ? null
                    : rowNumberLabels[0];

                questions.Add(new GeneralDataQuestion(
                    sectionText,
                    questionText,
                    questionNumber,
                    qDef.FirstRow,
                    rowNumberLabels,
                    answerCells,
                    explanationCells));
            }
        }

        return questions;
    }

    private static void ValidateCrossSection(IReadOnlyList<SectionDefinition> sections)
    {
        // Section rows must be strictly increasing.
        for (int i = 0; i < sections.Count - 1; i++)
        {
            if (sections[i].SectionRow >= sections[i + 1].SectionRow)
                throw new FormatException(
                    $"Section rows must be strictly increasing: section at row {sections[i].SectionRow} " +
                    $"is followed by section at row {sections[i + 1].SectionRow}.");
        }

        // Questions in section N must not extend into section N+1's territory.
        for (int i = 0; i < sections.Count - 1; i++)
        {
            var current = sections[i];
            var next = sections[i + 1];
            foreach (var q in current.Questions)
            {
                int qLastRow = q.FirstRow + q.RowSpan - 1;
                if (qLastRow >= next.SectionRow)
                    throw new FormatException(
                        $"Question in section {current.SectionRow} starts at row {q.FirstRow} with rowspan {q.RowSpan} (last row {qLastRow}), " +
                        $"which overlaps with the next section starting at row {next.SectionRow}.");
            }
        }
    }
}
