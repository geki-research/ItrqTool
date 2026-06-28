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
using ItrqTool.Integration.Tests.WorksheetStructure;

namespace ItrqTool.Integration.Tests.RlqV01;

/// <summary>
/// End-to-end tests for the 5a-iv range-ref List resolution. Uses the range-ref fixture
/// variant (<see cref="RlqV01BaselineFactory.WriteCurrentRangeRef"/> etc.) where the
/// material-change (L) DV is sourced from a "Lists" backing sheet rather than an inline string.
/// Mirrors <see cref="RlqV01DvConformancePerturbationTests"/> in assertion style: filter to the
/// InputConformance subset, assert by Check + CellAddresses, never a total count.
/// This file is separate so the 5a-iii exact-set test file stays frozen.
/// </summary>
public sealed class RlqV01DvConformanceRangeRefTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-rlqv01-dv-rangeref", Guid.NewGuid().ToString("N"));

    private static RiskLevelQuestionValidationV01Task BuildTask()
    {
        var reader = new ClosedXmlExcelStructureReader(NullLogger<ClosedXmlExcelStructureReader>.Instance);
        return new(reader, StructureGateTestSupport.Mediator(reader),
            NullLogger<RiskLevelQuestionValidationV01Task>.Instance);
    }

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
    public async Task RangeRefList_CleanBaseline_EmitsNoConformanceFindings()
    {
        // Template L-DV: List from Lists!$A$1:$A$2 ("Yes","No") — range-ref resolved.
        // Current L values: all "No" (conforming). No InputConformance finding expected.

        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            RlqV01BaselineFactory.WriteCurrentRangeRef(currentPath);
            RlqV01BaselineFactory.WriteTemplateRangeRef(templatePath);
            RlqV01BaselineFactory.WritePreviousRangeRef(previousPath);
            await File.WriteAllTextAsync(configPath, RlqV01BaselineFactory.SyntheticConfigJson);

            var result = await BuildTask().ExecuteAsync(
                BuildContext(currentPath, templatePath, previousPath, configPath, reportPath, dir),
                CancellationToken.None);

            result.Succeeded.Should().BeTrue(
                "task must succeed; errors: {0}",
                string.Join("; ", result.Messages.Select(m => m.Text)));

            var report = ValidationReportSerializer.Deserialize(
                await File.ReadAllTextAsync(reportPath));

            (report.Halted ?? false).Should().BeFalse("keys are clean — gate does not fire");

            report.Findings.Should().NotContain(
                f => f.Check == ValidationCheck.InputConformance,
                "clean baseline with range-ref List DV must not trigger any DV-conformance finding");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task RangeRefList_LOutsideResolvedList_EmitsMaterialChangeNotConformant()
    {
        // Template L-DV: List from Lists!$A$1:$A$2 ("Yes","No") — range-ref resolved.
        // Current: L7 mutated to "Maybe" (not in the resolved list).
        // Row 7 = x2; single-row question, L is not merged there.
        // Expect exactly one InputConformance finding at L7 (Error).

        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            RlqV01BaselineFactory.WriteCurrentRangeRef(currentPath);
            RlqV01BaselineFactory.WriteTemplateRangeRef(templatePath);
            RlqV01BaselineFactory.WritePreviousRangeRef(previousPath);
            await File.WriteAllTextAsync(configPath, RlqV01BaselineFactory.SyntheticConfigJson);

            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First(ws => ws.Name == RlqV01WorkbookWriter.SheetName)
                  .Cell(7, "L").Value = "Maybe";
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
                "exactly one InputConformance finding expected (L7 'Maybe' not in resolved list {Yes,No}); actual: {0}",
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
}
