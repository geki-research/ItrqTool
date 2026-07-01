using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Infrastructure;

namespace ItrqTool.Infrastructure.Tests;

// Regression coverage for BL-051: a LibreOffice-authored workbook carrying a legacy VML
// cell-comment shape fails inside ClosedXML's XLWorkbook constructor before any worksheet
// lookup ("Sequence contains no matching element"). The fixture below reproduces that exact
// crash; RobustWorkbookLoader strips the comment/VML parts on the load-failure path and retries.
public sealed class RobustWorkbookLoaderTests
{
    private static string CrashingFixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "assets", "lo-repro-CRASHING_comment.xlsx");

    private static ClosedXmlExcelStructureReader Reader() =>
        new(NullLogger<ClosedXmlExcelStructureReader>.Instance);

    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-infra-robustloader-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void PlainXLWorkbookCtor_OnLibreOfficeCommentFixture_Throws()
    {
        var act = () => new XLWorkbook(CrashingFixturePath());

        act.Should().Throw<Exception>("the LibreOffice legacy-VML comment shape is not parseable by ClosedXML");
    }

    [Fact]
    public void GetWorksheetNames_OnLibreOfficeCommentFixture_LoadsViaSanitizerAndListsAllSheets()
    {
        var names = Reader().GetWorksheetNames(CrashingFixturePath());

        names.Should().BeEquivalentTo(new[] { "Sheet1_blank", "Sheet2_cellcomment", "Sheet3_namedrange" });
    }

    [Fact]
    public void ReadCells_OnLibreOfficeCommentFixture_SeededNumericCellSurvivesSanitization()
    {
        var result = Reader().ReadCells(CrashingFixturePath(), "Sheet2_cellcomment", new[] { "B2:B2" });

        result.Should().ContainKey("B2");
        result["B2"].NativeValue.Should().Be(42d);
    }

    [Fact]
    public void ReadCells_OnNormalWorkbook_FastPathUnchanged()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "normal.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");
                ws.Cell("B2").Value = 7;
                wb.SaveAs(filePath);
            }

            var result = Reader().ReadCells(filePath, "Sheet1", new[] { "B2:B2" });

            result.Should().ContainKey("B2");
            result["B2"].NativeValue.Should().Be(7d);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
