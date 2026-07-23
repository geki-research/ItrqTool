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
/// Exact-set tests for the cross-year findings:
/// <c>cross-year.answer-deviation</c> and <c>cross-year.same-xrefid-text-diverged</c>.
/// </summary>
public sealed class GdV01CrossYearPerturbationTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-gdv01-cy", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Deviation_AboveThreshold_ExactlyOneDeviationAtH11()
    {
        // Q2:A-01 (row 11, G-ST): set current H11 = 4, keep previous H11 = 2 (baseline).
        // relChange = |4 - 2| / |2| = 1.0 = 100% >= 25% threshold -> Deviation fires.
        // 4 >= 0 (WholeNumber DV) -> no InputConformance. Deviation = 0 for H4 and H12.
        // Expected: exactly 1 Deviation/Warning at H11.

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

            // Raise current H11 from 2 to 4: relChange = 100% >= 25%.
            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First().Cell(11, "H").Value = 4;
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the gate does not fire");
            GdV01Assert.Exactly(result.Findings,
                new GdV01ExpectedFinding(
                    ValidationCheck.Deviation, FindingEvaluation.Warning,
                    "H11", "Answer at H11 changed from 2 to 4"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void Deviation_BelowThreshold_ZeroFindings()
    {
        // Q2:A-01 (row 11, G-ST): set current H11 = 11, previous H11 = 10.
        // relChange = |11 - 10| / |10| = 0.10 = 10% < 25% threshold -> no Deviation.
        // 11 >= 0 (WholeNumber DV) -> no InputConformance. No other deltas.
        // Expected: zero findings.

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
                wb.Worksheets.First().Cell(11, "H").Value = 11;
                wb.Save();
            }

            using (var wb = new XLWorkbook(previousPath))
            {
                wb.Worksheets.First().Cell(11, "H").Value = 10;
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the gate does not fire");
            GdV01Assert.Exactly(result.Findings);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void SameXrefIdTextDiverged_QuestionTextChanged_ExactlyOneStructureWarningAtQ11()
    {
        // Q2 (first row 11): change current D11 text to something completely unlike baseline
        // "Q2 text". The per-answer XrefIds (Q2:A-01 / Q2:A-02) remain identical in both
        // years so the aligner matches the question by XrefId but detects text divergence
        // -> SameXrefIdTextDiverged fires at Q{rowNumber} = Q11.
        // H/L values and DVs unchanged -> no conformance, required-input, or deviation findings.
        // Expected: exactly 1 Structure/Warning at Q11.

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

            // Overwrite Q2's text column (D, row 11) in the current workbook.
            // The previous workbook retains "Q2 text" so the aligner sees XrefId match + text divergence.
            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First().Cell(11, "D").Value = "Completely rewritten question content";
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the gate does not fire");
            GdV01Assert.Exactly(result.Findings,
                new GdV01ExpectedFinding(
                    ValidationCheck.Structure, FindingEvaluation.Warning,
                    "Q11", "question text has diverged"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── BLG-0022 native-integration proofs (Decimal-DV comma-decimal variant — GD twin of RLQ 5a) ──
    //
    // GdAnswerDeviationCell resolves EACH side's operand native-first (paired current/previous native
    // selectors), falling back to an invariant text parse that carries NumberStyles.Float|AllowThousands
    // — so under invariant culture a comma is SILENTLY EATEN as a thousands separator ("3,75" → 375),
    // corrupting (not failing) the operand. The reader runs under de-DE so the answers render comma-form;
    // the landed native path keeps the true doubles (stamped for BOTH current and previous by
    // GdDvPatcher). See the deliverable report for the profile-wiring revert-probe RED bar.

    private static readonly CultureInfo CommaDecimalCulture = new("de-DE");

    private static ExcelCellStructure ReadCell(string path, int row, string col)
    {
        var reader = new ClosedXmlExcelStructureReader(NullLogger<ClosedXmlExcelStructureReader>.Instance);
        var cells = reader.ReadCells(path, GdV01WorkbookWriter.SheetName, new[] { $"{col}{row}" });
        return cells[$"{col}{row}"];
    }

    [Fact]
    public void CommaDecimalWithinThreshold_NativePath_EmitsNoFabricatedDeviation()
    {
        // Deviation FLIP. H12 (Q2:A-02): current 1.55 / previous 1.5 — a genuine 3% move, under the 25%
        // threshold → NO finding on the native path. The old text path eats the commas ("1,55" → 155,
        // "1,5" → 15) and computes |155-15|/15 = 933% ≥ 25% → a FABRICATED finding. The comma-form +
        // native-double pre-asserts on BOTH sides anchor non-vacuity: the absence is real. (The trio's
        // only real deviation is H11/D10; here we assert H12 stays clean.)
        var prevCulture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = CommaDecimalCulture;
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");

            GdV01BaselineFactory.WriteDeviationTrio(currentPath, templatePath, previousPath);

            // Non-vacuity pre-asserts on BOTH sides — deviation resolves each operand independently.
            var curH12 = ReadCell(currentPath, GdV01BaselineFactory.DecQ2A02Row, "H");
            var prevH12 = ReadCell(previousPath, GdV01BaselineFactory.DecQ2A02Row, "H");
            curH12.TextValue.Should().Contain(",", "current H12 must render comma-form");
            curH12.NativeValue.Should().BeOfType<double>().Which.Should().Be(1.55);
            prevH12.TextValue.Should().Contain(",", "previous H12 must render comma-form");
            prevH12.NativeValue.Should().BeOfType<double>().Which.Should().Be(1.5);

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the gate does not fire");
            result.Findings.Should().NotContain(
                f => f.Check == ValidationCheck.Deviation && f.CellAddresses == "H12",
                "native |1.55-1.5|/1.5 = 3% < 25%; the comma must NOT be eaten into a fabricated 933% move");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            Thread.CurrentThread.CurrentCulture = prevCulture;
        }
    }

    [Fact]
    public void CommaDecimalPastThreshold_NativePath_EmitsCorrectPercentage_D10()
    {
        // Deviation D10 — the percentage correction Chunk 2 could not prove (its operands scaled
        // equally). H11 (Q2:A-01): current 3.75 / previous 2.5, chosen with DIFFERENT decimal-place
        // counts so the comma-eating text path scales the two operands UNEQUALLY:
        //   native: |3.75 - 2.5| / 2.5 = 0.50  → "50 %"    (correct)
        //   text  : |375  - 25 | / 25  = 14.0  → "1,400 %" (corrupted — commas eaten as thousands)
        // Both exceed 25%, so the finding is PRESENT on either path; only its printed percentage differs.
        // The GD deviation message format equals RLQ's verbatim: "Answer at {cell} changed from {prev}
        // to {cur} ({P0 invariant}), exceeding the {P0 invariant} year-over-year deviation threshold."
        // We pin the message to the native-correct percentage and assert the corrupted one is absent.
        var prevCulture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = CommaDecimalCulture;
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");

            GdV01BaselineFactory.WriteDeviationTrio(currentPath, templatePath, previousPath);

            var curH11 = ReadCell(currentPath, GdV01BaselineFactory.DecQ2A01Row, "H");
            var prevH11 = ReadCell(previousPath, GdV01BaselineFactory.DecQ2A01Row, "H");
            curH11.TextValue.Should().Contain(",", "current H11 must render comma-form");
            curH11.NativeValue.Should().BeOfType<double>().Which.Should().Be(3.75);
            prevH11.TextValue.Should().Contain(",", "previous H11 must render comma-form");
            prevH11.NativeValue.Should().BeOfType<double>().Which.Should().Be(2.5);

            var correctPct   = (0.5d).ToString("P0", CultureInfo.InvariantCulture);   // "50 %"
            var corruptedPct = (14.0d).ToString("P0", CultureInfo.InvariantCulture);  // "1,400 %" (text-path)

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the gate does not fire");

            // Exact set: exactly one Deviation finding, at H11, carrying the native-correct percentage.
            GdV01Assert.Exactly(result.Findings,
                new GdV01ExpectedFinding(
                    ValidationCheck.Deviation, FindingEvaluation.Warning,
                    "H11", $"changed from 2,5 to 3,75 ({correctPct})"));

            // And explicitly: the corrupted text-path percentage must be absent.
            var deviation = GdV01Assert.Single(result.Findings, ValidationCheck.Deviation);
            deviation.CheckResult.Should().Contain(correctPct,
                "the native operands (3.75, 2.5) give the true 50% relative move");
            deviation.CheckResult.Should().NotContain(corruptedPct,
                "the comma-eaten text operands (375, 25) would have printed a corrupted 1,400% — the bug this proves fixed");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            Thread.CurrentThread.CurrentCulture = prevCulture;
        }
    }
}
