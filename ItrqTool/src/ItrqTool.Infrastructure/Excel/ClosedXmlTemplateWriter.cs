using ClosedXML.Excel;
using ItrqTool.Domain;

namespace ItrqTool.Infrastructure.Excel;

public sealed class ClosedXmlTemplateWriter : IExcelTemplateWriter
{
    public void Populate(
        string templatePath,
        string sheetName,
        IReadOnlyList<CellWriteEntry> cells,
        string outputPath)
    {
        using var workbook = RobustWorkbookLoader.Open(templatePath);

        if (!workbook.TryGetWorksheet(sheetName, out var worksheet))
            throw new ArgumentException(
                $"Worksheet '{sheetName}' was not found in template '{templatePath}'.",
                nameof(sheetName));

        foreach (var entry in cells)
        {
            var col  = entry.Column.ToUpperInvariant();
            var cell = worksheet.Cell($"{col}{entry.Row}");
            ExcelStyleHelper.WriteWithStylePreservation(worksheet, cell, col, entry.TypedValue ?? entry.Value);
        }

        workbook.SaveAs(outputPath);
    }
}
