using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using ItrqTool.Domain.Validation;
using Xunit;

namespace ItrqTool.Integration.Tests.GdV01;

/// <summary>
/// Step-4 resolution e2e tests: proves <c>GdDvPatcher.ResolveRangeAndNamedLists</c> correctly
/// resolves RangeRef and NamedRange List DV sources and that the resolved vocabulary feeds the
/// <c>InputConformance</c> check. One "conformant → zero findings" guard per DV-source kind, plus
/// "not conformant → exactly one finding" cases for each branch (H range-ref, L range-ref, H named-range).
/// Each variant applies the same DV in both current and template so FrozenConstraint stays silent.
/// </summary>
public sealed class GdV01DvResolutionPerturbationTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-gdv01-dv-resolution", Guid.NewGuid().ToString("N"));

    private const int Q2A01Row = 11;   // anchor row for Q2:A-01 (H/L under test)

    // ── RangeRef H — proves step-4 RangeRef branch e2e ───────────────────────────────────────

    [Fact]
    public void RangeRefH_CleanBaseline_ZeroFindings()
    {
        // H11 DV = List from Lists!A1:A2 ({Yes,No}) — range-ref resolved by GdDvPatcher step 4.
        // H11 = "Yes" (conformant). Template H11 DV = same (FrozenConstraint stays silent).
        // All other H rows keep WholeNumber DV; L rows keep inline List DV.
        // Expected: zero findings.

        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");

            GdV01BaselineFactory.WriteCurrentHRangeRef(currentPath);
            GdV01BaselineFactory.WriteTemplateHRangeRef(templatePath);
            GdV01BaselineFactory.WritePrevious(previousPath);

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the gate does not fire");
            GdV01Assert.Exactly(result.Findings);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void RangeRefH_OutOfVocabularyValue_ExactlyOneInputConformanceAtH11()
    {
        // H11 DV = List from Lists!A1:A2 ({Yes,No}) — range-ref resolved.
        // H11 mutated to "Maybe" (not in resolved vocabulary {Yes,No}).
        // Expected: exactly 1 InputConformance/Error at H11 containing "does not conform".

        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");

            GdV01BaselineFactory.WriteCurrentHRangeRef(currentPath);
            GdV01BaselineFactory.WriteTemplateHRangeRef(templatePath);
            GdV01BaselineFactory.WritePrevious(previousPath);

            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First(ws => ws.Name == GdV01WorkbookWriter.SheetName)
                  .Cell(Q2A01Row, "H").Value = "Maybe";
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the gate does not fire");
            GdV01Assert.Exactly(result.Findings,
                new GdV01ExpectedFinding(
                    ValidationCheck.InputConformance, FindingEvaluation.Error,
                    "H11", "does not conform"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── RangeRef L — proves step-4 RangeRef branch on the material-change role ───────────────

    [Fact]
    public void RangeRefL_OutOfVocabularyValue_ExactlyOneInputConformanceAtL11()
    {
        // L11 and L12 DV = List from Lists!A1:A2 ({Yes,No}) — range-ref resolved.
        // L11 mutated to "Maybe" (not in resolved vocabulary). L12 = "No" (conformant).
        // H DV stays WholeNumber on all rows (no Deviation or FrozenConstraint from H).
        // Expected: exactly 1 InputConformance/Error at L11 containing "does not conform".

        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");

            GdV01BaselineFactory.WriteCurrentLRangeRef(currentPath);
            GdV01BaselineFactory.WriteTemplateLRangeRef(templatePath);
            GdV01BaselineFactory.WritePrevious(previousPath);

            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First(ws => ws.Name == GdV01WorkbookWriter.SheetName)
                  .Cell(Q2A01Row, "L").Value = "Maybe";
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

    // ── NamedRange H — proves step-4 NamedRange branch e2e ───────────────────────────────────

    [Fact]
    public void NamedRangeH_CleanBaseline_ZeroFindings()
    {
        // H11 DV = List from named range "GdAnswerOptions" → Lists!A1:A2 ({Yes,No}) — resolved.
        // H11 = "Yes" (conformant). Template H11 DV = same named-range formula.
        // Expected: zero findings.

        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");

            GdV01BaselineFactory.WriteCurrentHNamedRange(currentPath);
            GdV01BaselineFactory.WriteTemplateHNamedRange(templatePath);
            GdV01BaselineFactory.WritePrevious(previousPath);

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the gate does not fire");
            GdV01Assert.Exactly(result.Findings);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void NamedRangeH_OutOfVocabularyValue_ExactlyOneInputConformanceAtH11()
    {
        // H11 DV = List sourced from named range "GdAnswerOptions" → {Yes,No} — resolved.
        // H11 mutated to "Maybe" (not in resolved vocabulary).
        // Expected: exactly 1 InputConformance/Error at H11 containing "does not conform".

        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");

            GdV01BaselineFactory.WriteCurrentHNamedRange(currentPath);
            GdV01BaselineFactory.WriteTemplateHNamedRange(templatePath);
            GdV01BaselineFactory.WritePrevious(previousPath);

            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First(ws => ws.Name == GdV01WorkbookWriter.SheetName)
                  .Cell(Q2A01Row, "H").Value = "Maybe";
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the gate does not fire");
            GdV01Assert.Exactly(result.Findings,
                new GdV01ExpectedFinding(
                    ValidationCheck.InputConformance, FindingEvaluation.Error,
                    "H11", "does not conform"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
