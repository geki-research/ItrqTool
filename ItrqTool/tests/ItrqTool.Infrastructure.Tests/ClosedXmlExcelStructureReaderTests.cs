using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Infrastructure;

namespace ItrqTool.Infrastructure.Tests;

public sealed class ClosedXmlExcelStructureReaderTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-infra-reader-tests", Guid.NewGuid().ToString("N"));

    private static ClosedXmlExcelStructureReader Reader() =>
        new(NullLogger<ClosedXmlExcelStructureReader>.Instance);

    [Fact]
    public void ReadRows_BasicContent_ReturnsCorrectRowNumbersAndTextValues()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "basic.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");
                ws.Cell(1, 3).Value = "Chapter One";
                ws.Cell(2, 3).Value = "1.1) What is risk?";
                ws.Cell(3, 3).Value = "Section header";
                wb.SaveAs(filePath);
            }

            var rows = Reader().ReadRows(filePath, "Sheet1");

            rows.Should().HaveCount(3);
            rows[0].RowNumber.Should().Be(1);
            rows[0].CellsByColumn["C"].TextValue.Should().Be("Chapter One");
            rows[1].RowNumber.Should().Be(2);
            rows[1].CellsByColumn["C"].TextValue.Should().Be("1.1) What is risk?");
            rows[2].RowNumber.Should().Be(3);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void ReadRows_FullyBlankRow_IsNotReturned()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "blank.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");
                ws.Cell(1, 3).Value = "Row one";
                // Row 2 is blank
                ws.Cell(3, 3).Value = "Row three";
                wb.SaveAs(filePath);
            }

            var rows = Reader().ReadRows(filePath, "Sheet1");

            rows.Should().HaveCount(2);
            rows.Select(r => r.RowNumber).Should().BeEquivalentTo(new[] { 1, 3 });
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void ReadRows_CellWithDataValidation_DataValidationTypeIsNonNull()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "dv.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");
                ws.Cell(1, 3).Value = "Question text";
                var dvCell = ws.Cell(1, 4);
                dvCell.Value = "answer";
                var dv = dvCell.CreateDataValidation();
                dv.List(ws.Range("A1:A3"));
                wb.SaveAs(filePath);
            }

            var rows = Reader().ReadRows(filePath, "Sheet1");

            var row = rows.Single(r => r.RowNumber == 1);
            row.CellsByColumn.Should().ContainKey("D");
            row.CellsByColumn["D"].DataValidationType.Should().NotBeNull();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── DataValidationFormula ─────────────────────────────────────────────────

    [Fact]
    public void ReadRows_CellWithInlineListDv_DataValidationFormulaContainsListValues()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "dv-inline.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");
                ws.Cell(1, 3).Value = "Question text";
                var dvCell = ws.Cell(1, 4);
                dvCell.Value = "Yes";
                var dv = dvCell.CreateDataValidation();
                dv.List("\"Yes,No,N/A\"");
                wb.SaveAs(filePath);
            }

            var rows = Reader().ReadRows(filePath, "Sheet1");

            var cell = rows.Single(r => r.RowNumber == 1).CellsByColumn["D"];
            cell.DataValidationFormula.Should().NotBeNull();
            cell.DataValidationFormula.Should().Contain("Yes");
            cell.DataValidationFormula.Should().Contain("No");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void ReadRows_CellWithNoDv_DataValidationFormulaIsNull()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "dv-none.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");
                ws.Cell(1, 3).Value = "No DV here";
                wb.SaveAs(filePath);
            }

            var rows = Reader().ReadRows(filePath, "Sheet1");

            var cell = rows.Single(r => r.RowNumber == 1).CellsByColumn["C"];
            cell.DataValidationFormula.Should().BeNull();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
