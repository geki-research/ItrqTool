using System.Globalization;
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

    // ── BLG-0022 native-integration proofs (Decimal-DV comma-decimal variant) ─────────────────
    //
    // These two tests exercise the WHOLE workbook → NativeValue → AnswerNativeValue → nativeSelector
    // → DvConformanceEvaluator flow end-to-end for a genuinely-numeric, comma-rendered Decimal cell —
    // the case no committed fixture reached before (all were WholeNumber-integral, dot-form). The
    // reader is run under the German (comma-decimal) culture so cell.GetString() yields comma-form
    // text ("9,1"); the evaluator still parses invariantly, so the OLD text path (Decimal branch,
    // NumberStyles.Float, no thousands) would FAIL to parse the comma and false-report NotConformant.
    // The landed native path reads the double straight from the cell and judges it correctly. See the
    // profile-wiring revert-probe evidence in the deliverable report for the matching RED bar.
    //
    // Culture flows across the task's await (verified) and is thread-local (Thread.CurrentThread only,
    // NOT DefaultThreadCurrentCulture), so parallel xUnit classes are unaffected.

    private static readonly CultureInfo CommaDecimalCulture = new("de-DE");

    private static ExcelCellStructure ReadCell(string path, int row, string col)
    {
        var reader = new ClosedXmlExcelStructureReader(NullLogger<ClosedXmlExcelStructureReader>.Instance);
        var cells = reader.ReadCells(path, RlqV01WorkbookWriter.SheetName, new[] { $"{col}{row}" });
        return cells[$"{col}{row}"];
    }

    [Fact]
    public async Task InRangeCommaDecimal_NativePath_EmitsNoConformanceFinding()
    {
        // Conformance A (non-vacuous fix). Decimal DV Between(0,100) on H6; current H6 = 9.1, stored
        // numeric. Under de-DE the reader renders it comma-form ("9,1"), which the old invariant
        // Decimal text-parse REJECTS (Float, no thousands) → would have false-reported NotConformant.
        // The native path reads 9.1d ∈ [0,100] → conformant → no finding. The comma-form + native-double
        // pre-asserts are the non-vacuity anchor: they prove the green is real, not a dot-form accident.
        var prevCulture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = CommaDecimalCulture;
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            RlqV01BaselineFactory.WriteCurrentDecimal(currentPath);
            RlqV01BaselineFactory.WriteTemplateDecimal(templatePath);
            RlqV01BaselineFactory.WritePreviousDecimal(previousPath);
            await File.WriteAllTextAsync(configPath, RlqV01BaselineFactory.SyntheticConfigJson);

            // Non-vacuity pre-asserts: the cell is genuinely numeric AND its text renders comma-form.
            var h6 = ReadCell(currentPath, RlqV01BaselineFactory.DecX1Row, "H");
            h6.TextValue.Should().Contain(",",
                "the fixture is vacuous unless the numeric cell renders comma-form through the reader");
            h6.NativeValue.Should().BeOfType<double>().Which.Should().Be(9.1,
                "the cell must be genuinely numeric so the native path has a double to compare");
            h6.DataValidationType.Should().Be("Decimal", "the answer DV is the Decimal variant");

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
                f => f.Check == ValidationCheck.InputConformance && f.CellAddresses == "H6",
                "9.1 ∈ [0,100] under the native compare; the comma-form text must NOT false-reject it");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            Thread.CurrentThread.CurrentCulture = prevCulture;
        }
    }

    [Fact]
    public async Task OutOfRangeCommaDecimal_NativePath_StillEmitsNotConformant()
    {
        // Conformance B (CNV-0029 — surface, don't skip). Decimal DV Between(0,100) on H7; current
        // H7 = 150.5, stored numeric, rendered "150,5" under de-DE. The native path reads 150.5d ∉
        // [0,100] → NotConformant. Only the FALSE locale rejection disappears; a genuinely out-of-range
        // value is still surfaced. On the clean Decimal trio the InputConformance subset is exactly {H7}.
        var prevCulture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = CommaDecimalCulture;
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            RlqV01BaselineFactory.WriteCurrentDecimal(currentPath);
            RlqV01BaselineFactory.WriteTemplateDecimal(templatePath);
            RlqV01BaselineFactory.WritePreviousDecimal(previousPath);
            await File.WriteAllTextAsync(configPath, RlqV01BaselineFactory.SyntheticConfigJson);

            var h7 = ReadCell(currentPath, RlqV01BaselineFactory.DecX2Row, "H");
            h7.TextValue.Should().Contain(",", "the out-of-range cell must also render comma-form");
            h7.NativeValue.Should().BeOfType<double>().Which.Should().Be(150.5);

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
                "exactly one InputConformance finding expected (H7 = 150.5 ∉ [0,100]); the in-range comma cells (H6/H8/H13) must NOT false-reject; actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            conformanceFindings[0].CellAddresses.Should().Be("H7",
                "the out-of-range Decimal answer is at the x2 anchor row 7");
            conformanceFindings[0].Evaluation.Should().Be(FindingEvaluation.Error);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            Thread.CurrentThread.CurrentCulture = prevCulture;
        }
    }
}
