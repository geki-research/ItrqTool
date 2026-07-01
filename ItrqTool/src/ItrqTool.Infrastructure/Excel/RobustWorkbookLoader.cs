using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ItrqTool.Infrastructure.Excel;

/// <summary>
/// Some LibreOffice-authored workbooks carry a legacy VML cell-comment shape that ClosedXML
/// 0.102.3 (and 0.105.0) cannot parse, failing inside the XLWorkbook constructor before any
/// worksheet lookup. Comments are outside every task's read/write contract, so on a load
/// failure this strips the comment/VML parts via the OpenXML SDK and retries.
/// </summary>
internal static class RobustWorkbookLoader
{
    internal static XLWorkbook Open(string path)
    {
        try
        {
            return new XLWorkbook(path);
        }
        catch (Exception)
        {
            try
            {
                var ms = new MemoryStream();
                using (var fs = File.OpenRead(path))
                    fs.CopyTo(ms);
                ms.Position = 0;

                using (var doc = SpreadsheetDocument.Open(ms, isEditable: true))
                {
                    foreach (var ws in doc.WorkbookPart!.WorksheetParts)
                    {
                        if (ws.WorksheetCommentsPart is not null)
                            ws.DeletePart(ws.WorksheetCommentsPart);

                        foreach (var vml in ws.VmlDrawingParts.ToList())
                            ws.DeletePart(vml);

                        foreach (var ld in ws.Worksheet.Elements<LegacyDrawing>().ToList())
                            ld.Remove();

                        ws.Worksheet.Save();
                    }
                }

                ms.Position = 0;
                return new XLWorkbook(ms);
            }
            catch (Exception sanitizeEx)
            {
                throw new InvalidOperationException(
                    $"Workbook '{path}' could not be loaded even after stripping comment/VML parts.",
                    sanitizeEx);
            }
        }
    }
}
