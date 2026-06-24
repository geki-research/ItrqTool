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
/// Perturbation tests for BL-025 chunk (b): the <c>dv-vocabulary-unresolvable</c> surface in
/// <c>DvConformanceCell</c> fires through the real RLQ v01 validate path when an answer-cell
/// (H, role "answer") List DV has a controlled vocabulary that cannot be resolved from the
/// empty template.
/// <para>
/// Each test asserts by <c>Check</c> + <c>CellAddresses</c> + a <c>CheckResult</c> substring
/// to disambiguate the new finding from the <c>not-conformant</c> finding (lessons 83/112).
/// Neither finding-id strings nor total finding counts are asserted.
/// </para>
/// </summary>
public sealed class RlqV01DvVocabularyUnresolvablePerturbationTests
{
    // H and L anchor rows in the fixture (Q1=6, Q2=7, Q3=8 anchor, Q4=13).
    private static readonly int[] AnchorRows = [6, 7, 8, 13];

    // Substring present in every dv-vocabulary-unresolvable CheckResult, absent in not-conformant.
    private const string UnresolvableSubstring = "could not be resolved from the empty template";

    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-rlqv01-dvvocab", Guid.NewGuid().ToString("N"));

    private static RiskLevelQuestionValidationV01Task BuildTask() =>
        new(new ClosedXmlExcelStructureReader(NullLogger<ClosedXmlExcelStructureReader>.Instance),
            NullLogger<RiskLevelQuestionValidationV01Task>.Instance);

    private static TaskExecutionContext BuildContext(
        string currentPath, string templatePath, string previousPath,
        string configPath, string reportPath, string dir) =>
        new(
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

    // Post-processes a copy of the factory template: replaces H DV on each anchor row with
    // "=NonExistentListName" (NamedRange List). DvRangeRefResolver calls
    // ResolveDefinedNameValues("NonExistentListName") → null (name not in workbook) →
    // AnswerDvListValues stays null → DvConformanceEvaluator returns UnresolvableList → finding fires.
    private static void WriteTemplateWithUnresolvableHDv(string path)
    {
        RlqV01BaselineFactory.WriteTemplate(path);
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.First();
        foreach (var row in AnchorRows)
            ws.Cell(row, "H").CreateDataValidation().List("=NonExistentListName");
        wb.Save();
    }

    /// <summary>
    /// Scenario 4: template H DV is a NamedRange List referencing an undefined name →
    /// AnswerDvListValues null for each aligned template question → one InputConformance finding
    /// per present H value (4 questions, all H values 1,2,3,4 present) → findings at H6/H7/H8/H13.
    /// Asserts on H6 (x1 anchor). Non-vacuity: if 0 questions parsed the Contain assertion fails.
    /// </summary>
    [Fact]
    public async Task AnswerH_ListUnresolvable_EmitsInputConformanceFindingAtH6()
    {
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
            // Template H DV replaced with unresolvable NamedRange List on all anchor rows.
            // Current H values (1,2,3,4) are present and non-blank; L DV stays inline "Yes,No".
            WriteTemplateWithUnresolvableHDv(templatePath);
            RlqV01BaselineFactory.WritePrevious(previousPath);
            await File.WriteAllTextAsync(configPath, RlqV01BaselineFactory.SyntheticConfigJson);

            var result = await BuildTask().ExecuteAsync(
                BuildContext(currentPath, templatePath, previousPath, configPath, reportPath, dir),
                CancellationToken.None);

            result.Succeeded.Should().BeTrue(
                "task must succeed; errors: {0}",
                string.Join("; ", result.Messages.Select(m => m.Text)));

            var report = ValidationReportSerializer.Deserialize(
                await File.ReadAllTextAsync(reportPath));

            (report.Halted ?? false).Should().BeFalse("keys are clean — identity gate must not fire");

            var unresolvableFindings = report.Findings
                .Where(f => f.Check == ValidationCheck.InputConformance
                         && f.CheckResult.Contains(UnresolvableSubstring))
                .ToList();

            // Non-vacuity: Contain fails if 0 questions parsed (fixture↔config geometry mismatch).
            unresolvableFindings.Should().NotBeEmpty(
                "parsing 0 questions yields no InputConformance findings — fixture geometry must match config; " +
                "all InputConformance findings: {0}",
                string.Join("; ", report.Findings
                    .Where(f => f.Check == ValidationCheck.InputConformance)
                    .Select(f => $"[{f.Evaluation}] @ {f.CellAddresses}: {f.CheckResult}")));

            unresolvableFindings.Should().Contain(
                f => f.CellAddresses == "H6",
                "H DV on the x1 anchor row (6) is unresolvable; dv-vocabulary-unresolvable must fire at H6; " +
                "actual unresolvable InputConformance findings: {0}",
                string.Join("; ", unresolvableFindings.Select(f =>
                    $"[{f.Evaluation}] @ {f.CellAddresses}: {f.CheckResult}")));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    /// <summary>
    /// Scenario 5 (clean-guard): unmodified v01 baseline workbooks (H DV = WholeNumber, L DV =
    /// inline List "Yes,No" — both resolvable). The dv-vocabulary-unresolvable surface must NOT fire.
    /// Non-vacuity for this guard is established by scenario 4 above, which uses the identical
    /// fixture geometry and confirms 4 questions are parsed and aligned.
    /// </summary>
    [Fact]
    public async Task CleanBaseline_ResolvableDvs_NoUnresolvableVocabularyFinding()
    {
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

            var result = await BuildTask().ExecuteAsync(
                BuildContext(currentPath, templatePath, previousPath, configPath, reportPath, dir),
                CancellationToken.None);

            result.Succeeded.Should().BeTrue(
                "task must succeed; errors: {0}",
                string.Join("; ", result.Messages.Select(m => m.Text)));

            var report = ValidationReportSerializer.Deserialize(
                await File.ReadAllTextAsync(reportPath));

            // H DV is WholeNumber (not a List → evaluator never reaches UnresolvableList).
            // L DV is inline "Yes,No" (ClassifySource → Inline → MaterialChangeDvListValues resolved immediately).
            report.Findings
                .Where(f => f.Check == ValidationCheck.InputConformance
                         && f.CheckResult.Contains(UnresolvableSubstring))
                .Should().BeEmpty(
                    "resolvable DVs must not trigger the dv-vocabulary-unresolvable surface; " +
                    "all InputConformance findings: {0}",
                    string.Join("; ", report.Findings
                        .Where(f => f.Check == ValidationCheck.InputConformance)
                        .Select(f => $"[{f.Evaluation}] @ {f.CellAddresses}: {f.CheckResult}")));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
