using ClosedXML.Excel; // test fixture creation only
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks;
using ItrqTool.Tasks.Validation;

namespace ItrqTool.Tasks.Tests.ControlLevelQuestionValidation;

public sealed class ControlLevelQuestionValidationV01TaskTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-clqval-tests", Guid.NewGuid().ToString("N"));

    private const string ValidConfigJson = """
    {
      "textColumn":"C","guidanceColumn":"E","previousAnswerColumn":"F","answerColumn":"H",
      "strengthsColumn":"I","weaknessesColumn":"J","providedByColumn":"M","xrefIdColumn":"N",
      "sheetName":"CLQ","chapterRows":[1],"sectionRows":["2:3-3"],
      "allowedAnswers":["1","2","3","4","N/A"],"deviationThreshold":2
    }
    """;

    // Rows that parse to one current question with a BLANK identity key → malformed key →
    // one XrefIdEmptyOrDuplicated (Fatal) finding per workbook.
    private static IReadOnlyList<ExcelRowStructure> MalformedKeyRows() =>
    [
        new ExcelRowStructure(1, new Dictionary<string, ExcelCellStructure> { ["C"] = new("Chapter", null, null, null) }),
        new ExcelRowStructure(2, new Dictionary<string, ExcelCellStructure> { ["C"] = new("Section", null, null, null) }),
        new ExcelRowStructure(3, new Dictionary<string, ExcelCellStructure>
        {
            ["C"] = new("1.1) What is risk?", null, null, null),
            ["H"] = new("1", null, null, null)
            // no "N" cell → XrefId blank → malformed
        })
    ];

    private static ControlLevelQuestionValidationV01Task MakeTask(IExcelStructureReader reader) =>
        new(reader, NullLogger<ControlLevelQuestionValidationV01Task>.Instance);

    private static (string current, string template, string previous, string config) WriteInputs(
        string dir, string configJson, bool createCurrent = true)
    {
        var current = Path.Combine(dir, "current.xlsx");
        var template = Path.Combine(dir, "template.xlsx");
        var previous = Path.Combine(dir, "previous.xlsx");
        if (createCurrent)
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("CLQ"); wb.SaveAs(current); }
        using (var wb = new XLWorkbook()) { wb.Worksheets.Add("CLQ"); wb.SaveAs(template); }
        using (var wb = new XLWorkbook()) { wb.Worksheets.Add("CLQ"); wb.SaveAs(previous); }

        var configPath = Path.Combine(dir, "config.json");
        File.WriteAllText(configPath, configJson);
        return (current, template, previous, configPath);
    }

    private static TaskExecutionContext Ctx(
        string dir, string current, string template, string previous, string config, out string reportPath)
    {
        reportPath = Path.Combine(dir, "report.json");
        return new TaskExecutionContext(
            TaskId: "validate",
            InputPaths: new Dictionary<string, string>
            {
                ["currentResponse"] = current,
                ["emptyTemplate"] = template,
                ["previousResponse"] = previous
            },
            OutputPaths: new Dictionary<string, string> { ["report"] = reportPath },
            Logger: NullLogger.Instance,
            WorkingDirectory: dir)
        {
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["configurationFullFilename"] = config
            }
        };
    }

    // ── Success path: report written, deserializable, ≥1 finding ─────────────────

    [Fact]
    public async Task ExecuteAsync_ValidInputs_WritesDeserializableReportWithFindings()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var (c, t, p, cfg) = WriteInputs(dir, ValidConfigJson);
            var reader = Substitute.For<IExcelStructureReader>();
            reader.ReadRows(Arg.Any<string>(), "CLQ").Returns(MalformedKeyRows());

            var ctx = Ctx(dir, c, t, p, cfg, out var reportPath);

            var result = await MakeTask(reader).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            File.Exists(reportPath).Should().BeTrue();

            var report = ValidationReportSerializer.Deserialize(await File.ReadAllTextAsync(reportPath));
            report.Sheet.Should().Be("CLQ");
            report.TaskType.Should().Be("ControlLevelQuestionValidation_v01");
            report.Findings.Should().NotBeEmpty();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Fatal finding does NOT fail the task ─────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_FatalFinding_DoesNotFailTask()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var (c, t, p, cfg) = WriteInputs(dir, ValidConfigJson);
            var reader = Substitute.For<IExcelStructureReader>();
            reader.ReadRows(Arg.Any<string>(), "CLQ").Returns(MalformedKeyRows());

            var ctx = Ctx(dir, c, t, p, cfg, out var reportPath);

            var result = await MakeTask(reader).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            var report = ValidationReportSerializer.Deserialize(await File.ReadAllTextAsync(reportPath));
            report.Findings.Should().Contain(f => f.Evaluation == FindingEvaluation.Fatal);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Zero findings → empty report, still succeeds ─────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NoQuestions_WritesEmptyReport_Succeeds()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var (c, t, p, cfg) = WriteInputs(dir, ValidConfigJson);
            var reader = Substitute.For<IExcelStructureReader>();
            reader.ReadRows(Arg.Any<string>(), "CLQ").Returns([]);

            var ctx = Ctx(dir, c, t, p, cfg, out var reportPath);

            var result = await MakeTask(reader).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            var report = ValidationReportSerializer.Deserialize(await File.ReadAllTextAsync(reportPath));
            report.Findings.Should().BeEmpty();
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Info);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Missing input file → Succeeded:false ─────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingInputFile_Fails()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            // Do not create the current-response workbook.
            var (c, t, p, cfg) = WriteInputs(dir, ValidConfigJson, createCurrent: false);
            var reader = Substitute.For<IExcelStructureReader>();
            reader.ReadRows(Arg.Any<string>(), "CLQ").Returns([]);

            var ctx = Ctx(dir, c, t, p, cfg, out _);

            var result = await MakeTask(reader).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains("currentResponse"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Invalid config → Succeeded:false ─────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_InvalidConfig_Fails()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            const string badConfig = """
            {
              "textColumn":"C","guidanceColumn":"E","previousAnswerColumn":"F","answerColumn":"H",
              "strengthsColumn":"I","weaknessesColumn":"J","providedByColumn":"M","xrefIdColumn":"N",
              "sheetName":"CLQ","allowedAnswers":["1"],"deviationThreshold":0
            }
            """;
            var (c, t, p, cfg) = WriteInputs(dir, badConfig);
            var reader = Substitute.For<IExcelStructureReader>();
            reader.ReadRows(Arg.Any<string>(), "CLQ").Returns([]);

            var ctx = Ctx(dir, c, t, p, cfg, out _);

            var result = await MakeTask(reader).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains("DeviationThreshold"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Cancellation propagates ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_AlreadyCancelledToken_Throws()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var (c, t, p, cfg) = WriteInputs(dir, ValidConfigJson);
            var reader = Substitute.For<IExcelStructureReader>();
            reader.ReadRows(Arg.Any<string>(), "CLQ").Returns([]);

            var ctx = Ctx(dir, c, t, p, cfg, out _);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var act = async () => await MakeTask(reader).ExecuteAsync(ctx, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
