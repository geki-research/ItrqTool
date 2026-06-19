using ClosedXML.Excel; // test fixture creation only
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks;
using ItrqTool.Tasks.Validation;

namespace ItrqTool.Tasks.Tests.ControlLevelQuestionValidationV02;

public sealed class ControlLevelQuestionValidationV02TaskTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-clqv02-tests", Guid.NewGuid().ToString("N"));

    private const string ValidConfigJson = """
    {
      "textColumn":"D","guidanceColumn":"E","previousAnswerColumn":"F","previousExplanationColumn":"G","answerColumn":"H",
      "strengthsColumn":"I","weaknessesColumn":"J",
      "answerStabilityColumn":"K","providedByColumn":"N","xrefIdColumn":"O",
      "sheetName":"CLQ","chapterRows":["1"],"sectionRows":["2:3-4"],
      "allowedAnswers":["1","2","3","4","N/A"],
      "allowedStabilityAnswers":["Yes","No"],
      "deviationThreshold":2
    }
    """;

    // Two questions with valid answers and stability values. Using the same rows for
    // all three workbooks keeps within-year structure and cross-year alignment clean
    // so no config-level failure can block the task.
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
            ["D"] = new("1.1) Question one",   null, null, null),
            ["H"] = new("1",                   null, null, null),
            ["I"] = new("Strengths text",      null, null, null),
            ["K"] = new("Yes",                 null, null, null),
            ["N"] = new("OrgUnit",             null, null, null),
            ["O"] = new("XREF-001",            null, null, null),
        }),
        new ExcelRowStructure(4, new Dictionary<string, ExcelCellStructure>
        {
            ["D"] = new("1.2) Question two",   null, null, null),
            ["H"] = new("3",                   null, null, null),
            ["I"] = new("Strengths2",          null, null, null),
            ["J"] = new("Weaknesses2",         null, null, null),
            ["K"] = new("No",                  null, null, null),
            ["N"] = new("OrgUnit",             null, null, null),
            ["O"] = new("XREF-002",            null, null, null),
        }),
    ];

    // ReadCells stub: returns DV cells for H3:H4 and K3:K4 ranges.
    // DvPatcher looks up by address; extra entries are ignored.
    private static IReadOnlyDictionary<string, ExcelCellStructure> DvCells() =>
        new Dictionary<string, ExcelCellStructure>
        {
            ["H3"] = new(null, "List", "\"1,2,3,4,N/A\"", null),
            ["H4"] = new(null, "List", "\"1,2,3,4,N/A\"", null),
            ["K3"] = new(null, "List", "\"Yes,No\"",      null),
            ["K4"] = new(null, "List", "\"Yes,No\"",      null),
        };

    private static ControlLevelQuestionValidationV02Task MakeTask(IExcelStructureReader reader) =>
        new(reader, NullLogger<ControlLevelQuestionValidationV02Task>.Instance);

    private static (string current, string template, string previous, string config) WriteInputs(
        string dir, string configJson, bool createCurrent = true)
    {
        var current  = Path.Combine(dir, "current.xlsx");
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
        string dir, string current, string template, string previous, string config,
        out string reportPath)
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

    // ── TaskType ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TaskType_IsCorrectString()
    {
        var reader = Substitute.For<IExcelStructureReader>();
        MakeTask(reader).TaskType.Should().Be("ControlLevelQuestionValidation_v02");
    }

    // ── Success smoke ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ValidInputs_WritesDeserializableReport_Succeeds()
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
            report.TaskType.Should().Be("ControlLevelQuestionValidation_v02");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Failure path: missing input key ──────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingInputKey_Fails_WithErrorMessage()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var (_, t, p, cfg) = WriteInputs(dir, ValidConfigJson, createCurrent: false);
            var reader = Substitute.For<IExcelStructureReader>();

            var reportPath = Path.Combine(dir, "report.json");
            // Omit currentResponse from InputPaths → TryGetInput returns false.
            var ctx = new TaskExecutionContext(
                TaskId: "validate",
                InputPaths: new Dictionary<string, string>
                {
                    ["emptyTemplate"]    = t,
                    ["previousResponse"] = p,
                },
                OutputPaths: new Dictionary<string, string> { ["report"] = reportPath },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["configurationFullFilename"] = cfg,
                }
            };

            var result = await MakeTask(reader).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains("currentResponse"));
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

            var ctx = Ctx(dir, c, t, p, cfg, out _);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var act = async () => await MakeTask(reader).ExecuteAsync(ctx, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
