using ClosedXML.Excel; // test fixture creation only
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Tasks;
using ItrqTool.Tasks.Validation;

namespace ItrqTool.Tasks.Tests.ControlLevelQuestionValidationV01Core;

/// <summary>
/// Minimal smoke for the v01-on-core task (m3): TaskType string, success path writes a
/// deserializable report carrying the CANONICAL taskType, and cancellation propagates.
/// The full bespoke task-test suite is retargeted at m4; this is not a duplicate of it.
/// </summary>
public sealed class ControlLevelQuestionValidationV01CoreTaskTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-clqv01-core-tests", Guid.NewGuid().ToString("N"));

    // v01 column map: D/E/F/H/I/J/M/N — no K, no answer-stability fields.
    private const string ValidConfigJson = """
    {
      "textColumn":"D","guidanceColumn":"E","previousAnswerColumn":"F","answerColumn":"H",
      "strengthsColumn":"I","weaknessesColumn":"J","providedByColumn":"M","xrefIdColumn":"N",
      "sheetName":"CLQ","chapterRows":["1"],"sectionRows":["2:3-4"],
      "allowedAnswers":["1","2","3","4"],
      "deviationThreshold":2
    }
    """;

    private static IReadOnlyList<ExcelRowStructure> ValidQuestionRows() =>
    [
        new ExcelRowStructure(1, new Dictionary<string, ExcelCellStructure>
        {
            ["D"] = new("Chapter 1", null, null, null),
        }),
        new ExcelRowStructure(2, new Dictionary<string, ExcelCellStructure>
        {
            ["D"] = new("Section 1", null, null, null),
        }),
        new ExcelRowStructure(3, new Dictionary<string, ExcelCellStructure>
        {
            ["D"] = new("1.1) Question one", null, null, null),
            ["H"] = new("1",                 null, null, null),
            ["I"] = new("Strengths text",    null, null, null),
            ["M"] = new("OrgUnit",           null, null, null),
            ["N"] = new("XREF-001",          null, null, null),
        }),
        new ExcelRowStructure(4, new Dictionary<string, ExcelCellStructure>
        {
            ["D"] = new("1.2) Question two", null, null, null),
            ["H"] = new("3",                 null, null, null),
            ["I"] = new("Strengths2",        null, null, null),
            ["J"] = new("Weaknesses2",       null, null, null),
            ["M"] = new("OrgUnit",           null, null, null),
            ["N"] = new("XREF-002",          null, null, null),
        }),
    ];

    private static IReadOnlyDictionary<string, ExcelCellStructure> DvCells() =>
        new Dictionary<string, ExcelCellStructure>
        {
            ["H3"] = new(null, "List", "\"1,2,3,4\"", null),
            ["H4"] = new(null, "List", "\"1,2,3,4\"", null),
        };

    private static ControlLevelQuestionValidationV01CoreTask MakeTask(IExcelStructureReader reader) =>
        new(reader, NullLogger<ControlLevelQuestionValidationV01CoreTask>.Instance);

    private static (string current, string template, string previous, string config) WriteInputs(
        string dir, string configJson)
    {
        var current  = Path.Combine(dir, "current.xlsx");
        var template = Path.Combine(dir, "template.xlsx");
        var previous = Path.Combine(dir, "previous.xlsx");
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
                ["currentResponse"]  = current,
                ["emptyTemplate"]    = template,
                ["previousResponse"] = previous,
            },
            OutputPaths: new Dictionary<string, string> { ["report"] = reportPath },
            Logger: NullLogger.Instance,
            WorkingDirectory: dir)
        {
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["configurationFullFilename"] = config,
            }
        };
    }

    [Fact]
    public void TaskType_IsTemporaryCoreString_AndCanonicalConstIsV01()
    {
        var reader = Substitute.For<IExcelStructureReader>();
        MakeTask(reader).TaskType.Should().Be("ControlLevelQuestionValidation_v01_core");
        ControlLevelQuestionValidationV01CoreTask.CanonicalTaskType
            .Should().Be("ControlLevelQuestionValidation_v01");
    }

    [Fact]
    public async Task ExecuteAsync_ValidInputs_WritesReport_WithCanonicalTaskType_Succeeds()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var (c, t, p, cfg) = WriteInputs(dir, ValidConfigJson);
            var reader = Substitute.For<IExcelStructureReader>();
            reader.ReadRows(Arg.Any<string>(), "CLQ").Returns(ValidQuestionRows());
            reader.ReadCells(Arg.Any<string>(), "CLQ", Arg.Any<IReadOnlyList<string>>())
                  .Returns(DvCells());

            var ctx = Ctx(dir, c, t, p, cfg, out var reportPath);

            var result = await MakeTask(reader).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            File.Exists(reportPath).Should().BeTrue();

            var report = ValidationReportSerializer.Deserialize(await File.ReadAllTextAsync(reportPath));
            report.Sheet.Should().Be("CLQ");
            report.TaskType.Should().Be("ControlLevelQuestionValidation_v01");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyCancelledToken_Throws()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var (c, t, p, cfg) = WriteInputs(dir, ValidConfigJson);
            var reader = Substitute.For<IExcelStructureReader>();

            var ctx = Ctx(dir, c, t, p, cfg, out _);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var act = async () => await MakeTask(reader).ExecuteAsync(ctx, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
