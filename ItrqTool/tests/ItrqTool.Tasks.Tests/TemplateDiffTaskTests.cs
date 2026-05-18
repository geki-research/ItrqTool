using ClosedXML.Excel; // test fixture creation only
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Tasks;

namespace ItrqTool.Tasks.Tests;

public sealed class TemplateDiffTaskTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-tdiff-tests", Guid.NewGuid().ToString("N"));

    private static TemplateDiffTask MakeTask(
        IExcelStructureReader? reader = null,
        IExcelWriter? writer = null)
    {
        var r = reader ?? Substitute.For<IExcelStructureReader>();
        var w = writer ?? Substitute.For<IExcelWriter>();
        return new TemplateDiffTask(r, w, NullLogger<TemplateDiffTask>.Instance);
    }

    private static IReadOnlyList<ExcelRowStructure> MinimalRows() =>
    [
        new ExcelRowStructure(1, new Dictionary<string, ExcelCellStructure>
        {
            ["C"] = new("1.1) What is risk?", null, null)
        })
    ];

    // ── Success path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ValidInputs_SucceedsAndOutputFileExists()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            // Create minimal synthetic workbooks so file-existence check passes
            var oldPath = Path.Combine(dir, "old.xlsx");
            var newPath = Path.Combine(dir, "new.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Control Level Questions");
                ws.Cell(1, 3).Value = "1.1) What is risk?";
                wb.SaveAs(oldPath);
            }
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Control Level Questions");
                ws.Cell(1, 3).Value = "1.1) What is risk?";
                wb.SaveAs(newPath);
            }

            var configPath = Path.Combine(dir, "config.json");
            await File.WriteAllTextAsync(configPath,
                """{"sheetName":"Control Level Questions","textColumn":"C","inputColumn":"D","chapterRows":[],"sectionRows":[]}""");

            var reportPath = Path.Combine(dir, "report.xlsx");

            var structureReader = Substitute.For<IExcelStructureReader>();
            structureReader.ReadRows(Arg.Any<string>(), Arg.Any<string>()).Returns(MinimalRows());

            var excelWriter = Substitute.For<IExcelWriter>();
            excelWriter.When(w => w.WriteWorkbook(Arg.Any<ExcelWorkbookData>(), Arg.Any<string>()))
                .Do(ci => File.WriteAllBytes(ci.ArgAt<string>(1), []));

            var ctx = new TaskExecutionContext(
                TaskId: "diff",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string> { ["report"] = reportPath },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["oldWorkbookPath"] = oldPath,
                    ["newWorkbookPath"] = newPath,
                    ["configPath"] = configPath
                }
            };

            var result = await MakeTask(structureReader, excelWriter).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            File.Exists(reportPath).Should().BeTrue();
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Info);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Failure: missing parameter ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingConfigPath_ReturnsFailureWithErrorMessage()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var ctx = new TaskExecutionContext(
                TaskId: "diff",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string> { ["report"] = Path.Combine(dir, "r.xlsx") },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["oldWorkbookPath"] = "old.xlsx",
                    ["newWorkbookPath"] = "new.xlsx"
                    // configPath deliberately omitted
                }
            };

            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Error);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Failure: file not found ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ConfigFileNotFound_ReturnsFailureWithErrorMessage()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var ctx = new TaskExecutionContext(
                TaskId: "diff",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string> { ["report"] = Path.Combine(dir, "r.xlsx") },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["oldWorkbookPath"] = Path.Combine(dir, "old.xlsx"),
                    ["newWorkbookPath"] = Path.Combine(dir, "new.xlsx"),
                    ["configPath"] = Path.Combine(dir, "does-not-exist.json")
                }
            };

            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Error);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Cancellation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_AlreadyCancelledToken_ThrowsOperationCanceledException()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var oldPath = Path.Combine(dir, "old.xlsx");
            var newPath = Path.Combine(dir, "new.xlsx");
            var configPath = Path.Combine(dir, "config.json");
            File.WriteAllText(oldPath, "");
            File.WriteAllText(newPath, "");
            await File.WriteAllTextAsync(configPath,
                """{"sheetName":"Control Level Questions","textColumn":"C","inputColumn":"D","chapterRows":[],"sectionRows":[]}""");

            var ctx = new TaskExecutionContext(
                TaskId: "diff",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string> { ["report"] = Path.Combine(dir, "r.xlsx") },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["oldWorkbookPath"] = oldPath,
                    ["newWorkbookPath"] = newPath,
                    ["configPath"] = configPath
                }
            };

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var act = async () => await MakeTask().ExecuteAsync(ctx, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
