using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;

namespace ItrqTool.Tasks.QuestionnaireValidation.Parsing;

/// <summary>
/// Sheet-agnostic question parser. Faithful generalization of CLQ_v01's
/// <c>InternalClqQuestionParser</c>: it owns the structural machinery (iterate rows over the
/// layout, classify chapter / section / question rows, capture the current chapter and
/// section names, skip blank question rows with a warning, collect <see cref="TaskMessage"/>s)
/// and hands each question row to a per-version <paramref name="recordFactory"/> as a
/// <see cref="QuestionRowContext"/>.
/// <para>
/// What the parser deliberately does NOT own: how a question's identity is derived from its
/// columns. In particular the CLQ "number embedded in the text-column prefix" scheme
/// (<see cref="QuestionNumberParser"/>) lives in the factory, not here — RLQ reads its number
/// from its own column and GD is multi-row, so the shared parser must not assume the prefix
/// scheme or any specific column letters. Reading payload columns is the factory's job.
/// </para>
/// </summary>
public static class QuestionParser
{
    public static IReadOnlyList<T> Parse<T>(
        IReadOnlyList<ExcelRowStructure> rows,
        QuestionnaireLayout layout,
        Func<QuestionRowContext, T> recordFactory,
        ICollection<TaskMessage> messages)
        where T : class, IAlignmentIdentity
    {
        var chapterByRow = layout.Chapters.ToDictionary(c => c.RowNumber);
        var sectionByRow = layout.Sections.ToDictionary(s => s.SectionRow);

        var questions = new List<T>();
        string currentChapter = "";
        string currentSection = "";
        LayoutSection? currentSectionDef = null;

        foreach (var row in rows) // IExcelStructureReader guarantees ascending row-number order
        {
            if (chapterByRow.TryGetValue(row.RowNumber, out var chap))
            {
                currentChapter = GetCellText(row, chap.NameColumn) ?? "";
                currentSection = "";
                currentSectionDef = null;
                continue;
            }

            if (sectionByRow.TryGetValue(row.RowNumber, out var secDef))
            {
                currentSection = GetCellText(row, secDef.NameColumn) ?? "";
                currentSectionDef = secDef;
                continue;
            }

            if (currentSectionDef is null) continue;
            if (row.RowNumber < currentSectionDef.FirstQuestionRow) continue;
            if (row.RowNumber > currentSectionDef.LastQuestionRow) continue;

            var text = GetCellText(row, layout.QuestionTextColumn) ?? "";

            if (string.IsNullOrWhiteSpace(text))
            {
                messages.Add(new TaskMessage(MessageSeverity.Warning,
                    $"Row {row.RowNumber}: text column ({layout.QuestionTextColumn}) is blank — row skipped.",
                    DateTimeOffset.Now));
                continue;
            }

            questions.Add(recordFactory(new QuestionRowContext(
                Row: row,
                RowNumber: row.RowNumber,
                ChapterName: currentChapter,
                SectionName: currentSection)));
        }

        return questions;
    }

    private static string? GetCellText(ExcelRowStructure row, string column)
        => row.CellsByColumn.TryGetValue(column.ToUpperInvariant(), out var cell) ? cell.TextValue : null;
}
