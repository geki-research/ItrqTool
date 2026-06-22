using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Infrastructure;
using ItrqTool.Tasks;
using ItrqTool.Tasks.Validation;

namespace ItrqTool.Integration.Tests.RlqV01;

/// <summary>
/// Exact-subset tests for the finding-6a cross-year numeric deviation findings emitted by
/// <c>CrossYearDeviationCell</c> for the answer column (H, role "answer", threshold 2). The
/// realigned baseline (previous H == current H = 1/2/3/4) is cross-year clean; each perturbation
/// reopens the baseline current workbook and moves one H value, leaving DV intact. Mirrors
/// <see cref="RlqV01DvConformancePerturbationTests"/>: filter to the <c>Deviation</c> subset and
/// assert by Check + CellAddresses, never a total count.
/// </summary>
public sealed class RlqV01CrossYearDeviationPerturbationTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-rlqv01-deviation", Guid.NewGuid().ToString("N"));

    private static RiskLevelQuestionValidationV01Task BuildTask() =>
        new(new ClosedXmlExcelStructureReader(NullLogger<ClosedXmlExcelStructureReader>.Instance),
            NullLogger<RiskLevelQuestionValidationV01Task>.Instance);

    private static TaskExecutionContext BuildContext(
        string currentPath, string templatePath, string previousPath,
        string configPath, string reportPath, string dir) =>
        new(
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
            },
        };

    private static void WriteBaselineTrio(
        string currentPath, string templatePath, string previousPath)
    {
        RlqV01BaselineFactory.WriteCurrent(currentPath);
        RlqV01BaselineFactory.WriteTemplate(templatePath);
        RlqV01BaselineFactory.WritePrevious(previousPath);
    }

    [Fact]
    public async Task CleanRealignedBaseline_EmitsNoDeviationFindings()
    {
        // Realigned baseline: current H {1,2,3,4} == previous H {1,2,3,4} → every Agree row has
        // delta 0 < threshold 2 → no Deviation finding.
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            WriteBaselineTrio(currentPath, templatePath, previousPath);
            await File.WriteAllTextAsync(configPath, RlqV01BaselineFactory.SyntheticConfigJson);

            var result = await BuildTask().ExecuteAsync(
                BuildContext(currentPath, templatePath, previousPath, configPath, reportPath, dir),
                CancellationToken.None);

            result.Succeeded.Should().BeTrue(
                "task must succeed; errors: {0}",
                string.Join("; ", result.Messages.Select(m => m.Text)));

            var report = ValidationReportSerializer.Deserialize(
                await File.ReadAllTextAsync(reportPath));

            report.Findings.Should().NotContain(
                f => f.Check == ValidationCheck.Deviation,
                "the realigned clean baseline has zero cross-year answer deviation");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task HMovedPastThreshold_EmitsOneDeviationAtAnchor_Warning()
    {
        // Current x1 (row 6) H: 1 → 5; previous H6 = 1 (realigned). delta |5-1| = 4 >= 2 → one
        // Deviation finding at H6. All other rows unchanged (delta 0). Answer DV WholeNumber >= 0:
        // 5 conforms, so no InputConformance noise.
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            WriteBaselineTrio(currentPath, templatePath, previousPath);
            await File.WriteAllTextAsync(configPath, RlqV01BaselineFactory.SyntheticConfigJson);

            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First().Cell(6, "H").Value = 5;
                wb.Save();
            }

            var result = await BuildTask().ExecuteAsync(
                BuildContext(currentPath, templatePath, previousPath, configPath, reportPath, dir),
                CancellationToken.None);

            result.Succeeded.Should().BeTrue(
                "task must succeed; errors: {0}",
                string.Join("; ", result.Messages.Select(m => m.Text)));

            var report = ValidationReportSerializer.Deserialize(
                await File.ReadAllTextAsync(reportPath));

            (report.Halted ?? false).Should().BeFalse("keys are clean — gate does not fire");

            var deviationFindings = report.Findings
                .Where(f => f.Check == ValidationCheck.Deviation)
                .ToList();

            deviationFindings.Should().ContainSingle(
                "exactly one Deviation finding expected (H6 moved 1 -> 5, delta 4 >= 2); actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            var finding = deviationFindings[0];
            finding.Evaluation.Should().Be(FindingEvaluation.Warning,
                "the default evaluation for CrossYearDeviationCell is Warning");
            finding.CellAddresses.Should().Be("H6",
                "the perturbation is at the x1 anchor row 6, column H");
            finding.CheckResult.Should().Contain("H6",
                "the message names the deviating answer cell");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task HMovedBelowThreshold_EmitsNoDeviation()
    {
        // Current x1 (row 6) H: 1 → 2; previous H6 = 1. delta |2-1| = 1 < 2 → no Deviation finding.
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            WriteBaselineTrio(currentPath, templatePath, previousPath);
            await File.WriteAllTextAsync(configPath, RlqV01BaselineFactory.SyntheticConfigJson);

            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First().Cell(6, "H").Value = 2;
                wb.Save();
            }

            var result = await BuildTask().ExecuteAsync(
                BuildContext(currentPath, templatePath, previousPath, configPath, reportPath, dir),
                CancellationToken.None);

            result.Succeeded.Should().BeTrue(
                "task must succeed; errors: {0}",
                string.Join("; ", result.Messages.Select(m => m.Text)));

            var report = ValidationReportSerializer.Deserialize(
                await File.ReadAllTextAsync(reportPath));

            report.Findings.Should().NotContain(
                f => f.Check == ValidationCheck.Deviation,
                "a sub-threshold change (delta 1 < 2) must not produce a Deviation finding");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
