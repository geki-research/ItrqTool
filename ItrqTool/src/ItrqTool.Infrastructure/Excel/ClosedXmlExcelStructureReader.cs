using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using ItrqTool.Domain;
using ItrqTool.Infrastructure.Excel;

namespace ItrqTool.Infrastructure;

public sealed class ClosedXmlExcelStructureReader : IExcelStructureReader
{
    private readonly ILogger<ClosedXmlExcelStructureReader> _logger;

    public ClosedXmlExcelStructureReader(ILogger<ClosedXmlExcelStructureReader> logger)
        => _logger = logger;

    public IReadOnlyList<ExcelRowStructure> ReadRows(string filePath, string sheetName)
    {
        using var workbook = RobustWorkbookLoader.Open(filePath);
        if (!workbook.TryGetWorksheet(sheetName, out var worksheet))
            throw new ArgumentException(
                $"Worksheet '{sheetName}' was not found in '{filePath}'. " +
                $"Available sheets: {string.Join(", ", workbook.Worksheets.Select(w => $"'{w.Name}'"))}.",
                nameof(sheetName));
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
        using var workbook = RobustWorkbookLoader.Open(filePath);
        if (!workbook.TryGetWorksheet(sheetName, out var worksheet))
            throw new ArgumentException(
                $"Worksheet '{sheetName}' was not found in '{filePath}'. " +
                $"Available sheets: {string.Join(", ", workbook.Worksheets.Select(w => $"'{w.Name}'"))}.",
                nameof(sheetName));
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

    public IReadOnlyList<string>? ResolveDefinedNameValues(string filePath, string sheetName, string name)
    {
        using var workbook = RobustWorkbookLoader.Open(filePath);

        IXLNamedRange? nr = null;
        // Worksheet-scoped name on the DV cell's own sheet shadows a workbook-scoped name.
        if (workbook.TryGetWorksheet(sheetName, out var ws)
            && ws.NamedRanges.TryGetValue(name, out var wsNr))
            nr = wsNr;
        else if (workbook.NamedRanges.TryGetValue(name, out var wbNr))
            nr = wbNr;

        if (nr is null) return null;                  // absent → NotCheckable

        var values = nr.Ranges
            .SelectMany(r => r.Cells())
            .Select(c => c.GetString())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .ToList();

        return values.Count == 0 ? null : values;     // all-blank → NotCheckable
    }

    public IReadOnlyList<string> GetWorksheetNames(string filePath)
    {
        using var workbook = RobustWorkbookLoader.Open(filePath);
        return workbook.Worksheets.Select(w => w.Name).ToList();
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

        object? nativeValue = cell.DataType switch
        {
            XLDataType.Number   => (object)cell.Value.GetNumber(),
            XLDataType.Text     => cell.Value.GetText(),
            XLDataType.DateTime => cell.Value.GetDateTime(),
            XLDataType.Boolean  => cell.Value.GetBoolean(),
            XLDataType.TimeSpan => cell.Value.GetTimeSpan(),
            _                   => null   // Blank and Error → null
        };

        return new ExcelCellStructure(
            textValue, dvType, dvFormula, cfOperator,
            dvOperator, dvFormula2, cfType, cfValue, cfValue2,
            nativeValue);
    }
}
