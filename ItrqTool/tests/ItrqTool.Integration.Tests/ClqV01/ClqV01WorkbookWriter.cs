using ClosedXML.Excel;

namespace ItrqTool.Integration.Tests.ClqV01;

/// <summary>
/// Writes a <see cref="ClqV01WorkbookDescriptor"/> to an .xlsx file using ClosedXML.
/// Chapter and section headers land in column D. Question fields land in D/E/F/H/I/J/M/N
/// per the production config column mapping. Null fields produce empty cells.
/// </summary>
public static class ClqV01WorkbookWriter
{
    public static void Write(
        string outputPath, string sheetName, ClqV01WorkbookDescriptor descriptor,
        IReadOnlyDictionary<int, string>? answerDvOverrides = null)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);

        foreach (var (row, text) in descriptor.ChapterHeaders)
            ws.Cell(row, "D").Value = text;

        foreach (var (row, text) in descriptor.SectionHeaders)
            ws.Cell(row, "D").Value = text;

        foreach (var q in descriptor.Questions)
        {
            ws.Cell(q.RowNumber, "D").Value = q.OriginalText;
            if (q.Guidance       != null) ws.Cell(q.RowNumber, "E").Value = q.Guidance;
            if (q.PreviousAnswer != null) ws.Cell(q.RowNumber, "F").Value = q.PreviousAnswer;
            if (q.Answer         != null) ws.Cell(q.RowNumber, "H").Value = q.Answer;
            if (q.Strengths      != null) ws.Cell(q.RowNumber, "I").Value = q.Strengths;
            if (q.Weaknesses     != null) ws.Cell(q.RowNumber, "J").Value = q.Weaknesses;
            if (q.ProvidedBy     != null) ws.Cell(q.RowNumber, "M").Value = q.ProvidedBy;
            ws.Cell(q.RowNumber, "N").Value = q.XrefId;
        }

        // Apply list DV to each question row's answer (H) cell.
        // Template H cells are blank but still receive DV so PatchAnswerDv can read it via ReadCells.
        const string DefaultDv = "\"1,2,3,4\"";
        foreach (var q in descriptor.Questions)
        {
            var formula = answerDvOverrides != null && answerDvOverrides.TryGetValue(q.RowNumber, out var ov)
                ? ov
                : DefaultDv;
            ws.Cell(q.RowNumber, "H").CreateDataValidation().List(formula);
        }

        wb.SaveAs(outputPath);
    }
}
