using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Infrastructure;
using ItrqTool.Tasks;
using ItrqTool.Tasks.Validation;
using ItrqTool.Integration.Tests.RlqV01;
using ItrqTool.Integration.Tests.RlqV02;

namespace ItrqTool.Integration.Tests.WorksheetStructure;

/// <summary>
/// Proves the StructureGate fires correctly through the <c>RunFromParsedGated</c> path when
/// the current workbook's header row is blank (the pre-uplift state): the task still Succeeds
/// (Mismatch is a data finding, not a failure), the gate finding folds into the report, and
/// the Halted argument is passed through unchanged.
/// Template and previous carry correct headers (default stampHeader:true), so only the current
/// Mismatches — one file, one Fatal finding.
/// </summary>
public sealed class RlqWrongVersionTests
{
    private static string TestWorkDir(string sub) =>
        Path.Combine(Path.GetTempPath(), $"ItrqTool-rlq-wrong-version-{sub}", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RlqV01Task_CurrentWithBlankHeaders_ExactlyOneStructureFatal_TaskSucceeds()
    {
        var dir = TestWorkDir("v01");
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            // current: stampHeader:false → blank header row → Mismatch on all non-blank schema cols
            RlqV01BaselineFactory.WriteCurrent(currentPath,  stampHeader: false);
            RlqV01BaselineFactory.WriteTemplate(templatePath);
            RlqV01BaselineFactory.WritePrevious(previousPath);
            await File.WriteAllTextAsync(configPath, RlqV01BaselineFactory.SyntheticConfigJson);

            var reader = new ClosedXmlExcelStructureReader(
                NullLogger<ClosedXmlExcelStructureReader>.Instance);
            var task = new RiskLevelQuestionValidationV01Task(
                reader,
                StructureGateTestSupport.Mediator(reader),
                NullLogger<RiskLevelQuestionValidationV01Task>.Instance);

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
    public async Task RlqV02Task_CurrentWithBlankHeaders_ExactlyOneStructureFatal_TaskSucceeds()
    {
        var dir = TestWorkDir("v02");
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            // current: stampHeader:false → blank header row → Mismatch on all non-blank schema cols
            RlqV02BaselineFactory.WriteCurrent(currentPath,  stampHeader: false);
            RlqV02BaselineFactory.WriteTemplate(templatePath);
            RlqV02BaselineFactory.WritePrevious(previousPath);
            await File.WriteAllTextAsync(configPath, RlqV02BaselineFactory.SyntheticConfigJson);

            var reader = new ClosedXmlExcelStructureReader(
                NullLogger<ClosedXmlExcelStructureReader>.Instance);
            var task = new RiskLevelQuestionValidationV02Task(
                reader,
                StructureGateTestSupport.Mediator(reader),
                NullLogger<RiskLevelQuestionValidationV02Task>.Instance);

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
