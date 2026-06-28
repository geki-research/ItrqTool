using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Infrastructure;
using ItrqTool.Tasks;
using ItrqTool.Tasks.ControlLevelQuestionValidationV01;
using ItrqTool.Tasks.ControlLevelQuestionValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.Validation;
using ItrqTool.Integration.Tests.ClqV01;
using ItrqTool.Integration.Tests.ClqV02;

namespace ItrqTool.Integration.Tests.WorksheetStructure;

/// <summary>
/// Proves the StructureGate fires correctly when the current workbook's header row is blank
/// (the pre-uplift state): the task still Succeeds (Mismatch is a data finding, not a failure)
/// but reports exactly one Fatal <c>structure.unexpected-worksheet-structure</c> finding.
/// Template and previous carry correct headers (default stampHeader:true), so only the current
/// Mismatches — one file, one Fatal finding.
/// </summary>
public sealed class ClqWrongVersionTests
{
    private static string TestWorkDir(string sub) =>
        Path.Combine(Path.GetTempPath(), $"ItrqTool-clq-wrong-version-{sub}", Guid.NewGuid().ToString("N"));

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Solution root (.slnx) not found above test output directory.");
    }

    [Fact]
    public async Task ClqV01Task_CurrentWithBlankHeaders_ExactlyOneStructureFatal_TaskSucceeds()
    {
        var configPath = Path.Combine(SolutionRoot(), "configs", "clq-v01-validation-config.json");
        var configJson = await File.ReadAllTextAsync(configPath);
        var config = ConfigLoader.Load<ClqV01Config>(configJson, c => c.Validate());
        var trio = ClqV01BaselineFactory.Build(config);

        var dir = TestWorkDir("v01");
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var reportPath   = Path.Combine(dir, "report.json");

            // current: stampHeader:false → blank header row → Mismatch on all non-blank schema cols
            ClqV01WorkbookWriter.Write(currentPath,  config.SheetName, trio.Current,  stampHeader: false);
            ClqV01WorkbookWriter.Write(templatePath, config.SheetName, trio.Template);
            ClqV01WorkbookWriter.Write(previousPath, config.SheetName, trio.Previous);

            var reader = new ClosedXmlExcelStructureReader(
                NullLogger<ClosedXmlExcelStructureReader>.Instance);
            var task = new ControlLevelQuestionValidationV01Task(
                reader,
                StructureGateTestSupport.Mediator(reader),
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

            result.Succeeded.Should().BeTrue("structure mismatch is a data finding, not a task failure");

            var report = ValidationReportSerializer.Deserialize(await File.ReadAllTextAsync(reportPath));
            report.Findings.Should().ContainSingle(f =>
                f.Check == ValidationCheck.Structure &&
                f.Evaluation == FindingEvaluation.Fatal &&
                f.CheckResult.StartsWith("structure.unexpected-worksheet-structure"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task ClqV02Task_CurrentWithBlankHeaders_ExactlyOneStructureFatal_TaskSucceeds()
    {
        var configPath = Path.Combine(SolutionRoot(), "configs", "clq-v02-validation-config.json");
        var configJson = await File.ReadAllTextAsync(configPath);
        var config = ConfigLoader.Load<ControlLevelQuestionValidationV02Config>(
            configJson, c => c.Validate());
        var trio = ClqV02BaselineFactory.Build(config);

        var dir = TestWorkDir("v02");
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var reportPath   = Path.Combine(dir, "report.json");

            // current: stampHeader:false → blank header row → Mismatch on all non-blank schema cols
            ClqV02WorkbookWriter.Write(currentPath,  config.SheetName, trio.Current,  stampHeader: false);
            ClqV02WorkbookWriter.Write(templatePath, config.SheetName, trio.Template);
            ClqV02WorkbookWriter.Write(previousPath, config.SheetName, trio.Previous);

            var reader = new ClosedXmlExcelStructureReader(
                NullLogger<ClosedXmlExcelStructureReader>.Instance);
            var task = new ControlLevelQuestionValidationV02Task(
                reader,
                StructureGateTestSupport.Mediator(reader),
                NullLogger<ControlLevelQuestionValidationV02Task>.Instance);

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

            result.Succeeded.Should().BeTrue("structure mismatch is a data finding, not a task failure");

            var report = ValidationReportSerializer.Deserialize(await File.ReadAllTextAsync(reportPath));
            report.Findings.Should().ContainSingle(f =>
                f.Check == ValidationCheck.Structure &&
                f.Evaluation == FindingEvaluation.Fatal &&
                f.CheckResult.StartsWith("structure.unexpected-worksheet-structure"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
