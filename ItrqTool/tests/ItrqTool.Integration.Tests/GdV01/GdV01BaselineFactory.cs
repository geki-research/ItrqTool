using ClosedXML.Excel;
using ItrqTool.Tasks.GeneralDataValidationV01;

namespace ItrqTool.Integration.Tests.GdV01;

/// <summary>
/// Builds the three GD workbooks used by the C3 pipeline tests and the synthetic config JSON
/// that wires the task to them. Mirrors <see cref="RlqV01.RlqV01BaselineFactory"/> for the
/// GD "General Data" sheet. Layout reuses the fixed 2-section, 2-question geometry defined by
/// <see cref="GdV01WorkbookWriter"/> (frozen).
/// <para>
/// Zero-findings guarantee on the clean trio: H answers are whole numbers (1/2/3) conforming
/// to the WholeNumber ≥ 0 DV, equal across current and previous (cross-year deviation = 0 ≤
/// threshold 0.25), L values conform to List "Yes,No", and all QIDs align across workbooks.
/// </para>
/// </summary>
public static class GdV01BaselineFactory
{
    // Column letters — mirror GdV01WorkbookWriter.Config() exactly.
    private const string QNumberCol  = "C";
    private const string TextCol     = "D";
    private const string GuidanceCol = "E";
    private const string ReqTypeCol  = "F";
    private const string PrevAnsCol  = "G";
    private const string AnsCol      = "H";
    private const string ReqExpCol   = "I";
    private const string PrevExpCol  = "J";
    private const string CurExpCol   = "K";
    private const string MatChgCol   = "L";
    private const string PrvdByCol   = "O";
    private const string XrefIdCol   = "Q";

    private const int SectionGCoRow = 3;
    private const int Q1Row = 4;
    private const int SectionGStRow = 10;
    private const int Q2A01Row = 11;
    private const int Q2A02Row = 12;

    /// <summary>
    /// The synthetic config JSON string matching <see cref="Config()"/>. Write to a configs
    /// directory at test runtime when a task-based test requires a config file on disk.
    /// Property names match <c>GdV01Config</c> init properties (ConfigLoader is case-insensitive).
    /// SectionRows match <c>GdV01WorkbookWriter.Config().SectionRows</c> exactly.
    /// </summary>
    public const string SyntheticConfigJson = """
        {
          "QuestionNumberColumn": "C",
          "TextColumn": "D",
          "GuidanceColumn": "E",
          "RequestedTypeColumn": "F",
          "PreviousAnswerColumn": "G",
          "AnswerColumn": "H",
          "RequestedExplanationColumn": "I",
          "PreviousExplanationColumn": "J",
          "CurrentExplanationColumn": "K",
          "MaterialChangeColumn": "L",
          "ProvidedByColumn": "O",
          "XrefIdColumn": "Q",
          "SheetName": "General Data",
          "SectionRows": ["3:4-9", "10:11-43"],
          "DeviationThreshold": 0.25,
          "MaterialChangeSections": ["G-ST"],
          "SeverityOverrides": {}
        }
        """;

    /// <summary>The <see cref="GdV01Config"/> matching the workbooks this factory produces.</summary>
    public static GdV01Config Config() => GdV01WorkbookWriter.Config();

    /// <summary>Writes the current-response workbook (all answer/explanation fields filled).</summary>
    public static void WriteCurrent(string outputPath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(GdV01WorkbookWriter.SheetName);
        WriteCurrentBody(ws);
        ApplyAnswerDv(ws);
        ApplyMaterialChangeDv(ws);
        wb.SaveAs(outputPath);
    }

    /// <summary>
    /// Writes the current-response body onto <paramref name="ws"/>: section headers, all
    /// questions' columns, explanation rows, and Q column on every answer row. H values are
    /// whole numbers (1/2/3) conforming to the WholeNumber ≥ 0 DV. No DV is applied; the
    /// caller is responsible for DV.
    /// </summary>
    internal static void WriteCurrentBody(IXLWorksheet ws)
    {
        WriteSectionHeaders(ws);
        WriteQ1(ws, answer: 1, curExp: "cur1");
        WriteQ2A01(ws, answer: 2, materialChange: "Yes", curExp: "cur2a");
        WriteQ2A02(ws, answer: 3, materialChange: "No");
    }

