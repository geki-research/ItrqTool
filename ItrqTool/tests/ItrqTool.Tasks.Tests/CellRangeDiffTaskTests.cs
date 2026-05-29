using ClosedXML.Excel; // test fixture creation only
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Domain.Reporting;
using ItrqTool.Tasks;

namespace ItrqTool.Tasks.Tests;

public sealed class CellRangeDiffTaskTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-cellrange-task-tests", Guid.NewGuid().ToString("N"));

    private static CellRangeDiffTask MakeTask(
        IExcelStructureReader? reader = null,
        IHtmlCellRangeDiffReportWriter? htmlWriter = null)
    {
        var r = reader ?? Substitute.For<IExcelStructureReader>();
        var w = htmlWriter ?? Substitute.For<IHtmlCellRangeDiffReportWriter>();
        return new CellRangeDiffTask(r, w, NullLogger<CellRangeDiffTask>.Instance);
    }

    private static TaskExecutionContext MakeCtx(
        string dir,
        Dictionary<string, string> parameters,
        string reportFile = "report.html")
        => new(
            TaskId: "crd",
            InputPaths: new Dictionary<string, string>(),
            OutputPaths: new Dictionary<string, string> { ["report"] = Path.Combine(dir, reportFile) },
            Logger: NullLogger.Instance,
            WorkingDirectory: dir)
        {
            Parameters = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase)
        };

    // ── Success: value-only diff classifies changed/unchanged correctly ─────

    [Fact]
    public async Task ExecuteAsync_ValueOnlyScope_ClassifiesCells()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var f1 = Path.Combine(dir, "f1.xlsx");
            var f2 = Path.Combine(dir, "f2.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("Sheet1"); wb.SaveAs(f1); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("Sheet1"); wb.SaveAs(f2); }

            var reader = Substitute.For<IExcelStructureReader>();
            reader.ReadCells(f1, "Sheet1", Arg.Any<IReadOnlyList<string>>())
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["A1"] = new("same", null, null, null),
                    ["A2"] = new("old",  null, null, null)
                });
            reader.ReadCells(f2, "Sheet1", Arg.Any<IReadOnlyList<string>>())
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["A1"] = new("same", null, null, null),
                    ["A2"] = new("new",  null, null, null)
                });

            HtmlDiffCellRangeReportData? captured = null;
            var writer = Substitute.For<IHtmlCellRangeDiffReportWriter>();
            writer.When(w => w.WriteReport(Arg.Any<HtmlDiffCellRangeReportData>(), Arg.Any<string>()))
                .Do(ci => { captured = ci.ArgAt<HtmlDiffCellRangeReportData>(0);
                             File.WriteAllText(ci.ArgAt<string>(1), "<html/>"); });

            var ctx = MakeCtx(dir, new Dictionary<string, string>
            {
                ["file1Path"]    = f1,
                ["file2Path"]    = f2,
                ["sheet1Name"]   = "Sheet1",
                ["sheet2Name"]   = "Sheet1",
                ["ranges"]       = "A1:A2",
                ["compareScope"] = "Value"
            });

            var result = await MakeTask(reader, writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            captured.Should().NotBeNull();
            captured!.Changed.Should().HaveCount(1);
            captured.Changed[0].Address.Should().Be("A2");
            captured.Unchanged.Should().HaveCount(1);
            captured.Unchanged[0].Address.Should().Be("A1");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── ValueAndDvCf: blank cell with DV change → DvChanged == true ──────
    // The reader returns null TextValue (blank cell) with a DV type — this exercises
    // the task's classification of a DV-only change on an otherwise-blank cell.

    [Fact]
    public async Task ExecuteAsync_ValueAndDvCfScope_BlankCellWithDvChange_IsChanged()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var f1 = Path.Combine(dir, "f1-dv.xlsx");
            var f2 = Path.Combine(dir, "f2-dv.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("Data"); wb.SaveAs(f1); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("Data"); wb.SaveAs(f2); }

            // Reader returns blank cells: f1 has "List:Yes,No", f2 has "List:Yes,No,N/A"
            var reader = Substitute.For<IExcelStructureReader>();
            reader.ReadCells(f1, "Data", Arg.Any<IReadOnlyList<string>>())
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["B5"] = new(null, "List", "\"Yes,No\"", null)
                });
            reader.ReadCells(f2, "Data", Arg.Any<IReadOnlyList<string>>())
                .Returns(new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase)
                {
                    ["B5"] = new(null, "List", "\"Yes,No,N/A\"", null)
                });

            HtmlDiffCellRangeReportData? captured = null;
            var writer = Substitute.For<IHtmlCellRangeDiffReportWriter>();
            writer.When(w => w.WriteReport(Arg.Any<HtmlDiffCellRangeReportData>(), Arg.Any<string>()))
                .Do(ci => { captured = ci.ArgAt<HtmlDiffCellRangeReportData>(0);
                             File.WriteAllText(ci.ArgAt<string>(1), "<html/>"); });

            var ctx = MakeCtx(dir, new Dictionary<string, string>
            {
                ["file1Path"]    = f1,
                ["file2Path"]    = f2,
                ["sheet1Name"]   = "Data",
                ["sheet2Name"]   = "Data",
                ["ranges"]       = "B5:B5",
                ["compareScope"] = "ValueAndDvCf"
            });

            var result = await MakeTask(reader, writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            captured.Should().NotBeNull();
            var cell = captured!.Changed.Should().ContainSingle().Subject;
            cell.Address.Should().Be("B5");
            cell.DvChanged.Should().BeTrue();
            cell.TextChanged.Should().BeFalse();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Failure: malformed range token ─────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MalformedRange_FailsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var f = Path.Combine(dir, "x.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("S"); wb.SaveAs(f); }

            var ctx = MakeCtx(dir, new Dictionary<string, string>
            {
                ["file1Path"]    = f,
                ["file2Path"]    = f,
                ["sheet1Name"]   = "S",
                ["sheet2Name"]   = "S",
                ["ranges"]       = "NOT_VALID!!",
                ["compareScope"] = "Value"
            });

            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Error);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Failure: missing required parameter ───────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingCompareScope_FailsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var ctx = MakeCtx(dir, new Dictionary<string, string>
            {
                ["file1Path"]  = "x.xlsx",
                ["file2Path"]  = "y.xlsx",
                ["sheet1Name"] = "S",
                ["sheet2Name"] = "S",
                ["ranges"]     = "A1:A5"
                // compareScope intentionally omitted
            });

            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains("compareScope"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Failure: unrecognised compareScope ────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_UnrecognisedCompareScope_FailsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var f = Path.Combine(dir, "x.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("S"); wb.SaveAs(f); }

            var ctx = MakeCtx(dir, new Dictionary<string, string>
            {
                ["file1Path"]    = f,
                ["file2Path"]    = f,
                ["sheet1Name"]   = "S",
                ["sheet2Name"]   = "S",
                ["ranges"]       = "A1:A2",
                ["compareScope"] = "UNKNOWN"
            });

            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains("UNKNOWN"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Failure: missing file ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingFile_FailsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var ctx = MakeCtx(dir, new Dictionary<string, string>
            {
                ["file1Path"]    = Path.Combine(dir, "does-not-exist.xlsx"),
                ["file2Path"]    = Path.Combine(dir, "also-missing.xlsx"),
                ["sheet1Name"]   = "S",
                ["sheet2Name"]   = "S",
                ["ranges"]       = "A1:A2",
                ["compareScope"] = "Value"
            });

            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Error);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Failure: missing sheet propagates via exception ───────────────────

    [Fact]
    public async Task ExecuteAsync_MissingSheet_FailsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var f = Path.Combine(dir, "x.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("ActualSheet"); wb.SaveAs(f); }

            // Reader throws when the sheet is missing — task must catch and return Succeeded=false
            var reader = Substitute.For<IExcelStructureReader>();
            reader.ReadCells(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
                .Returns(_ => throw new InvalidOperationException("Sheet 'DoesNotExist' not found."));

            var ctx = MakeCtx(dir, new Dictionary<string, string>
            {
                ["file1Path"]    = f,
                ["file2Path"]    = f,
                ["sheet1Name"]   = "DoesNotExist",
                ["sheet2Name"]   = "ActualSheet",
                ["ranges"]       = "A1:A2",
                ["compareScope"] = "Value"
            });

            var result = await MakeTask(reader).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Error);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Cancellation propagates ───────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var f = Path.Combine(dir, "x.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("S"); wb.SaveAs(f); }

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var ctx = MakeCtx(dir, new Dictionary<string, string>
            {
                ["file1Path"]    = f,
                ["file2Path"]    = f,
                ["sheet1Name"]   = "S",
                ["sheet2Name"]   = "S",
                ["ranges"]       = "A1:A2",
                ["compareScope"] = "Value"
            });

            Func<Task> act = () => MakeTask().ExecuteAsync(ctx, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
