using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Infrastructure;
using ItrqTool.Tasks;
using ItrqTool.Tasks.ControlLevelQuestionValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.Validation;
using ItrqTool.Integration.Tests.WorksheetStructure;

namespace ItrqTool.Integration.Tests.ClqV02;

/// <summary>
/// Proves that the baseline trio produced by <see cref="ClqV02BaselineFactory"/> yields
/// zero findings when run through <see cref="ControlLevelQuestionValidationV02Task"/> with
/// the production config. This is the correctness anchor for the generator; if it fails,
/// the baseline logic has a consistency bug.
/// </summary>
public sealed class ClqV02BaselineTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-clqv02-baseline", Guid.NewGuid().ToString("N"));

    private static string FindConfigAssetPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Solution root (.slnx) not found above test output directory.");
        return Path.Combine(dir.FullName, "configs", "clq-v02-validation-config.json");
    }

    [Fact]
    public async Task BaselineTrioWithProductionConfig_ZeroFindings()
    {
        var configPath = FindConfigAssetPath();
        var configJson = await File.ReadAllTextAsync(configPath);
        var config = ConfigLoader.Load<ControlLevelQuestionValidationV02Config>(
            configJson, c => c.Validate());

        var trio = ClqV02BaselineFactory.Build(config);

        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var reportPath   = Path.Combine(dir, "report.json");

            ClqV02WorkbookWriter.Write(currentPath,  config.SheetName, trio.Current);
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
                    ["previousResponse"] = previousPath
                },
                OutputPaths: new Dictionary<string, string> { ["report"] = reportPath },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["configurationFullFilename"] = configPath
                }
            };

            var result = await task.ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue(
                "baseline task should succeed; errors: {0}",
                string.Join("; ", result.Messages.Select(m => m.Text)));

            var report = ValidationReportSerializer.Deserialize(
                await File.ReadAllTextAsync(reportPath));

            report.Findings.Should().BeEmpty(
                "baseline trio is fully consistent; no findings expected; got: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
