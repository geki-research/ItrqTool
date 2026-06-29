using ClosedXML.Excel;
using ItrqTool.Tasks.GeneralDataValidationV01;

namespace ItrqTool.Integration.Tests.GdV01;

/// <summary>
/// Writes a fixed synthetic GD workbook to disk using ClosedXML. Mirrors
/// <see cref="RlqV01.RlqV01WorkbookWriter"/> for the GD sheet "General Data".
/// <para>
/// Sheet layout (2 sections, 2 questions):
/// <list type="bullet">
///   <item>Row 3  — section header: D3 = "G-CO" (L-excluded section).</item>
///   <item>Row 4  — Q1 bare-qid: C/D/E/F/G/H/I/K/O filled, Q4="Q1", L blank (G-CO excluded).</item>
///   <item>Row 10 — section header: D10 = "G-ST" (L-required section).</item>
///   <item>Row 11 — Q2:A-01: C/D/E/F/G/H/I/K/L/O filled, Q11="Q2:A-01".</item>
///   <item>Row 12 — Q2:A-02: second answer to qid Q2, C/D blank (display block from row 11),
///         H/L/G/O filled, Q12="Q2:A-02", I/J/K blank.</item>
/// </list>
/// </para>
/// </summary>
public static class GdV01WorkbookWriter
{
    public const string SheetName = "General Data";

    private const int SectionGCoRow = 3;
    private const int Q1Row = 4;
    private const int SectionGStRow = 10;
    private const int Q2A01Row = 11;
    private const int Q2A02Row = 12;

    /// <summary>The <see cref="GdV01Config"/> matching the structure this writer produces.</summary>
    public static GdV01Config Config() => new()
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
        ProvidedByColumn           = "O",
        XrefIdColumn               = "Q",
        SheetName                  = SheetName,
        // Two declared sections: G-CO (L-excluded) and G-ST (L-required). ExpectedNames equal the
        // actual D3/D10 headers this writer produces, so the section-header gate stays silent.
        Sections =
        [
            new GdSectionSpec(HeaderRow: 3,  FirstDataRow: 4,  LastDataRow: 9,  ExpectedName: "G-CO", MaterialChangeRequired: false),
            new GdSectionSpec(HeaderRow: 10, FirstDataRow: 11, LastDataRow: 43, ExpectedName: "G-ST", MaterialChangeRequired: true),
        ],
        DeviationThreshold         = 0.25,
    };

    /// <summary>
    /// Writes a clean GD workbook to <paramref name="outputPath"/>. Answer values are whole
    /// numbers (1/2/3) conforming to the WholeNumber ≥ 0 DV applied to H. L uses inline
    /// List "Yes,No" DV on G-ST anchor rows.
    /// </summary>
    public static void Write(string outputPath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);

        // ── Section G-CO header ───────────────────────────────────────────────
        ws.Cell(SectionGCoRow, "D").Value = "G-CO";

        // ── Q1 (row 4): bare-qid, 1 explanation row, L blank (G-CO excluded) ─
        WriteOncePerQuestion(ws, Q1Row, number: "1", text: "Q1 text", suffix: "1", answer: 1);
        ws.Cell(Q1Row, "Q").Value = "Q1";
        WriteExplanation(ws, Q1Row, requested: "req1", previous: null, current: "cur1");
        // L intentionally blank — Q1 is in G-CO (L-excluded section).

        // ── Section G-ST header ───────────────────────────────────────────────
        ws.Cell(SectionGStRow, "D").Value = "G-ST";

        // ── Q2:A-01 (row 11): first answer to qid Q2 ─────────────────────────
        WriteOncePerQuestion(ws, Q2A01Row, number: "2", text: "Q2 text", suffix: "2a", answer: 2);
        ws.Cell(Q2A01Row, "Q").Value = "Q2:A-01";
        ws.Cell(Q2A01Row, "L").Value = "Yes";
        WriteExplanation(ws, Q2A01Row, requested: "req2a", previous: null, current: "cur2a");

        // ── Q2:A-02 (row 12): second answer; C/D blank (display block from row 11) ──
        ws.Cell(Q2A02Row, "G").Value = "prev_3";
        ws.Cell(Q2A02Row, "H").Value = 3;
        ws.Cell(Q2A02Row, "L").Value = "No";
        ws.Cell(Q2A02Row, "O").Value = "TestOU";
        ws.Cell(Q2A02Row, "Q").Value = "Q2:A-02";
        // I/J/K blank — no explanation requested for A-02.

        // ── Answer DV (H, WholeNumber ≥ 0) on each answer's anchor row ────────
        foreach (var row in new[] { Q1Row, Q2A01Row, Q2A02Row })
            ws.Cell(row, "H").CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);

        // ── Material-change DV (L, List "Yes,No") on G-ST anchor rows ─────────
        foreach (var row in new[] { Q2A01Row, Q2A02Row })
            ws.Cell(row, "L").CreateDataValidation().List("\"Yes,No\"");

        wb.SaveAs(outputPath);
    }

    // Fills C/D/E/F/G/H/O on a question's anchor row (once-per-question or once-per-first-answer).
    private static void WriteOncePerQuestion(
        IXLWorksheet ws, int row, string number, string text, string suffix, int answer)
    {
        ws.Cell(row, "C").Value = number;
        ws.Cell(row, "D").Value = text;
        ws.Cell(row, "E").Value = $"Guidance {suffix}.";
        ws.Cell(row, "F").Value = $"Type{suffix}";
        ws.Cell(row, "G").Value = $"prev_{answer}";
        ws.Cell(row, "H").Value = answer;
        ws.Cell(row, "O").Value = "TestOU";
    }

    // Fills per-row explanation cells I/J/K.
    private static void WriteExplanation(
        IXLWorksheet ws, int row, string? requested, string? previous, string? current)
    {
        if (requested != null) ws.Cell(row, "I").Value = requested;
        if (previous  != null) ws.Cell(row, "J").Value = previous;
        if (current   != null) ws.Cell(row, "K").Value = current;
    }
}
