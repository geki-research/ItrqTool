using ClosedXML.Excel;
using ItrqTool.Domain;

namespace ItrqTool.Infrastructure;

public sealed class ClosedXmlExcelWriter : IExcelWriter
{
    public void WriteWorkbook(ExcelWorkbookData data, string filePath)
    {
        using var workbook = new XLWorkbook();

        foreach (var sheetData in data.Sheets)
        {
            var ws = workbook.Worksheets.Add(sheetData.Name);

            for (int col = 0; col < sheetData.Headers.Count; col++)
            {
                var cell = ws.Cell(1, col + 1);
                cell.Value = sheetData.Headers[col];
                cell.Style.Font.Bold = true;
            }

            for (int row = 0; row < sheetData.Rows.Count; row++)
            {
                var rowData = sheetData.Rows[row];
                for (int col = 0; col < rowData.Count; col++)
                    ws.Cell(row + 2, col + 1).Value = rowData[col];
            }

            ws.Columns().AdjustToContents();
        }

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        workbook.SaveAs(filePath);
    }
}
