using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Infrastructure;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;

namespace ItrqTool.Integration.Tests.RlqV01;

/// <summary>
/// THE PROOF. Validates that the real <see cref="IExcelStructureReader"/>
/// (<see cref="ClosedXmlExcelStructureReader"/>) presents real merged cells the way the
/// chunk-1a <see cref="RlqV01QuestionParser"/> assumes: the merged once-per-question value is
/// visible only on the group's top row, the continuation rows read blank there, and the
/// (un-merged) repeated XrefId plus the per-row explanation triplet stay populated on every
/// row. Reads a workbook written with REAL ClosedXML merges through the real reader, parses,
/// and asserts the exact parsed records.
/// </summary>
public sealed class RlqV01ReaderParserTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-rlqv01-readerproof", Guid.NewGuid().ToString("N"));

    private static IExcelStructureReader Reader() =>
        new ClosedXmlExcelStructureReader(NullLogger<ClosedXmlExcelStructureReader>.Instance);

    [Fact]
    public void RealReader_MergedCells_ParseToExactRecords()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "rlq.xlsx");
            RlqV01WorkbookWriter.Write(path);

            var config = RlqV01WorkbookWriter.Config();
            var layout = RlqV01Profile.Build(config).Layout;
            var messages = new List<TaskMessage>();

            var rows = Reader().ReadRows(path, config.SheetName);
            var questions = RlqV01QuestionParser.Parse(rows, layout, config, messages);

            // Exactly 4 records — Q3's three rows collapse to one; no spurious continuation records.
            questions.Should().HaveCount(4,
                "Q1, Q2, Q3 (multi-row collapse), Q4 — got: {0}",
                string.Join(" | ", questions.Select(q => $"row {q.RowNumber} xref {q.XrefId}")));

            var q1 = questions[0];
            var q2 = questions[1];
            var q3 = questions[2];
            var q4 = questions[3];

            // ── Q1: single row, 0 explanations → one all-blank triplet (chunk-1a convention) ──
            q1.RowNumber.Should().Be(6);
            q1.XrefId.Should().Be("x1");
            q1.QuestionText.Should().Be("Q1 text");
            q1.SectionName.Should().Be("Section One");
            q1.ExplanationRows.Should().ContainSingle();
            q1.ExplanationRows[0].Should().Be(new RlqExplanationRow(null, null, null));

            // ── Q2: single row, 1 explanation → one populated triplet ──
            q2.RowNumber.Should().Be(7);
            q2.XrefId.Should().Be("x2");
            q2.ExplanationRows.Should().ContainSingle();
            q2.ExplanationRows[0].Should().Be(new RlqExplanationRow("r2", "p2", "c2"));

            // ── Q3: THE multi-row collapse — ONE record, fields from row 8, 3 triplets in row order ──
            q3.RowNumber.Should().Be(8, "once-per-question fields read from the merged group's top row");
            q3.XrefId.Should().Be("x3");
            q3.QuestionText.Should().Be("Q3 text");
            q3.OriginalText.Should().Be("Q3 text");
            q3.QuestionNumber.Should().Be("3");
            q3.Answer.Should().Be("ans3");
            q3.MaterialChange.Should().Be("No3");
            q3.ProvidedBy.Should().Be("unit3");
            q3.Guidance.Should().Be("guidance3");
            q3.RequestedType.Should().Be("Document3");
            q3.PreviousAnswer.Should().Be("prev3");
            q3.SectionName.Should().Be("Section One");
            q3.ExplanationRows.Should().HaveCount(3, "rows 8, 9, 10 each contribute one triplet");
            q3.ExplanationRows.Should().Equal(
                new RlqExplanationRow("r3a", "p3a", "c3a"),
                new RlqExplanationRow("r3b", "p3b", "c3b"),
                new RlqExplanationRow("r3c", "p3c", "c3c"));

            // ── Q4: second section ──
            q4.RowNumber.Should().Be(13);
            q4.XrefId.Should().Be("x4");
            q4.SectionName.Should().Be("Section Two");

            // No XrefId-blank warnings expected — every question row carries a grouping key.
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
            var path = Path.Combine(dir, "rlq-noncontig.xlsx");
            WriteNonContiguousXref(path);

            var config = RlqV01WorkbookWriter.Config();
            var layout = RlqV01Profile.Build(config).Layout;
            var messages = new List<TaskMessage>();

            var rows = Reader().ReadRows(path, config.SheetName);
            var questions = RlqV01QuestionParser.Parse(rows, layout, config, messages);

            // x1, x2, x1 across three contiguous rows → three contiguous runs → three records;
            // the two "x1" runs are SEPARATE records (the duplicate seam holds through the reader).
            questions.Should().HaveCount(3);
            questions.Select(q => (q.RowNumber, q.XrefId)).Should().Equal(
                (6, "x1"), (7, "x2"), (8, "x1"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // Three single-row questions in Section One (rows 6,7,8) with XrefIds x1, x2, x1 — the
    // repeated x1 is non-contiguous, so the parser must NOT merge them into one question.
    private static void WriteNonContiguousXref(string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(RlqV01WorkbookWriter.SheetName);
        ws.Cell(5, "D").Value = "Section One";
        foreach (var (row, xref) in new[] { (6, "x1"), (7, "x2"), (8, "x1") })
        {
            ws.Cell(row, "C").Value = row.ToString();
            ws.Cell(row, "D").Value = $"Q at row {row}";
            ws.Cell(row, "H").Value = $"ans{row}";
            ws.Cell(row, "Q").Value = xref;
        }
        wb.SaveAs(path);
    }
}
