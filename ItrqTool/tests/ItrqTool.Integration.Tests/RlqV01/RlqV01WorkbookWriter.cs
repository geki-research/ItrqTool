using ClosedXML.Excel;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;

namespace ItrqTool.Integration.Tests.RlqV01;

/// <summary>
/// Writes a fixed synthetic RLQ workbook to disk using ClosedXML, with REAL merged cells for
/// the once-per-question columns of the multi-row question (Q3, rows 8–10). This is the
/// load-bearing fixture for the chunk-1b proof: it exercises the exact merged-cell shape the
/// chunk-1a <see cref="RlqV01QuestionParser"/> assumes — the merged value sits on the group's
/// top row and the continuation rows read blank there, while the per-row explanation triplet
/// (I/J/K) and the (un-merged, repeated) XrefId column Q stay populated on every row.
/// <para>
/// Sheet layout (sheet "IT Risk Level Questions"):
/// <list type="bullet">
///   <item>Row 5 — section header: D5 = "Section One" (C5 blank).</item>
///   <item>Row 6 — Q1 single-row, 0 explanations: C/D/E/F/G/H/L/O filled, Q6="x1", I/J/K blank.</item>
///   <item>Row 7 — Q2 single-row, 1 explanation: C/D/E/F/G/H/L/O filled, Q7="x2", I/J/K filled.</item>
///   <item>Rows 8–10 — Q3 multi-row, 3 explanations: row 8 carries the once-per-question cells;
///         rows 9/10 leave those blank (merged), repeat Q="x3", and carry triplets 2 and 3.
///         C/D/E/F/G/H/L/O are each merged 8:10; column Q and I/J/K are NOT merged.</item>
///   <item>Row 12 — section header: D12 = "Section Two".</item>
///   <item>Row 13 — Q4 single-row: C/D/E/F/G/H/L/O filled, Q13="x4".</item>
/// </list>
/// </para>
/// </summary>
public static class RlqV01WorkbookWriter
{
    public const string SheetName = "IT Risk Level Questions";

    /// <summary>The <see cref="RlqV01Config"/> matching the structure this writer produces.</summary>
    public static RlqV01Config Config() => new()
    {
        QuestionNumberColumn      = "C",
        TextColumn                = "D",
        GuidanceColumn            = "E",
        RequestedTypeColumn       = "F",
        PreviousAnswerColumn      = "G",
        AnswerColumn              = "H",
        RequestedExplanationColumn = "I",
        PreviousExplanationColumn  = "J",
        CurrentExplanationColumn   = "K",
        MaterialChangeColumn      = "L",
        ProvidedByColumn          = "O",
        XrefIdColumn              = "Q",
        SheetName                 = SheetName,
        SectionRows               = ["5:6-10", "12:13-13"],
    };

    public static void Write(string outputPath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);

        // ── Section One header ─────────────────────────────────────────────
        ws.Cell(5, "D").Value = "Section One";

        // ── Q1 (row 6): single row, 0 explanations ─────────────────────────
        WriteOncePerQuestion(ws, 6, number: "1", text: "Q1 text", suffix: "1");
        ws.Cell(6, "Q").Value = "x1";
        // I6/J6/K6 deliberately blank.

        // ── Q2 (row 7): single row, 1 explanation ──────────────────────────
        WriteOncePerQuestion(ws, 7, number: "2", text: "Q2 text", suffix: "2");
        ws.Cell(7, "Q").Value = "x2";
        WriteExplanation(ws, 7, "r2", "p2", "c2");

        // ── Q3 (rows 8–10): multi-row, 3 explanations ──────────────────────
        WriteOncePerQuestion(ws, 8, number: "3", text: "Q3 text", suffix: "3");
        ws.Cell(8, "Q").Value = "x3";
        WriteExplanation(ws, 8, "r3a", "p3a", "c3a");

        // Continuation rows: once-per-question columns left blank (they become merged),
        // XrefId repeated (NOT merged), explanation triplets per row.
        ws.Cell(9, "Q").Value = "x3";
        WriteExplanation(ws, 9, "r3b", "p3b", "c3b");
        ws.Cell(10, "Q").Value = "x3";
        WriteExplanation(ws, 10, "r3c", "p3c", "c3c");

        // Real merges for every once-per-question column over the Q3 group.
        foreach (var col in new[] { "C", "D", "E", "F", "G", "H", "L", "O" })
            ws.Range($"{col}8:{col}10").Merge();

        // ── Section Two header ─────────────────────────────────────────────
        ws.Cell(12, "D").Value = "Section Two";

        // ── Q4 (row 13): single row ────────────────────────────────────────
        WriteOncePerQuestion(ws, 13, number: "4", text: "Q4 text", suffix: "4");
        ws.Cell(13, "Q").Value = "x4";

        // Answer DV (column H, whole-number ≥ 0) on each question's answer cell / merged anchor.
        foreach (var anchorRow in new[] { 6, 7, 8, 13 })
            ws.Cell(anchorRow, "H").CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);

        // Material-change DV (column L, List Yes,No) on each question's anchor row — parity with
        // RlqV01BaselineFactory so the writer stays consistent with the full-pipeline fixtures.
        foreach (var anchorRow in new[] { 6, 7, 8, 13 })
            ws.Cell(anchorRow, "L").CreateDataValidation().List("\"Yes,No\"");

        wb.SaveAs(outputPath);
    }

    // Fills the once-per-question columns (C/D/E/F/G/H/L/O) on a question's top row.
    private static void WriteOncePerQuestion(
        IXLWorksheet ws, int row, string number, string text, string suffix)
    {
        ws.Cell(row, "C").Value = number;          // question number
        ws.Cell(row, "D").Value = text;            // question text
        ws.Cell(row, "E").Value = $"guidance{suffix}";
        ws.Cell(row, "F").Value = $"Document{suffix}";
        ws.Cell(row, "G").Value = $"prev{suffix}";
        ws.Cell(row, "H").Value = $"ans{suffix}";  // answer
        ws.Cell(row, "L").Value = $"No{suffix}";   // material change
        ws.Cell(row, "O").Value = $"unit{suffix}"; // provided by
    }

    // Fills the per-row explanation triplet (I/J/K).
    private static void WriteExplanation(
        IXLWorksheet ws, int row, string requested, string previous, string current)
    {
        ws.Cell(row, "I").Value = requested;
        ws.Cell(row, "J").Value = previous;
        ws.Cell(row, "K").Value = current;
    }
}
