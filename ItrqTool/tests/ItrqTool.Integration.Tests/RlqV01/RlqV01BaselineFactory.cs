using ClosedXML.Excel;

namespace ItrqTool.Integration.Tests.RlqV01;

/// <summary>
/// Builds the three RLQ workbooks consumed by <c>rlq-v01-validation-trial.json</c> and the
/// synthetic config JSON that wires the task to them. Reuses the fixed merged-cell/multi-row
/// layout defined by <see cref="RlqV01WorkbookWriter"/> (frozen): 2 sections, 4 questions,
/// Q3 is multi-row (rows 8–10).
/// <para>
/// Config JSON is generated at test runtime and written to the temp configs directory — no
/// production config asset is committed (the real production config awaits real section-row
/// ranges, chunk 2).
/// </para>
/// <para>
/// Zero-findings guarantee: the RLQ_v01 profile carries an empty finding catalogue, so any
/// correctly parsed trio produces zero findings regardless of field values.
/// </para>
/// </summary>
public static class RlqV01BaselineFactory
{
    // Column mapping mirrors RlqV01WorkbookWriter.Config() exactly.
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

    // Fixed XrefIds matching the RlqV01WorkbookWriter layout.
    private static readonly (int Row, string XrefId)[] SingleRowQuestions =
    [
        (6,  "x1"),
        (7,  "x2"),
        (13, "x4"),
    ];

    // Q3 spans rows 8–10 (3 explanation rows).
    private const int Q3AnchorRow   = 8;
    private const int Q3LastRow     = 10;
    private const string Q3XrefId   = "x3";

    private static readonly int[] Q3Rows = [8, 9, 10];
    private static readonly string[] MergedCols = ["C", "D", "E", "F", "G", "H", "L", "O"];

