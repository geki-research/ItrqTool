using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using ItrqTool.Domain.Validation;
using Xunit;

namespace ItrqTool.Integration.Tests.GdV01;

/// <summary>
/// BL-025 surface tests for the GD_v01 pipeline: the <c>dv-vocabulary-unresolvable</c>
/// finding fires when an answer cell's List DV references an undefined name so that
/// <c>GdDvPatcher</c> step-4 cannot resolve the controlled vocabulary.
/// <para>
/// Both H (answer role) and L (material-change role) are exercised. The unresolvable DV
/// is applied to BOTH current and template so <c>GdAnswerFrozenConstraintCell</c> stays
/// silent (same formula on both sides). The finding is identified by
/// <c>ValidationCheck.InputConformance</c> plus a <c>CheckResult</c> substring that
/// distinguishes it from <c>not-conformant</c> (following the same pattern as
/// <c>RlqV01DvVocabularyUnresolvablePerturbationTests</c>).
/// </para>
/// </summary>
public sealed class GdV01DvUnresolvablePerturbationTests
{
    private const int Q2A01Row = 11;   // anchor row for Q2:A-01 (H/L under test)
    private const int Q2A02Row = 12;   // anchor row for Q2:A-02

    // Substring present in every dv-vocabulary-unresolvable CheckResult from GdAnswerConformanceCell,
    // absent from the not-conformant CheckResult.
    private const string UnresolvableSubstring = "could not be resolved from the empty template";

    // Undefined name that DvRangeRefResolver cannot find in the workbook → null list → unresolvable.
    private const string UndefinedName = "=NonExistentGdListName";

    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-gdv01-dv-unresolvable", Guid.NewGuid().ToString("N"));

    // ── H-column helpers ──────────────────────────────────────────────────────────────────────

    // Writes the baseline current workbook and overrides H11 DV to an undefined NamedRange List.
    // Applied to both current and template so FrozenConstraint stays silent.
    private static void WriteCurrentWithUnresolvableHDv(string path)
    {
        GdV01BaselineFactory.WriteCurrent(path);
        using var wb = new XLWorkbook(path);
        wb.Worksheets.First(ws => ws.Name == GdV01WorkbookWriter.SheetName)
          .Cell(Q2A01Row, "H").CreateDataValidation().List(UndefinedName);
        wb.Save();
    }

    private static void WriteTemplateWithUnresolvableHDv(string path)
    {
        GdV01BaselineFactory.WriteTemplate(path);
        using var wb = new XLWorkbook(path);
        wb.Worksheets.First(ws => ws.Name == GdV01WorkbookWriter.SheetName)
          .Cell(Q2A01Row, "H").CreateDataValidation().List(UndefinedName);
        wb.Save();
    }

    // ── L-column helpers ──────────────────────────────────────────────────────────────────────

    private static void WriteCurrentWithUnresolvableLDv(string path)
    {
        GdV01BaselineFactory.WriteCurrent(path);
        using var wb = new XLWorkbook(path);
        wb.Worksheets.First(ws => ws.Name == GdV01WorkbookWriter.SheetName)
          .Cell(Q2A01Row, "L").CreateDataValidation().List(UndefinedName);
        wb.Save();
    }

    private static void WriteTemplateWithUnresolvableLDv(string path)
    {
        GdV01BaselineFactory.WriteTemplate(path);
        using var wb = new XLWorkbook(path);
        wb.Worksheets.First(ws => ws.Name == GdV01WorkbookWriter.SheetName)
          .Cell(Q2A01Row, "L").CreateDataValidation().List(UndefinedName);
        wb.Save();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void UnresolvableAnswerH_EmitsInputConformanceFindingAtH11()
    {
        // H11 DV = List with undefined name "NonExistentGdListName" in both current and template.
        // GdDvPatcher step-4: ResolveDefinedNameValues returns null → AnswerDvListValues stays null.
        // DvConformanceEvaluator sees dvType = "List", listValues = null → UnresolvableList.
        // H11 value = 2 (from baseline WriteCurrent, non-blank) → present-gate passes.
        // H4 and H12 keep WholeNumber DV → conformant, no deviation.
        // FrozenConstraint H11: current formula == template formula (same undefined name) → silent.
        // Expected: exactly 1 InputConformance/Error at H11 with unresolvable CheckResult substring.

        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");

            WriteCurrentWithUnresolvableHDv(currentPath);
            WriteTemplateWithUnresolvableHDv(templatePath);
            GdV01BaselineFactory.WritePrevious(previousPath);

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the identity gate does not fire");

            var unresolvableFindings = result.Findings
                .Where(f => f.Check == ValidationCheck.InputConformance
                         && f.CheckResult.Contains(UnresolvableSubstring, StringComparison.Ordinal))
                .ToList();

            unresolvableFindings.Should().ContainSingle(
                "exactly one dv-vocabulary-unresolvable finding expected (H11 DV references an " +
                "undefined name); all InputConformance findings: {0}",
                string.Join("; ", result.Findings
                    .Where(f => f.Check == ValidationCheck.InputConformance)
                    .Select(f => $"[{f.Evaluation}] @ {f.CellAddresses}: {f.CheckResult}")));

            var finding = unresolvableFindings[0];
            finding.Evaluation.Should().Be(FindingEvaluation.Error,
                "unresolvable List DV defaults to Error");
            finding.CellAddresses.Should().Be("H11",
                "the unresolvable DV is on Q2:A-01 anchor row 11, column H");
            finding.CheckResult.Should().Contain("H11",
                "the CheckResult names the cell where the unresolvable DV was found");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void UnresolvableMaterialChangeL_EmitsInputConformanceFindingAtL11()
    {
        // L11 DV = List with undefined name "NonExistentGdListName" in both current and template.
        // GdDvPatcher step-4: null → MaterialChangeDvListValues stays null → UnresolvableList.
        // L11 value = "Yes" (from baseline WriteCurrent, non-blank) → present-gate passes.
        // L12 keeps inline List "Yes,No" → conformant, no unresolvable there.
        // H DV unchanged (WholeNumber on all rows) → no H findings.
        // FrozenConstraint L11: current formula == template formula (same undefined name) → silent.
        // Expected: exactly 1 InputConformance/Error at L11 with unresolvable CheckResult substring.

        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");

            WriteCurrentWithUnresolvableLDv(currentPath);
            WriteTemplateWithUnresolvableLDv(templatePath);
            GdV01BaselineFactory.WritePrevious(previousPath);

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("keys are clean — the identity gate does not fire");

            var unresolvableFindings = result.Findings
                .Where(f => f.Check == ValidationCheck.InputConformance
                         && f.CheckResult.Contains(UnresolvableSubstring, StringComparison.Ordinal))
                .ToList();

            unresolvableFindings.Should().ContainSingle(
                "exactly one dv-vocabulary-unresolvable finding expected (L11 DV references an " +
                "undefined name); all InputConformance findings: {0}",
                string.Join("; ", result.Findings
                    .Where(f => f.Check == ValidationCheck.InputConformance)
                    .Select(f => $"[{f.Evaluation}] @ {f.CellAddresses}: {f.CheckResult}")));

            var finding = unresolvableFindings[0];
            finding.Evaluation.Should().Be(FindingEvaluation.Error,
                "unresolvable List DV defaults to Error");
            finding.CellAddresses.Should().Be("L11",
                "the unresolvable DV is on Q2:A-01 anchor row 11, column L");
            finding.CheckResult.Should().Contain("L11",
                "the CheckResult names the cell where the unresolvable DV was found");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
