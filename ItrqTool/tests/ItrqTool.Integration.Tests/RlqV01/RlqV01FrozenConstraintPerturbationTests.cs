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
/// Exact-set tests for the chunk-2 finding-4 frozen-constraint findings emitted by
/// <c>FrozenConstraintCell</c> for the answer DV (column H, role "answer-dv") and the
/// material-change DV (column L, role "material-change-dv"). Each test writes a bespoke
/// current-response workbook with one DV rule changed relative to the baseline, while
/// using the standard factory for the template and previous workbooks.
/// </summary>
public sealed class RlqV01FrozenConstraintPerturbationTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-rlqv01-frozen-constraint", Guid.NewGuid().ToString("N"));

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
    public async Task HDvRuleChanged_EmitsExactlyOneAnswerDvFrozenConstraintFinding()
    {
        // Template H-DV: WholeNumber ≥ 0 on all anchor rows (baseline ApplyAnswerDv).
        // Current H-DV: WholeNumber ≥ 1 on x1 anchor row 6, ≥ 0 on rows 7/8/13.
        // DvComparer.IsDvChangedFull fires for row 6 only → exactly one finding at H6.

        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            // Template and previous use the unperturbed baseline (H-DV ≥ 0 everywhere).
            RlqV01BaselineFactory.WriteTemplate(templatePath);
            RlqV01BaselineFactory.WritePrevious(previousPath);
            await File.WriteAllTextAsync(configPath, RlqV01BaselineFactory.SyntheticConfigJson);

            // Current: same structure as baseline but H6 DV is WholeNumber ≥ 1 (changed).
            WritePerturbedCurrentHDv(currentPath, perturbedRow: 6, greaterThanOrEqualTo: 1);

            var result = await BuildTask().ExecuteAsync(
                BuildContext(currentPath, templatePath, previousPath, configPath, reportPath, dir),
                CancellationToken.None);

            result.Succeeded.Should().BeTrue(
                "task must succeed; errors: {0}",
                string.Join("; ", result.Messages.Select(m => m.Text)));

            var report = ValidationReportSerializer.Deserialize(
                await File.ReadAllTextAsync(reportPath));

            (report.Halted ?? false).Should().BeFalse("keys are clean — gate does not fire");

            // Exact-set: exactly one finding — FrozenConstraint / Error at H6.
            report.Findings.Should().ContainSingle(
                "exactly one answer-DV frozen-constraint finding expected; actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            var finding = report.Findings[0];
            finding.Check.Should().Be(ValidationCheck.FrozenConstraint,
                "FrozenConstraintCell emits ValidationCheck.FrozenConstraint");
            finding.Evaluation.Should().Be(FindingEvaluation.Error,
                "the default evaluation for this primitive is Error");
            finding.CellAddresses.Should().Be("H6",
                "the perturbation is at the x1 anchor row 6, column H");
            finding.CheckResult.Should().Contain("H6",
                "the message names the mutated cell address");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task LDvRuleChanged_EmitsExactlyOneMaterialChangeDvFrozenConstraintFinding()
    {
        // Template L-DV: List "Yes,No" on all anchor rows (baseline ApplyMaterialChangeDv).
        // Current L-DV: List "Yes,No,Maybe" on x2 anchor row 7, "Yes,No" on rows 6/8/13.
        // DvComparer.IsDvChangedFull fires for row 7 only → exactly one finding at L7.

        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            RlqV01BaselineFactory.WriteTemplate(templatePath);
            RlqV01BaselineFactory.WritePrevious(previousPath);
            await File.WriteAllTextAsync(configPath, RlqV01BaselineFactory.SyntheticConfigJson);

            // Current: same structure as baseline but L7 DV is List "Yes,No,Maybe" (changed).
            WritePerturbedCurrentLDv(currentPath, perturbedRow: 7, perturbedList: "\"Yes,No,Maybe\"");

            var result = await BuildTask().ExecuteAsync(
                BuildContext(currentPath, templatePath, previousPath, configPath, reportPath, dir),
                CancellationToken.None);

            result.Succeeded.Should().BeTrue(
                "task must succeed; errors: {0}",
                string.Join("; ", result.Messages.Select(m => m.Text)));

            var report = ValidationReportSerializer.Deserialize(
                await File.ReadAllTextAsync(reportPath));

            (report.Halted ?? false).Should().BeFalse("keys are clean — gate does not fire");

            // Exact-set: exactly one finding — FrozenConstraint / Error at L7.
            report.Findings.Should().ContainSingle(
                "exactly one material-change-DV frozen-constraint finding expected; actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            var finding = report.Findings[0];
            finding.Check.Should().Be(ValidationCheck.FrozenConstraint,
                "FrozenConstraintCell emits ValidationCheck.FrozenConstraint");
            finding.Evaluation.Should().Be(FindingEvaluation.Error,
                "the default evaluation for this primitive is Error");
            finding.CellAddresses.Should().Be("L7",
                "the perturbation is at the x2 anchor row 7, column L");
            finding.CheckResult.Should().Contain("L7",
                "the message names the mutated cell address");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task CleanWorkbook_EmitsNoFrozenConstraintFindings()
    {
        // Unperturbed baseline: all three workbooks carry identical DV on H and L.
        // FrozenConstraintCell compares template vs current — both identical → no findings.

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
                f => f.Check == ValidationCheck.FrozenConstraint,
                "clean workbooks with identical DV on both H and L must not trigger any frozen-constraint finding");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Perturbation workbook writers ──────────────────────────────────────────────────────

    // Writes a current-response workbook identical to RlqV01BaselineFactory.WriteCurrent except
    // H-DV on `perturbedRow` uses WholeNumber ≥ `greaterThanOrEqualTo` instead of the baseline ≥ 0.
    private static void WritePerturbedCurrentHDv(string path, int perturbedRow, int greaterThanOrEqualTo)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(RlqV01WorkbookWriter.SheetName);

        RlqV01BaselineFactory.WriteCurrentBody(ws);

        // H DV: use the perturbed value for the target row, baseline ≥ 0 for all others.
        foreach (var row in new[] { 6, 7, 8, 13 })
        {
            int threshold = row == perturbedRow ? greaterThanOrEqualTo : 0;
            ws.Cell(row, "H").CreateDataValidation().WholeNumber.EqualOrGreaterThan(threshold);
        }

        // L DV: baseline List "Yes,No" everywhere (unchanged).
        foreach (var row in new[] { 6, 7, 8, 13 })
            ws.Cell(row, "L").CreateDataValidation().List("\"Yes,No\"");

        wb.SaveAs(path);
    }

    // Writes a current-response workbook identical to RlqV01BaselineFactory.WriteCurrent except
    // L-DV on `perturbedRow` uses the given `perturbedList` instead of the baseline "Yes,No".
    private static void WritePerturbedCurrentLDv(string path, int perturbedRow, string perturbedList)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(RlqV01WorkbookWriter.SheetName);

        RlqV01BaselineFactory.WriteCurrentBody(ws);

        // H DV: baseline ≥ 0 everywhere (unchanged).
        foreach (var row in new[] { 6, 7, 8, 13 })
            ws.Cell(row, "H").CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);

        // L DV: use the perturbed list for the target row, baseline "Yes,No" for all others.
        foreach (var row in new[] { 6, 7, 8, 13 })
        {
            string list = row == perturbedRow ? perturbedList : "\"Yes,No\"";
            ws.Cell(row, "L").CreateDataValidation().List(list);
        }

        wb.SaveAs(path);
    }

}
