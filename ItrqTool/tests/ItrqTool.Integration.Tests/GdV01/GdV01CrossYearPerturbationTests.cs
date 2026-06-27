using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using ItrqTool.Domain.Validation;
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
}
