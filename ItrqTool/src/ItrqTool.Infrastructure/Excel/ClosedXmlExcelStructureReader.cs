using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;

namespace ItrqTool.Infrastructure;

public sealed class ClosedXmlExcelStructureReader : IExcelStructureReader
{
    private readonly ILogger<ClosedXmlExcelStructureReader> _logger;

    public ClosedXmlExcelStructureReader(ILogger<ClosedXmlExcelStructureReader> logger)
        => _logger = logger;

    public IReadOnlyList<ExcelRowStructure> ReadRows(string filePath, string sheetName)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheet(sheetName);
        var result = new List<ExcelRowStructure>();

        foreach (var row in worksheet.RowsUsed())
        {
            var cellsByColumn = new Dictionary<string, ExcelCellStructure>();
            bool hasContent = false;

            foreach (var cell in row.CellsUsed())
            {
                var colLetter = cell.Address.ColumnLetter.ToUpperInvariant();
                var cellStructure = BuildCellStructure(worksheet, cell);
                if (!string.IsNullOrWhiteSpace(cellStructure.TextValue))
                    hasContent = true;
                cellsByColumn[colLetter] = cellStructure;
            }

            if (hasContent)
                result.Add(new ExcelRowStructure(row.RowNumber(), cellsByColumn));
        }

        return result;
    }

    public IReadOnlyDictionary<string, ExcelCellStructure> ReadCells(
        string filePath, string sheetName, IReadOnlyList<string> a1Ranges)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheet(sheetName); // throws if missing — same as ReadRows
        var result = new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase);

        foreach (var a1 in a1Ranges)
        {
            var range = worksheet.Range(a1);
            foreach (var cell in range.Cells()) // .Cells() = ALL addresses incl. blanks — NOT CellsUsed()
            {
                var address = $"{cell.Address.ColumnLetter.ToUpperInvariant()}{cell.Address.RowNumber}";
                result[address] = BuildCellStructure(worksheet, cell); // overlap dedupe: last-write-wins, same data
            }
        }
        return result;
    }

    private ExcelCellStructure BuildCellStructure(IXLWorksheet worksheet, IXLCell cell)
    {
        var textValue = cell.GetString();

        string? dvType = null;
        string? dvFormula = null;
        string? dvOperator = null;
        string? dvFormula2 = null;
        try
        {
            foreach (var dv in worksheet.DataValidations)
            {
                foreach (var range in dv.Ranges)
                {
                    if (range.Contains(cell))
                    {
                        dvType = dv.AllowedValues.ToString();
                        dvFormula = dv.Value;
                        dvOperator = dv.AllowedValues is XLAllowedValues.List
                                                        or XLAllowedValues.Custom
                                                        or XLAllowedValues.AnyValue
                            ? null
                            : dv.Operator.ToString();
                        dvFormula2 = string.IsNullOrEmpty(dv.MaxValue) ? null : dv.MaxValue;
                        goto dvFound;
                    }
                }
            }
            dvFound:;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not read data validation for cell {Address}", cell.Address);
        }

        string? cfOperator = null;
        string? cfType = null;
        string? cfValue = null;
        string? cfValue2 = null;
        try
        {
            foreach (var cf in worksheet.ConditionalFormats)
            {
                foreach (var range in cf.Ranges)
                {
                    if (range.Contains(cell))
                    {
                        cfOperator = cf.Operator.ToString();
                        cfType = cf.ConditionalFormatType.ToString();
                        cfValue = cf.Values.ContainsKey(1) ? cf.Values[1].Value : null;
                        cfValue2 = cf.Values.ContainsKey(2) ? cf.Values[2].Value : null;
                        goto cfFound;
                    }
                }
            }
            cfFound:;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not read conditional format for cell {Address}", cell.Address);
        }

        return new ExcelCellStructure(
            textValue, dvType, dvFormula, cfOperator,
            dvOperator, dvFormula2, cfType, cfValue, cfValue2);
    }
}
