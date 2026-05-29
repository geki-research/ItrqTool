using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Infrastructure;

namespace ItrqTool.Infrastructure.Tests;

public sealed class ClosedXmlExcelStructureReaderReadCellsTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-infra-readcells-tests", Guid.NewGuid().ToString("N"));

    private static ClosedXmlExcelStructureReader Reader() =>
        new(NullLogger<ClosedXmlExcelStructureReader>.Instance);

    // 1. Blank cell carrying a DV list is returned with DV type — the headline capability
    //    that ReadRows cannot provide (CellsUsed() skips blank cells).
    [Fact]
    public void ReadCells_BlankCellWithDvList_IsReturnedWithDvType()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "blank-dv.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");
                var dv = ws.Cell("B5").CreateDataValidation();
                dv.List("\"Yes,No,N/A\"");
                wb.SaveAs(filePath);
            }

            var result = Reader().ReadCells(filePath, "Sheet1", new[] { "B5:B5" });

            result.Should().ContainKey("B5");
            var cell = result["B5"];
            cell.TextValue.Should().BeNullOrEmpty();
            cell.DataValidationType.Should().Be("List");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // 2. Populated cell: text value, DV rule, and CF rule are all captured.
    [Fact]
    public void ReadCells_PopulatedCellWithDvAndCf_CapturesAllThree()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "populated-dv-cf.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");
                ws.Cell("C3").Value = "SomeText";
                var dv = ws.Cell("C3").CreateDataValidation();
                dv.List("\"A,B,C\"");
                ws.Cell("C3").AddConditionalFormat().WhenGreaterThan(0).Fill.BackgroundColor = XLColor.Red;
                wb.SaveAs(filePath);
            }

            var result = Reader().ReadCells(filePath, "Sheet1", new[] { "C3:C3" });

            result.Should().ContainKey("C3");
            var cell = result["C3"];
            cell.TextValue.Should().Be("SomeText");
            cell.DataValidationType.Should().Be("List");
            cell.ConditionalFormattingType.Should().NotBeNull();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // 3. Fully blank range: every address in the range is returned even with no content.
    [Fact]
    public void ReadCells_FullyBlankRange_ReturnsAllAddressesWithEmptyText()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "blank-range.xlsx");
            using (var wb = new XLWorkbook())
            {
                wb.Worksheets.Add("Sheet1");
                wb.SaveAs(filePath);
            }

            var result = Reader().ReadCells(filePath, "Sheet1", new[] { "A1:B2" });

            result.Should().HaveCount(4);
            result.Should().ContainKey("A1");
            result.Should().ContainKey("A2");
            result.Should().ContainKey("B1");
            result.Should().ContainKey("B2");
            foreach (var cell in result.Values)
            {
                cell.TextValue.Should().BeNullOrEmpty();
                cell.DataValidationType.Should().BeNull();
            }
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // 4. Multiple ranges with an overlapping cell: the shared address appears exactly once.
    [Fact]
    public void ReadCells_MultipleRangesWithOverlap_OverlappingAddressAppearsOnce()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "overlap.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");
                ws.Cell("A1").Value = "X";
                wb.SaveAs(filePath);
            }

            var result = Reader().ReadCells(filePath, "Sheet1", new[] { "A1:A2", "A1:B1" });

            result.Keys.Count(k => k.Equals("A1", StringComparison.OrdinalIgnoreCase))
                  .Should().Be(1);
            // A2 and B1 are also present from the two ranges
            result.Should().ContainKey("A2");
            result.Should().ContainKey("B1");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // 5. Missing sheet throws (consistent with ReadRows behaviour).
    [Fact]
    public void ReadCells_MissingSheet_Throws()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "missingsheet.xlsx");
            using (var wb = new XLWorkbook())
            {
                wb.Worksheets.Add("Sheet1");
                wb.SaveAs(filePath);
            }

            var act = () => Reader().ReadCells(filePath, "DoesNotExist", new[] { "A1:A1" });
            act.Should().Throw<Exception>();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // 6. Empty ranges list returns an empty dictionary.
    [Fact]
    public void ReadCells_EmptyRangesList_ReturnsEmptyDictionary()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "empty-ranges.xlsx");
            using (var wb = new XLWorkbook())
            {
                wb.Worksheets.Add("Sheet1");
                wb.SaveAs(filePath);
            }

            var result = Reader().ReadCells(filePath, "Sheet1", Array.Empty<string>());

            result.Should().BeEmpty();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
