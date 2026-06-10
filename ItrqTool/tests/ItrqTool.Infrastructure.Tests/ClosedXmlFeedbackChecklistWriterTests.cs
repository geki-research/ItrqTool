using ClosedXML.Excel;
using FluentAssertions;
using Xunit;
using ItrqTool.Domain.Validation;
using ItrqTool.Infrastructure.Excel;

namespace ItrqTool.Infrastructure.Tests;

public sealed class ClosedXmlFeedbackChecklistWriterTests
{
    // ── fixture helpers ───────────────────────────────────────────────────────

    private static string TemplatePath() =>
        Path.Combine(AppContext.BaseDirectory, "templates", "feedback-checklist-template.xlsx");

    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-checklist-writer-tests", Guid.NewGuid().ToString("N"));

    // Column map matching the template's current layout (A–I for task columns;
    // J = Response and K = Comment are deliberately absent from the map).
    private static IReadOnlyDictionary<ChecklistColumn, string> DefaultColumnMap() =>
        new Dictionary<ChecklistColumn, string>
        {
            [ChecklistColumn.Counter]        = "A",
            [ChecklistColumn.Worksheet]      = "B",
            [ChecklistColumn.QuestionNumber] = "C",
            [ChecklistColumn.CellAddresses]  = "D",
            [ChecklistColumn.QuestionText]   = "E",
            [ChecklistColumn.RequestedData]  = "F",
            [ChecklistColumn.ProvidedBy]     = "G",
            [ChecklistColumn.Evaluation]     = "H",
            [ChecklistColumn.CheckResult]    = "I",
        };

    // Two rows reusing the spike's test data
    private static IReadOnlyList<FeedbackChecklistRow> TwoRows() =>
    [
        new FeedbackChecklistRow(1, "sheet 1",  "5",  "B7",  "question text 1", "requested data 1", "person 1", FindingEvaluation.Error,   "finding 1"),
        new FeedbackChecklistRow(2, "sheet 2",  "6",  "B17", "question text 2", "requested data 2", "person 2", FindingEvaluation.Warning, "finding 2"),
    ];

    private static FeedbackChecklistWriterOptions DefaultOptions(int dataStartRow = 2) =>
        new("Checklist", dataStartRow, DefaultColumnMap());

    private static ClosedXmlFeedbackChecklistWriter MakeWriter() =>
        new();

    // ── values land in the correct cells ─────────────────────────────────────

    [Fact]
    public void Populate_WritesValuesToCorrectColumnsAndRows()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var output = Path.Combine(dir, "out.xlsx");
            MakeWriter().Populate(TwoRows(), TemplatePath(), output, DefaultOptions());

            using var wb = new XLWorkbook(output);
            var ws = wb.Worksheet("Checklist");

            // Row 2 (first data row)
            ws.Cell("A2").GetString().Should().Be("1");
            ws.Cell("B2").GetString().Should().Be("sheet 1");
            ws.Cell("C2").GetString().Should().Be("5");
            ws.Cell("D2").GetString().Should().Be("B7");
            ws.Cell("E2").GetString().Should().Be("question text 1");
            ws.Cell("F2").GetString().Should().Be("requested data 1");
            ws.Cell("G2").GetString().Should().Be("person 1");
            ws.Cell("H2").GetString().Should().Be("Error");
            ws.Cell("I2").GetString().Should().Be("finding 1");

            // Row 3 (second data row)
            ws.Cell("A3").GetString().Should().Be("2");
            ws.Cell("B3").GetString().Should().Be("sheet 2");
            ws.Cell("C3").GetString().Should().Be("6");
            ws.Cell("D3").GetString().Should().Be("B17");
            ws.Cell("E3").GetString().Should().Be("question text 2");
            ws.Cell("F3").GetString().Should().Be("requested data 2");
            ws.Cell("G3").GetString().Should().Be("person 2");
            ws.Cell("H3").GetString().Should().Be("Warning");
            ws.Cell("I3").GetString().Should().Be("finding 2");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Evaluation serializes as exact enum member name ───────────────────────

    [Fact]
    public void Populate_EvaluationCells_ContainExactEnumMemberName()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var output = Path.Combine(dir, "out.xlsx");
            MakeWriter().Populate(TwoRows(), TemplatePath(), output, DefaultOptions());

            using var wb = new XLWorkbook(output);
            var ws = wb.Worksheet("Checklist");

            ws.Cell("H2").GetString().Should().Be("Error");
            ws.Cell("H3").GetString().Should().Be("Warning");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── lesson-59 regression: row-3 cells inherit column style (center/middle)
    //    and must keep it after the write ──────────────────────────────────────
    //
    // Template column styles (from diagnostic, columns A–I):
    //   A: Center/Center WrapText=False   D: Center/Center WrapText=False
    //   B: Center/Center WrapText=False   E: Center/Center WrapText=True
    //   C: explicit XF   WrapText=False   F: Center/Center WrapText=True
    //                                     G: Center/Center WrapText=True
    //                                     H: Center/Center WrapText=False
    //                                     I: Left/Center   WrapText=True
    //
    // A plain write strips non-explicit cells back to General/Bottom/WrapText=False.
    // The style-preserving write must restore the effective column alignment on each cell.

