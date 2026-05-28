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
    // Raw formula string from the DV rule (dv.Value from ClosedXML).
    // Inline lists: e.g. "\"Yes,No,N/A\"". Range refs: e.g. "Sheet!$A$1:$A$5".
    // Null if no DV rule applies.
    string? DataValidationFormula,
    // Name of the ClosedXML XLConditionalFormattingOperatorValues
    // enum value for the first matching conditional format,
    // or null if no conditional format applies.
    string? ConditionalFormattingOperator,
    // DV operator (dv.Operator.ToString()); null for List/Custom/AnyValue types
    // (ClosedXML reports a meaningless Between sentinel for those).
    string? DataValidationOperator = null,
    // Upper-bound DV formula (dv.MaxValue); null when empty (i.e. single-value operators).
    string? DataValidationFormula2 = null,
    // CF type (cf.ConditionalFormatType.ToString()); null when no CF applies.
    string? ConditionalFormattingType = null,
    // First CF comparison value; null when no CF or no value.
    string? ConditionalFormattingValue = null,
    // Second CF comparison value (Between/NotBetween); null otherwise.
    string? ConditionalFormattingValue2 = null
);
