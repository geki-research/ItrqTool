using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;

namespace ItrqTool.Tasks.QuestionnaireValidation.Parsing;

/// <summary>
/// Sheet-agnostic generalization of CLQ_v01's <c>PatchAnswerDv</c> (the lesson-71
/// blank-cell-safe DV re-read). <see cref="IExcelStructureReader.ReadRows"/> /
/// <c>CellsUsed</c> omit blank cells that carry only data validation, so a blank-but-DV'd
/// template answer cell is invisible to the row parse. This re-reads the configured column
/// over the question rows' span via the address-driven <see cref="IExcelStructureReader.ReadCells"/>,
/// matches each cell to its question by <see cref="IAlignmentIdentity.RowNumber"/>, and lets a
/// per-role <paramref name="applyDv"/> updater stamp the DV fields onto the question.
/// </summary>
/// <remarks>
/// Behaviour identical to v01's answer-DV patch, parameterized by column + updater so it can
/// be called once per DV-role. <see cref="IExcelStructureReader.ReadCells"/> returns one entry
/// per address in range (including blank cells); the empty-cell fallback below preserves v01's
/// "absent ⇒ null DV fields" outcome defensively.
/// </remarks>
public static class DvPatcher
{
    private static readonly ExcelCellStructure Empty = new(null, null, null, null);

    public static IReadOnlyList<T> Patch<T>(
        IExcelStructureReader reader,
        string filePath, string sheetName, string column,
        IReadOnlyList<T> questions,
        Func<T, ExcelCellStructure, T> applyDv)
        where T : class, IAlignmentIdentity
    {
        if (questions.Count == 0) return questions;

        var col = column.ToUpperInvariant();
        int firstRow = questions.Min(q => q.RowNumber);
        int lastRow  = questions.Max(q => q.RowNumber);

        var cells = reader.ReadCells(filePath, sheetName, [$"{col}{firstRow}:{col}{lastRow}"]);

        return questions
            .Select(q =>
            {
                var addr = $"{col}{q.RowNumber}";
                var cell = cells.TryGetValue(addr, out var c) ? c : Empty;
                return applyDv(q, cell);
            })
            .ToList();
    }
}