    /// <summary>Writes the empty-template workbook (same structure; H and K blank; DV applied).</summary>
    public static void WriteTemplate(string outputPath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(GdV01WorkbookWriter.SheetName);
        WriteSectionHeaders(ws);
        WriteQ1Template(ws);
        WriteQ2A01Template(ws);
        WriteQ2A02Template(ws);
        ApplyAnswerDv(ws);
        ApplyMaterialChangeDv(ws);
        wb.SaveAs(outputPath);
    }

    /// <summary>
    /// Writes the previous-response workbook (same XrefIds and text; H matches current so
    /// cross-year deviation = 0 on the clean baseline).
    /// </summary>
    public static void WritePrevious(string outputPath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(GdV01WorkbookWriter.SheetName);
        WriteSectionHeaders(ws);
        // H values MATCH current (1/2/3) so cross-year deviation = 0 ≤ threshold 0.25.
        // Text values MATCH current so GdAnswerTextDivergedCell is silent.
        WriteQ1(ws, answer: 1, curExp: "prev_cur1");
        WriteQ2A01(ws, answer: 2, materialChange: "Yes", curExp: "prev_cur2a");
        WriteQ2A02(ws, answer: 3, materialChange: "No");
        ApplyAnswerDv(ws);
        ApplyMaterialChangeDv(ws);
        wb.SaveAs(outputPath);
    }

    private static void WriteSectionHeaders(IXLWorksheet ws)
    {
        ws.Cell(SectionGCoRow, TextCol).Value = "G-CO";
        ws.Cell(SectionGStRow, TextCol).Value = "G-ST";
    }

    // Q1: bare-qid, 1 explanation, L blank (G-CO — L excluded).
    private static void WriteQ1(IXLWorksheet ws, int answer, string curExp)
    {
        ws.Cell(Q1Row, QNumberCol).Value  = "1";
        ws.Cell(Q1Row, TextCol).Value     = "Q1 text";
        ws.Cell(Q1Row, GuidanceCol).Value = "Guidance 1.";
        ws.Cell(Q1Row, ReqTypeCol).Value  = "Type1";
        ws.Cell(Q1Row, PrevAnsCol).Value  = $"prev_{answer}";
        ws.Cell(Q1Row, AnsCol).Value      = answer;
        ws.Cell(Q1Row, ReqExpCol).Value   = "req1";
        ws.Cell(Q1Row, CurExpCol).Value   = curExp;
        // L deliberately blank — Q1 in G-CO, L not required.
        ws.Cell(Q1Row, PrvdByCol).Value   = "TestOU";
        ws.Cell(Q1Row, XrefIdCol).Value   = "Q1";
    }

    // Q1 template variant: H and K blank.
    private static void WriteQ1Template(IXLWorksheet ws)
    {
        ws.Cell(Q1Row, QNumberCol).Value  = "1";
        ws.Cell(Q1Row, TextCol).Value     = "Q1 text";
        ws.Cell(Q1Row, GuidanceCol).Value = "Guidance 1.";
        ws.Cell(Q1Row, ReqTypeCol).Value  = "Type1";
        ws.Cell(Q1Row, ReqExpCol).Value   = "req1";
        // H (answer), K (current explanation), L (material-change), G, O intentionally blank.
        ws.Cell(Q1Row, XrefIdCol).Value   = "Q1";
    }

    // Q2:A-01: first answer to qid Q2 (anchor row 11).
    private static void WriteQ2A01(IXLWorksheet ws, int answer, string materialChange, string curExp)
    {
        ws.Cell(Q2A01Row, QNumberCol).Value  = "2";
        ws.Cell(Q2A01Row, TextCol).Value     = "Q2 text";
        ws.Cell(Q2A01Row, GuidanceCol).Value = "Guidance 2a.";
        ws.Cell(Q2A01Row, ReqTypeCol).Value  = "Type2a";
        ws.Cell(Q2A01Row, PrevAnsCol).Value  = $"prev_{answer}";
        ws.Cell(Q2A01Row, AnsCol).Value      = answer;
        ws.Cell(Q2A01Row, ReqExpCol).Value   = "req2a";
        ws.Cell(Q2A01Row, CurExpCol).Value   = curExp;
        ws.Cell(Q2A01Row, MatChgCol).Value   = materialChange;
        ws.Cell(Q2A01Row, PrvdByCol).Value   = "TestOU";
        ws.Cell(Q2A01Row, XrefIdCol).Value   = "Q2:A-01";
    }

