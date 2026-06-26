using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using ItrqTool.Domain.Validation;
using Xunit;

namespace ItrqTool.Integration.Tests.GdV01;

/// <summary>
/// Exact-set tests for within-year explanation-completeness (finding id
/// <c>input-cell.explanation.incomplete</c>) and within-year structure findings
/// (<c>structure.question-removed</c>, <c>structure.question-added</c>,
/// <c>structure.question-row-shifted</c>).
/// </summary>
public sealed class GdV01ExplanationStructurePerturbationTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-gdv01-expstruct", Guid.NewGuid().ToString("N"));

    // ── Explanation completeness ──────────────────────────────────────────────────────────────

    [Fact]
    public void ExplanationIncomplete_BlankCurrentExplanationWhileRequested_OneFindingAtK4()
    {
        // Q1 (row 4): I4 = "req1" (requested, non-blank) and K4 = "cur1" (complete in baseline).
        // Blank K4 in current while I4 stays filled → the row is now explanation-incomplete.
        // Expected: exactly 1 MissingResponse at K4 (current-explanation column, row 4).

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
                wb.Worksheets.First().Cell(4, "K").Value = "";
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the gate does not fire");
            GdV01Assert.Exactly(result.Findings,
                new GdV01ExpectedFinding(
                    ValidationCheck.MissingResponse, FindingEvaluation.Error,
                    "K4", "K4"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Structure: absent from response ──────────────────────────────────────────────────────

    [Fact]
    public void StructureAbsentFromResponse_TemplateHasExtraQid_OneFindingAtTemplateRow()
    {
        // Add Q3 to the TEMPLATE at row 13 (within section G-ST range 10:11-43).
        // Current does NOT have Q3 → Q3 is in alignment.WithinYearRemoved.
        // Expected: exactly 1 Structure/Error finding at Q13 containing "absent from the response".

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

            // Add Q3 to template at row 13 (G-ST, within declared range 10:11-43).
            using (var wb = new XLWorkbook(templatePath))
            {
                var ws = wb.Worksheets.First();
                ws.Cell(13, "C").Value = "3";
                ws.Cell(13, "D").Value = "Q3 text";
                ws.Cell(13, "Q").Value = "Q3";
                ws.Cell(13, "H").CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
                ws.Cell(13, "L").CreateDataValidation().List("\"Yes,No\"");
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys in all three workbooks remain clean");
            GdV01Assert.Exactly(result.Findings,
                new GdV01ExpectedFinding(
                    ValidationCheck.Structure, FindingEvaluation.Error,
                    "Q13", "absent from the response"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Structure: absent from template ──────────────────────────────────────────────────────

    [Fact]
    public void StructureAbsentFromTemplate_CurrentHasExtraQid_OneFindingAtCurrentRow()
    {
        // Add Q3 to the CURRENT at row 13 (G-ST, within declared range 10:11-43) with
        // valid H/L values and conformant DV. Template does NOT have Q3 → AddedInResponse.
        // Expected: exactly 1 Structure/Error finding at Q13 containing "absent from the empty template".

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

            // Add Q3 to current at row 13 (G-ST) with conformant H and L values.
            using (var wb = new XLWorkbook(currentPath))
            {
                var ws = wb.Worksheets.First();
                ws.Cell(13, "C").Value = "3";
                ws.Cell(13, "D").Value = "Q3 text";
                ws.Cell(13, "G").Value = "prev_3";
                ws.Cell(13, "H").Value = 1;
                ws.Cell(13, "L").Value = "Yes";
                ws.Cell(13, "O").Value = "TestOU";
                ws.Cell(13, "Q").Value = "Q3";
                ws.Cell(13, "H").CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
                ws.Cell(13, "L").CreateDataValidation().List("\"Yes,No\"");
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys in all three workbooks remain clean");
            GdV01Assert.Exactly(result.Findings,
                new GdV01ExpectedFinding(
                    ValidationCheck.Structure, FindingEvaluation.Error,
                    "Q13", "absent from the empty template"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Structure: row-shifted ────────────────────────────────────────────────────────────────

    [Fact]
    public void StructureRowShifted_Q2AppearsBeforeQ3InCurrentButAfterInTemplate_OneFindingAtQ11()
    {
        // Add Q3 to TEMPLATE at row 5 (G-CO, within range 3:4-9) and to CURRENT at row 13
        // (G-ST, within range 10:11-43). Leave PREVIOUS unchanged (no Q3 — cross-year Neither).
        //
        // Template order (by row): Q1(4), Q3(5), Q2(11) → template ranks: Q1=1, Q3=2, Q2=3.
        // Current order: Q1(4), Q2(11), Q3(13) → rank sequence [1, 3, 2].
        //
        // Rank-minimal LIS: [1, 2] = Q1, Q3 → Q2 (rank 3 at current row 11) is out of order.
        // Expected: exactly 1 Structure/Error at Q11 containing "expected to follow".
        //
        // Q3 DV matches in both template and current → frozen-constraint silent.
        // Q3 H and L values are valid → no required-input or conformance findings.

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

            // Template: add Q3 at row 5 (G-CO); H DV and L DV match current Q3's to keep
            // frozen-constraint silent when patcher stamps both workbooks.
            using (var wb = new XLWorkbook(templatePath))
            {
                var ws = wb.Worksheets.First();
                ws.Cell(5, "C").Value = "3";
                ws.Cell(5, "D").Value = "Q3 text";
                ws.Cell(5, "Q").Value = "Q3";
                ws.Cell(5, "H").CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
                ws.Cell(5, "L").CreateDataValidation().List("\"Yes,No\"");
                wb.Save();
            }

            // Current: add Q3 at row 13 (G-ST) with conformant H and L values.
            using (var wb = new XLWorkbook(currentPath))
            {
                var ws = wb.Worksheets.First();
                ws.Cell(13, "C").Value = "3";
                ws.Cell(13, "D").Value = "Q3 text";
                ws.Cell(13, "G").Value = "prev_3";
                ws.Cell(13, "H").Value = 1;
                ws.Cell(13, "L").Value = "Yes";
                ws.Cell(13, "O").Value = "TestOU";
                ws.Cell(13, "Q").Value = "Q3";
                ws.Cell(13, "H").CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
                ws.Cell(13, "L").CreateDataValidation().List("\"Yes,No\"");
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("all XrefId keys are clean");

            // Exactly 1 row-shift finding at Q11 (Q2's anchor row in current).
            GdV01Assert.Exactly(result.Findings,
                new GdV01ExpectedFinding(
                    ValidationCheck.Structure, FindingEvaluation.Error,
                    "Q11", "expected to follow"));

            // The finding must name Q2 as the shifted identity key.
            result.Findings[0].CheckResult.Should()
                .Contain("'Q2'", "the shifted question is Q2");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
