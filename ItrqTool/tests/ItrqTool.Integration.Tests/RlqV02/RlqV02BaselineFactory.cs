using ClosedXML.Excel;
using ItrqTool.Integration.Tests.WorksheetStructure;
using ItrqTool.Tasks.RiskLevelQuestionValidationV02;

namespace ItrqTool.Integration.Tests.RlqV02;

/// <summary>
/// Builds the three RLQ workbooks consumed by <c>rlq-v02-validation-trial.json</c> and the
/// synthetic config JSON that wires the task to them. Reuses the fixed merged-cell/multi-row
/// layout from the v01 baseline: 2 sections, 4 questions, Q3 is multi-row (rows 8–10).
/// <para>
/// v02 additions vs v01: HowExplanation (M) is once-per-question on the anchor/first row
/// (merged over the Q3 group like the other once-per-question columns); provided-by maps to
/// P (was O in v01); xref-id maps to R (was Q in v01). Columns C–L are unchanged.
/// </para>
/// <para>
/// L is always "No" (inline DV Yes,No) so Rule 1 (M required when L=Yes) stays silent on
/// the clean baseline. M is written on every anchor row so the M read path is exercised.
/// H answers are whole-number integers (x1=1, x2=2, x3=3, x4=4) conforming to the
/// WholeNumber ≥ 0 answer DV.
/// </para>
/// </summary>
public static class RlqV02BaselineFactory
{
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
    private const string HowExpCol   = "M";  // new in v02
    private const string PrvdByCol   = "P";  // was O in v01
    private const string XrefIdCol   = "R";  // was Q in v01

    internal const string SheetName = "IT Risk Level Questions";

    private static readonly (int Row, string XrefId)[] SingleRowQuestions =
    [
        (6,  "x1"),
        (7,  "x2"),
        (13, "x4"),
    ];

    private const int    Q3AnchorRow = 8;
    private const int    Q3LastRow   = 10;
    private const string Q3XrefId   = "x3";

    private static readonly int[]    Q3Rows    = [8, 9, 10];
    // M added (once-per-question); P replaces O; R (XrefId) is NOT merged — repeated per row.
    private static readonly string[] MergedCols = ["C", "D", "E", "F", "G", "H", "L", "M", "P"];

