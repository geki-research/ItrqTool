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

namespace ItrqTool.Integration.Tests.RlqV02;

/// <summary>
/// Re-proves the inherited v01 finding set on the v02 fixture (chunk 3d).
/// One representative perturbation per inherited check family (two for the gate):
///   1.  input-presence   — MissingResponse/Error at H{row} (column H unchanged; ProvidedBy → P)
///   2a. xref-id gate blank — Structure/Fatal at R{row} (Q→R shift); gate halts
///   2b. xref-id gate dup  — Structure/Fatal at R{row} (Q→R shift); gate halts
///   3.  structure         — Structure/Error at R{row} (Q→R shift)
///   4.  frozen-constraint — FrozenConstraint/Error at H{row} (column H unchanged)
///   5.  DV-conformance    — InputConformance/Error at H{row} (column H unchanged)
///   6.  cross-year dev    — Deviation/Warning at H{row} (column H unchanged)
///   7.  explanation-comp  — MissingResponse/Error at K{row} (column K unchanged)
///
/// v02 fixture layout (RlqV02BaselineFactory):
///   Q1 @ row 6  (xref x1, single-row)
///   Q2 @ row 7  (xref x2, single-row)
///   Q3 @ rows 8–10 (xref x3, multi-row — anchor row 8; R is NOT merged, repeated per row)
///   Q4 @ row 13 (xref x4, single-row)
/// v02 column shift vs v01: HowExplanation M (new), ProvidedBy O→P, XrefId Q→R; C–L unchanged.
/// ProvidedBy factory value: "TestOU" written in column P.
/// </summary>
public sealed class RlqV02InheritedFindingSetReproofTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-rlqv02-inherited", Guid.NewGuid().ToString("N"));

    private static RiskLevelQuestionValidationV02Task BuildTask()
    {
        var reader = new ClosedXmlExcelStructureReader(NullLogger<ClosedXmlExcelStructureReader>.Instance);
        return new(reader, StructureGateTestSupport.Mediator(reader),
            NullLogger<RiskLevelQuestionValidationV02Task>.Instance);
    }

    private static async Task<ValidationReport> RunAsync(
        string currentPath, string templatePath, string previousPath,
        string configPath, string reportPath, string dir)
    {
        var result = await BuildTask().ExecuteAsync(
            new TaskExecutionContext(
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
            },
            CancellationToken.None);

        result.Succeeded.Should().BeTrue(
            "task must succeed; errors: {0}",
            string.Join("; ", result.Messages.Select(m => m.Text)));

        return ValidationReportSerializer.Deserialize(await File.ReadAllTextAsync(reportPath));
    }

    private static void WriteBaselineTrio(
        string currentPath, string templatePath, string previousPath)
    {
        RlqV02BaselineFactory.WriteCurrent(currentPath);
        RlqV02BaselineFactory.WriteTemplate(templatePath);
        RlqV02BaselineFactory.WritePrevious(previousPath);
    }

    // Writes a perturbed current workbook: same body as the baseline but H DV on `perturbedRow`
    // uses WholeNumber ≥ `greaterThanOrEqualTo` instead of the baseline ≥ 0. All other anchor rows
    // keep ≥ 0; L DV stays "Yes,No" on all anchor rows. Used for frozen-constraint case 4.
    private static void WritePerturbedCurrentHDv(string path, int perturbedRow, int greaterThanOrEqualTo)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(RlqV02BaselineFactory.SheetName);
        StructureHeaderStamper.Stamp(ws, "rlq", "v02");
        RlqV02BaselineFactory.WriteCurrentBody(ws);
        foreach (var row in new[] { 6, 7, 8, 13 })
        {
            int threshold = row == perturbedRow ? greaterThanOrEqualTo : 0;
            ws.Cell(row, "H").CreateDataValidation().WholeNumber.EqualOrGreaterThan(threshold);
        }
        foreach (var row in new[] { 6, 7, 8, 13 })
            ws.Cell(row, "L").CreateDataValidation().List("\"Yes,No\"");
        wb.SaveAs(path);
    }

    // ── 1. Input-presence ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Blank H7 (Q2 answer, column H — unchanged from v01) → input-cell.answer.missing
    /// (MissingResponse/Error) at H7. The finding's ProvidedBy == "TestOU" — the factory
    /// writes that value in column P (v02 shift from O), proving the P feed reaches the check.
    /// </summary>
    [Fact]
    public async Task InputPresence_BlankAnswerH7_OneFindingAtH7_WithProvidedByFromP()
    {
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
            await File.WriteAllTextAsync(configPath, RlqV02BaselineFactory.SyntheticConfigJson);

            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First().Cell(7, "H").Value = "";   // blank Q2 answer
                wb.Save();
            }

            var report = await RunAsync(currentPath, templatePath, previousPath, configPath, reportPath, dir);

            (report.Halted ?? false).Should().BeFalse("clean xref-ids — gate does not fire");

            var presenceFindings = report.Findings
                .Where(f => f.Check == ValidationCheck.MissingResponse)
                .ToList();

            presenceFindings.Should().ContainSingle(
                "exactly one input-presence finding expected (H7 blanked); actual: {0}",
                string.Join("; ", presenceFindings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            var finding = presenceFindings[0];
            finding.Evaluation.Should().Be(FindingEvaluation.Error);
            finding.CellAddresses.Should().Be("H7",
                "H is the answer column (unchanged); Q2 anchor is at row 7");
            finding.CheckResult.Should().Contain("is empty");
            finding.ProvidedBy.Should().Be("TestOU",
                "ProvidedBy is populated from column P (v02 shift from O); factory writes 'TestOU' there");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── 2a. XrefId gate — blank ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Blank R7 (Q2's XrefId — column R in v02, shifted from Q) and L7 → exactly one
    /// structure.xrefid-empty-or-duplicated (Structure/Fatal) at R7, Halted=true.
    /// Complementarity: no MissingResponse despite blank L7 (gate halts before input checks).
    /// Proves the identity gate reads the shifted R column.
    /// </summary>
    [Fact]
    public async Task XrefIdGate_BlankXrefIdInColumnR_FatalFindingAtR7_HaltsChain()
    {
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
            await File.WriteAllTextAsync(configPath, RlqV02BaselineFactory.SyntheticConfigJson);

            using (var wb = new XLWorkbook(currentPath))
            {
                var ws = wb.Worksheets.First();
                ws.Cell(7, "R").Value = "";   // blank Q2's XrefId (column R in v02, was Q in v01)
                ws.Cell(7, "L").Value = "";   // complementarity: prove input check suppressed
                wb.Save();
            }

            var report = await RunAsync(currentPath, templatePath, previousPath, configPath, reportPath, dir);

            report.Halted.Should().BeTrue(
                "blank XrefId in column R must trigger the identity gate and halt the chain");

            report.Findings.Should().ContainSingle(
                "exactly one blank-key finding expected (Q2's R7); actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            report.Findings.Should().Contain(f =>
                f.Check == ValidationCheck.Structure &&
                f.Evaluation == FindingEvaluation.Fatal &&
                f.CellAddresses == "R7" &&
                f.CheckResult.Contains("blank", StringComparison.Ordinal),
                because: "Fatal/Structure finding at R7 (v02 column) with 'blank' in CheckResult");

            report.Findings.Should().NotContain(
                f => f.Check == ValidationCheck.MissingResponse,
                "gate halts before input-presence checks run — no MissingResponse despite blank L7");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── 2b. XrefId gate — duplicate ──────────────────────────────────────────────────────────

    /// <summary>
    /// Three-way duplicate XrefId "x1" across R6, R8, R13 (Q3 rows 8–10 and Q4 row 13 changed
    /// to "x1" in column R — R is not merged, so each row is set individually) → exactly three
    /// structure.xrefid-empty-or-duplicated (Structure/Fatal) findings, Halted=true.
    /// Complementarity: L6/L8/L13 blanked — proves input checks are suppressed by the gate.
    /// Proves the gate reads column R (v02 XrefId shift from Q).
    /// </summary>
    [Fact]
    public async Task XrefIdGate_DuplicateXrefIdInColumnR_ThreeFatalFindings_HaltsChain()
    {
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
            await File.WriteAllTextAsync(configPath, RlqV02BaselineFactory.SyntheticConfigJson);

            using (var wb = new XLWorkbook(currentPath))
            {
                var ws = wb.Worksheets.First();

                // Q3 (rows 8–10) and Q4 (row 13) carry "x1" in column R (not merged in v02).
                ws.Cell(8,  "R").Value = "x1";
                ws.Cell(9,  "R").Value = "x1";
                ws.Cell(10, "R").Value = "x1";
                ws.Cell(13, "R").Value = "x1";

                // Complementarity: blank L on all three malformed questions.
                ws.Cell(6,  "L").Value = "";   // Q1 (L single-row)
                ws.Cell(8,  "L").Value = "";   // Q3 anchor (L merged over 8–10)
                ws.Cell(13, "L").Value = "";   // Q4

                wb.Save();
            }

            var report = await RunAsync(currentPath, templatePath, previousPath, configPath, reportPath, dir);

            report.Halted.Should().BeTrue(
                "duplicate XrefId in column R must trigger the identity gate and halt the chain");

            var expectedAddresses = new[] { "R6", "R8", "R13" };

            report.Findings.Should().HaveCount(3,
                "exactly three Fatal Structure findings expected (x1 on parsed records at rows 6, 8, 13); actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            foreach (var addr in expectedAddresses)
                report.Findings.Should().Contain(f =>
                    f.Check == ValidationCheck.Structure &&
                    f.Evaluation == FindingEvaluation.Fatal &&
                    f.CellAddresses == addr &&
                    f.CheckResult.Contains("duplicated", StringComparison.Ordinal) &&
                    f.CheckResult.Contains("x1", StringComparison.Ordinal),
                    because: $"Fatal/Structure finding at {addr} (column R) with 'duplicated' and 'x1'");

            report.Findings.Should().NotContain(
                f => f.Check == ValidationCheck.MissingResponse,
                "gate halts before input-presence checks run — no MissingResponse despite blank L cells");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── 3. Structure ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// In-place x4→x5 swap at R13 → structure.question-removed (x4 absent from response) and
    /// structure.question-added (x5 absent from template), both Structure/Error at R13.
    /// Keys stay clean (x1/x2/x3/x5 each once) → gate does not fire.
    /// Proves the structure checks read XrefId from column R (v02 shift from Q).
    /// </summary>
    [Fact]
    public async Task Structure_InPlaceXrefIdSwapAtR13_RemovedAndAddedFindingsBothAtR13()
    {
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
            await File.WriteAllTextAsync(configPath, RlqV02BaselineFactory.SyntheticConfigJson);

            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First().Cell(13, "R").Value = "x5";   // x4 → x5 in column R
                wb.Save();
            }

            var report = await RunAsync(currentPath, templatePath, previousPath, configPath, reportPath, dir);

            (report.Halted ?? false).Should().BeFalse(
                "x4→x5 swap keeps all keys distinct and non-blank — gate does not fire");

            report.Findings.Should().HaveCount(2,
                "exactly one removed (x4) + one added (x5) finding expected; actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            report.Findings.Should().AllSatisfy(f =>
            {
                f.Check.Should().Be(ValidationCheck.Structure);
                f.Evaluation.Should().Be(FindingEvaluation.Error);
                f.CellAddresses.Should().Be("R13",
                    "both structure findings reference column R (v02 XrefId column) at row 13");
            });

            report.Findings.Should().Contain(f =>
                f.CheckResult.Contains("absent from the response", StringComparison.Ordinal) &&
                f.CheckResult.Contains("x4", StringComparison.Ordinal),
                because: "the removed finding names x4 as absent from the response");

            report.Findings.Should().Contain(f =>
                f.CheckResult.Contains("absent from the empty template", StringComparison.Ordinal) &&
                f.CheckResult.Contains("x5", StringComparison.Ordinal),
                because: "the added finding names x5 as absent from the empty template");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── 4. Frozen-constraint ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Current H6 DV changed to WholeNumber ≥ 1 (template has ≥ 0) → exactly one
    /// constraint.answer-dv.validation-rule-changed (FrozenConstraint/Error) at H6.
    /// Column H is unchanged between v01 and v02. The M-insert did not shift H.
    /// </summary>
    [Fact]
    public async Task FrozenConstraint_HDvRuleChangedOnH6_OneFrozenConstraintFindingAtH6()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            // Template and previous use unperturbed baseline (H DV ≥ 0 everywhere).
            RlqV02BaselineFactory.WriteTemplate(templatePath);
            RlqV02BaselineFactory.WritePrevious(previousPath);
            await File.WriteAllTextAsync(configPath, RlqV02BaselineFactory.SyntheticConfigJson);

            // Current: same body but H6 DV is WholeNumber ≥ 1 (perturbed from baseline ≥ 0).
            WritePerturbedCurrentHDv(currentPath, perturbedRow: 6, greaterThanOrEqualTo: 1);

            var report = await RunAsync(currentPath, templatePath, previousPath, configPath, reportPath, dir);

            (report.Halted ?? false).Should().BeFalse("keys are clean — gate does not fire");

            report.Findings.Should().ContainSingle(
                "exactly one FrozenConstraint finding expected (H6 DV changed ≥0 → ≥1); actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            var finding = report.Findings[0];
            finding.Check.Should().Be(ValidationCheck.FrozenConstraint);
            finding.Evaluation.Should().Be(FindingEvaluation.Error);
            finding.CellAddresses.Should().Be("H6",
                "H is the answer column (unchanged); Q1 anchor is at row 6");
            finding.CheckResult.Should().Contain("H6");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── 5. DV-conformance ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Current H6 set to -1 (violates template WholeNumber ≥ 0) → exactly one
    /// input-cell.answer.not-conformant (InputConformance/Error) at H6.
    /// Column H is unchanged between v01 and v02.
    /// </summary>
    [Fact]
    public async Task DvConformance_HViolatesWholeNumberDvAtH6_OneInputConformanceFindingAtH6()
    {
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
            await File.WriteAllTextAsync(configPath, RlqV02BaselineFactory.SyntheticConfigJson);

            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First().Cell(6, "H").Value = -1;   // -1 < 0 → violates WholeNumber ≥ 0
                wb.Save();
            }

            var report = await RunAsync(currentPath, templatePath, previousPath, configPath, reportPath, dir);

            (report.Halted ?? false).Should().BeFalse("keys are clean — gate does not fire");

            var conformanceFindings = report.Findings
                .Where(f => f.Check == ValidationCheck.InputConformance)
                .ToList();

            conformanceFindings.Should().ContainSingle(
                "exactly one InputConformance finding expected (H6 = -1 violates WholeNumber ≥ 0); actual: {0}",
                string.Join("; ", conformanceFindings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            var finding = conformanceFindings[0];
            finding.Evaluation.Should().Be(FindingEvaluation.Error);
            finding.CellAddresses.Should().Be("H6",
                "H is the answer column (unchanged); Q1 anchor is at row 6");
            finding.CheckResult.Should().Contain("H6");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── 6. Cross-year deviation ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Current H13 (Q4 answer) changed from 4 to 6; previous H13 = 4 →
    /// |6−4|/|4| = 0.50 ≥ 0.25 threshold → cross-year.answer-deviation (Deviation/Warning) at H13.
    /// Column H is unchanged between v01 and v02.
    /// </summary>
    [Fact]
    public async Task CrossYearDeviation_HMovedPastThresholdAtH13_OneWarningFindingAtH13()
    {
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
            await File.WriteAllTextAsync(configPath, RlqV02BaselineFactory.SyntheticConfigJson);

            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First().Cell(13, "H").Value = 6;   // 4 → 6; |6−4|/|4| = 0.50 ≥ 0.25
                wb.Save();
            }

            var report = await RunAsync(currentPath, templatePath, previousPath, configPath, reportPath, dir);

            (report.Halted ?? false).Should().BeFalse("keys are clean — gate does not fire");

            var deviationFindings = report.Findings
                .Where(f => f.Check == ValidationCheck.Deviation)
                .ToList();

            deviationFindings.Should().ContainSingle(
                "exactly one Deviation finding expected (H13: 4→6, 50% ≥ 25%); actual: {0}",
                string.Join("; ", deviationFindings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            var finding = deviationFindings[0];
            finding.Evaluation.Should().Be(FindingEvaluation.Warning,
                "CrossYearDeviationCell emits Warning by default");
            finding.CellAddresses.Should().Be("H13",
                "H is the answer column (unchanged); Q4 anchor is at row 13");
            finding.CheckResult.Should().Contain("H13");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── 7. Explanation-completeness ───────────────────────────────────────────────────────────

    /// <summary>
    /// Blank K8 (Q3 anchor row current-explanation) while I8 (requested explanation) remains
    /// filled → input-cell.explanation.incomplete (MissingResponse/Error) at K8.
    /// ExplanationCompletenessCellV02 emits per-row. Column K is unchanged between v01 and v02.
    /// </summary>
    [Fact]
    public async Task ExplanationCompleteness_BlankK8WithI8Requested_OneFindingAtK8()
    {
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
            await File.WriteAllTextAsync(configPath, RlqV02BaselineFactory.SyntheticConfigJson);

            using (var wb = new XLWorkbook(currentPath))
            {
                // Q3 anchor: I8 is "req3_8" (non-blank, stays filled); K8 is the current-explanation.
                wb.Worksheets.First().Cell(8, "K").Value = "";   // blank current-explanation on Q3 row 8
                wb.Save();
            }

            var report = await RunAsync(currentPath, templatePath, previousPath, configPath, reportPath, dir);

            (report.Halted ?? false).Should().BeFalse("keys are clean — gate does not fire");

            var kFindings = report.Findings
                .Where(f => f.Check == ValidationCheck.MissingResponse
                            && f.CellAddresses.StartsWith("K", StringComparison.Ordinal))
                .ToList();

            kFindings.Should().ContainSingle(
                "exactly one explanation-incomplete finding expected (K8 blanked, I8 still requested); actual: {0}",
                string.Join("; ", kFindings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            var finding = kFindings[0];
            finding.Evaluation.Should().Be(FindingEvaluation.Error,
                "ExplanationCompletenessCellV02 emits Error by default");
            finding.CellAddresses.Should().Be("K8",
                "K is the current-explanation column (unchanged); Q3 anchor is at row 8");
            finding.CheckResult.Should().Contain("K8");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