    /// <summary>
    /// The synthetic config JSON string. Write this to the configs directory at test runtime.
    /// Property names match <c>RlqV01Config</c> init properties (ConfigLoader is case-insensitive).
    /// SectionRows match <c>RlqV01WorkbookWriter.Config().SectionRows</c> exactly.
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
          "SheetName": "IT Risk Level Questions",
          "SectionRows": ["5:6-10", "12:13-13"],
          "SeverityOverrides": {}
        }
        """;

    /// <summary>Writes the current-response workbook (all answer/explanation fields filled).</summary>
    public static void WriteCurrent(string outputPath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(RlqV01WorkbookWriter.SheetName);

        WriteSectionHeaders(ws);

        // Single-row questions
        foreach (var (row, xref) in SingleRowQuestions)
        {
            WriteOncePerQuestion(ws, row, number: xref, text: $"Question {xref} text", suffix: xref);
            ws.Cell(row, XrefIdCol).Value = xref;
            ws.Cell(row, CurExpCol).Value = $"Current explanation for {xref}.";
        }

        // Q3 multi-row: merged once-per-question on anchor row, explanation per row
        WriteOncePerQuestion(ws, Q3AnchorRow, number: Q3XrefId, text: "Question x3 text", suffix: Q3XrefId);
        foreach (var row in Q3Rows)
        {
            ws.Cell(row, XrefIdCol).Value = Q3XrefId;
            ws.Cell(row, ReqExpCol).Value = $"req3_{row}";
            ws.Cell(row, PrevExpCol).Value = $"prev3_{row}";
            ws.Cell(row, CurExpCol).Value = $"cur3_{row}";
        }
        ApplyQ3Merges(ws);

        ApplyAnswerDv(ws);
        wb.SaveAs(outputPath);
    }

    /// <summary>Writes the empty-template workbook (same structure; answer and explanation cells blank).</summary>
    public static void WriteTemplate(string outputPath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(RlqV01WorkbookWriter.SheetName);

        WriteSectionHeaders(ws);

        foreach (var (row, xref) in SingleRowQuestions)
        {
            WriteOncePerQuestionTemplate(ws, row, number: xref, text: $"Question {xref} text");
            ws.Cell(row, XrefIdCol).Value = xref;
            // Answer (H), explanation (I/J/K) intentionally blank for template
        }

        WriteOncePerQuestionTemplate(ws, Q3AnchorRow, number: Q3XrefId, text: "Question x3 text");
        foreach (var row in Q3Rows)
            ws.Cell(row, XrefIdCol).Value = Q3XrefId;
        ApplyQ3Merges(ws);

        ApplyAnswerDv(ws);
        wb.SaveAs(outputPath);
    }

    /// <summary>Writes the previous-response workbook (same XrefIds and question text; prior-year answers).</summary>
    public static void WritePrevious(string outputPath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(RlqV01WorkbookWriter.SheetName);

        WriteSectionHeaders(ws);

        foreach (var (row, xref) in SingleRowQuestions)
        {
            WriteOncePerQuestion(ws, row, number: xref, text: $"Question {xref} text",
                suffix: xref, answerPrefix: "prev_ans_");
            ws.Cell(row, XrefIdCol).Value = xref;
            ws.Cell(row, CurExpCol).Value = $"Previous year explanation for {xref}.";
        }

        WriteOncePerQuestion(ws, Q3AnchorRow, number: Q3XrefId, text: "Question x3 text",
            suffix: Q3XrefId, answerPrefix: "prev_ans_");
        foreach (var row in Q3Rows)
        {
            ws.Cell(row, XrefIdCol).Value = Q3XrefId;
            ws.Cell(row, ReqExpCol).Value = $"prev_req3_{row}";
            ws.Cell(row, PrevExpCol).Value = $"prev_prev3_{row}";
            ws.Cell(row, CurExpCol).Value = $"prev_cur3_{row}";
        }
        ApplyQ3Merges(ws);

        ApplyAnswerDv(ws);
        wb.SaveAs(outputPath);
    }

    private static void WriteSectionHeaders(IXLWorksheet ws)
    {
        ws.Cell(5,  TextCol).Value = "Section One";
        ws.Cell(12, TextCol).Value = "Section Two";
    }

    private static void WriteOncePerQuestion(
        IXLWorksheet ws, int row, string number, string text, string suffix,
        string answerPrefix = "ans_")
    {
        ws.Cell(row, QNumberCol).Value  = number;
        ws.Cell(row, TextCol).Value     = text;
        ws.Cell(row, GuidanceCol).Value = $"Guidance {suffix}.";
        ws.Cell(row, ReqTypeCol).Value  = $"Document{suffix}";
        ws.Cell(row, PrevAnsCol).Value  = $"prev_{suffix}";
        ws.Cell(row, AnsCol).Value      = $"{answerPrefix}{suffix}";
        ws.Cell(row, MatChgCol).Value   = "No";
        ws.Cell(row, PrvdByCol).Value   = "TestOU";
    }

    private static void WriteOncePerQuestionTemplate(
        IXLWorksheet ws, int row, string number, string text)
    {
        ws.Cell(row, QNumberCol).Value  = number;
        ws.Cell(row, TextCol).Value     = text;
        ws.Cell(row, GuidanceCol).Value = $"Guidance {number}.";
        ws.Cell(row, ReqTypeCol).Value  = $"Document{number}";
        // PreviousAnswer (G), Answer (H), MaterialChange (L), ProvidedBy (O) intentionally blank
    }

    private static void ApplyQ3Merges(IXLWorksheet ws)
    {
        foreach (var col in MergedCols)
            ws.Range($"{col}{Q3AnchorRow}:{col}{Q3LastRow}").Merge();
    }

    private static void ApplyAnswerDv(IXLWorksheet ws)
    {
        // Answer DV (H) on each question's anchor row, mirroring RlqV01WorkbookWriter.
        foreach (var (row, _) in SingleRowQuestions)
            ws.Cell(row, AnsCol).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
        ws.Cell(Q3AnchorRow, AnsCol).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
    }
}
