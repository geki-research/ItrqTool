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

namespace ItrqTool.Integration.Tests.RlqV01;

/// <summary>
/// Exact-set test for the chunk-2 finding-2 malformed-XrefId surfacing
/// (structure.xrefid-empty-or-duplicated, Fatal, Structure) and its complementarity
/// with the finding-1 input-presence checks (no MissingResponse findings on malformed rows).
/// </summary>
public sealed class RlqV01XrefIdIntegrityPerturbationTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-rlqv01-xrefid", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ThreeWayDuplicateXrefId_ExactlyThreeFindings_NoMissingResponseExtras()
    {
        // Baseline layout (RlqV01BaselineFactory + RlqV01WorkbookWriter):
        //   Q1 @ row 6   (xref x1, single-row)
        //   Q2 @ row 7   (xref x2, single-row)
        //   Q3 @ rows 8–10 (xref x3, multi-row — collapses to ONE parsed record at anchor row 8)
        //   Q4 @ row 13  (xref x4, single-row)
        //
        // Perturbations (current workbook only):
        //   Q8, Q9, Q10  ← "x1"  — Q3's XrefId changed to x1 (collapses to anchor row 8)
        //   Q13          ← "x1"  — Q4's XrefId changed to x1
        //
        // Result: x1 appears on three PARSED question records (Q1@6, Q3@8, Q4@13).
        // Rows 8–10 are non-contiguous with row 6 (row 7 = Q2 with x2 separates them), so
        // each is an independent record. ClassifyKeys sees countByValue["x1"] = 3 → all three
        // are Duplicate → MalformedKeyCheck emits one finding per record.
        //
        // Complementarity: L6, L8, L13 are blanked. All three malformed questions have
        // WithinYear == NotEvaluatedMalformedKey → RequiredInputCellAnyValue skips them →
        // no input-cell.material-change.missing (MissingResponse) findings despite blank L cells.
        //
        // Q2 (row 7, xref x2) is valid (x2 count=1) and its L7 is filled ("No") → no finding.

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

            // Reopen the current workbook and apply all perturbations.
            using (var wb = new XLWorkbook(currentPath))
            {
                var ws = wb.Worksheets.First();

                // Change Q3's XrefId (column Q, rows 8–10) to "x1". Column Q is not merged,
                // so each continuation row must be set individually.
                ws.Cell(8,  "Q").Value = "x1";
                ws.Cell(9,  "Q").Value = "x1";
                ws.Cell(10, "Q").Value = "x1";

                // Change Q4's XrefId (Q13) to "x1".
                ws.Cell(13, "Q").Value = "x1";

                // Complementarity: blank L on all three malformed questions.
                // L is merged over rows 8–10 for Q3; setting L8 blanks the anchor.
                ws.Cell(6,  "L").Value = "";   // Q1
                ws.Cell(8,  "L").Value = "";   // Q3 anchor (merged over 8–10)
                ws.Cell(13, "L").Value = "";   // Q4

                wb.Save();
            }

            var task = new RiskLevelQuestionValidationV01Task(
                new ClosedXmlExcelStructureReader(
                    NullLogger<ClosedXmlExcelStructureReader>.Instance),
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

            // ── Exact-set: exactly 3 findings, all structure.xrefid-empty-or-duplicated ──

            report.Findings.Should().HaveCount(3,
                "exactly three malformed-key findings expected (x1 on parsed records at rows 6, 8, 13); actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            var expectedAddresses = new[] { "Q6", "Q8", "Q13" };
            foreach (var addr in expectedAddresses)
                report.Findings.Should().Contain(f =>
                    f.Check == ValidationCheck.Structure &&
                    f.Evaluation == FindingEvaluation.Fatal &&
                    f.CellAddresses == addr &&
                    f.CheckResult.Contains("duplicated", StringComparison.Ordinal) &&
                    f.CheckResult.Contains("x1", StringComparison.Ordinal),
                    because: $"expected Fatal/Structure finding at {addr} with 'duplicated' and 'x1'");

            // ── Complementarity: no input-presence (MissingResponse) findings ──
            // L6, L8, L13 are blank, but all three rows are NotEvaluatedMalformedKey
            // → RequiredInputCellAnyValue skips them. Q2 (row 7, valid) has L7 filled.
            report.Findings.Should().NotContain(
                f => f.Check == ValidationCheck.MissingResponse,
                "input-presence checks must be suppressed on malformed-key rows");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task BlankXrefId_SurfacedAsStructureFatal_AndSuppressesInputPresence()
    {
        // Baseline layout:
        //   Q1 @ row 6  (xref x1, single-row)
        //   Q2 @ row 7  (xref x2, single-row)
        //   Q3 @ rows 8–10 (xref x3, multi-row)
        //   Q4 @ row 13 (xref x4, single-row)
        //
        // Perturbation (current workbook only):
        //   Q7  (Q2's XrefId)        ← "" — blank: parser emits a degenerate null-key record
        //   L7  (Q2's MaterialChange) ← "" — blank: proves malformed row suppresses input-presence
        //
        // Expected: exactly 1 finding — Q7 blank XrefId (Fatal, Structure).
        // Complementarity: no MissingResponse finding despite L7 blank
        //   (degenerate record has WithinYear == NotEvaluatedMalformedKey
        //    → RequiredInputCellAnyValue skips it).

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

            using (var wb = new XLWorkbook(currentPath))
            {
                var ws = wb.Worksheets.First();
                ws.Cell(7, "Q").Value = ""; // blank Q2's XrefId — degenerate null-key record
                ws.Cell(7, "L").Value = ""; // blank L7 — proves malformed row suppresses input-presence
                wb.Save();
            }

            var task = new RiskLevelQuestionValidationV01Task(
                new ClosedXmlExcelStructureReader(
                    NullLogger<ClosedXmlExcelStructureReader>.Instance),
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

            // ── Exact-set: exactly 1 finding ──
            report.Findings.Should().HaveCount(1,
                "exactly one blank-key finding expected (Q2's XrefId at Q7); actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            report.Findings.Should().Contain(f =>
                f.Check == ValidationCheck.Structure &&
                f.Evaluation == FindingEvaluation.Fatal &&
                f.CellAddresses == "Q7" &&
                f.CheckResult.Contains("blank", StringComparison.Ordinal),
                because: "expected Fatal/Structure finding at Q7 with 'blank' in CheckResult");

            // ── Complementarity: no MissingResponse findings ──
            // L7 is blank but row 7 is NotEvaluatedMalformedKey → RequiredInputCellAnyValue skips it.
            report.Findings.Should().NotContain(
                f => f.Check == ValidationCheck.MissingResponse,
                "input-presence checks must be suppressed on the malformed (blank-XrefId) row");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
