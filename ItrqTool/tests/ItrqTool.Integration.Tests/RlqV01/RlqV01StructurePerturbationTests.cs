using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Infrastructure;
using ItrqTool.Tasks;
using ItrqTool.Tasks.Validation;
using ItrqTool.Integration.Tests.WorksheetStructure;

namespace ItrqTool.Integration.Tests.RlqV01;

/// <summary>
/// Exact-set test for the chunk-2 finding-3a within-year structure findings
/// (structure.question-removed + structure.question-added, both Error, Structure) emitted by
/// <c>WithinYearStructureCheck</c>. An in-place XrefId swap (x4 → x5 at row 13) removes the
/// template's x4 from the response and introduces x5 the template never declared — one removed
/// + one added finding, both anchored at Q13. The keys stay clean (x1/x2/x3/x5 each once,
/// template x1/x2/x3/x4 each once), so the identity gate does NOT fire and the extension chain
/// runs to completion.
/// </summary>
public sealed class RlqV01StructurePerturbationTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-rlqv01-structure", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InPlaceXrefIdSwap_ExactlyTwoStructureFindings_RemovedAndAdded()
    {
        // Baseline layout (RlqV01BaselineFactory + RlqV01WorkbookWriter):
        //   Q1 @ row 6   (xref x1, single-row)
        //   Q2 @ row 7   (xref x2, single-row)
        //   Q3 @ rows 8–10 (xref x3, multi-row → one parsed record at anchor row 8)
        //   Q4 @ row 13  (xref x4, single-row — section 2's declared range 13-13)
        //
        // Perturbation (current workbook only): in-place x4 → x5 swap at row 13.
        //   Q13 ← "x5" — Q4's XrefId becomes x5. L13/H13 kept valid (x5 triggers no
        //                input-presence finding). Row 13 stays within section 2's declared
        //                range 13-13, so the parser still parses it (rows OUTSIDE declared
        //                ranges are skipped — an appended row would not be parsed).
        //
        // Result:
        //   - template x4 has no counterpart in current → WithinYearRemoved contains x4
        //     → structure.question-removed at Q13 (template row 13).
        //   - current x5 is absent from the template → AddedInResponse
        //     → structure.question-added at Q13 (current row 13).
        // Keys remain clean → the identity gate does NOT fire (report.Halted is false).

        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            RlqV01BaselineFactory.WriteCurrent(currentPath);
            RlqV01BaselineFactory.WriteTemplate(templatePath);
            RlqV01BaselineFactory.WritePrevious(previousPath);
            await File.WriteAllTextAsync(configPath, RlqV01BaselineFactory.SyntheticConfigJson);

            // Reopen the current workbook and perform the in-place x4 → x5 swap.
            using (var wb = new XLWorkbook(currentPath))
            {
                var ws = wb.Worksheets.First();
                ws.Cell(13, "Q").Value = "x5";   // x4 → x5; L13/H13 left intact (valid)
                wb.Save();
            }

            var reader = new ClosedXmlExcelStructureReader(NullLogger<ClosedXmlExcelStructureReader>.Instance);
            var task = new RiskLevelQuestionValidationV01Task(
                reader, StructureGateTestSupport.Mediator(reader),
                NullLogger<RiskLevelQuestionValidationV01Task>.Instance);

            var ctx = new TaskExecutionContext(
                TaskId: "validate",
                InputPaths: new Dictionary<string, string>
                {
                    ["currentResponse"]  = currentPath,
                    ["emptyTemplate"]    = templatePath,
                    ["previousResponse"] = previousPath,
                },
                OutputPaths: new Dictionary<string, string> { ["report"] = reportPath },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["configurationFullFilename"] = configPath,
                },
            };

            var result = await task.ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue(
                "task must succeed; errors: {0}",
                string.Join("; ", result.Messages.Select(m => m.Text)));

            var report = ValidationReportSerializer.Deserialize(
                await File.ReadAllTextAsync(reportPath));

            // ── Clean keys ⇒ the gate did NOT fire ──
            (report.Halted ?? false).Should().BeFalse(
                "the swap keeps all XrefId keys distinct and non-blank, so the identity gate does not halt the chain");

            // ── Exact-set: exactly 2 findings, both structure, both Error, both at Q13 ──
            report.Findings.Should().HaveCount(2,
                "exactly one removed (x4) + one added (x5) finding expected; actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            report.Findings.Should().AllSatisfy(f =>
            {
                f.Check.Should().Be(ValidationCheck.Structure);
                f.Evaluation.Should().Be(FindingEvaluation.Error);
                f.CellAddresses.Should().Be("Q13");
            });

            report.Findings.Should().Contain(f =>
                f.CheckResult.Contains("absent from the response", StringComparison.Ordinal) &&
                f.CheckResult.Contains("x4", StringComparison.Ordinal),
                because: "the removed finding names x4 as absent from the response");

            report.Findings.Should().Contain(f =>
                f.CheckResult.Contains("absent from the empty template", StringComparison.Ordinal) &&
                f.CheckResult.Contains("x5", StringComparison.Ordinal),
                because: "the added finding names x5 as absent from the empty template");

            // ── No input-presence and no malformed-key findings ──
            report.Findings.Should().NotContain(
                f => f.Check == ValidationCheck.MissingResponse,
                "L13/H13 are valid and every other question is fully filled — no input-presence finding");
            report.Findings.Should().NotContain(
                f => f.CheckResult.Contains("blank", StringComparison.Ordinal) ||
                     f.CheckResult.Contains("duplicated", StringComparison.Ordinal),
                "keys remain clean — no structure.xrefid-empty-or-duplicated finding");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task InPlaceXrefIdReorder_ExactlyOneRowShiftFinding_NamingNeighbours()
    {
        // Baseline order (anchor rows): x1@6, x2@7, x3@8 (multi-row 8–10), x4@13.
        // Template ranks by anchor row: x1=1, x2=2, x3=3, x4=4.
        //
        // Perturbation (current workbook only): reorder x2 ↔ x3 IN PLACE by relabelling their
        // XrefId cells — Q7 ← "x3" (the single-row question at row 7) and Q8/Q9/Q10 ← "x2" (the
        // whole multi-row group). Every relabelled row stays inside section 1's declared range
        // (6–10), so the parser still parses them. The keys remain {x1,x3,x2,x4} (each once),
        // so the identity gate does NOT fire.
        //
        // Current order of MATCHED questions becomes x1(rank1), x3(rank3), x2(rank2), x4(rank4)
        // ⇒ rank sequence [1,3,2,4]. The rank-minimal LIS keeps 1,2,4, flagging the rank-3
        // question (now at row 7) as a single structure.question-row-shifted at Q7. There is no
        // removed/added finding (all four template keys are still present and matched).

        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            RlqV01BaselineFactory.WriteCurrent(currentPath);
            RlqV01BaselineFactory.WriteTemplate(templatePath);
            RlqV01BaselineFactory.WritePrevious(previousPath);
            await File.WriteAllTextAsync(configPath, RlqV01BaselineFactory.SyntheticConfigJson);

            // Reopen the current workbook and reorder x2 ↔ x3 in place via their XrefId cells.
            using (var wb = new XLWorkbook(currentPath))
            {
                var ws = wb.Worksheets.First();
                ws.Cell(7, "Q").Value = "x3";    // single-row question at row 7 now carries x3
                ws.Cell(8, "Q").Value = "x2";    // multi-row group (rows 8–10) now carries x2
                ws.Cell(9, "Q").Value = "x2";
                ws.Cell(10, "Q").Value = "x2";
                wb.Save();
            }

            var reader = new ClosedXmlExcelStructureReader(NullLogger<ClosedXmlExcelStructureReader>.Instance);
            var task = new RiskLevelQuestionValidationV01Task(
                reader, StructureGateTestSupport.Mediator(reader),
                NullLogger<RiskLevelQuestionValidationV01Task>.Instance);

            var ctx = new TaskExecutionContext(
                TaskId: "validate",
                InputPaths: new Dictionary<string, string>
                {
                    ["currentResponse"]  = currentPath,
                    ["emptyTemplate"]    = templatePath,
                    ["previousResponse"] = previousPath,
                },
                OutputPaths: new Dictionary<string, string> { ["report"] = reportPath },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["configurationFullFilename"] = configPath,
                },
            };

            var result = await task.ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue(
                "task must succeed; errors: {0}",
                string.Join("; ", result.Messages.Select(m => m.Text)));

            var report = ValidationReportSerializer.Deserialize(
                await File.ReadAllTextAsync(reportPath));

            // ── Clean keys ⇒ the gate did NOT fire ──
            (report.Halted ?? false).Should().BeFalse(
                "the reorder keeps all XrefId keys distinct and non-blank, so the identity gate does not halt");

            // ── Exact-set: exactly one row-shift finding at Q7, naming both neighbours ──
            var shift = report.Findings.Should().ContainSingle(
                "exactly one structure.question-row-shifted expected; actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}"))).Subject;

            shift.Check.Should().Be(ValidationCheck.Structure);
            shift.Evaluation.Should().Be(FindingEvaluation.Error);
            shift.CellAddresses.Should().Be("Q7");
            shift.CheckResult.Should()
                .Contain("identity key 'x3'")
                .And.Contain("expected to follow 'x2'")
                .And.Contain("found following 'x1'");

            // ── No removed/added findings spuriously fire ──
            report.Findings.Should().NotContain(
                f => f.CheckResult.Contains("absent from the response", StringComparison.Ordinal) ||
                     f.CheckResult.Contains("absent from the empty template", StringComparison.Ordinal),
                "all four template keys are still present and matched — no removed/added finding");

            // ── No input-presence findings ──
            report.Findings.Should().NotContain(
                f => f.Check == ValidationCheck.MissingResponse,
                "every relabelled question keeps its answer/material-change cells filled");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
