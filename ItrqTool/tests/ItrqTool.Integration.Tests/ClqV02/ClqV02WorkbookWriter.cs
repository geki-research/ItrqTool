using ClosedXML.Excel;
using ItrqTool.Integration.Tests.WorksheetStructure;

namespace ItrqTool.Integration.Tests.ClqV02;

/// <summary>
/// Writes a <see cref="ClqV02WorkbookDescriptor"/> to an .xlsx file using ClosedXML.
/// Chapter and section headers land in column D. Question fields land in D/E/F/H/I/J/K/N/O
/// per the production config column mapping. Null fields produce empty cells.
/// </summary>
public static class ClqV02WorkbookWriter
{
    public static void Write(
        string outputPath, string sheetName, ClqV02WorkbookDescriptor descriptor,
        IReadOnlyDictionary<int, string>? answerDvOverrides = null,
        IReadOnlyDictionary<int, string>? stabilityDvOverrides = null,
        bool stampHeader = true)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);
        if (stampHeader) StructureHeaderStamper.Stamp(ws, "clq", "v02");

        foreach (var (row, text) in descriptor.ChapterHeaders)
            ws.Cell(row, "D").Value = text;

        foreach (var (row, text) in descriptor.SectionHeaders)
            ws.Cell(row, "D").Value = text;

        foreach (var q in descriptor.Questions)
        {
            ws.Cell(q.RowNumber, "D").Value = q.OriginalText;
            if (q.Guidance        != null) ws.Cell(q.RowNumber, "E").Value = q.Guidance;
            if (q.PreviousAnswer  != null) ws.Cell(q.RowNumber, "F").Value = q.PreviousAnswer;
            if (q.Answer          != null) ws.Cell(q.RowNumber, "H").Value = q.Answer;
            if (q.Strengths       != null) ws.Cell(q.RowNumber, "I").Value = q.Strengths;
            if (q.Weaknesses      != null) ws.Cell(q.RowNumber, "J").Value = q.Weaknesses;
            if (q.AnswerStability != null) ws.Cell(q.RowNumber, "K").Value = q.AnswerStability;
            if (q.ProvidedBy      != null) ws.Cell(q.RowNumber, "N").Value = q.ProvidedBy;
            ws.Cell(q.RowNumber, "O").Value = q.XrefId;
        }

        // Answer DV (H) — unchanged from v01 pattern.
        const string DefaultDv = "\"1,2,3,4\"";
        foreach (var q in descriptor.Questions)
        {
            var formula = answerDvOverrides != null && answerDvOverrides.TryGetValue(q.RowNumber, out var ov)
                ? ov
                : DefaultDv;
            ws.Cell(q.RowNumber, "H").CreateDataValidation().List(formula);
        }

        // Answer-stability DV (K) — applied to every question row in every workbook so
        // template's blank K cell still carries DV, mirroring how blank template H carries
        // the answer DV.
        const string DefaultStabilityDv = "\"Yes,No\"";
        foreach (var q in descriptor.Questions)
        {
            var stabilityFormula = stabilityDvOverrides != null
                && stabilityDvOverrides.TryGetValue(q.RowNumber, out var sov)
                ? sov : DefaultStabilityDv;
            ws.Cell(q.RowNumber, "K").CreateDataValidation().List(stabilityFormula);
        }

        wb.SaveAs(outputPath);
    }
}
