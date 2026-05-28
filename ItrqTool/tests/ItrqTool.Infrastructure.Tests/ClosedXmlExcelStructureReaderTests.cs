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

    // ── DataValidationOperator / DataValidationFormula2 ───────────────────────

    [Fact]
    public void ReadRows_SingleValueWholeNumberDv_PopulatesDvOperatorAndNullFormula2()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "dv-wholenumber-gt.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");
                ws.Cell(1, 3).Value = "Question text";
                var dvCell = ws.Cell(1, 4);
                dvCell.Value = "5";
                var dv = dvCell.CreateDataValidation();
                dv.AllowedValues = XLAllowedValues.WholeNumber;
                dv.Operator = XLOperator.GreaterThan;
                dv.Value = "5";
                wb.SaveAs(filePath);
            }

            var rows = Reader().ReadRows(filePath, "Sheet1");

            var cell = rows.Single(r => r.RowNumber == 1).CellsByColumn["D"];
            cell.DataValidationType.Should().Be("WholeNumber");
            cell.DataValidationFormula.Should().Be("5");
            cell.DataValidationOperator.Should().Be("GreaterThan");
            cell.DataValidationFormula2.Should().BeNull();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void ReadRows_BetweenWholeNumberDv_PopulatesDvOperatorAndBothFormulas()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "dv-wholenumber-between.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");
                ws.Cell(1, 3).Value = "Question text";
                var dvCell = ws.Cell(1, 4);
                dvCell.Value = "5";
                var dv = dvCell.CreateDataValidation();
                dv.AllowedValues = XLAllowedValues.WholeNumber;
                dv.Operator = XLOperator.Between;
                dv.MinValue = "1";
                dv.MaxValue = "10";
                wb.SaveAs(filePath);
            }

            var rows = Reader().ReadRows(filePath, "Sheet1");

            var cell = rows.Single(r => r.RowNumber == 1).CellsByColumn["D"];
            cell.DataValidationType.Should().Be("WholeNumber");
            cell.DataValidationFormula.Should().Be("1");
            cell.DataValidationOperator.Should().Be("Between");
            cell.DataValidationFormula2.Should().Be("10");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void ReadRows_ListDv_DvOperatorIsNull()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "dv-list-operator-null.xlsx");
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
            cell.DataValidationType.Should().Be("List");
            cell.DataValidationOperator.Should().BeNull();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void ReadRows_CustomDv_DvOperatorIsNull()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "dv-custom-operator-null.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");
                ws.Cell(1, 3).Value = "Question text";
                var dvCell = ws.Cell(1, 4);
                dvCell.Value = "answer";
                var dv = dvCell.CreateDataValidation();
                dv.Custom("=D1>0");
                wb.SaveAs(filePath);
            }

            var rows = Reader().ReadRows(filePath, "Sheet1");

            var cell = rows.Single(r => r.RowNumber == 1).CellsByColumn["D"];
            cell.DataValidationType.Should().Be("Custom");
            cell.DataValidationOperator.Should().BeNull();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── ConditionalFormattingType / Value / Value2 ────────────────────────────

    [Fact]
    public void ReadRows_CellIsCfSingleValue_PopulatesCfTypeAndValue()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "cf-cellis-single.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");
                ws.Cell(1, 3).Value = "Question text";
                var cfCell = ws.Cell(1, 4);
                cfCell.Value = "5";
                cfCell.AddConditionalFormat().WhenGreaterThan(5).Fill.BackgroundColor = XLColor.Red;
                wb.SaveAs(filePath);
            }

            var rows = Reader().ReadRows(filePath, "Sheet1");

            var cell = rows.Single(r => r.RowNumber == 1).CellsByColumn["D"];
            cell.ConditionalFormattingType.Should().Be("CellIs");
            cell.ConditionalFormattingValue.Should().Be("5");
            cell.ConditionalFormattingValue2.Should().BeNull();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void ReadRows_CellIsCfBetween_PopulatesBothCfValues()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "cf-cellis-between.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");
                ws.Cell(1, 3).Value = "Question text";
                var cfCell = ws.Cell(1, 4);
                cfCell.Value = "5";
                cfCell.AddConditionalFormat().WhenBetween(1, 10).Fill.BackgroundColor = XLColor.Red;
                wb.SaveAs(filePath);
            }

            var rows = Reader().ReadRows(filePath, "Sheet1");

            var cell = rows.Single(r => r.RowNumber == 1).CellsByColumn["D"];
            cell.ConditionalFormattingType.Should().Be("CellIs");
            cell.ConditionalFormattingValue.Should().Be("1");
            cell.ConditionalFormattingValue2.Should().Be("10");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void ReadRows_ExpressionCf_PopulatesCfTypeAndValue()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "cf-expression.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");
                ws.Cell(1, 3).Value = "Question text";
                var cfCell = ws.Cell(1, 4);
                cfCell.Value = "5";
                cfCell.AddConditionalFormat().WhenIsTrue("D1>5").Fill.BackgroundColor = XLColor.Red;
                wb.SaveAs(filePath);
            }

            var rows = Reader().ReadRows(filePath, "Sheet1");

            var cell = rows.Single(r => r.RowNumber == 1).CellsByColumn["D"];
            cell.ConditionalFormattingType.Should().Be("Expression");
            cell.ConditionalFormattingValue.Should().NotBeNull();
            cell.ConditionalFormattingValue2.Should().BeNull();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void ReadRows_NoDvNoCf_AllFiveNewFieldsAreNull()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "no-dv-no-cf.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");
                ws.Cell(1, 3).Value = "Plain text, no DV or CF";
                wb.SaveAs(filePath);
            }

            var rows = Reader().ReadRows(filePath, "Sheet1");

            var cell = rows.Single(r => r.RowNumber == 1).CellsByColumn["C"];
            cell.DataValidationOperator.Should().BeNull();
            cell.DataValidationFormula2.Should().BeNull();
            cell.ConditionalFormattingType.Should().BeNull();
            cell.ConditionalFormattingValue.Should().BeNull();
            cell.ConditionalFormattingValue2.Should().BeNull();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void ReadRows_ColorScaleCf_DoesNotThrowAndYieldsNullCfValues()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "cf-colorscale.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Sheet1");
                ws.Cell(1, 3).Value = "Question text";
                var scaledCell = ws.Cell(1, 4);
                scaledCell.Value = "5";
                scaledCell.AddConditionalFormat().ColorScale();
                wb.SaveAs(filePath);
            }

            // Assert no exception is thrown on read
            var readAction = () => Reader().ReadRows(filePath, "Sheet1");
            readAction.Should().NotThrow();

            var rows = Reader().ReadRows(filePath, "Sheet1");

            var cfCell = rows.Single(r => r.RowNumber == 1).CellsByColumn["D"];
            cfCell.ConditionalFormattingType.Should().NotBeNull();
            cfCell.ConditionalFormattingValue.Should().BeNull();
            cfCell.ConditionalFormattingValue2.Should().BeNull();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
