using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Infrastructure;
using ItrqTool.Tasks;
using ItrqTool.Tasks.ControlLevelQuestionValidation;
using ItrqTool.Tasks.Validation;

namespace ItrqTool.Integration.Tests.ClqV01;

/// <summary>
/// Exact-set tests for the CLQ_v01 trial scenario.
/// 4c-1: ten local perturbations → 11 findings.
/// 4c-2: six structural/cross-year rows added → 23 findings total.
/// 4c-3: distinct per-row baseline text; DUMMY tie-break crutches removed.
/// </summary>
public sealed class ClqV01TrialTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-clqv01-trial", Guid.NewGuid().ToString("N"));

    private static string FindConfigAssetPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Solution root (.slnx) not found above test output directory.");
        return Path.Combine(dir.FullName, "configs", "clq-v01-validation-config.json");
    }

    [Fact]
    public async Task TenLocalPerturbations_ExactlyElevenFindings_NoExtras()
    {
        var configPath = FindConfigAssetPath();
        var configJson = await File.ReadAllTextAsync(configPath);
        var config = ControlLevelQuestionValidationV01ConfigLoader.Load(configJson);

        var baseline = ClqV01BaselineFactory.Build(config);
        var scenario = ClqV01TrialScenario.Build(baseline);

        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var reportPath   = Path.Combine(dir, "report.json");

            ClqV01WorkbookWriter.Write(currentPath,  config.SheetName, scenario.Trio.Current,
                scenario.CurrentAnswerDvOverrides);
            ClqV01WorkbookWriter.Write(templatePath, config.SheetName, scenario.Trio.Template);
            ClqV01WorkbookWriter.Write(previousPath, config.SheetName, scenario.Trio.Previous);

            var task = new ControlLevelQuestionValidationV01Task(
                new ClosedXmlExcelStructureReader(
                    NullLogger<ClosedXmlExcelStructureReader>.Instance),
                NullLogger<ControlLevelQuestionValidationV01Task>.Instance);

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

            // Exact count (10 rows, R30 contributes 2 → 11 total).
            report.Findings.Should().HaveCount(11,
                "exactly 11 findings expected (10 rows, R30 contributes 2); actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            // Every expected finding is present.
            foreach (var expected in scenario.ExpectedFindings)
            {
                report.Findings.Should().Contain(f =>
                    f.Check == expected.Check &&
                    f.Evaluation == expected.Evaluation &&
                    f.CellAddresses == expected.CellAddresses &&
                    f.CheckResult.Contains(expected.CheckResultSubstring, StringComparison.Ordinal),
                    because:
                        $"expected [{expected.Evaluation}] {expected.Check} @ {expected.CellAddresses} " +
                        $"with CheckResult containing '{expected.CheckResultSubstring}'");
            }

            // No unexpected findings (the exact-set contract).
            var unexpected = report.Findings
                .Where(f => !scenario.ExpectedFindings.Any(ex =>
                    ex.Check == f.Check &&
                    ex.Evaluation == f.Evaluation &&
                    ex.CellAddresses == f.CellAddresses &&
                    f.CheckResult.Contains(ex.CheckResultSubstring, StringComparison.Ordinal)))
                .ToList();

            unexpected.Should().BeEmpty(
                "no unexpected findings allowed; found: {0}",
                string.Join("; ", unexpected.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── 4c-2: ten local + six structural rows → 23 findings ──────────────────────

    [Fact]
    public async Task FullScenario_TenLocalPlusSixStructuralRows_ExactFindings_NoExtras()
    {
        var configPath = FindConfigAssetPath();
        var configJson = await File.ReadAllTextAsync(configPath);
        var config = ControlLevelQuestionValidationV01ConfigLoader.Load(configJson);

        var baseline = ClqV01BaselineFactory.Build(config);
        var structural = ClqV01TrialScenario.BuildStructuralPerturbations(baseline);
        var scenario = ClqV01TrialScenario.Build(baseline, structural);

        // Sanity-check the scenario builder produced the expected total.
        scenario.ExpectedFindings.Should().HaveCount(23,
            "10 local (11 findings) + 5 structural rows (12 findings) = 23 total; " +
            "BuildStructuralPerturbations returned {0} findings",
            structural.Sum(p => p.ExpectedFindings.Count));

        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var reportPath   = Path.Combine(dir, "report.json");

            ClqV01WorkbookWriter.Write(currentPath,  config.SheetName, scenario.Trio.Current,
                scenario.CurrentAnswerDvOverrides);
            ClqV01WorkbookWriter.Write(templatePath, config.SheetName, scenario.Trio.Template);
            ClqV01WorkbookWriter.Write(previousPath, config.SheetName, scenario.Trio.Previous);

            var task = new ControlLevelQuestionValidationV01Task(
                new ClosedXmlExcelStructureReader(
                    NullLogger<ClosedXmlExcelStructureReader>.Instance),
                NullLogger<ControlLevelQuestionValidationV01Task>.Instance);

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

            static string Fmt(ValidationFinding f) =>
                $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}";

            // Exact count: 23 (11 local + 12 structural).
            report.Findings.Should().HaveCount(scenario.ExpectedFindings.Count,
                "exactly {0} findings expected; actual:\n{1}",
                scenario.ExpectedFindings.Count,
                string.Join("\n", report.Findings.Select(Fmt)));

            // Every expected finding is present.
            foreach (var expected in scenario.ExpectedFindings)
            {
                report.Findings.Should().Contain(f =>
                    f.Check == expected.Check &&
                    f.Evaluation == expected.Evaluation &&
                    f.CellAddresses == expected.CellAddresses &&
                    f.CheckResult.Contains(expected.CheckResultSubstring, StringComparison.Ordinal),
                    because:
                        $"expected [{expected.Evaluation}] {expected.Check} @ {expected.CellAddresses} " +
                        $"with CheckResult containing '{expected.CheckResultSubstring}'");
            }

            // No unexpected findings (the exact-set contract).
            var unexpected = report.Findings
                .Where(f => !scenario.ExpectedFindings.Any(ex =>
                    ex.Check == f.Check &&
                    ex.Evaluation == f.Evaluation &&
                    ex.CellAddresses == f.CellAddresses &&
                    f.CheckResult.Contains(ex.CheckResultSubstring, StringComparison.Ordinal)))
                .ToList();

            unexpected.Should().BeEmpty(
                "no unexpected findings allowed; found:\n{0}",
                string.Join("\n", unexpected.Select(Fmt)));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

}