    [Fact]
    public void Populate_InheritingCells_KeepCenterMiddleAlignmentAndWrap()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var output = Path.Combine(dir, "out.xlsx");
            MakeWriter().Populate(TwoRows(), TemplatePath(), output, DefaultOptions());

            using var wb = new XLWorkbook(output);
            var ws = wb.Worksheet("Checklist");

            // Non-explicit data cells (A,B,D,E,F,G,H,I) must reflect column style after write.
            // C3 is explicit (its own XF), so it is also verified but via its own captured style.

            // Center/Center columns — horizontal must not revert to General
            foreach (var col in new[] { "A", "B", "D", "E", "F", "G", "H" })
            {
                var cell = ws.Cell($"{col}3");
                cell.Style.Alignment.Horizontal.Should().Be(
                    XLAlignmentHorizontalValues.Center,
                    $"{cell.Address}: horizontal must stay Center (column style), not revert to General");
                cell.Style.Alignment.Vertical.Should().Be(
                    XLAlignmentVerticalValues.Center,
                    $"{cell.Address}: vertical must stay Center (column style), not revert to Bottom");
            }

            // I3 is Left/Center per its column style
            ws.Cell("I3").Style.Alignment.Horizontal.Should().Be(
                XLAlignmentHorizontalValues.Left, "I3 horizontal must stay Left (column style)");
            ws.Cell("I3").Style.Alignment.Vertical.Should().Be(
                XLAlignmentVerticalValues.Center, "I3 vertical must stay Center (column style)");

            // WrapText-True columns (E, F, G, I per column style): must stay True
            foreach (var col in new[] { "E", "F", "G", "I" })
            {
                var cell = ws.Cell($"{col}3");
                cell.Style.Alignment.WrapText.Should().BeTrue(
                    $"{cell.Address}: WrapText must stay True (column style)");
            }
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── DV and CF survive the write ───────────────────────────────────────────

    [Fact]
    public void Populate_DataValidationAndConditionalFormats_Preserved()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var output = Path.Combine(dir, "out.xlsx");
            MakeWriter().Populate(TwoRows(), TemplatePath(), output, DefaultOptions());

            using var wb = new XLWorkbook(output);
            var ws = wb.Worksheet("Checklist");

            ws.DataValidations.Count().Should().Be(1, "template has 1 data-validation rule");
            ws.ConditionalFormats.Count().Should().Be(4, "template has 4 conditional-format rules");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── unmapped columns (J = Response, K = Comment) remain empty ─────────────

    [Fact]
    public void Populate_UnmappedColumns_AreUntouched()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var output = Path.Combine(dir, "out.xlsx");
            MakeWriter().Populate(TwoRows(), TemplatePath(), output, DefaultOptions());

            using var wb = new XLWorkbook(output);
            var ws = wb.Worksheet("Checklist");

            ws.Cell("J2").IsEmpty().Should().BeTrue("Response column must not be written");
            ws.Cell("K2").IsEmpty().Should().BeTrue("Comment column must not be written");
            ws.Cell("J3").IsEmpty().Should().BeTrue("Response column must not be written");
            ws.Cell("K3").IsEmpty().Should().BeTrue("Comment column must not be written");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── configurable start row ────────────────────────────────────────────────

    [Fact]
    public void Populate_CustomDataStartRow_WritesRowsStartingAtConfiguredRow()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var output = Path.Combine(dir, "out.xlsx");
            MakeWriter().Populate(TwoRows(), TemplatePath(), output, DefaultOptions(dataStartRow: 5));

            using var wb = new XLWorkbook(output);
            var ws = wb.Worksheet("Checklist");

            // rows 5 and 6 hold the data
            ws.Cell("A5").GetString().Should().Be("1");
            ws.Cell("H5").GetString().Should().Be("Error");
            ws.Cell("A6").GetString().Should().Be("2");
            ws.Cell("H6").GetString().Should().Be("Warning");

            // row 2 must be empty (default start row must not be hardcoded)
            ws.Cell("A2").IsEmpty().Should().BeTrue("row 2 must be empty when start row is 5");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── invalid map: bad column letter ───────────────────────────────────────

    [Fact]
    public void Populate_InvalidColumnLetter_ThrowsArgumentException()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var output   = Path.Combine(dir, "out.xlsx");
            var badMap   = new Dictionary<ChecklistColumn, string>(DefaultColumnMap())
            {
                [ChecklistColumn.Counter] = "1A"  // not a valid column letter
            };
            var options  = new FeedbackChecklistWriterOptions("Checklist", 2, badMap);

            var act = () => MakeWriter().Populate(TwoRows(), TemplatePath(), output, options);
            act.Should().Throw<ArgumentException>()
                .WithMessage("*'1A'*");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── invalid map: duplicate column letters ─────────────────────────────────

    [Fact]
    public void Populate_DuplicateColumnLetters_ThrowsArgumentException()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var output   = Path.Combine(dir, "out.xlsx");
            var dupMap   = new Dictionary<ChecklistColumn, string>(DefaultColumnMap())
            {
                [ChecklistColumn.Counter]   = "A",
                [ChecklistColumn.Worksheet] = "A"   // duplicate
            };
            var options  = new FeedbackChecklistWriterOptions("Checklist", 2, dupMap);

            var act = () => MakeWriter().Populate(TwoRows(), TemplatePath(), output, options);
            act.Should().Throw<ArgumentException>()
                .WithMessage("*uplicate*");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
