using ClosedXML.Excel; // test fixture creation only
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Domain.Reporting;
using ItrqTool.Tasks;

namespace ItrqTool.Tasks.Tests;

public sealed class RiskLevelQuestionDiffTaskTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-rlqdiff-tests", Guid.NewGuid().ToString("N"));

    private static RiskLevelQuestionDiffTask MakeTask(
        IExcelStructureReader? reader = null,
        IHtmlReportWriter? htmlWriter = null)
    {
        var r = reader ?? Substitute.For<IExcelStructureReader>();
        var w = htmlWriter ?? Substitute.For<IHtmlReportWriter>();
        return new RiskLevelQuestionDiffTask(r, w, NullLogger<RiskLevelQuestionDiffTask>.Instance);
    }

    private static IReadOnlyList<ExcelRowStructure> MinimalRows() =>
    [
        new ExcelRowStructure(1, new Dictionary<string, ExcelCellStructure>
        {
            ["B"] = new("1.1", null, null, null),
            ["C"] = new("What is risk?", null, null, null)
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
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath = Path.Combine(dir, "current.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Risk Level Questions");
                ws.Cell(1, 2).Value = "1.1";
                ws.Cell(1, 3).Value = "What is risk?";
                wb.SaveAs(previousPath);
            }
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Risk Level Questions");
                ws.Cell(1, 2).Value = "1.1";
                ws.Cell(1, 3).Value = "What is risk?";
                wb.SaveAs(currentPath);
            }

            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            const string configJson = """{"sheetName":"Risk Level Questions","numberColumn":"B","textColumn":"C","answerColumn":"D","explanationColumn":"E","sectionRows":[]}""";
            await File.WriteAllTextAsync(previousConfigPath, configJson);
            await File.WriteAllTextAsync(currentConfigPath, configJson);

            var reportPath = Path.Combine(dir, "report.html");

            var structureReader = Substitute.For<IExcelStructureReader>();
            structureReader.ReadRows(Arg.Any<string>(), Arg.Any<string>()).Returns(MinimalRows());

            var htmlWriter = Substitute.For<IHtmlReportWriter>();
            htmlWriter.When(w => w.WriteReport(Arg.Any<HtmlDiffReportData>(), Arg.Any<string>()))
                .Do(ci => File.WriteAllText(ci.ArgAt<string>(1), "<html/>"));

            var ctx = new TaskExecutionContext(
                TaskId: "diff",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string> { ["report"] = reportPath },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["previousWorkbookFullFilename"]      = previousPath,
                    ["currentWorkbookFullFilename"]       = currentPath,
                    ["previousConfigurationFullFilename"] = previousConfigPath,
                    ["currentConfigurationFullFilename"]  = currentConfigPath
                }
            };

            var result = await MakeTask(structureReader, htmlWriter).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            File.Exists(reportPath).Should().BeTrue();
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Info);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task ExecuteAsync_Success_OutputPathEndsWithHtml()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath = Path.Combine(dir, "current.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("RLQ"); wb.SaveAs(previousPath); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("RLQ"); wb.SaveAs(currentPath); }

            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            const string configJson = """{"sheetName":"RLQ","numberColumn":"B","textColumn":"C","answerColumn":"D","explanationColumn":"E","sectionRows":[]}""";
            await File.WriteAllTextAsync(previousConfigPath, configJson);
            await File.WriteAllTextAsync(currentConfigPath, configJson);

            var reportPath = Path.Combine(dir, "report.html");

            string? capturedPath = null;
            var htmlWriter = Substitute.For<IHtmlReportWriter>();
            htmlWriter.When(w => w.WriteReport(Arg.Any<HtmlDiffReportData>(), Arg.Any<string>()))
                .Do(ci => capturedPath = ci.ArgAt<string>(1));

            var structureReader = Substitute.For<IExcelStructureReader>();
            structureReader.ReadRows(Arg.Any<string>(), Arg.Any<string>()).Returns([]);

            var ctx = new TaskExecutionContext(
                TaskId: "diff",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string> { ["report"] = reportPath },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["previousWorkbookFullFilename"]      = previousPath,
                    ["currentWorkbookFullFilename"]       = currentPath,
                    ["previousConfigurationFullFilename"] = previousConfigPath,
                    ["currentConfigurationFullFilename"]  = currentConfigPath
                }
            };

            var result = await MakeTask(structureReader, htmlWriter).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            capturedPath.Should().NotBeNull();
            capturedPath!.Should().EndWith(".html");
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

    // ── ParseQuestions — explicit section ranges ───────────────────────────────

    [Fact]
    public async Task ExecuteAsync_QuestionsInSectionRange_AreParsedAndAppearInDiff()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath = Path.Combine(dir, "current.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("RLQ");
                ws.Cell(1, 3).Value = "Section A";
                ws.Cell(2, 2).Value = "1.1";
                ws.Cell(2, 3).Value = "What is risk?";
                ws.Cell(3, 3).Value = "Outside range — should be skipped";
                wb.SaveAs(previousPath);
            }
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("RLQ");
                ws.Cell(1, 3).Value = "Section A";
                ws.Cell(2, 2).Value = "1.1";
                ws.Cell(2, 3).Value = "What is a risk?"; // slightly different
                ws.Cell(3, 3).Value = "Outside range — should be skipped";
                wb.SaveAs(currentPath);
            }

            var configJson = """{"sheetName":"RLQ","numberColumn":"B","textColumn":"C","answerColumn":"D","explanationColumn":"E","sectionRows":["1:2-2"]}""";
            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            await File.WriteAllTextAsync(previousConfigPath, configJson);
            await File.WriteAllTextAsync(currentConfigPath, configJson);

            var reportPath = Path.Combine(dir, "report.html");

            var structureReader = Substitute.For<IExcelStructureReader>();
            structureReader.ReadRows(Arg.Any<string>(), "RLQ").Returns(
            [
                new ExcelRowStructure(1, new Dictionary<string, ExcelCellStructure>
                    { ["C"] = new("Section A", null, null, null) }),
                new ExcelRowStructure(2, new Dictionary<string, ExcelCellStructure>
                {
                    ["B"] = new("1.1", null, null, null),
                    ["C"] = new("What is risk?", null, null, null)
                }),
                new ExcelRowStructure(3, new Dictionary<string, ExcelCellStructure>
                    { ["C"] = new("Outside range", null, null, null) })
            ]);

            var htmlWriter = Substitute.For<IHtmlReportWriter>();

            var ctx = new TaskExecutionContext(
                TaskId: "diff",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string> { ["report"] = reportPath },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["previousWorkbookFullFilename"]      = previousPath,
                    ["currentWorkbookFullFilename"]       = currentPath,
                    ["previousConfigurationFullFilename"] = previousConfigPath,
                    ["currentConfigurationFullFilename"]  = currentConfigPath
                }
            };

            var result = await MakeTask(structureReader, htmlWriter).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Info && m.Text.Contains("Compared 1 question"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task ExecuteAsync_RowsOutsideSectionRange_AreSkipped()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath = Path.Combine(dir, "current.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("RLQ"); wb.SaveAs(previousPath); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("RLQ"); wb.SaveAs(currentPath); }

            // Section range is rows 10-20 but we only supply rows 1-2 → nothing in range
            var configJson = """{"sheetName":"RLQ","numberColumn":"B","textColumn":"C","answerColumn":"D","explanationColumn":"E","sectionRows":["9:10-20"]}""";
            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            await File.WriteAllTextAsync(previousConfigPath, configJson);
            await File.WriteAllTextAsync(currentConfigPath, configJson);

            var reportPath = Path.Combine(dir, "report.html");

            var structureReader = Substitute.For<IExcelStructureReader>();
            structureReader.ReadRows(Arg.Any<string>(), "RLQ").Returns(
            [
                new ExcelRowStructure(1, new Dictionary<string, ExcelCellStructure>
                    { ["C"] = new("Outside", null, null, null) }),
                new ExcelRowStructure(2, new Dictionary<string, ExcelCellStructure>
                    { ["C"] = new("Also outside", null, null, null) }),
            ]);

            var htmlWriter = Substitute.For<IHtmlReportWriter>();

            var ctx = new TaskExecutionContext(
                TaskId: "diff",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string> { ["report"] = reportPath },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["previousWorkbookFullFilename"]      = previousPath,
                    ["currentWorkbookFullFilename"]       = currentPath,
                    ["previousConfigurationFullFilename"] = previousConfigPath,
                    ["currentConfigurationFullFilename"]  = currentConfigPath
                }
            };

            var result = await MakeTask(structureReader, htmlWriter).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Info &&
                m.Text.Contains("0 added, 0 removed, 0 changed"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task ExecuteAsync_InvalidSectionRowsFormat_ReturnsFailure()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath = Path.Combine(dir, "current.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("RLQ"); wb.SaveAs(previousPath); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("RLQ"); wb.SaveAs(currentPath); }

            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            await File.WriteAllTextAsync(previousConfigPath,
                """{"sheetName":"RLQ","numberColumn":"B","textColumn":"C","answerColumn":"D","explanationColumn":"E","sectionRows":["badformat"]}""");
            await File.WriteAllTextAsync(currentConfigPath,
                """{"sheetName":"RLQ","numberColumn":"B","textColumn":"C","answerColumn":"D","explanationColumn":"E","sectionRows":[]}""");

            var reportPath = Path.Combine(dir, "report.html");

            var structureReader = Substitute.For<IExcelStructureReader>();
            structureReader.ReadRows(Arg.Any<string>(), "RLQ").Returns([]);

            var ctx = new TaskExecutionContext(
                TaskId: "diff",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string> { ["report"] = reportPath },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["previousWorkbookFullFilename"]      = previousPath,
                    ["currentWorkbookFullFilename"]       = currentPath,
                    ["previousConfigurationFullFilename"] = previousConfigPath,
                    ["currentConfigurationFullFilename"]  = currentConfigPath
                }
            };

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
                """{"sheetName":"PreviousSheet","numberColumn":"B","textColumn":"C","answerColumn":"D","explanationColumn":"E","sectionRows":[]}""");
            await File.WriteAllTextAsync(currentConfigPath,
                """{"sheetName":"CurrentSheet","numberColumn":"B","textColumn":"C","answerColumn":"D","explanationColumn":"E","sectionRows":[]}""");

            var reportPath = Path.Combine(dir, "report.html");

            var structureReader = Substitute.For<IExcelStructureReader>();
            structureReader.ReadRows(Arg.Any<string>(), Arg.Any<string>()).Returns([]);

            var htmlWriter = Substitute.For<IHtmlReportWriter>();

            var ctx = new TaskExecutionContext(
                TaskId: "diff",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string> { ["report"] = reportPath },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["previousWorkbookFullFilename"]      = previousPath,
                    ["currentWorkbookFullFilename"]       = currentPath,
                    ["previousConfigurationFullFilename"] = previousConfigPath,
                    ["currentConfigurationFullFilename"]  = currentConfigPath
                }
            };

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
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("RLQ"); wb.SaveAs(previousPath); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("RLQ"); wb.SaveAs(currentPath); }

            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            const string configJson = """{"sheetName":"RLQ","numberColumn":"B","textColumn":"C","answerColumn":"D","explanationColumn":"E","sectionRows":[]}""";
            await File.WriteAllTextAsync(previousConfigPath, configJson);
            await File.WriteAllTextAsync(currentConfigPath, configJson);

            HtmlDiffReportData? captured = null;
            var htmlWriter = Substitute.For<IHtmlReportWriter>();
            htmlWriter.When(w => w.WriteReport(Arg.Any<HtmlDiffReportData>(), Arg.Any<string>()))
                .Do(ci => captured = ci.ArgAt<HtmlDiffReportData>(0));

            var structureReader = Substitute.For<IExcelStructureReader>();
            structureReader.ReadRows(Arg.Any<string>(), Arg.Any<string>()).Returns([]);

            var ctx = new TaskExecutionContext(
                TaskId: "diff",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string> { ["report"] = Path.Combine(dir, "report.html") },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["previousWorkbookFullFilename"]      = previousPath,
                    ["currentWorkbookFullFilename"]       = currentPath,
                    ["previousConfigurationFullFilename"] = previousConfigPath,
                    ["currentConfigurationFullFilename"]  = currentConfigPath
                    // reportTitle intentionally absent
                }
            };

            var result = await MakeTask(structureReader, htmlWriter).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            captured.Should().NotBeNull();
            captured!.Title.Should().Be("Risk Level Questions Diff Report");
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
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("RLQ"); wb.SaveAs(previousPath); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("RLQ"); wb.SaveAs(currentPath); }

            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            const string configJson = """{"sheetName":"RLQ","numberColumn":"B","textColumn":"C","answerColumn":"D","explanationColumn":"E","sectionRows":[]}""";
            await File.WriteAllTextAsync(previousConfigPath, configJson);
            await File.WriteAllTextAsync(currentConfigPath, configJson);

            HtmlDiffReportData? captured = null;
            var htmlWriter = Substitute.For<IHtmlReportWriter>();
            htmlWriter.When(w => w.WriteReport(Arg.Any<HtmlDiffReportData>(), Arg.Any<string>()))
                .Do(ci => captured = ci.ArgAt<HtmlDiffReportData>(0));

            var structureReader = Substitute.For<IExcelStructureReader>();
            structureReader.ReadRows(Arg.Any<string>(), Arg.Any<string>()).Returns([]);

            var ctx = new TaskExecutionContext(
                TaskId: "diff",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string> { ["report"] = Path.Combine(dir, "report.html") },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["previousWorkbookFullFilename"]      = previousPath,
                    ["currentWorkbookFullFilename"]       = currentPath,
                    ["previousConfigurationFullFilename"] = previousConfigPath,
                    ["currentConfigurationFullFilename"]  = currentConfigPath,
                    ["reportTitle"] = "My Custom Diff Title"
                }
            };

            var result = await MakeTask(structureReader, htmlWriter).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            captured.Should().NotBeNull();
            captured!.Title.Should().Be("My Custom Diff Title");
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
            const string configJson = """{"sheetName":"Risk Level Questions","numberColumn":"B","textColumn":"C","answerColumn":"D","explanationColumn":"E","sectionRows":[]}""";
            await File.WriteAllTextAsync(previousConfigPath, configJson);
            await File.WriteAllTextAsync(currentConfigPath, configJson);

            var ctx = new TaskExecutionContext(
                TaskId: "diff",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string> { ["report"] = Path.Combine(dir, "r.html") },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["previousWorkbookFullFilename"]      = previousPath,
                    ["currentWorkbookFullFilename"]       = currentPath,
                    ["previousConfigurationFullFilename"] = previousConfigPath,
                    ["currentConfigurationFullFilename"]  = currentConfigPath
                }
            };

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var act = async () => await MakeTask().ExecuteAsync(ctx, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── RLQ-specific: ExplanationChanged ──────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ChangedQuestionWithExplanationDifference_PopulatesExplanationFieldsInReportData()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var currentPath = Path.Combine(dir, "current.xlsx");
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("RLQ"); wb.SaveAs(previousPath); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("RLQ"); wb.SaveAs(currentPath); }

            var previousConfigPath = Path.Combine(dir, "previous-config.json");
            var currentConfigPath = Path.Combine(dir, "current-config.json");
            var configJson = """{"sheetName":"RLQ","numberColumn":"B","textColumn":"C","answerColumn":"D","explanationColumn":"E","sectionRows":["1:2-2"]}""";
            await File.WriteAllTextAsync(previousConfigPath, configJson);
            await File.WriteAllTextAsync(currentConfigPath, configJson);

            // Both sides have identical question text but different explanation prompts.
            var previousRows = new List<ExcelRowStructure>
            {
                new(1, new Dictionary<string, ExcelCellStructure>
                    { ["C"] = new("Section A", null, null, null) }),
                new(2, new Dictionary<string, ExcelCellStructure>
                {
                    ["B"] = new("1.1", null, null, null),
                    ["C"] = new("What is risk?", null, null, null),
                    ["E"] = new("Old explanation text", null, null, null)
                })
            };
            var currentRows = new List<ExcelRowStructure>
            {
                new(1, new Dictionary<string, ExcelCellStructure>
                    { ["C"] = new("Section A", null, null, null) }),
                new(2, new Dictionary<string, ExcelCellStructure>
                {
                    ["B"] = new("1.1", null, null, null),
                    ["C"] = new("What is risk?", null, null, null),
                    ["E"] = new("New explanation text", null, null, null)
                })
            };

            var structureReader = Substitute.For<IExcelStructureReader>();
            structureReader.ReadRows(previousPath, "RLQ").Returns(previousRows);
            structureReader.ReadRows(currentPath, "RLQ").Returns(currentRows);

            HtmlDiffReportData? captured = null;
            var htmlWriter = Substitute.For<IHtmlReportWriter>();
            htmlWriter.When(w => w.WriteReport(Arg.Any<HtmlDiffReportData>(), Arg.Any<string>()))
                .Do(ci => captured = ci.ArgAt<HtmlDiffReportData>(0));

            var ctx = new TaskExecutionContext(
                TaskId: "diff",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string> { ["report"] = Path.Combine(dir, "report.html") },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["previousWorkbookFullFilename"]      = previousPath,
                    ["currentWorkbookFullFilename"]       = currentPath,
                    ["previousConfigurationFullFilename"] = previousConfigPath,
                    ["currentConfigurationFullFilename"]  = currentConfigPath
                }
            };

            var result = await MakeTask(structureReader, htmlWriter).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            captured.Should().NotBeNull();
            captured!.Changed.Should().HaveCount(1);

            var changedQ = captured.Changed[0];
            changedQ.OldExplanation.Should().Be("Old explanation text");
            changedQ.NewExplanation.Should().Be("New explanation text");
            changedQ.ExplanationChanged.Should().BeTrue();
            changedQ.TextChanged.Should().BeFalse(); // text is identical
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
