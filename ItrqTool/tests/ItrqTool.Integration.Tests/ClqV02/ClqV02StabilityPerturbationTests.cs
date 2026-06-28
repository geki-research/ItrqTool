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

public sealed record ClqV02ExpectedFinding(
    ValidationCheck Check,
    FindingEvaluation Evaluation,
    string CellAddresses,
    string CheckResultSubstring);

/// <summary>
/// Exact-set test for the three new v02 answer-stability findings:
/// missing (Error), not-in-allowed-set (Fatal), validation-rule-changed (Error).
/// </summary>
public sealed class ClqV02StabilityPerturbationTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-clqv02-stability", Guid.NewGuid().ToString("N"));

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
    public async Task ThreeStabilityPerturbations_ExactlyThreeFindings_NoExtras()
    {
        var configPath = FindConfigAssetPath();
        var configJson = await File.ReadAllTextAsync(configPath);
        var config = ConfigLoader.Load<ControlLevelQuestionValidationV02Config>(
            configJson, c => c.Validate());

        var trio = ClqV02BaselineFactory.Build(config);

        // Three single-row anomalies on current rows 7/8/9 (all question rows in section 6–13).
        // Row 9's spec is left valid ("Yes"); only its current K-DV is narrowed via the override.
        var perturbedQuestions = trio.Current.Questions
            .Select(q => q.RowNumber switch
            {
                7 => q with { AnswerStability = null },       // → input-cell.answer-stability.missing  (Error)  @ K7
                8 => q with { AnswerStability = "Maybe" },    // → input-cell.answer-stability.not-in-allowed-set (Fatal) @ K8
                _ => q
            })
            .ToList();
        var perturbedCurrent = trio.Current with { Questions = perturbedQuestions };

        // Row 9: narrow the current K-DV to Yes-only vs the template's Yes,No → rule-changed.
        var stabilityDvOverrides = new Dictionary<int, string> { [9] = "\"Yes\"" };

        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var reportPath   = Path.Combine(dir, "report.json");

            ClqV02WorkbookWriter.Write(currentPath,  config.SheetName, perturbedCurrent,
                stabilityDvOverrides: stabilityDvOverrides);              // override → CURRENT only
            ClqV02WorkbookWriter.Write(templatePath, config.SheetName, trio.Template);   // defaults
            ClqV02WorkbookWriter.Write(previousPath, config.SheetName, trio.Previous);   // defaults

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
                "task must succeed; errors: {0}",
                string.Join("; ", result.Messages.Select(m => m.Text)));

            var report = ValidationReportSerializer.Deserialize(
                await File.ReadAllTextAsync(reportPath));

            var expected = new[]
            {
                new ClqV02ExpectedFinding(ValidationCheck.MissingResponse, FindingEvaluation.Error, "K7",
                    "is empty; the value was not provided"),
                new ClqV02ExpectedFinding(ValidationCheck.MissingResponse, FindingEvaluation.Fatal, "K8",
                    "is not in the allowed set"),
                new ClqV02ExpectedFinding(ValidationCheck.FrozenConstraint, FindingEvaluation.Error, "K9",
                    "differs from the template"),
            };

            report.Findings.Should().HaveCount(3,
                "exactly three stability findings expected; actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            foreach (var ex in expected)
                report.Findings.Should().Contain(f =>
                    f.Check == ex.Check &&
                    f.Evaluation == ex.Evaluation &&
                    f.CellAddresses == ex.CellAddresses &&
                    f.CheckResult.Contains(ex.CheckResultSubstring, StringComparison.Ordinal),
                    because: $"expected [{ex.Evaluation}] {ex.Check} @ {ex.CellAddresses} containing '{ex.CheckResultSubstring}'");

            var unexpected = report.Findings
                .Where(f => !expected.Any(ex =>
                    ex.Check == f.Check && ex.Evaluation == f.Evaluation &&
                    ex.CellAddresses == f.CellAddresses &&
                    f.CheckResult.Contains(ex.CheckResultSubstring, StringComparison.Ordinal)))
                .ToList();
            unexpected.Should().BeEmpty("no unexpected findings allowed; found: {0}",
                string.Join("; ", unexpected.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
