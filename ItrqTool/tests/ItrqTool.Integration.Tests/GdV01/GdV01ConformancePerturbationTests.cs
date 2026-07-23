using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ItrqTool.Integration.Tests.GdV01;

/// <summary>
/// Exact-set tests for the within-year DV conformance findings
/// (<c>input-cell.answer.not-conformant</c> and <c>input-cell.material-change.not-conformant</c>).
/// </summary>
public sealed class GdV01ConformancePerturbationTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-gdv01-conform", Guid.NewGuid().ToString("N"));

    [Fact]
    public void HNotConformant_NegativeValue_ExactlyOneInputConformanceAtH4()
    {
        // Q1 (row 4): set H4 = -1 in both current and previous (so cross-year deviation = 0;
        // no Deviation finding). The DV rule is preserved (WholeNumber EqualOrGreaterThan 0).
        // Present-gate: -1 is non-blank → InputConformance runs. -1 < 0 → NotConformant.
        // Expected: exactly 1 InputConformance at H4.

        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");

            GdV01BaselineFactory.WriteCurrent(currentPath);
            GdV01BaselineFactory.WriteTemplate(templatePath);
            GdV01BaselineFactory.WritePrevious(previousPath);

            // Set H4 = -1 in current (violates WholeNumber ≥ 0).
            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First().Cell(4, "H").Value = -1;
                wb.Save();
            }

            // Mirror H4 = -1 in previous so cross-year deviation = 0 → no Deviation finding.
            using (var wb = new XLWorkbook(previousPath))
            {
                wb.Worksheets.First().Cell(4, "H").Value = -1;
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the gate does not fire");
            GdV01Assert.Exactly(result.Findings,
                new GdV01ExpectedFinding(
                    ValidationCheck.InputConformance, FindingEvaluation.Error,
                    "H4", "does not conform"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void LNotConformant_OutOfVocabularyValue_ExactlyOneInputConformanceAtL11()
    {
        // Q2:A-01 (row 11, G-ST): set L11 = "Maybe" (not in List "Yes,No" DV).
        // Present-gate: "Maybe" is non-blank → InputConformance runs. "Maybe" ∉ {Yes,No} → NotConformant.
        // Expected: exactly 1 InputConformance at L11.

        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");

            GdV01BaselineFactory.WriteCurrent(currentPath);
            GdV01BaselineFactory.WriteTemplate(templatePath);
            GdV01BaselineFactory.WritePrevious(previousPath);

            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First().Cell(11, "L").Value = "Maybe";
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the gate does not fire");
            GdV01Assert.Exactly(result.Findings,
                new GdV01ExpectedFinding(
                    ValidationCheck.InputConformance, FindingEvaluation.Error,
                    "L11", "does not conform"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── BLG-0022 native-integration proofs (Decimal-DV comma-decimal variant — GD twin of RLQ 5a) ──
    //
    // The whole workbook → NativeValue → GdAnswer.AnswerNativeValue (stamped per-answer by
    // GdDvPatcher.StampInline) → nativeSelector → DvConformanceEvaluator flow, exercised on a
    // genuinely-numeric, comma-rendered Decimal answer cell — the case no committed GD fixture reached
    // (all were WholeNumber-integral, dot-form). The reader runs under the German (comma-decimal)
    // culture so cell.GetString() yields comma-form ("9,1"); the evaluator parses invariantly, so the
    // OLD text path (Decimal branch, NumberStyles.Float, no thousands) would FAIL to parse the comma
    // and false-report NotConformant. The landed native path reads the double straight from the cell.
    //
    // GdV01PipelineRunner.Run is synchronous, so the de-DE scope simply wraps it (no await boundary).
    // Culture is thread-local (Thread.CurrentThread only), restored in finally — parallel xUnit
    // classes are unaffected. See the profile-wiring revert-probe RED bar in the deliverable report.

    private static readonly CultureInfo CommaDecimalCulture = new("de-DE");

    private static ExcelCellStructure ReadCell(string path, int row, string col)
    {
        var reader = new ClosedXmlExcelStructureReader(NullLogger<ClosedXmlExcelStructureReader>.Instance);
        var cells = reader.ReadCells(path, GdV01WorkbookWriter.SheetName, new[] { $"{col}{row}" });
        return cells[$"{col}{row}"];
    }

    [Fact]
    public void InRangeCommaDecimal_NativePath_EmitsNoConformanceFinding()
    {
        // Conformance A (non-vacuous fix). Decimal DV Between(0,100) on H4; current H4 = 9.1, stored
        // numeric. Under de-DE the reader renders it "9,1", which the old invariant Decimal text-parse
        // REJECTS (Float, no thousands) → would false-report NotConformant. The native path reads 9.1d
        // ∈ [0,100] → conformant → no finding. On the conformance trio the only real finding is H11
        // (150.5, out-of-range); we assert nothing fires at H4. The comma-form + native-double
        // pre-asserts anchor non-vacuity: the green is real, not a dot-form accident.
        var prevCulture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = CommaDecimalCulture;
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");

            GdV01BaselineFactory.WriteConformanceTrio(currentPath, templatePath, previousPath);

            // Non-vacuity pre-asserts: genuinely numeric AND comma-form through the reader.
            var h4 = ReadCell(currentPath, GdV01BaselineFactory.DecQ1Row, "H");
            h4.TextValue.Should().Contain(",",
                "the fixture is vacuous unless the numeric cell renders comma-form through the reader");
            h4.NativeValue.Should().BeOfType<double>().Which.Should().Be(9.1,
                "the cell must be genuinely numeric so the native path has a double to compare");
            h4.DataValidationType.Should().Be("Decimal", "the answer DV is the Decimal variant");

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the gate does not fire");
            result.Findings.Should().NotContain(
                f => f.Check == ValidationCheck.InputConformance && f.CellAddresses == "H4",
                "9.1 ∈ [0,100] under the native compare; the comma-form text must NOT false-reject it");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            Thread.CurrentThread.CurrentCulture = prevCulture;
        }
    }

    [Fact]
    public void OutOfRangeCommaDecimal_NativePath_StillEmitsNotConformant()
    {
        // Conformance B (CNV-0029 — surface, don't skip). Decimal DV Between(0,100) on H11; current
        // H11 = 150.5, numeric, rendered "150,5" under de-DE. The native path reads 150.5d ∉ [0,100] →
        // NotConformant. Only the FALSE locale rejection disappears; a genuinely out-of-range value is
        // still surfaced. On the clean Decimal conformance trio the whole finding set is exactly one
        // InputConformance at H11 (H4/H12 = 9.1 in-range must NOT false-reject).
        var prevCulture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = CommaDecimalCulture;
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");

            GdV01BaselineFactory.WriteConformanceTrio(currentPath, templatePath, previousPath);

            var h11 = ReadCell(currentPath, GdV01BaselineFactory.DecQ2A01Row, "H");
            h11.TextValue.Should().Contain(",", "the out-of-range cell must also render comma-form");
            h11.NativeValue.Should().BeOfType<double>().Which.Should().Be(150.5);

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the gate does not fire");
            GdV01Assert.Exactly(result.Findings,
                new GdV01ExpectedFinding(
                    ValidationCheck.InputConformance, FindingEvaluation.Error,
                    "H11", "does not conform"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            Thread.CurrentThread.CurrentCulture = prevCulture;
        }
    }
}
