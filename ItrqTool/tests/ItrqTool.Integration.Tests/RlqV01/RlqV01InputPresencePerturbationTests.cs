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

public sealed record RlqExpectedFinding(
    ValidationCheck Check,
    FindingEvaluation Evaluation,
    string CellAddresses,
    string CheckResultSubstring);

/// <summary>
/// Exact-set test for the two chunk-2 finding-1 input-presence findings:
/// material-change missing (Error) and answer missing (Error).
/// </summary>
public sealed class RlqV01InputPresencePerturbationTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-rlqv01-presence", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TwoInputPresencePerturbations_ExactlyTwoFindings_NoExtras()
    {
        // Perturb two DIFFERENT single-row questions in the current workbook:
        //   Q1 anchor row 6 (xref x1): blank L6 → input-cell.material-change.missing (Error)
        //   Q2 anchor row 7 (xref x2): blank H7 → input-cell.answer.missing (Error)
        // The template and previous workbooks are left unmodified (full L and H values),
        // so cross-year alignment is unaffected and the baseline e2e test stays green.

        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            // Write the clean baseline trio (reuse read-only — do NOT modify the factory).
            RlqV01BaselineFactory.WriteCurrent(currentPath);
            RlqV01BaselineFactory.WriteTemplate(templatePath);
            RlqV01BaselineFactory.WritePrevious(previousPath);
            await File.WriteAllTextAsync(configPath, RlqV01BaselineFactory.SyntheticConfigJson);

            // Reopen the current workbook and blank the two target cells.
            // L6 = MaterialChange on Q1 (single-row, no merge); H7 = Answer on Q2 (single-row).
            using (var wb = new XLWorkbook(currentPath))
            {
                var ws = wb.Worksheets.First();
                ws.Cell(6, "L").Value = "";   // blank material-change on Q1
                ws.Cell(7, "H").Value = "";   // blank answer on Q2
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

            var expected = new[]
            {
                new RlqExpectedFinding(ValidationCheck.MissingResponse, FindingEvaluation.Error, "L6",
                    "is empty; the value was not provided"),
                new RlqExpectedFinding(ValidationCheck.MissingResponse, FindingEvaluation.Error, "H7",
                    "is empty; the value was not provided"),
            };

            report.Findings.Should().HaveCount(2,
                "exactly two input-presence findings expected; actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            foreach (var ex in expected)
                report.Findings.Should().Contain(f =>
                    f.Check == ex.Check &&
                    f.Evaluation == ex.Evaluation &&
                    f.CellAddresses == ex.CellAddresses &&
                    f.CheckResult.Contains(ex.CheckResultSubstring, StringComparison.Ordinal),
                    because: $"expected [{ex.Evaluation}] {ex.Check} @ {ex.CellAddresses} containing '{ex.CheckResultSubstring}'");

            var unexpected = report.Findings
                .Where(f => !expected.Any(ex =>
                    ex.Check == f.Check && ex.Evaluation == f.Evaluation &&
                    ex.CellAddresses == f.CellAddresses &&
                    f.CheckResult.Contains(ex.CheckResultSubstring, StringComparison.Ordinal)))
                .ToList();
            unexpected.Should().BeEmpty("no unexpected findings allowed; found: {0}",
                string.Join("; ", unexpected.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task MultiRowQuestion_BlankAnchorMaterialChange_OneFindingAtAnchor_NoContinuationNoise()
    {
        // Q3 is the multi-row question in the baseline factory: anchor row 8, continuation rows 9–10
        // (C/D/E/F/G/H/L/O are merged over rows 8–10 into a single parsed question).
        // Blanking L8 (the anchor of the merged material-change range) must produce exactly ONE
        // finding at "L8" — not one per spanned row. Continuation rows 9 and 10 must be silent
        // because the parser collapses the group into a single AlignedQuestion with RowNumber == 8.
        const int Q3AnchorRow = 8;
        const int Q3ContRow1  = 9;
        const int Q3ContRow2  = 10;

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

            // Blank the merged L cell on Q3's anchor row. The continuation cells L9/L10 already
            // carry no value (merged range value lives on the anchor in ClosedXML).
            using (var wb = new XLWorkbook(currentPath))
            {
                var ws = wb.Worksheets.First();
                ws.Cell(Q3AnchorRow, "L").Value = "";
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

            // Exactly ONE finding — one collapsed question, one blank anchor cell.
            report.Findings.Should().HaveCount(1,
                "exactly one finding expected (multi-row group collapses to one question); actual: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            var finding = report.Findings[0];
            finding.Check.Should().Be(ValidationCheck.MissingResponse);
            finding.Evaluation.Should().Be(FindingEvaluation.Error);
            finding.CellAddresses.Should().Be($"L{Q3AnchorRow}",
                "the finding must reference the anchor row, not a continuation row");
            finding.CheckResult.Should().Contain("is empty");

            // Explicit no-continuation-noise assertions (document the intent separately from count==1).
            report.Findings.Should().NotContain(
                f => f.CellAddresses == $"L{Q3ContRow1}",
                "continuation row {0} must not generate a separate finding", Q3ContRow1);
            report.Findings.Should().NotContain(
                f => f.CellAddresses == $"L{Q3ContRow2}",
                "continuation row {0} must not generate a separate finding", Q3ContRow2);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
