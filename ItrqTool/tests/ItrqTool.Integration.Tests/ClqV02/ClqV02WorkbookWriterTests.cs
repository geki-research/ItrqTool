using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using Xunit;

namespace ItrqTool.Integration.Tests.ClqV02;

/// <summary>
/// Smoke tests for <see cref="ClqV02WorkbookWriter"/>: verifies the column-shift
/// (K=AnswerStability, N=ProvidedBy, O=XrefId) and DV placement are correct before
/// the baseline-zero pipeline run in I1b.
/// </summary>
public sealed class ClqV02WorkbookWriterTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-clqv02-writer", Guid.NewGuid().ToString("N"));

    private const string SheetName = "IT Risk Control Self-Assessment";

    private static ClqV02WorkbookDescriptor MakeDescriptor() =>
        new(
            ChapterHeaders: [(1, "Chapter 1")],
            SectionHeaders: [(2, "Section 1.1")],
            Questions:
            [
                new ClqV02QuestionSpec(
                    RowNumber: 3,
                    XrefId: "Q001",
                    OriginalText: "1.1) First question text",
                    Guidance: "First guidance",
                    PreviousAnswer: "2",
                    Answer: "2",
                    Strengths: "Baseline strengths.",
                    Weaknesses: "Baseline weaknesses.",
                    ProvidedBy: "TestOrgUnit",
                    AnswerStability: "Yes"),

                new ClqV02QuestionSpec(
                    RowNumber: 4,
                    XrefId: "Q002",
                    OriginalText: "1.2) Second question text",
                    Guidance: null,
                    PreviousAnswer: null,
                    Answer: null,
                    Strengths: null,
                    Weaknesses: null,
                    ProvidedBy: "TestOrgUnit",
                    AnswerStability: null),
            ]);

    [Fact]
    public void Write_NonNullStabilityRow_AllColumnsAtCorrectAddresses()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "workbook.xlsx");
            ClqV02WorkbookWriter.Write(path, SheetName, MakeDescriptor());

            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet(SheetName);

            // Chapter and section headers in column D.
            ws.Cell(1, "D").GetString().Should().Be("Chapter 1");
            ws.Cell(2, "D").GetString().Should().Be("Section 1.1");

            // Row 3 — all non-null fields.
            ws.Cell(3, "D").GetString().Should().Be("1.1) First question text");
            ws.Cell(3, "E").GetString().Should().Be("First guidance");
            ws.Cell(3, "F").GetString().Should().Be("2");
            ws.Cell(3, "H").GetString().Should().Be("2");
            ws.Cell(3, "I").GetString().Should().Be("Baseline strengths.");
            ws.Cell(3, "J").GetString().Should().Be("Baseline weaknesses.");
            ws.Cell(3, "K").GetString().Should().Be("Yes");      // AnswerStability — new K column
            ws.Cell(3, "N").GetString().Should().Be("TestOrgUnit"); // ProvidedBy — shifted M→N
            ws.Cell(3, "O").GetString().Should().Be("Q001");        // XrefId — shifted N→O
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void Write_NullStabilityRow_KBlankNAndOStillWritten()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "workbook.xlsx");
            ClqV02WorkbookWriter.Write(path, SheetName, MakeDescriptor());

            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet(SheetName);

            // Row 4 — AnswerStability is null → K cell must be blank.
            ws.Cell(4, "K").GetString().Should().BeEmpty(
                "null AnswerStability must produce an empty K cell");

            // N and O are still written (ProvidedBy non-null, XrefId always written).
            ws.Cell(4, "N").GetString().Should().Be("TestOrgUnit");
            ws.Cell(4, "O").GetString().Should().Be("Q002");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void Write_BothRows_HAndKCarryListDv()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "workbook.xlsx");
            ClqV02WorkbookWriter.Write(path, SheetName, MakeDescriptor());

            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet(SheetName);

            // Both question rows must carry DV on H and K.
            foreach (var row in new[] { 3, 4 })
            {
                var hDv = ws.Cell(row, "H").GetDataValidation();
                hDv.Should().NotBeNull($"H{row} must carry answer list DV");
                hDv.Value.Should().Contain("1,2,3,4",
                    $"H{row} answer DV list must contain the four allowed values");

                var kDv = ws.Cell(row, "K").GetDataValidation();
                kDv.Should().NotBeNull($"K{row} must carry stability list DV");
                kDv.Value.Should().Contain("Yes,No",
                    $"K{row} stability DV list must contain the two allowed stability values");
            }
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
