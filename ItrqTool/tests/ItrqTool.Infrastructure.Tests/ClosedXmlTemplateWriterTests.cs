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
}
