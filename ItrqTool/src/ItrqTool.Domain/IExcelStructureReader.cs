namespace ItrqTool.Domain;

/// <summary>
/// Reads raw cell content and Excel structural metadata (data
/// validation type, conditional formatting operator) that
/// IExcelReader does not expose.
/// </summary>
public interface IExcelStructureReader
{
    /// <summary>
    /// Returns one ExcelRowStructure per non-empty row in the
    /// sheet, in ascending row-number order.
    /// </summary>
    IReadOnlyList<ExcelRowStructure> ReadRows(string filePath, string sheetName);
}

public record ExcelRowStructure(
    int RowNumber,
    // Key = uppercase column letter, e.g. "C", "D"
    IReadOnlyDictionary<string, ExcelCellStructure> CellsByColumn
);

public record ExcelCellStructure(
    string? TextValue,
    // Name of the ClosedXML XLAllowedValues enum value,
    // or null if no data validation is applied to this cell.
    string? DataValidationType,
    // Name of the ClosedXML XLConditionalFormattingOperatorValues
    // enum value for the first matching conditional format,
    // or null if no conditional format applies.
    string? ConditionalFormattingOperator
);
