using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Infrastructure;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using ItrqTool.Tasks.RiskLevelQuestionValidationV02;

namespace ItrqTool.Integration.Tests.RlqV02;

/// <summary>
/// THE PROOF. Validates that the real <see cref="IExcelStructureReader"/>
/// (<see cref="ClosedXmlExcelStructureReader"/>) presents real merged cells the way the
/// v02 parser assumes: the merged once-per-question value is visible only on the group's top
/// row, the continuation rows read blank there, and the (un-merged) repeated XrefId (R) plus
/// the per-row explanation triplet stay populated on every row.
/// <para>
/// v02-specific assertions: HowExplanation (M) is read on the anchor row; ProvidedBy comes
/// from P; XrefId comes from R.
/// </para>
/// <para>
/// Uses <see cref="RlqV02BaselineFactory.WriteCurrent"/> directly (lighter path — no separate
/// WorkbookWriter) paired with <see cref="RlqV02BaselineFactory.Config"/> for the layout.
/// </para>
/// </summary>
public sealed class RlqV02ReaderParserTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-rlqv02-readerproof", Guid.NewGuid().ToString("N"));

    private static IExcelStructureReader Reader() =>
        new ClosedXmlExcelStructureReader(NullLogger<ClosedXmlExcelStructureReader>.Instance);

    [Fact]
    public void RealReader_MergedCells_ParseToExactRecords()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "rlq-v02.xlsx");
            RlqV02BaselineFactory.WriteCurrent(path);

            var config = RlqV02BaselineFactory.Config();
            var layout = RlqV02Profile.Build(config).Layout;
            var messages = new List<TaskMessage>();

            var rows = Reader().ReadRows(path, config.SheetName);
            var questions = RlqV02QuestionParser.Parse(rows, layout, config, messages);

            // Exactly 4 records — Q3's three rows collapse to one.
            questions.Should().HaveCount(4,
                "Q1, Q2, Q3 (multi-row collapse), Q4 — got: {0}",
                string.Join(" | ", questions.Select(q => $"row {q.RowNumber} xref {q.XrefId}")));

            var q1 = questions[0];
            var q2 = questions[1];
            var q3 = questions[2];
            var q4 = questions[3];

            // ── Q1: single row ────────────────────────────────────────────────────
            q1.RowNumber.Should().Be(6);
            q1.XrefId.Should().Be("x1");           // from R column
            q1.QuestionText.Should().Be("Question x1 text");
            q1.SectionName.Should().Be("Section One");
            q1.ProvidedBy.Should().Be("TestOU");   // from P column (v02 shift)
            q1.HowExplanation.Should().Be("HowExp x1.");  // from M column (new v02 field)
            q1.Answer.Should().Be("1");            // integer 1 read as text
            q1.MaterialChange.Should().Be("No");
            q1.ExplanationRows.Should().ContainSingle();
            q1.ExplanationRows[0].Should().Be(
                new RlqExplanationRow(null, null, "Current explanation for x1.", 6));

            // ── Q2: single row ────────────────────────────────────────────────────
            q2.RowNumber.Should().Be(7);
            q2.XrefId.Should().Be("x2");
            q2.ProvidedBy.Should().Be("TestOU");
            q2.HowExplanation.Should().Be("HowExp x2.");
            q2.Answer.Should().Be("2");
            q2.ExplanationRows.Should().ContainSingle();
            q2.ExplanationRows[0].Should().Be(
                new RlqExplanationRow(null, null, "Current explanation for x2.", 7));

            // ── Q3: THE multi-row collapse — ONE record, once-per-question from row 8 ──
            q3.RowNumber.Should().Be(8, "once-per-question fields read from the merged group's top row");
            q3.XrefId.Should().Be("x3");           // from R column (un-merged, repeated)
            q3.QuestionText.Should().Be("Question x3 text");
            q3.OriginalText.Should().Be("Question x3 text");
            q3.QuestionNumber.Should().Be("x3");
            q3.Answer.Should().Be("3");            // integer 3 read as text (merged, from row 8)
            q3.MaterialChange.Should().Be("No");   // merged, from row 8
            q3.ProvidedBy.Should().Be("TestOU");   // P column, merged, from row 8 (v02 shift)
            q3.HowExplanation.Should().Be("HowExp x3.");  // M column, merged, from row 8 (new v02)
            q3.SectionName.Should().Be("Section One");
            q3.ExplanationRows.Should().HaveCount(3, "rows 8, 9, 10 each contribute one triplet");
            q3.ExplanationRows.Should().Equal(
                new RlqExplanationRow("req3_8",  "prev3_8",  "cur3_8",  8),
                new RlqExplanationRow("req3_9",  "prev3_9",  "cur3_9",  9),
                new RlqExplanationRow("req3_10", "prev3_10", "cur3_10", 10));

            // ── Q4: second section ────────────────────────────────────────────────
            q4.RowNumber.Should().Be(13);
            q4.XrefId.Should().Be("x4");
            q4.SectionName.Should().Be("Section Two");
            q4.ProvidedBy.Should().Be("TestOU");
            q4.HowExplanation.Should().Be("HowExp x4.");
            q4.Answer.Should().Be("4");

            // No XrefId-blank warnings expected.
            messages.Should().NotContain(m => m.Severity == MessageSeverity.Warning);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void RealReader_NonContiguousXref_EmitsTwoRecords()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "rlq-v02-noncontig.xlsx");
            WriteNonContiguousXref(path);

            var config = RlqV02BaselineFactory.Config();
            var layout = RlqV02Profile.Build(config).Layout;
            var messages = new List<TaskMessage>();

            var rows = Reader().ReadRows(path, config.SheetName);
            var questions = RlqV02QuestionParser.Parse(rows, layout, config, messages);

            // x1, x2, x1 across three contiguous rows → three contiguous runs → three records.
            questions.Should().HaveCount(3);
            questions.Select(q => (q.RowNumber, q.XrefId)).Should().Equal(
                (6, "x1"), (7, "x2"), (8, "x1"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // Three single-row questions in Section One (rows 6, 7, 8) with XrefIds x1, x2, x1 in
    // the R column (v02 xref-id column). The repeated x1 is non-contiguous so the parser
    // must NOT merge them into one question.
    private static void WriteNonContiguousXref(string path)
    {
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add(RlqV02BaselineFactory.SheetName);
        ws.Cell(5, "D").Value = "Section One";
        foreach (var (row, xref) in new[] { (6, "x1"), (7, "x2"), (8, "x1") })
        {
            ws.Cell(row, "C").Value = row.ToString();
            ws.Cell(row, "D").Value = $"Q at row {row}";
            ws.Cell(row, "H").Value = $"ans{row}";
            ws.Cell(row, "R").Value = xref;          // v02: XrefId is in R (not Q)
        }
        wb.SaveAs(path);
    }
}
