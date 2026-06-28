using ClosedXML.Excel; // test fixture creation only
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Tasks;
using ItrqTool.Tasks.WorksheetStructure;

namespace ItrqTool.Tasks.Tests.ControlLevelQuestionValidationV01;

/// <summary>
/// Proves the CLQ-v01 task maps a mediator AssetError (missing schema asset) to
/// <c>Succeeded:false</c> with an Error message containing the reason — before the
/// pipeline even runs. The substitute reader satisfies ctor wiring but is never invoked
/// because the schema load fails first.
/// </summary>
public sealed class ClqAssetErrorTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-clq-asset-error", Guid.NewGuid().ToString("N"));

    private const string ValidConfigJson = """
    {
      "textColumn":"C","guidanceColumn":"E","previousAnswerColumn":"F","previousExplanationColumn":"G","answerColumn":"H",
      "strengthsColumn":"I","weaknessesColumn":"J","providedByColumn":"M","xrefIdColumn":"N",
      "sheetName":"CLQ","chapterRows":["1"],"sectionRows":["2:3-3"],
      "allowedAnswers":["1","2","3","4","N/A"],"deviationThreshold":2
    }
    """;

    [Fact]
    public async Task ClqV01Task_MissingSchemaAsset_Fails_WithAssetErrorMessage()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("CLQ"); wb.SaveAs(currentPath); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("CLQ"); wb.SaveAs(templatePath); }
            using (var wb = new XLWorkbook()) { wb.Worksheets.Add("CLQ"); wb.SaveAs(previousPath); }
            File.WriteAllText(configPath, ValidConfigJson);

            // Substitute reader — satisfies ctor but ReadCells is never called because
            // the schema load fails before the gate reaches mediator.ReadCells.
            var reader = Substitute.For<IExcelStructureReader>();

            // Real mediator with an empty temp dir as schemasBaseDir — no schemas/ folder
            // exists there, so the loader throws WorksheetSchemaLoadException → AssetError.
            var emptyBaseDir = Path.Combine(dir, "empty-base");
            Directory.CreateDirectory(emptyBaseDir);
            var mediator = new WorksheetStructureMediator(
                reader,
                new WorksheetStructureSchemaLoader(),
                [new SchemaVerificationStrategyV1()],
                emptyBaseDir);

            var task = new ControlLevelQuestionValidationV01Task(
                reader,
                mediator,
                NullLogger<ControlLevelQuestionValidationV01Task>.Instance);

            var ctx = new TaskExecutionContext(
                TaskId: "validate",
                InputPaths: new Dictionary<string, string>
                {
                    ["currentResponse"]  = currentPath,
                    ["emptyTemplate"]    = templatePath,
                    ["previousResponse"] = previousPath,
                },
                OutputPaths: new Dictionary<string, string> { ["report"] = reportPath },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["configurationFullFilename"] = configPath,
                }
            };

            var result = await task.ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error &&
                m.Text.Contains("schema asset error"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