    /// <summary>
    /// The synthetic config JSON string. Write this to the configs directory at test runtime.
    /// Property names match <c>RlqV02Config</c> init properties (ConfigLoader is case-insensitive).
    /// SectionRows match the fixture layout (section headers at rows 5 and 12).
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
          "HowExplanationColumn": "M",
          "ProvidedByColumn": "P",
          "XrefIdColumn": "R",
          "SheetName": "IT Risk Level Questions",
          "SectionRows": ["5:6-10", "12:13-13"],
          "DeviationThreshold": 0.25,
          "MaterialChangeExplanationTriggers": ["Yes"],
          "SeverityOverrides": {}
        }
        """;

    /// <summary>
    /// Returns the <see cref="RlqV02Config"/> matching the structure this factory produces.
    /// SectionRows match the fixture layout (section headers at rows 5 and 12).
    /// </summary>
    public static RlqV02Config Config() => new()
    {
        QuestionNumberColumn       = "C",
        TextColumn                 = "D",
        GuidanceColumn             = "E",
        RequestedTypeColumn        = "F",
        PreviousAnswerColumn       = "G",
        AnswerColumn               = "H",
        RequestedExplanationColumn = "I",
        PreviousExplanationColumn  = "J",
        CurrentExplanationColumn   = "K",
        MaterialChangeColumn       = "L",
        HowExplanationColumn       = "M",
        ProvidedByColumn           = "P",
        XrefIdColumn               = "R",
        SheetName                  = SheetName,
        SectionRows                = ["5:6-10", "12:13-13"],
        DeviationThreshold         = 0.25,
        MaterialChangeExplanationTriggers = ["Yes"],
    };

    /// <summary>Writes the current-response workbook (all answer/explanation fields filled).</summary>
    public static void WriteCurrent(string outputPath, bool stampHeader = true)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);
        if (stampHeader) StructureHeaderStamper.Stamp(ws, "rlq", "v02");
        WriteCurrentBody(ws);
        ApplyAnswerDv(ws);
        ApplyMaterialChangeDv(ws);
        wb.SaveAs(outputPath);
    }

    /// <summary>
    /// Writes the current-response body onto <paramref name="ws"/>: section headers, all four
    /// questions' once-per-question columns (C/D/E/F/G/H/L/M/P), the Q3 per-row I/J/K
    /// explanation rows, the Q3 merges, and the R column on every row. H answer values are
    /// whole-number integers (x1=1, x2=2, x3=3, x4=4) conforming to the WholeNumber ≥ 0
    /// answer DV. M (HowExplanation) is written on anchor rows; merged (blank) on continuation
    /// rows. No data validation is applied; the caller is responsible for DV.
    /// </summary>
    internal static void WriteCurrentBody(IXLWorksheet ws)
    {
        WriteSectionHeaders(ws);

        foreach (var (row, xref) in SingleRowQuestions)
        {
            int answer = row switch { 6 => 1, 7 => 2, 13 => 4, _ => 0 };
            WriteOncePerQuestion(ws, row, number: xref, text: $"Question {xref} text",
                suffix: xref, answer: answer);
            ws.Cell(row, XrefIdCol).Value = xref;
            ws.Cell(row, CurExpCol).Value = $"Current explanation for {xref}.";
        }

        WriteOncePerQuestion(ws, Q3AnchorRow, number: Q3XrefId, text: "Question x3 text",
            suffix: Q3XrefId, answer: 3);
        foreach (var row in Q3Rows)
        {
            ws.Cell(row, XrefIdCol).Value = Q3XrefId;
            ws.Cell(row, ReqExpCol).Value  = $"req3_{row}";
            ws.Cell(row, PrevExpCol).Value = $"prev3_{row}";
            ws.Cell(row, CurExpCol).Value  = $"cur3_{row}";
        }
        ApplyQ3Merges(ws);
    }

    /// <summary>Writes the empty-template workbook (same structure; answer and explanation cells blank).</summary>
    public static void WriteTemplate(string outputPath, bool stampHeader = true)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);
        if (stampHeader) StructureHeaderStamper.Stamp(ws, "rlq", "v02");

        WriteSectionHeaders(ws);

        foreach (var (row, xref) in SingleRowQuestions)
        {
            WriteOncePerQuestionTemplate(ws, row, number: xref, text: $"Question {xref} text");
            ws.Cell(row, XrefIdCol).Value = xref;
            // H, L, M, P intentionally blank for template
        }

        WriteOncePerQuestionTemplate(ws, Q3AnchorRow, number: Q3XrefId, text: "Question x3 text");
        foreach (var row in Q3Rows)
            ws.Cell(row, XrefIdCol).Value = Q3XrefId;
        ApplyQ3Merges(ws);

        ApplyAnswerDv(ws);
        ApplyMaterialChangeDv(ws);
        wb.SaveAs(outputPath);
    }

    /// <summary>Writes the previous-response workbook (same XrefIds and question text; prior-year answers).</summary>
    public static void WritePrevious(string outputPath, bool stampHeader = true)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);
        if (stampHeader) StructureHeaderStamper.Stamp(ws, "rlq", "v02");

        WriteSectionHeaders(ws);

        // H answers MATCH current (x1=1, x2=2, x4=4): cross-year deviation (finding 6a) is zero.
        foreach (var (row, xref) in SingleRowQuestions)
        {
            int answer = row switch { 6 => 1, 7 => 2, 13 => 4, _ => 0 };
            WriteOncePerQuestion(ws, row, number: xref, text: $"Question {xref} text",
                suffix: xref, answer: answer);
            ws.Cell(row, XrefIdCol).Value = xref;
            ws.Cell(row, CurExpCol).Value = $"Previous year explanation for {xref}.";
        }

        // Q3 H answer MATCHES current (3): zero cross-year deviation on the clean baseline.
        WriteOncePerQuestion(ws, Q3AnchorRow, number: Q3XrefId, text: "Question x3 text",
            suffix: Q3XrefId, answer: 3);
        foreach (var row in Q3Rows)
        {
            ws.Cell(row, XrefIdCol).Value  = Q3XrefId;
            ws.Cell(row, ReqExpCol).Value  = $"prev_req3_{row}";
            ws.Cell(row, PrevExpCol).Value = $"prev_prev3_{row}";
            ws.Cell(row, CurExpCol).Value  = $"prev_cur3_{row}";
        }
        ApplyQ3Merges(ws);

        ApplyAnswerDv(ws);
        ApplyMaterialChangeDv(ws);
        wb.SaveAs(outputPath);
    }

    private static void WriteSectionHeaders(IXLWorksheet ws)
    {
        ws.Cell(5,  TextCol).Value = "Section One";
        ws.Cell(12, TextCol).Value = "Section Two";
    }

    private static void WriteOncePerQuestion(
        IXLWorksheet ws, int row, string number, string text, string suffix, int answer)
    {
        ws.Cell(row, QNumberCol).Value  = number;
        ws.Cell(row, TextCol).Value     = text;
        ws.Cell(row, GuidanceCol).Value = $"Guidance {suffix}.";
        ws.Cell(row, ReqTypeCol).Value  = $"Document{suffix}";
        ws.Cell(row, PrevAnsCol).Value  = $"prev_{suffix}";
        ws.Cell(row, AnsCol).Value      = answer;
        ws.Cell(row, MatChgCol).Value   = "No";
        ws.Cell(row, HowExpCol).Value   = $"HowExp {suffix}.";  // v02: M column, once-per-question
        ws.Cell(row, PrvdByCol).Value   = "TestOU";             // v02: P column (was O)
    }

    private static void WriteOncePerQuestionTemplate(
        IXLWorksheet ws, int row, string number, string text)
    {
        ws.Cell(row, QNumberCol).Value  = number;
        ws.Cell(row, TextCol).Value     = text;
        ws.Cell(row, GuidanceCol).Value = $"Guidance {number}.";
        ws.Cell(row, ReqTypeCol).Value  = $"Document{number}";
        // G (PreviousAnswer), H (Answer), L (MaterialChange), M (HowExplanation),
        // P (ProvidedBy) intentionally blank for template
    }

    private static void ApplyQ3Merges(IXLWorksheet ws)
    {
        foreach (var col in MergedCols)
            ws.Range($"{col}{Q3AnchorRow}:{col}{Q3LastRow}").Merge();
    }

    private static void ApplyAnswerDv(IXLWorksheet ws)
    {
        foreach (var (row, _) in SingleRowQuestions)
            ws.Cell(row, AnsCol).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
        ws.Cell(Q3AnchorRow, AnsCol).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
    }

    private static void ApplyMaterialChangeDv(IXLWorksheet ws)
    {
        // Inline List "Yes,No" on each question anchor row. L="No" on all questions keeps
        // Rule 1 (M required when L=Yes) silent on the clean baseline.
        foreach (var (row, _) in SingleRowQuestions)
            ws.Cell(row, MatChgCol).CreateDataValidation().List("\"Yes,No\"");
        ws.Cell(Q3AnchorRow, MatChgCol).CreateDataValidation().List("\"Yes,No\"");
    }
}
