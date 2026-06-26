using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using ItrqTool.Domain.Validation;
using Xunit;

namespace ItrqTool.Integration.Tests.GdV01;

/// <summary>
/// Exact-set tests for the within-year required-input presence findings
/// (<c>input-cell.answer.missing</c> and <c>input-cell.material-change.missing</c>)
/// and the G-CO section gate (L not required outside <c>MaterialChangeSections</c>).
/// </summary>
public sealed class GdV01RequiredInputPerturbationTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-gdv01-required", Guid.NewGuid().ToString("N"));

    [Fact]
    public void HMissing_OnGCoAnchor_ExactlyOneMissingResponseAtH4()
    {
        // Q1 (row 4, G-CO): blank H4 (answer not provided).
        // Section gate for L: G-CO is NOT in MaterialChangeSections → no L finding.
        // Expected: exactly 1 MissingResponse at H4.

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
                wb.Worksheets.First().Cell(4, "H").Value = "";
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the gate does not fire");
            GdV01Assert.Exactly(result.Findings,
                new GdV01ExpectedFinding(
                    ValidationCheck.MissingResponse, FindingEvaluation.Error,
                    "H4", "not provided"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void LMissing_OnGStAnchor_ExactlyOneMissingResponseAtL11()
    {
        // Q2:A-01 (row 11, G-ST, MaterialChangeSections contains G-ST): blank L11.
        // Expected: exactly 1 MissingResponse at L11.

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
                wb.Worksheets.First().Cell(11, "L").Value = "";
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the gate does not fire");
            GdV01Assert.Exactly(result.Findings,
                new GdV01ExpectedFinding(
                    ValidationCheck.MissingResponse, FindingEvaluation.Error,
                    "L11", "not provided"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void LBlank_OnGCoAnchor_NoMissingResponseForL4()
    {
        // Q1 (row 4, G-CO): L4 is already blank in the baseline (G-CO is NOT in
        // MaterialChangeSections = ["G-ST"]), so the section gate skips L there.
        // The clean trio produces zero findings; this test proves the gate explicitly.

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

            // L4 is already blank in the clean baseline; explicit blank to document intent.
            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First().Cell(4, "L").Value = "";
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the gate does not fire");
            result.Findings.Should().NotContain(
                f => f.Check == ValidationCheck.MissingResponse && f.CellAddresses == "L4",
                "L is not required in G-CO (not in MaterialChangeSections) — the section gate skips it");
            result.Findings.Should().BeEmpty(
                "the clean baseline yields zero findings even with L4 explicitly blank in G-CO");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
