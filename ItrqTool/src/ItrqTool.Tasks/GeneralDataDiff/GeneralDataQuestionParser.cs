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
        GeneralDataConfig config,
        ICollection<TaskMessage> messages)
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

            var sectionLabel = string.IsNullOrWhiteSpace(sectionText)
                ? $"section at row {section.SectionRow}"
                : $"section '{sectionText}'";

            foreach (var qDef in section.Questions)
            {
                // Drops 1–3 are all expected-but-unmet questions: the config declares a
                // question at qDef.FirstRow, but the workbook can't supply one. The workbook
                // is the untrusted artifact, so each is an Error. Messages colour the live
                // log but do NOT fail the task.

                // Drop 1: declared first row absent from the sheet.
                if (!rowsByNumber.TryGetValue(qDef.FirstRow, out var firstRow))
                {
                    messages.Add(new(MessageSeverity.Error,
                        $"Row {qDef.FirstRow} ({sectionLabel}): config expects a question but the row " +
                        "is absent from the sheet.",
                        DateTimeOffset.Now));
                    continue;
                }

                // Drop 2: declared question row present but its text column is absent.
                if (!firstRow.CellsByColumn.TryGetValue(textCol, out var textCell))
                {
                    messages.Add(new(MessageSeverity.Error,
                        $"Row {qDef.FirstRow} ({sectionLabel}): config expects a question but text " +
                        $"column '{textCol}' is absent from the row.",
                        DateTimeOffset.Now));
                    continue;
                }

                // Drop 3: declared question row present but its text cell is blank.
                var questionText = textCell.TextValue?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(questionText))
                {
                    messages.Add(new(MessageSeverity.Error,
                        $"Row {qDef.FirstRow} ({sectionLabel}): config expects a question but the text " +
                        $"cell ({textCol}) is blank.",
                        DateTimeOffset.Now));
                    continue;
                }

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

                    // Drop 4: an interior row of the question's declared span is absent from
                    // the sheet. (offset 0 is firstRow, already confirmed present above, so
                    // this only fires for interior/trailing span rows.) Part of the question
                    // is missing → Error. One message per absent row.
                    if (row is null)
                    {
                        messages.Add(new(MessageSeverity.Error,
                            $"Row {rowNum} ({sectionLabel}, question at row {qDef.FirstRow}): config " +
                            $"declares this row as part of the question's {qDef.RowSpan}-row span " +
                            "but it is absent from the sheet.",
                            DateTimeOffset.Now));
                        continue;
                    }

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
                                    cell.ConditionalFormattingOperator,
                                    cell.DataValidationOperator,
                                    cell.DataValidationFormula2,
                                    cell.ConditionalFormattingType,
                                    cell.ConditionalFormattingValue,
                                    cell.ConditionalFormattingValue2));
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
                                expCell.ConditionalFormattingOperator,
                                expCell.DataValidationOperator,
                                expCell.DataValidationFormula2,
                                expCell.ConditionalFormattingType,
                                expCell.ConditionalFormattingValue,
                                expCell.ConditionalFormattingValue2));
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
