using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Infrastructure;

namespace ItrqTool.Infrastructure.Tests;

public sealed class ClosedXmlExcelWriterTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-infra-writer-tests", Guid.NewGuid().ToString("N"));

    private static ClosedXmlExcelWriter Writer() => new();

    [Fact]
    public void WriteWorkbook_TwoSheets_WritesCorrectNamesHeadersAndRows()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "out.xlsx");
            var data = new ExcelWorkbookData([
                new ExcelSheetData("Sheet One",
                    ["Col A", "Col B"],
                    [["r1a", "r1b"], ["r2a", "r2b"]]),
                new ExcelSheetData("Sheet Two",
                    ["X", "Y", "Z"],
                    [["1", "2", "3"]])
            ]);

            Writer().WriteWorkbook(data, filePath);

            using var wb = new XLWorkbook(filePath);
            wb.Worksheets.Should().HaveCount(2);

            var ws1 = wb.Worksheet("Sheet One");
            ws1.Cell(1, 1).GetString().Should().Be("Col A");
            ws1.Cell(1, 2).GetString().Should().Be("Col B");
            ws1.Cell(1, 1).Style.Font.Bold.Should().BeTrue();
            ws1.Cell(2, 1).GetString().Should().Be("r1a");
            ws1.Cell(3, 2).GetString().Should().Be("r2b");

            var ws2 = wb.Worksheet("Sheet Two");
            ws2.Cell(1, 3).GetString().Should().Be("Z");
            ws2.RowsUsed().Count().Should().Be(2); // header + 1 data row
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void WriteWorkbook_OverwritesExistingFile_WithoutThrowing()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "overwrite.xlsx");
            var data = new ExcelWorkbookData([
                new ExcelSheetData("S", ["H"], [["v"]])
            ]);

            Writer().WriteWorkbook(data, filePath);
            var act = () => Writer().WriteWorkbook(data, filePath);

            act.Should().NotThrow();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