    // Q2:A-01 template variant: H and K blank.
    private static void WriteQ2A01Template(IXLWorksheet ws)
    {
        ws.Cell(Q2A01Row, QNumberCol).Value  = "2";
        ws.Cell(Q2A01Row, TextCol).Value     = "Q2 text";
        ws.Cell(Q2A01Row, GuidanceCol).Value = "Guidance 2a.";
        ws.Cell(Q2A01Row, ReqTypeCol).Value  = "Type2a";
        ws.Cell(Q2A01Row, ReqExpCol).Value   = "req2a";
        // H, K, L, G, O blank.
        ws.Cell(Q2A01Row, XrefIdCol).Value   = "Q2:A-01";
    }

    // Q2:A-02: second answer to qid Q2 (anchor row 12); C/D blank (display block from row 11).
    private static void WriteQ2A02(IXLWorksheet ws, int answer, string materialChange)
    {
        ws.Cell(Q2A02Row, PrevAnsCol).Value = $"prev_{answer}";
        ws.Cell(Q2A02Row, AnsCol).Value     = answer;
        ws.Cell(Q2A02Row, MatChgCol).Value  = materialChange;
        ws.Cell(Q2A02Row, PrvdByCol).Value  = "TestOU";
        ws.Cell(Q2A02Row, XrefIdCol).Value  = "Q2:A-02";
        // I/J/K blank — no explanation requested for A-02.
    }

    // Q2:A-02 template variant: same as response variant (H is blank by omission since no answer).
    private static void WriteQ2A02Template(IXLWorksheet ws)
    {
        ws.Cell(Q2A02Row, XrefIdCol).Value = "Q2:A-02";
        // H, L, G, O blank.
    }

    private static void ApplyAnswerDv(IXLWorksheet ws)
    {
        // Answer DV (H, WholeNumber ≥ 0) on each answer's anchor row.
        foreach (var row in new[] { Q1Row, Q2A01Row, Q2A02Row })
            ws.Cell(row, AnsCol).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
    }

    private static void ApplyMaterialChangeDv(IXLWorksheet ws)
    {
        // Material-change DV (L, List "Yes,No") on G-ST answer anchor rows only.
        foreach (var row in new[] { Q2A01Row, Q2A02Row })
            ws.Cell(row, MatChgCol).CreateDataValidation().List("\"Yes,No\"");
    }

    // ── Range-ref and named-range DV variant writers ──────────────────────────────────────────
    // These produce variant trios where the H (answer) or L (material-change) DV on one or more
    // G-ST answer rows uses a List sourced from a "Lists" backing sheet (range-ref) or a
    // workbook-scoped named range, instead of the baseline WholeNumber / inline-List DV.
    //
    // The "previous" workbook is always the standard baseline (WritePrevious) — GdAnswerDeviationCell
    // gates on AnswerDvType being WholeNumber/Decimal and skips List-typed cells, so previous H DV
    // does not need to change for these tests.
    //
    // Each variant changes the DV in BOTH current AND template (identical formula → FrozenConstraint
    // silent). The "Lists" sheet is added to each workbook so the range-ref formula resolves locally.

    private const string ListsSheetName = "Lists";
    private const string HAnswerNamedRange = "GdAnswerOptions";

    private static IXLWorksheet AddListsSheet(XLWorkbook wb)
    {
        var ls = wb.Worksheets.Add(ListsSheetName);
        ls.Cell("A1").Value = "Yes";
        ls.Cell("A2").Value = "No";
        return ls;
    }

    // ── H-column range-ref variant ────────────────────────────────────────────────────────────
    // H11 DV = List sourced from Lists!A1:A2 (Yes/No). H4 and H12 keep WholeNumber ≥ 0.
    // Current H11 = "Yes" (conformant with the range-ref List vocabulary).

    public static void WriteCurrentHRangeRef(string outputPath)
    {
        using var wb = new XLWorkbook();
        var listsWs = AddListsSheet(wb);
        var ws = wb.Worksheets.Add(GdV01WorkbookWriter.SheetName);
        WriteCurrentBody(ws);
        ws.Cell(Q2A01Row, AnsCol).Value = "Yes";   // conformant with List {Yes, No}
        ws.Cell(Q1Row,    AnsCol).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
        ws.Cell(Q2A01Row, AnsCol).CreateDataValidation().List(listsWs.Range("A1:A2"));
        ws.Cell(Q2A02Row, AnsCol).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
        ApplyMaterialChangeDv(ws);
        wb.SaveAs(outputPath);
    }

