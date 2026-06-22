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
/// Exact-set tests for the chunk-2 finding-5 DV-conformance findings emitted by
/// <c>DvConformanceCell</c> for the answer column (H, role "answer") and the
/// material-change column (L, role "material-change"). Each perturbation test reopens
/// the baseline current workbook and mutates one cell value, leaving the baseline DV
/// intact — the evaluator is tested against the TEMPLATE DV, not a rewritten rule.
/// </summary>
public sealed class RlqV01DvConformancePerturbationTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-rlqv01-dv-conformance", Guid.NewGuid().ToString("N"));

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

    [Fact]
    public async Task HViolatingWholeNumberDv_EmitsAnswerNotConformant()
    {
        // Template H-DV: WholeNumber EqualOrGreaterThan 0 on all anchor rows (baseline ApplyAnswerDv).
        // Current: baseline WriteCurrent (H6=1, H7=2, H8=3, H13=4), then H6 set to -1.
        // Evaluator: WholeNumber + EqualOrGreaterThan 0 → -1 >= 0 false → NotConformant at H6.
        // All other H values conform; L values ("No") are in {Yes,No}.

        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            RlqV01BaselineFactory.WriteCurrent(currentPath);
            RlqV01BaselineFactory.WriteTemplate(templatePath);
            RlqV01BaselineFactory.WritePrevious(previousPath);
            await File.WriteAllTextAsync(configPath, RlqV01BaselineFactory.SyntheticConfigJson);

            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First().Cell(6, "H").Value = -1;
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

            var conformanceFindings = report.Findings
                .Where(f => f.Check == ValidationCheck.InputConformance)
                .ToList();

            conformanceFindings.Should().ContainSingle(
                "exactly one InputConformance finding expected (H6 violates WholeNumber >= 0); actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            var finding = conformanceFindings[0];
            finding.Evaluation.Should().Be(FindingEvaluation.Error,
                "the default evaluation for DvConformanceCell is Error");
            finding.CellAddresses.Should().Be("H6",
                "the perturbation is at the x1 anchor row 6, column H");
            finding.CheckResult.Should().Contain("H6",
                "the message names the mutated cell address");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task LOutsideAllowedList_EmitsMaterialChangeNotConformant()
    {
        // Template L-DV: List "Yes,No" (inline) on all anchor rows (baseline ApplyMaterialChangeDv).
        // Current: baseline WriteCurrent (L7="No"), then L7 set to "Maybe".
        // Evaluator: List branch, resolvedListValues=["Yes","No"] from template stamp → "Maybe" not a member
        // → NotConformant at L7. Row 7 = x2, L is not merged there.
        // All other L values ("No") are in {Yes,No}; all H values conform.

        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            RlqV01BaselineFactory.WriteCurrent(currentPath);
            RlqV01BaselineFactory.WriteTemplate(templatePath);
            RlqV01BaselineFactory.WritePrevious(previousPath);
            await File.WriteAllTextAsync(configPath, RlqV01BaselineFactory.SyntheticConfigJson);

            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First().Cell(7, "L").Value = "Maybe";
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

            var conformanceFindings = report.Findings
                .Where(f => f.Check == ValidationCheck.InputConformance)
                .ToList();

            conformanceFindings.Should().ContainSingle(
                "exactly one InputConformance finding expected (L7 value 'Maybe' not in {Yes,No}); actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            var finding = conformanceFindings[0];
            finding.Evaluation.Should().Be(FindingEvaluation.Error,
                "the default evaluation for DvConformanceCell is Error");
            finding.CellAddresses.Should().Be("L7",
                "the perturbation is at the x2 anchor row 7, column L");
            finding.CheckResult.Should().Contain("L7",
                "the message names the mutated cell address");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task CleanBaseline_EmitsNoConformanceFindings()
    {
        // Unperturbed baseline: H values {1,2,3,4} all satisfy WholeNumber >= 0;
        // L values "No" are in the template list {Yes,No}.
        // DvConformanceCell must not emit any InputConformance finding.

        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            RlqV01BaselineFactory.WriteCurrent(currentPath);
            RlqV01BaselineFactory.WriteTemplate(templatePath);
            RlqV01BaselineFactory.WritePrevious(previousPath);
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
                f => f.Check == ValidationCheck.InputConformance,
                "clean baseline workbooks must not trigger any DV-conformance finding");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
