using ClosedXML.Excel;
using FluentAssertions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Infrastructure.Excel;

namespace ItrqTool.Infrastructure.Tests;

public sealed class ClosedXmlTemplateWriterTests
{
    private const string SheetName = "Data";

    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-template-writer-tests", Guid.NewGuid().ToString("N"));

    // Build a template whose target cells INHERIT their style from the column:
    // alignment/wrap/number-format are set at the COLUMN level only; the cells
    // themselves carry no explicit XF. This reproduces the lesson-59 gotcha where
    // writing a value silently materialises a bare default cell style.
    private static string BuildInheritingTemplate(string dir)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "template.xlsx");

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);

        // Column C: distinctive inherited style.
        var colC = ws.Column("C").Style;
        colC.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        colC.Alignment.Vertical   = XLAlignmentVerticalValues.Top;
        colC.Alignment.WrapText   = true;
        colC.NumberFormat.Format  = "0.00";

        // A pre-existing value in an untouched cell, to prove we don't clear it.
        ws.Cell("A1").Value = "keep-me";

        wb.SaveAs(path);
        return path;
    }

    private static ClosedXmlTemplateWriter MakeWriter() => new();

    // ── 1. values land in the correct cells; untouched cells unchanged ─────────

    [Fact]
    public void Populate_WritesValuesToCorrectCells_AndLeavesOthersUnchanged()
    {
        var dir = TestWorkDir();
        var template = BuildInheritingTemplate(dir);
        var output = Path.Combine(dir, "out.xlsx");

        var cells = new List<CellWriteEntry>
        {
            new(3, "C", "hello"),
            new(5, "C", "world"),
        };

        MakeWriter().Populate(template, SheetName, cells, output);

        using var wb = new XLWorkbook(output);
        var ws = wb.Worksheet(SheetName);

        ws.Cell("C3").GetString().Should().Be("hello");
        ws.Cell("C5").GetString().Should().Be("world");
        // No entry for C4 → untouched (blank).
        ws.Cell("C4").IsEmpty().Should().BeTrue();
        // Pre-existing value preserved.
        ws.Cell("A1").GetString().Should().Be("keep-me");
    }

    // ── 2. formatting preserved (the gotcha test) ──────────────────────────────

    [Fact]
    public void Populate_PreservesInheritedColumnFormatting()
    {
        var dir = TestWorkDir();
        var template = BuildInheritingTemplate(dir);
        var output = Path.Combine(dir, "out.xlsx");

        MakeWriter().Populate(template, SheetName,
            new List<CellWriteEntry> { new(3, "C", "text") }, output);

        using var wb = new XLWorkbook(output);
        var ws = wb.Worksheet(SheetName);
        var style = ws.Cell("C3").Style;

        style.Alignment.Horizontal.Should().Be(XLAlignmentHorizontalValues.Center);
        style.Alignment.Vertical.Should().Be(XLAlignmentVerticalValues.Top);
        style.Alignment.WrapText.Should().BeTrue();
        style.NumberFormat.Format.Should().Be("0.00");
    }

    // ── 3. string-value typing (mirrors the verbatim helper's string branch) ───
    // NOTE: the prompt anticipated numeric-looking strings being coerced to numbers.
    // The extracted helper's `case string s: cell.Value = s` branch assigns the string
    // verbatim, and ClosedXML keeps it as Text — there is no string→number coercion for
    // a `string`-typed value (only the `int` branch produces a number). The helper was
    // moved verbatim (non-negotiable), so this test asserts the ACTUAL behavior and the
    // discrepancy is surfaced in the report rather than the proven helper being altered.
    [Fact]
    public void Populate_WritesNumericLookingString_AsTextValue()
    {
        var dir = TestWorkDir();
        var template = BuildInheritingTemplate(dir);
        var output = Path.Combine(dir, "out.xlsx");

        MakeWriter().Populate(template, SheetName,
            new List<CellWriteEntry> { new(3, "C", "1") }, output);

        using var wb = new XLWorkbook(output);
        var ws = wb.Worksheet(SheetName);
        ws.Cell("C3").DataType.Should().Be(XLDataType.Text);
        ws.Cell("C3").GetString().Should().Be("1");
    }

    // ── 4. missing sheet → clear exception naming the sheet ────────────────────

    [Fact]
    public void Populate_MissingSheet_ThrowsClearException()
    {
        var dir = TestWorkDir();
        var template = BuildInheritingTemplate(dir);
        var output = Path.Combine(dir, "out.xlsx");

        var act = () => MakeWriter().Populate(template, "NoSuchSheet",
            new List<CellWriteEntry> { new(3, "C", "x") }, output);

        act.Should().Throw<ArgumentException>()
           .WithMessage("*NoSuchSheet*");
    }

    // ── 5. template not mutated in place ───────────────────────────────────────

    [Fact]
    public void Populate_DoesNotMutateTemplateInPlace()
    {
        var dir = TestWorkDir();
        var template = BuildInheritingTemplate(dir);
        var output = Path.Combine(dir, "out.xlsx");

        var before = File.ReadAllBytes(template);

        MakeWriter().Populate(template, SheetName,
            new List<CellWriteEntry> { new(3, "C", "text") }, output);

        var after = File.ReadAllBytes(template);
        after.Should().Equal(before);
        File.Exists(output).Should().BeTrue();
    }

    // ── 6. typed-value write: native numeric/date/bool branches ───────────────

    // 6a. int TypedValue → stored as Number
    [Fact]
    public void Populate_IntTypedValue_WritesAsNumber()
    {
        var dir = TestWorkDir();
        var template = BuildInheritingTemplate(dir);
        var output = Path.Combine(dir, "out.xlsx");

        MakeWriter().Populate(template, SheetName,
            new List<CellWriteEntry> { new(3, "C", "7", TypedValue: 7) }, output);

        using var wb = new XLWorkbook(output);
        var cell = wb.Worksheet(SheetName).Cell("C3");
        cell.DataType.Should().Be(XLDataType.Number);
        cell.GetValue<int>().Should().Be(7);
    }

    // 6b. double TypedValue into a decimal-format cell → stored as Number, format preserved
    [Fact]
    public void Populate_DoubleTypedValue_InDecimalFormatCell_WritesAsNumberPreservesFormat()
    {
        var dir = TestWorkDir();
        var template = BuildInheritingTemplate(dir);  // column C has "0.00" format
        var output = Path.Combine(dir, "out.xlsx");

        MakeWriter().Populate(template, SheetName,
            new List<CellWriteEntry> { new(3, "C", "1.5", TypedValue: 1.5d) }, output);

        using var wb = new XLWorkbook(output);
        var cell = wb.Worksheet(SheetName).Cell("C3");
        cell.DataType.Should().Be(XLDataType.Number);
        cell.GetValue<double>().Should().BeApproximately(1.5, 1e-9);
        cell.Style.NumberFormat.Format.Should().Be("0.00");
    }

    // 6c. cross-type: int TypedValue into a decimal-format cell → stored as Number (not text)
    [Fact]
    public void Populate_IntTypedValue_InDecimalFormatCell_WritesAsNumber_NotText()
    {
        var dir = TestWorkDir();
        var template = BuildInheritingTemplate(dir);  // column C has "0.00" format
        var output = Path.Combine(dir, "out.xlsx");

        MakeWriter().Populate(template, SheetName,
            new List<CellWriteEntry> { new(3, "C", "3", TypedValue: 3) }, output);

        using var wb = new XLWorkbook(output);
        var cell = wb.Worksheet(SheetName).Cell("C3");
        cell.DataType.Should().Be(XLDataType.Number);
        cell.GetValue<int>().Should().Be(3);
    }

    // 6d. merged anchor: numeric TypedValue written to anchor cell of merged range round-trips as Number
    [Fact]
    public void Populate_NumericTypedValue_ToMergedAnchor_WritesAsNumber()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        var templatePath = Path.Combine(dir, "merged_template.xlsx");

        using (var wb = new XLWorkbook())
        {
            var ws = wb.Worksheets.Add(SheetName);
            ws.Range("B2:B4").Merge();
            wb.SaveAs(templatePath);
        }

        var output = Path.Combine(dir, "out.xlsx");
        MakeWriter().Populate(templatePath, SheetName,
            new List<CellWriteEntry> { new(2, "B", "5", TypedValue: 5) }, output);

        using var wbOut = new XLWorkbook(output);
        var cell = wbOut.Worksheet(SheetName).Cell("B2");
        cell.DataType.Should().Be(XLDataType.Number);
        cell.GetValue<int>().Should().Be(5);
    }

    // 6e. text regression: 3-arg entry (TypedValue null) still writes as Text
    [Fact]
    public void Populate_NullTypedValue_NumericLookingString_StillWritesAsText()
    {
        var dir = TestWorkDir();
        var template = BuildInheritingTemplate(dir);
        var output = Path.Combine(dir, "out.xlsx");

        MakeWriter().Populate(template, SheetName,
            new List<CellWriteEntry> { new(3, "C", "42") }, output);

        using var wb = new XLWorkbook(output);
        var cell = wb.Worksheet(SheetName).Cell("C3");
        cell.DataType.Should().Be(XLDataType.Text);
        cell.GetString().Should().Be("42");
    }

    // 6f. decimal TypedValue → stored as Number
    [Fact]
    public void Populate_DecimalTypedValue_WritesAsNumber()
    {
        var dir = TestWorkDir();
        var template = BuildInheritingTemplate(dir);
        var output = Path.Combine(dir, "out.xlsx");

        MakeWriter().Populate(template, SheetName,
            new List<CellWriteEntry> { new(3, "C", "2.5", TypedValue: 2.5m) }, output);

        using var wb = new XLWorkbook(output);
        var cell = wb.Worksheet(SheetName).Cell("C3");
        cell.DataType.Should().Be(XLDataType.Number);
        cell.GetValue<decimal>().Should().Be(2.5m);
    }

}
