using ClosedXML.Excel; // test fixture creation only
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Domain.Reporting;
using ItrqTool.Tasks;

namespace ItrqTool.Tasks.Tests;

public sealed class GeneralDataDiffTaskTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-gddiff-tests", Guid.NewGuid().ToString("N"));

    private static GeneralDataDiffTask MakeTask(
        IExcelStructureReader? reader = null,
        IHtmlGeneralDataDiffReportWriter? htmlWriter = null)
    {
        var r = reader ?? Substitute.For<IExcelStructureReader>();
        var w = htmlWriter ?? Substitute.For<IHtmlGeneralDataDiffReportWriter>();
        return new GeneralDataDiffTask(r, w, NullLogger<GeneralDataDiffTask>.Instance);
    }

    private static IReadOnlyList<ExcelRowStructure> MinimalRows() =>
    [
        new ExcelRowStructure(1, new Dictionary<string, ExcelCellStructure>
        {
            ["C"] = new("General Data section", null, null, null)
        }),
        new ExcelRowStructure(2, new Dictionary<string, ExcelCellStructure>
        {
            ["B"] = new("1.1", null, null, null),
            ["C"] = new("What is the staffing count?", null, null, null)
        })
    ];

    private const string EmptyConfigJson =
        """{"sheetName":"General Data","numberColumn":"B","textColumn":"C","answerColumns":["D","E","F"],"explanationColumn":"G","sectionRows":[]}""";

    private const string OneQuestionConfigJson =
        """{"sheetName":"General Data","numberColumn":"B","textColumn":"C","answerColumns":["D","E","F"],"explanationColumn":"G","sectionRows":["1:2(1)"]}""";

    // ── Success path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ValidInputs_SucceedsAndWriterCalled()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath = Path.Combine(dir, "current.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("General Data"); wb.SaveAs(previousPath); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("General Data"); wb.SaveAs(currentPath); }

            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            await File.WriteAllTextAsync(previousConfigPath, EmptyConfigJson);
            await File.WriteAllTextAsync(currentConfigPath, EmptyConfigJson);

            var reportPath = Path.Combine(dir, "report.html");

            var structureReader = Substitute.For<IExcelStructureReader>();
            structureReader.ReadRows(Arg.Any<string>(), Arg.Any<string>()).Returns([]);

            var htmlWriter = Substitute.For<IHtmlGeneralDataDiffReportWriter>();
            htmlWriter.When(w => w.WriteReport(Arg.Any<HtmlDiffGeneralDataReportData>(), Arg.Any<string>()))
                .Do(ci => File.WriteAllText(ci.ArgAt<string>(1), "<html/>"));

            var ctx = MakeContext(dir, previousPath, currentPath,
                previousConfigPath, currentConfigPath, reportPath);

            var result = await MakeTask(structureReader, htmlWriter).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            htmlWriter.Received(1).WriteReport(Arg.Any<HtmlDiffGeneralDataReportData>(), reportPath);
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Info);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task ExecuteAsync_Success_OutputPathPassedToWriter()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath = Path.Combine(dir, "current.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("GD"); wb.SaveAs(previousPath); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("GD"); wb.SaveAs(currentPath); }

            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            var configJson = """{"sheetName":"GD","numberColumn":"B","textColumn":"C","answerColumns":["D","E","F"],"explanationColumn":"G","sectionRows":[]}""";
            await File.WriteAllTextAsync(previousConfigPath, configJson);
            await File.WriteAllTextAsync(currentConfigPath, configJson);

            var reportPath = Path.Combine(dir, "report.html");
            string? capturedPath = null;

            var htmlWriter = Substitute.For<IHtmlGeneralDataDiffReportWriter>();
            htmlWriter.When(w => w.WriteReport(Arg.Any<HtmlDiffGeneralDataReportData>(), Arg.Any<string>()))
                .Do(ci => capturedPath = ci.ArgAt<string>(1));

            var structureReader = Substitute.For<IExcelStructureReader>();
            structureReader.ReadRows(Arg.Any<string>(), Arg.Any<string>()).Returns([]);

            var ctx = MakeContext(dir, previousPath, currentPath,
                previousConfigPath, currentConfigPath, reportPath);

            var result = await MakeTask(structureReader, htmlWriter).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            capturedPath.Should().Be(reportPath);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Failure: missing parameter (one per key) ───────────────────────────────

    [Theory]
    [InlineData("previousWorkbookFullFilename")]
    [InlineData("currentWorkbookFullFilename")]
    [InlineData("previousConfigurationFullFilename")]
    [InlineData("currentConfigurationFullFilename")]
    public async Task ExecuteAsync_MissingRequiredParameter_ReturnsFailureNamingMissingKey(string missingKey)
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var allParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["previousWorkbookFullFilename"]      = "prev.xlsx",
                ["currentWorkbookFullFilename"]       = "curr.xlsx",
                ["previousConfigurationFullFilename"] = "prev-config.json",
                ["currentConfigurationFullFilename"]  = "curr-config.json"
            };
            allParams.Remove(missingKey);

            var ctx = new TaskExecutionContext(
                TaskId: "diff",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string> { ["report"] = Path.Combine(dir, "r.html") },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = allParams
            };

            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains(missingKey));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Failure: file not found ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WorkbookFileNotFound_ReturnsFailureWithErrorMessage()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            // Config files exist but workbook files do not.
            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            await File.WriteAllTextAsync(previousConfigPath, EmptyConfigJson);
            await File.WriteAllTextAsync(currentConfigPath, EmptyConfigJson);

            var ctx = new TaskExecutionContext(
                TaskId: "diff",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string> { ["report"] = Path.Combine(dir, "r.html") },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["previousWorkbookFullFilename"]      = Path.Combine(dir, "does-not-exist.xlsx"),
                    ["currentWorkbookFullFilename"]       = Path.Combine(dir, "also-missing.xlsx"),
                    ["previousConfigurationFullFilename"] = previousConfigPath,
                    ["currentConfigurationFullFilename"]  = currentConfigPath
                }
            };

            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Error);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

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
                OutputPaths: new Dictionary<string, string> { ["report"] = Path.Combine(dir, "r.html") },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["previousWorkbookFullFilename"]      = Path.Combine(dir, "prev.xlsx"),
                    ["currentWorkbookFullFilename"]       = Path.Combine(dir, "curr.xlsx"),
                    ["previousConfigurationFullFilename"] = Path.Combine(dir, "does-not-exist.json"),
                    ["currentConfigurationFullFilename"]  = Path.Combine(dir, "also-missing.json")
                }
            };

            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Error);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Questions parsed, diff counts reported ─────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_QuestionsInSectionRange_AreParsedAndCountedInMessages()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath = Path.Combine(dir, "current.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("General Data"); wb.SaveAs(previousPath); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("General Data"); wb.SaveAs(currentPath); }

            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            await File.WriteAllTextAsync(previousConfigPath, OneQuestionConfigJson);
            await File.WriteAllTextAsync(currentConfigPath, OneQuestionConfigJson);

            var reportPath = Path.Combine(dir, "report.html");

            var structureReader = Substitute.For<IExcelStructureReader>();
            // Both sides return the same question → 1 unchanged
            structureReader.ReadRows(Arg.Any<string>(), Arg.Any<string>()).Returns(MinimalRows());

            var htmlWriter = Substitute.For<IHtmlGeneralDataDiffReportWriter>();

            var ctx = MakeContext(dir, previousPath, currentPath,
                previousConfigPath, currentConfigPath, reportPath);

            var result = await MakeTask(structureReader, htmlWriter).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Info && m.Text.Contains("Compared 1 question"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task ExecuteAsync_RowsOutsideSectionRange_AreSkipped_DiffCountsZero()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath = Path.Combine(dir, "current.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("General Data"); wb.SaveAs(previousPath); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("General Data"); wb.SaveAs(currentPath); }

            // Section range is rows 10-10 but we return rows 1-2 → nothing in range
            var configJson = """{"sheetName":"General Data","numberColumn":"B","textColumn":"C","answerColumns":["D","E","F"],"explanationColumn":"G","sectionRows":["9:10(1)"]}""";
            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            await File.WriteAllTextAsync(previousConfigPath, configJson);
            await File.WriteAllTextAsync(currentConfigPath, configJson);

            var structureReader = Substitute.For<IExcelStructureReader>();
            structureReader.ReadRows(Arg.Any<string>(), Arg.Any<string>()).Returns(
            [
                new ExcelRowStructure(1, new Dictionary<string, ExcelCellStructure>
                    { ["C"] = new("Outside", null, null, null) }),
                new ExcelRowStructure(2, new Dictionary<string, ExcelCellStructure>
                    { ["C"] = new("Also outside", null, null, null) })
            ]);

            var htmlWriter = Substitute.For<IHtmlGeneralDataDiffReportWriter>();

            var ctx = MakeContext(dir, previousPath, currentPath,
                previousConfigPath, currentConfigPath, Path.Combine(dir, "r.html"));

            var result = await MakeTask(structureReader, htmlWriter).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Info &&
                m.Text.Contains("0 added, 0 removed, 0 changed"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Invalid SectionRows format ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_InvalidSectionRowsFormat_ReturnsFailure()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath = Path.Combine(dir, "current.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("General Data"); wb.SaveAs(previousPath); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("General Data"); wb.SaveAs(currentPath); }

            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            // "badformat" is not a valid section entry
            await File.WriteAllTextAsync(previousConfigPath,
                """{"sheetName":"General Data","numberColumn":"B","textColumn":"C","answerColumns":["D","E","F"],"explanationColumn":"G","sectionRows":["badformat"]}""");
            await File.WriteAllTextAsync(currentConfigPath, EmptyConfigJson);

            var structureReader = Substitute.For<IExcelStructureReader>();
            structureReader.ReadRows(Arg.Any<string>(), Arg.Any<string>()).Returns([]);

            var ctx = MakeContext(dir, previousPath, currentPath,
                previousConfigPath, currentConfigPath, Path.Combine(dir, "r.html"));

            var result = await MakeTask(structureReader).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Error);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Two different configs applied independently ────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SeparateConfigs_EachAppliedToItsOwnWorkbook()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath = Path.Combine(dir, "current.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("PreviousSheet"); wb.SaveAs(previousPath); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("CurrentSheet"); wb.SaveAs(currentPath); }

            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            await File.WriteAllTextAsync(previousConfigPath,
                """{"sheetName":"PreviousSheet","numberColumn":"B","textColumn":"C","answerColumns":["D","E","F"],"explanationColumn":"G","sectionRows":[]}""");
            await File.WriteAllTextAsync(currentConfigPath,
                """{"sheetName":"CurrentSheet","numberColumn":"B","textColumn":"C","answerColumns":["D","E","F"],"explanationColumn":"G","sectionRows":[]}""");

            var structureReader = Substitute.For<IExcelStructureReader>();
            structureReader.ReadRows(Arg.Any<string>(), Arg.Any<string>()).Returns([]);

            var htmlWriter = Substitute.For<IHtmlGeneralDataDiffReportWriter>();

            var ctx = MakeContext(dir, previousPath, currentPath,
                previousConfigPath, currentConfigPath, Path.Combine(dir, "r.html"));

            var result = await MakeTask(structureReader, htmlWriter).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            structureReader.Received(1).ReadRows(previousPath, "PreviousSheet");
            structureReader.Received(1).ReadRows(currentPath, "CurrentSheet");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── reportTitle parameter ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ReportTitleAbsent_DefaultTitleIsUsed()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath = Path.Combine(dir, "current.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("General Data"); wb.SaveAs(previousPath); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("General Data"); wb.SaveAs(currentPath); }

            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            await File.WriteAllTextAsync(previousConfigPath, EmptyConfigJson);
            await File.WriteAllTextAsync(currentConfigPath, EmptyConfigJson);

            HtmlDiffGeneralDataReportData? captured = null;
            var htmlWriter = Substitute.For<IHtmlGeneralDataDiffReportWriter>();
            htmlWriter.When(w => w.WriteReport(Arg.Any<HtmlDiffGeneralDataReportData>(), Arg.Any<string>()))
                .Do(ci => captured = ci.ArgAt<HtmlDiffGeneralDataReportData>(0));

            var structureReader = Substitute.For<IExcelStructureReader>();
            structureReader.ReadRows(Arg.Any<string>(), Arg.Any<string>()).Returns([]);

            var ctx = MakeContext(dir, previousPath, currentPath,
                previousConfigPath, currentConfigPath, Path.Combine(dir, "r.html"));
            // No reportTitle in parameters

            var result = await MakeTask(structureReader, htmlWriter).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            captured.Should().NotBeNull();
            captured!.Title.Should().Be("General Data Diff Report");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task ExecuteAsync_ReportTitlePresent_CustomTitleIsUsed()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath = Path.Combine(dir, "current.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("General Data"); wb.SaveAs(previousPath); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("General Data"); wb.SaveAs(currentPath); }

            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            await File.WriteAllTextAsync(previousConfigPath, EmptyConfigJson);
            await File.WriteAllTextAsync(currentConfigPath, EmptyConfigJson);

            HtmlDiffGeneralDataReportData? captured = null;
            var htmlWriter = Substitute.For<IHtmlGeneralDataDiffReportWriter>();
            htmlWriter.When(w => w.WriteReport(Arg.Any<HtmlDiffGeneralDataReportData>(), Arg.Any<string>()))
                .Do(ci => captured = ci.ArgAt<HtmlDiffGeneralDataReportData>(0));

            var structureReader = Substitute.For<IExcelStructureReader>();
            structureReader.ReadRows(Arg.Any<string>(), Arg.Any<string>()).Returns([]);

            var ctx = MakeContext(dir, previousPath, currentPath,
                previousConfigPath, currentConfigPath, Path.Combine(dir, "r.html"),
                reportTitle: "My Custom GD Title");

            var result = await MakeTask(structureReader, htmlWriter).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            captured.Should().NotBeNull();
            captured!.Title.Should().Be("My Custom GD Title");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Report data: answer cell changes ──────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_AnswerCellChangedBetweenVersions_AppearsInReportData()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath = Path.Combine(dir, "current.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("General Data"); wb.SaveAs(previousPath); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("General Data"); wb.SaveAs(currentPath); }

            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            await File.WriteAllTextAsync(previousConfigPath, OneQuestionConfigJson);
            await File.WriteAllTextAsync(currentConfigPath, OneQuestionConfigJson);

            var previousRows = new List<ExcelRowStructure>
            {
                new(1, new Dictionary<string, ExcelCellStructure>
                    { ["C"] = new("Section A", null, null, null) }),
                new(2, new Dictionary<string, ExcelCellStructure>
                {
                    ["B"] = new("1.1", null, null, null),
                    ["C"] = new("What is the staffing count?", null, null, null),
                    ["D"] = new("Old label", null, null, null)
                })
            };
            var currentRows = new List<ExcelRowStructure>
            {
                new(1, new Dictionary<string, ExcelCellStructure>
                    { ["C"] = new("Section A", null, null, null) }),
                new(2, new Dictionary<string, ExcelCellStructure>
                {
                    ["B"] = new("1.1", null, null, null),
                    ["C"] = new("What is the staffing count?", null, null, null),
                    ["D"] = new("New label", null, null, null)
                })
            };

            var structureReader = Substitute.For<IExcelStructureReader>();
            structureReader.ReadRows(previousPath, "General Data").Returns(previousRows);
            structureReader.ReadRows(currentPath, "General Data").Returns(currentRows);

            HtmlDiffGeneralDataReportData? captured = null;
            var htmlWriter = Substitute.For<IHtmlGeneralDataDiffReportWriter>();
            htmlWriter.When(w => w.WriteReport(Arg.Any<HtmlDiffGeneralDataReportData>(), Arg.Any<string>()))
                .Do(ci => captured = ci.ArgAt<HtmlDiffGeneralDataReportData>(0));

            var ctx = MakeContext(dir, previousPath, currentPath,
                previousConfigPath, currentConfigPath, Path.Combine(dir, "r.html"));

            var result = await MakeTask(structureReader, htmlWriter).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            captured.Should().NotBeNull();
            captured!.Changed.Should().HaveCount(1);

            var changedQ = captured.Changed[0];
            changedQ.AnswerCellsChanged.Should().BeTrue();
            changedQ.AnswerCellChanges.Should().HaveCount(1);
            changedQ.AnswerCellChanges[0].OldText.Should().Be("Old label");
            changedQ.AnswerCellChanges[0].NewText.Should().Be("New label");
            changedQ.TextChanged.Should().BeFalse();
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
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath = Path.Combine(dir, "current.xlsx");
            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            File.WriteAllText(previousPath, "");
            File.WriteAllText(currentPath, "");
            await File.WriteAllTextAsync(previousConfigPath, EmptyConfigJson);
            await File.WriteAllTextAsync(currentConfigPath, EmptyConfigJson);

            var ctx = MakeContext(dir, previousPath, currentPath,
                previousConfigPath, currentConfigPath, Path.Combine(dir, "r.html"));

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var act = async () => await MakeTask().ExecuteAsync(ctx, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Summary messages ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_EmptyBothSides_DiffCountsAllZeroInMessages()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath = Path.Combine(dir, "current.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("General Data"); wb.SaveAs(previousPath); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("General Data"); wb.SaveAs(currentPath); }

            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            await File.WriteAllTextAsync(previousConfigPath, EmptyConfigJson);
            await File.WriteAllTextAsync(currentConfigPath, EmptyConfigJson);

            var structureReader = Substitute.For<IExcelStructureReader>();
            structureReader.ReadRows(Arg.Any<string>(), Arg.Any<string>()).Returns([]);

            var ctx = MakeContext(dir, previousPath, currentPath,
                previousConfigPath, currentConfigPath, Path.Combine(dir, "r.html"));

            var result = await MakeTask(structureReader).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Info &&
                m.Text.Contains("0 added, 0 removed, 0 changed"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── TaskType ───────────────────────────────────────────────────────────────

    [Fact]
    public void TaskType_IsGeneralDataDiff()
    {
        MakeTask().TaskType.Should().Be("GeneralDataDiff");
    }

    // ── Helper ─────────────────────────────────────────────────────────────────

    private static TaskExecutionContext MakeContext(
        string dir,
        string previousPath,
        string currentPath,
        string previousConfigPath,
        string currentConfigPath,
        string reportPath,
        string? reportTitle = null)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["previousWorkbookFullFilename"]      = previousPath,
            ["currentWorkbookFullFilename"]       = currentPath,
            ["previousConfigurationFullFilename"] = previousConfigPath,
            ["currentConfigurationFullFilename"]  = currentConfigPath
        };
        if (reportTitle is not null)
            parameters["reportTitle"] = reportTitle;

        return new TaskExecutionContext(
            TaskId: "diff",
            InputPaths: new Dictionary<string, string>(),
            OutputPaths: new Dictionary<string, string> { ["report"] = reportPath },
            Logger: NullLogger.Instance,
            WorkingDirectory: dir)
        {
            Parameters = parameters
        };
    }
}
