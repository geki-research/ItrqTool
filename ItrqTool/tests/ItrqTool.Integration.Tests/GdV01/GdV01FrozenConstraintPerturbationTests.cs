using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using ItrqTool.Domain.Validation;
using Xunit;

namespace ItrqTool.Integration.Tests.GdV01;

/// <summary>
/// Exact-set tests for the frozen-constraint findings:
/// <c>constraint.answer-dv.validation-rule-changed</c> (role "answer-dv", column H) and
/// <c>constraint.material-change-dv.validation-rule-changed</c> (role "material-change-dv", column L).
/// The check compares the current answer's DV against the template answer's DV via
/// <c>DvComparer.IsDvChangedFull</c>. Only fires when
/// <c>WithinYearJoin == JoinedByXrefId</c> (template counterpart exists).
/// </summary>
public sealed class GdV01FrozenConstraintPerturbationTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-gdv01-fc", Guid.NewGuid().ToString("N"));

    [Fact]
    public void FrozenConstraintH_CurrentDvDiffersFromTemplate_ExactlyOneFrozenConstraintAtH11()
    {
        // Q2:A-01 (row 11, G-ST): change current H11 DV from WholeNumber >= 0 to WholeNumber >= 5.
        // Template H11 DV stays at WholeNumber >= 0. DvComparer sees a formula difference (0 vs 5)
        // -> FrozenConstraint fires at H11.
        // Set current H11 = 5 (conforms to WholeNumber >= 5) and previous H11 = 5 (deviation = 0%).
        // H4/H12: DVs and values unchanged -> no findings there.
        // Expected: exactly 1 FrozenConstraint/Error at H11.

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

            // Change current H11 DV to WholeNumber >= 5 and set value = 5 (conformant).
            // CreateDataValidation() removes the cell from any existing DV before registering the new rule.
            using (var wb = new XLWorkbook(currentPath))
            {
                var ws = wb.Worksheets.First();
                ws.Cell(11, "H").Value = 5;
                ws.Cell(11, "H").CreateDataValidation().WholeNumber.EqualOrGreaterThan(5);
                wb.Save();
            }

            // Set previous H11 = 5 to match current so deviation = |5-5|/|5| = 0 < threshold.
            using (var wb = new XLWorkbook(previousPath))
            {
                wb.Worksheets.First().Cell(11, "H").Value = 5;
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the gate does not fire");
            GdV01Assert.Exactly(result.Findings,
                new GdV01ExpectedFinding(
                    ValidationCheck.FrozenConstraint, FindingEvaluation.Error,
                    "H11", "Data-validation rule at H11 differs from the template."));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void FrozenConstraintL_CurrentDvDiffersFromTemplate_ExactlyOneFrozenConstraintAtL11()
    {
        // Q2:A-01 (row 11, G-ST): change current L11 DV from List "Yes,No" to List "Yes,No,Maybe".
        // Template L11 DV stays at List "Yes,No". The formula change is detected by DvComparer -> fires.
        // L11 value remains "Yes" (baseline), which conforms to "Yes,No,Maybe" -> no InputConformance.
        // H values unchanged -> no Deviation or H FrozenConstraint.
        // Expected: exactly 1 FrozenConstraint/Error at L11.

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

            // Change current L11 DV from List "Yes,No" to List "Yes,No,Maybe".
            // The baseline L11 value "Yes" conforms to the expanded vocabulary.
            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First().Cell(11, "L").CreateDataValidation().List("\"Yes,No,Maybe\"");
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the gate does not fire");
            GdV01Assert.Exactly(result.Findings,
                new GdV01ExpectedFinding(
                    ValidationCheck.FrozenConstraint, FindingEvaluation.Error,
                    "L11", "Data-validation rule at L11 differs from the template."));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