    public static void WriteTemplateHRangeRef(string outputPath)
    {
        using var wb = new XLWorkbook();
        var listsWs = AddListsSheet(wb);
        var ws = wb.Worksheets.Add(GdV01WorkbookWriter.SheetName);
        WriteSectionHeaders(ws);
        WriteQ1Template(ws);
        WriteQ2A01Template(ws);
        WriteQ2A02Template(ws);
        ws.Cell(Q1Row,    AnsCol).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
        ws.Cell(Q2A01Row, AnsCol).CreateDataValidation().List(listsWs.Range("A1:A2"));
        ws.Cell(Q2A02Row, AnsCol).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
        ApplyMaterialChangeDv(ws);
        wb.SaveAs(outputPath);
    }

    // ── L-column range-ref variant ────────────────────────────────────────────────────────────
    // L11 and L12 DV = List sourced from Lists!A1:A2 (Yes/No). H DV unchanged (WholeNumber).
    // Current L values stay "Yes"/"No" (conformant).

    public static void WriteCurrentLRangeRef(string outputPath)
    {
        using var wb = new XLWorkbook();
        var listsWs = AddListsSheet(wb);
        var ws = wb.Worksheets.Add(GdV01WorkbookWriter.SheetName);
        WriteCurrentBody(ws);
        ApplyAnswerDv(ws);
        foreach (var row in new[] { Q2A01Row, Q2A02Row })
            ws.Cell(row, MatChgCol).CreateDataValidation().List(listsWs.Range("A1:A2"));
        wb.SaveAs(outputPath);
    }

    public static void WriteTemplateLRangeRef(string outputPath)
    {
        using var wb = new XLWorkbook();
        var listsWs = AddListsSheet(wb);
        var ws = wb.Worksheets.Add(GdV01WorkbookWriter.SheetName);
        WriteSectionHeaders(ws);
        WriteQ1Template(ws);
        WriteQ2A01Template(ws);
        WriteQ2A02Template(ws);
        ApplyAnswerDv(ws);
        foreach (var row in new[] { Q2A01Row, Q2A02Row })
            ws.Cell(row, MatChgCol).CreateDataValidation().List(listsWs.Range("A1:A2"));
        wb.SaveAs(outputPath);
    }

    // ── H-column named-range variant ──────────────────────────────────────────────────────────
    // H11 DV = List sourced from workbook-scoped named range "GdAnswerOptions" → Lists!A1:A2.
    // H4 and H12 keep WholeNumber ≥ 0. Current H11 = "Yes" (conformant).

    public static void WriteCurrentHNamedRange(string outputPath)
    {
        using var wb = new XLWorkbook();
        var listsWs = AddListsSheet(wb);
        wb.NamedRanges.Add(HAnswerNamedRange, listsWs.Range("A1:A2"));
        var ws = wb.Worksheets.Add(GdV01WorkbookWriter.SheetName);
        WriteCurrentBody(ws);
        ws.Cell(Q2A01Row, AnsCol).Value = "Yes";   // conformant
        ws.Cell(Q1Row,    AnsCol).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
        ws.Cell(Q2A01Row, AnsCol).CreateDataValidation().List($"={HAnswerNamedRange}");
        ws.Cell(Q2A02Row, AnsCol).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
        ApplyMaterialChangeDv(ws);
        wb.SaveAs(outputPath);
    }

    public static void WriteTemplateHNamedRange(string outputPath)
    {
        using var wb = new XLWorkbook();
        var listsWs = AddListsSheet(wb);
        wb.NamedRanges.Add(HAnswerNamedRange, listsWs.Range("A1:A2"));
        var ws = wb.Worksheets.Add(GdV01WorkbookWriter.SheetName);
        WriteSectionHeaders(ws);
        WriteQ1Template(ws);
        WriteQ2A01Template(ws);
        WriteQ2A02Template(ws);
        ws.Cell(Q1Row,    AnsCol).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
        ws.Cell(Q2A01Row, AnsCol).CreateDataValidation().List($"={HAnswerNamedRange}");
        ws.Cell(Q2A02Row, AnsCol).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
        ApplyMaterialChangeDv(ws);
        wb.SaveAs(outputPath);
    }
}
