using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Parsing;

/// <summary>
/// Minimal question type for the parsing tests: the six <see cref="IAlignmentIdentity"/>
/// fields plus a single DV-role field (<see cref="Dv"/>) the patcher tests stamp.
/// </summary>
internal sealed record TestQuestion(
    int RowNumber,
    string? XrefId,
    string OriginalText,
    string QuestionText,
    string SectionName,
    string? QuestionNumber,
    string ChapterName,
    string? Dv = null) : IAlignmentIdentity;

internal static class ParsingTestSupport
{
    /// <summary>
    /// A CLQ-shaped factory: identity (number + stripped text) is derived from the text
    /// column HERE, in the factory — demonstrating that the prefix scheme stays out of the
    /// shared parser. Reads text from column C, XrefId from column N.
    /// </summary>
    public static TestQuestion ClqStyleFactory(QuestionRowContext ctx)
    {
        var text = CellText(ctx.Row, "C") ?? "";
        return new TestQuestion(
            RowNumber: ctx.RowNumber,
            XrefId: CellText(ctx.Row, "N"),
            OriginalText: text,
            QuestionText: QuestionNumberParser.StripPrefix(text),
            SectionName: ctx.SectionName,
            QuestionNumber: QuestionNumberParser.ExtractNumber(text),
            ChapterName: ctx.ChapterName);
    }

    public static ExcelRowStructure Row(int rowNumber, params (string Col, string? Text)[] cells)
    {
        var byCol = new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase);
        foreach (var (col, text) in cells)
            byCol[col.ToUpperInvariant()] = new ExcelCellStructure(text, null, null, null);
        return new ExcelRowStructure(rowNumber, byCol);
    }

    private static string? CellText(ExcelRowStructure row, string column)
        => row.CellsByColumn.TryGetValue(column.ToUpperInvariant(), out var c) ? c.TextValue : null;
}
