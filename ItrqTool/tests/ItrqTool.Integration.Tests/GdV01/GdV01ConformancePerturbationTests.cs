using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using ItrqTool.Domain.Validation;
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
}
