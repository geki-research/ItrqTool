using ClosedXML.Excel; // test fixture creation only
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Tasks;
using ItrqTool.Tasks.WorksheetStructure;

namespace ItrqTool.Tasks.Tests.ControlLevelQuestionValidationV01;

/// <summary>
/// Verifies that an unexpected exception in the outer catch block is forwarded to
/// ctx.Logger.LogError (representative for the whole inject+validation family).
/// </summary>
public sealed class ControlLevelQuestionValidationV01Task_LoggingTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-clqv01-logging-tests", Guid.NewGuid().ToString("N"));

    private const string ValidConfigJson = """
    {
      "textColumn":"C","guidanceColumn":"E","previousAnswerColumn":"F","previousExplanationColumn":"G","answerColumn":"H",
      "strengthsColumn":"I","weaknessesColumn":"J","providedByColumn":"M","xrefIdColumn":"N",
      "sheetName":"CLQ","chapterRows":["1"],"sectionRows":["2:3-3"],
      "allowedAnswers":["1","2","3","4","N/A"],"deviationThreshold":2
    }
    """;

    private static (string current, string template, string previous, string config) WriteInputs(string dir)
    {
        var current  = Path.Combine(dir, "current.xlsx");
        var template = Path.Combine(dir, "template.xlsx");
        var previous = Path.Combine(dir, "previous.xlsx");
        foreach (var p in new[] { current, template, previous })
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("CLQ"); wb.SaveAs(p); }
        var configPath = Path.Combine(dir, "config.json");
        File.WriteAllText(configPath, ValidConfigJson);
        return (current, template, previous, configPath);
    }

    // ── Unexpected exception is forwarded to ctx.Logger at Error level ───────────

    [Fact]
    public async Task ExecuteAsync_MediatorThrows_LogsErrorAndFails()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var (c, t, p, cfg) = WriteInputs(dir);

            var mediator = Substitute.For<IWorksheetStructureMediator>();
            mediator.Verify(Arg.Any<string>(), Arg.Any<WorksheetSchemaRef>())
                    .Returns(_ => throw new ArgumentException(
                        "Worksheet 'CLQ' was not found in 'previous.xlsx'. Available sheets: 'OtherSheet'."));

            var task = new ControlLevelQuestionValidationV01Task(
                Substitute.For<IExcelStructureReader>(),
                mediator,
                NullLogger<ControlLevelQuestionValidationV01Task>.Instance);

            var ctxLogger = Substitute.For<ILogger>();
            var reportPath = Path.Combine(dir, "report.json");
            var ctx = new TaskExecutionContext(
                TaskId: "validate",
                InputPaths: new Dictionary<string, string>
                {
                    ["currentResponse"] = c, ["emptyTemplate"] = t, ["previousResponse"] = p
                },
                OutputPaths: new Dictionary<string, string> { ["report"] = reportPath },
                Logger: ctxLogger,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["configurationFullFilename"] = cfg
                }
            };

            var result = await task.ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            ctxLogger.ReceivedCalls()
                     .Should().Contain(call =>
                         call.GetMethodInfo().Name == "Log" &&
                         (LogLevel)call.GetArguments()[0]! == LogLevel.Error);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
