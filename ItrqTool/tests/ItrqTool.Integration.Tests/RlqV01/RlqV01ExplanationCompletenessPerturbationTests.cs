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
/// Exact-subset tests for the finding-6b per-row explanation-completeness findings emitted by
/// <c>ExplanationCompletenessCell</c> for the current-explanation column (K). The clean baseline
/// is explanation-complete (single-row questions have K filled but no request in I; Q3's rows
/// 8/9/10 have both the request I and the current explanation K filled). Each perturbation reopens
/// the baseline current workbook and blanks one or more Q3 K cells (whose I requests stay filled),
/// turning those rows into incomplete explanations. Mirrors
/// <see cref="RlqV01CrossYearDeviationPerturbationTests"/>: filter to the
/// <c>MissingResponse</c> findings on the K column, assert by Check + CellAddresses, never a total.
/// </summary>
public sealed class RlqV01ExplanationCompletenessPerturbationTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-rlqv01-explanation", Guid.NewGuid().ToString("N"));

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

    // Explanation-completeness findings: MissingResponse on the current-explanation column (K).
    // (Answer/material-change presence findings are MissingResponse too, but on H/L — never K.)
    private static List<ValidationFinding> ExplanationFindings(ValidationReport report) =>
        report.Findings
            .Where(f => f.Check == ValidationCheck.MissingResponse
                        && f.CellAddresses.StartsWith("K", StringComparison.Ordinal))
            .ToList();

    [Fact]
    public async Task CleanBaseline_EmitsNoExplanationIncompleteFindings()
    {
        // No current row has a request (I) present with the current explanation (K) blank, so the
        // per-row completeness check stays silent.
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

            ExplanationFindings(report).Should().BeEmpty(
                "the clean baseline is explanation-complete (every requested row has a current explanation)");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task BlankOneRequestedCurrentExplanation_EmitsOneFindingAtK_Error()
    {
        // Q3 row 8 has request I8 filled and current explanation K8 filled. Blank K8 → that row is
        // now an incomplete explanation → exactly one finding at K8 (Error). Rows 9/10 stay complete.
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
                wb.Worksheets.First(ws => ws.Name == RlqV01WorkbookWriter.SheetName)
                  .Cell(8, "K").Value = "";   // blank the requested current explanation on Q3 row 8
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

            var findings = ExplanationFindings(report);

            findings.Should().ContainSingle(
                "exactly one explanation-incomplete finding expected (K8 blanked, I8 still requested); actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            var finding = findings[0];
            finding.Evaluation.Should().Be(FindingEvaluation.Error,
                "the default evaluation for ExplanationCompletenessCell is Error");
            finding.CellAddresses.Should().Be("K8",
                "the perturbation is at Q3 row 8, current-explanation column K");
            finding.CheckResult.Should().Contain("K8",
                "the message names the incomplete explanation cell");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task BlankTwoRequestedCurrentExplanations_EmitsOnePerOffendingRow()
    {
        // Q3 rows 8 and 9 both have their request (I) filled. Blank K8 AND K9 → two incomplete rows
        // → exactly two findings, one per offending row (K8, K9). Proves per-row emission.
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
                var ws = wb.Worksheets.First(s => s.Name == RlqV01WorkbookWriter.SheetName);
                ws.Cell(8, "K").Value = "";
                ws.Cell(9, "K").Value = "";
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

            var findings = ExplanationFindings(report);

            findings.Should().HaveCount(2,
                "one finding per offending row (K8, K9); actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));
            findings.Select(f => f.CellAddresses).Should().BeEquivalentTo(new[] { "K8", "K9" });
            findings.Should().OnlyContain(f => f.Evaluation == FindingEvaluation.Error);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
