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
                var textValue = cell.GetString();
                if (!string.IsNullOrWhiteSpace(textValue))
                    hasContent = true;

                string? dvType = null;
                try
                {
                    foreach (var dv in worksheet.DataValidations)
                    {
                        foreach (var range in dv.Ranges)
                        {
                            if (range.Contains(cell))
                            {
                                dvType = dv.AllowedValues.ToString();
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
                try
                {
                    foreach (var cf in worksheet.ConditionalFormats)
                    {
                        foreach (var range in cf.Ranges)
                        {
                            if (range.Contains(cell))
                            {
                                cfOperator = cf.Operator.ToString();
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

                cellsByColumn[colLetter] = new ExcelCellStructure(textValue, dvType, cfOperator);
            }

            if (hasContent)
                result.Add(new ExcelRowStructure(row.RowNumber(), cellsByColumn));
        }

        return result;
    }
}
