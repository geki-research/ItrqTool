using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using ItrqTool.Domain.Validation;
using Xunit;

namespace ItrqTool.Integration.Tests.GdV01;

/// <summary>
/// Tests for the identity-integrity gate halt: when a malformed XrefId key (blank, duplicate,
/// or unparseable) is present in the current workbook, the pipeline sets <c>Halted = true</c>
/// and emits only <see cref="ValidationCheck.Structure"/> / <see cref="FindingEvaluation.Fatal"/>
/// findings from <c>MalformedKeyCheck</c>. No extension findings are emitted.
/// </summary>
public sealed class GdV01GateHaltTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-gdv01-gate", Guid.NewGuid().ToString("N"));

    [Fact]
    public void BlankXrefId_HaltsGate_ExactlyOneStructureFatalAtQ4()
    {
        // Blank Q4 (Q1's XrefId) in current. Parser detects Blank reason → MalformedKeyCheck
        // emits one Fatal/Structure finding at Q4 with "blank". Gate halts; no extensions run.

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
                wb.Worksheets.First().Cell(4, "Q").Value = "";
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeTrue(
                "a blank XrefId key causes the identity-integrity gate to halt the chain");

            result.Findings.Should().AllSatisfy(f =>
            {
                f.Check.Should().Be(ValidationCheck.Structure);
                f.Evaluation.Should().Be(FindingEvaluation.Fatal);
            }, "the gate emits only Structure/Fatal findings when it halts");

            result.Findings.Should().ContainSingle(
                f => f.CellAddresses == "Q4" &&
                     f.CheckResult.Contains("blank", StringComparison.Ordinal),
                "exactly one blank-key finding at Q4");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void DuplicateXrefId_HaltsGate_TwoStructureFatalFindingsNameingQ1()
    {
        // Set Q11 = "Q1" in current, duplicating Q4's value. Parser records both rows 4 and 11
        // as Duplicate → MalformedKeyCheck emits two Fatal/Structure findings (Q4 and Q11),
        // both containing "Q1" and "duplicated". Gate halts; no extensions run.

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

            // Change Q2:A-01's XrefId from "Q2:A-01" to "Q1", duplicating Q1 at row 4.
            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First().Cell(11, "Q").Value = "Q1";
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeTrue(
                "a duplicated XrefId key causes the identity-integrity gate to halt the chain");

            result.Findings.Should().AllSatisfy(f =>
            {
                f.Check.Should().Be(ValidationCheck.Structure);
                f.Evaluation.Should().Be(FindingEvaluation.Fatal);
            }, "the gate emits only Structure/Fatal findings when it halts");

            result.Findings.Should().HaveCount(2,
                "exactly two duplicate-key findings expected (Q1 on parsed rows 4 and 11); actual: [{0}]",
                string.Join("; ", result.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            foreach (var addr in new[] { "Q4", "Q11" })
                result.Findings.Should().Contain(
                    f => f.CellAddresses == addr &&
                         f.CheckResult.Contains("duplicated", StringComparison.Ordinal) &&
                         f.CheckResult.Contains("Q1", StringComparison.Ordinal),
                    because: $"expected Fatal/Structure duplicate finding at {addr} naming 'Q1'");

            result.Findings.Should().NotContain(
                f => f.Check == ValidationCheck.MissingResponse,
                "the gate halts before any extension checks run");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void UnparseableXrefId_HaltsGate_ExactlyOneStructureFatalAtQ4()
    {
        // Set Q4 = "Q1::" in current. Segments: ["Q1", "", ""] — empty second segment →
        // Unparseable. MalformedKeyCheck emits one Fatal/Structure finding at Q4 containing
        // "unparseable" and "Q1::". Gate halts; no extensions run.

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
                wb.Worksheets.First().Cell(4, "Q").Value = "Q1::";
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeTrue(
                "an unparseable XrefId key causes the identity-integrity gate to halt the chain");

            result.Findings.Should().AllSatisfy(f =>
            {
                f.Check.Should().Be(ValidationCheck.Structure);
                f.Evaluation.Should().Be(FindingEvaluation.Fatal);
            }, "the gate emits only Structure/Fatal findings when it halts");

            result.Findings.Should().ContainSingle(
                f => f.CellAddresses == "Q4" &&
                     f.CheckResult.Contains("unparseable", StringComparison.Ordinal) &&
                     f.CheckResult.Contains("Q1::", StringComparison.Ordinal),
                "exactly one unparseable-key finding at Q4 naming 'Q1::'");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
