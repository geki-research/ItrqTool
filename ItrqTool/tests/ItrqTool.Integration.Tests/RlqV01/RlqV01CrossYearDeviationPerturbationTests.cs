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
/// Exact-subset tests for the finding-6a cross-year RELATIVE deviation findings emitted by
/// <c>CrossYearDeviationCell</c> for the answer column (H, role "answer", threshold 0.25 = 25%).
/// The realigned baseline (previous H == current H = 1/2/3/4) is cross-year clean; the perturbation
/// reopens the baseline current workbook and moves one H value past the relative threshold, leaving
/// DV intact. Mirrors <see cref="RlqV01DvConformancePerturbationTests"/>: filter to the
/// <c>Deviation</c> subset and assert by Check + CellAddresses, never a total count. The precise
/// boundary / sub-threshold / prev==0 cases live in the unit tests (CrossYearDeviationCellTests);
/// this proves end-to-end firing plus a clean baseline.
/// </summary>
public sealed class RlqV01CrossYearDeviationPerturbationTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-rlqv01-deviation", Guid.NewGuid().ToString("N"));

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
        // relative change 0 < threshold 0.25 → no Deviation finding.
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
    public async Task HMovedPastRelativeThreshold_EmitsOneDeviationAtAnchor_Warning()
    {
        // Current x4 (row 13) H: 4 -> 6; previous H13 = 4 (realigned). |6 - 4| / |4| = 0.50 >= 0.25
        // → one Deviation finding at H13. All other rows unchanged (relative change 0). Answer DV
        // WholeNumber >= 0: 6 conforms, so no InputConformance noise.
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
                wb.Worksheets.First().Cell(13, "H").Value = 6;
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
                "exactly one Deviation finding expected (H13 moved 4 -> 6, 50% >= 25%); actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            var finding = deviationFindings[0];
            finding.Evaluation.Should().Be(FindingEvaluation.Warning,
                "the default evaluation for CrossYearDeviationCell is Warning");
            finding.CellAddresses.Should().Be("H13",
                "the perturbation is at the x4 anchor row 13, column H");
            finding.CheckResult.Should().Contain("H13",
                "the message names the deviating answer cell");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── BLG-0022 native-integration proofs (Decimal-DV comma-decimal variant) ─────────────────
    //
    // Cross-year deviation exercises the SAME native plumbing on both operands: the answer's native
    // double is preferred over the invariant text parse, PER SIDE. The deviation cell's text fallback
    // carries NumberStyles.Float|AllowThousands, so under invariant culture a comma is SILENTLY EATEN
    // as a thousands separator ("3,75" → 375) — corrupting, not failing, the operand. The reader is run
    // under de-DE so the answers render comma-form; the landed native path keeps the true doubles.
    // See the deliverable report for the profile-wiring revert-probe RED bar (nativeSelector → null).

    private static readonly CultureInfo CommaDecimalCulture = new("de-DE");

    private static ExcelCellStructure ReadCell(string path, int row, string col)
    {
        var reader = new ClosedXmlExcelStructureReader(NullLogger<ClosedXmlExcelStructureReader>.Instance);
        var cells = reader.ReadCells(path, RlqV01WorkbookWriter.SheetName, new[] { $"{col}{row}" });
        return cells[$"{col}{row}"];
    }

    private static void WriteDecimalTrio(
        string currentPath, string templatePath, string previousPath)
    {
        RlqV01BaselineFactory.WriteCurrentDecimal(currentPath);
        RlqV01BaselineFactory.WriteTemplateDecimal(templatePath);
        RlqV01BaselineFactory.WritePreviousDecimal(previousPath);
    }

    [Fact]
    public async Task CommaDecimalWithinThreshold_NativePath_EmitsNoFabricatedDeviation()
    {
        // Deviation FLIP. x4 (H13): current 1.55 / previous 1.5 — a genuine 3% year-over-year move,
        // well under the 25% threshold → NO finding on the native path. The old text path eats the
        // commas ("1,55" → 155, "1,5" → 15) and computes |155-15|/15 = 933% ≥ 25% → a FABRICATED
        // finding. The comma-form + native-double pre-asserts anchor non-vacuity: the absence is real,
        // not a dot-form accident. (Under the revert-probe this same trio fabricates the H13 finding.)
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

            WriteDecimalTrio(currentPath, templatePath, previousPath);
            await File.WriteAllTextAsync(configPath, RlqV01BaselineFactory.SyntheticConfigJson);

            // Non-vacuity pre-asserts on BOTH sides — deviation resolves each operand independently.
            var curH13 = ReadCell(currentPath, RlqV01BaselineFactory.DecX4Row, "H");
            var prevH13 = ReadCell(previousPath, RlqV01BaselineFactory.DecX4Row, "H");
            curH13.TextValue.Should().Contain(",", "current x4 must render comma-form");
            curH13.NativeValue.Should().BeOfType<double>().Which.Should().Be(1.55);
            prevH13.TextValue.Should().Contain(",", "previous x4 must render comma-form");
            prevH13.NativeValue.Should().BeOfType<double>().Which.Should().Be(1.5);

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
                f => f.Check == ValidationCheck.Deviation && f.CellAddresses == "H13",
                "native |1.55-1.5|/1.5 = 3% < 25%; the comma must NOT be eaten into a fabricated 933% move");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            Thread.CurrentThread.CurrentCulture = prevCulture;
        }
    }

    [Fact]
    public async Task CommaDecimalPastThreshold_NativePath_EmitsCorrectPercentage_D10()
    {
        // Deviation D10 — the percentage correction Chunk 2 could not prove (its operands scaled
        // equally). x3 (H8): current 3.75 / previous 2.5, chosen with DIFFERENT decimal-place counts so
        // the comma-eating text path scales the two operands UNEQUALLY:
        //   native: |3.75 - 2.5| / 2.5 = 0.50  → "50 %"   (correct)
        //   text  : |375  - 25 | / 25  = 14.0  → "1,400 %" (corrupted — commas eaten as thousands)
        // Both exceed 25%, so the finding is PRESENT on either path; only its printed percentage differs.
        // We pin the message to the native-correct percentage and assert the corrupted one is absent.
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

            WriteDecimalTrio(currentPath, templatePath, previousPath);
            await File.WriteAllTextAsync(configPath, RlqV01BaselineFactory.SyntheticConfigJson);

            var curH8 = ReadCell(currentPath, RlqV01BaselineFactory.DecX3Row, "H");
            var prevH8 = ReadCell(previousPath, RlqV01BaselineFactory.DecX3Row, "H");
            curH8.TextValue.Should().Contain(",", "current x3 must render comma-form");
            curH8.NativeValue.Should().BeOfType<double>().Which.Should().Be(3.75);
            prevH8.TextValue.Should().Contain(",", "previous x3 must render comma-form");
            prevH8.NativeValue.Should().BeOfType<double>().Which.Should().Be(2.5);

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
                "exactly one Deviation finding expected (x3/H8 moved past threshold; x4 stays under, x1/x2 unchanged); actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            var finding = deviationFindings[0];
            finding.CellAddresses.Should().Be("H8", "the D10 deviation is at the x3 anchor row 8");
            finding.Evaluation.Should().Be(FindingEvaluation.Warning);

            // Pin the percentage to the native-correct value; the corrupted text-path value must be absent.
            var correctPct   = (0.5d).ToString("P0", CultureInfo.InvariantCulture);   // "50 %"
            var corruptedPct = (14.0d).ToString("P0", CultureInfo.InvariantCulture);  // "1,400 %" (text-path)
            finding.CheckResult.Should().Contain(correctPct,
                "the native operands (3.75, 2.5) give the true 50% relative move");
            finding.CheckResult.Should().NotContain(corruptedPct,
                "the comma-eaten text operands (375, 25) would have printed a corrupted 1,400% — the bug this proves fixed");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            Thread.CurrentThread.CurrentCulture = prevCulture;
        }
    }
}
