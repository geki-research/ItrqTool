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

namespace ItrqTool.Integration.Tests.RlqV02;

/// <summary>
/// Perturbation tests for BL-025 chunk (b): the <c>dv-vocabulary-unresolvable</c> surface in
/// <c>DvConformanceCell</c> fires through the real RLQ v02 validate path when an answer-cell
/// (H, role "answer") or material-change-cell (L, role "material-change") List DV has a
/// controlled vocabulary that cannot be resolved from the empty template.
/// <para>
/// Each test asserts by <c>Check</c> + <c>CellAddresses</c> + a <c>CheckResult</c> substring
/// to disambiguate the new finding from the <c>not-conformant</c> finding (lessons 83/112).
/// Neither finding-id strings nor total finding counts are asserted.
/// </para>
/// </summary>
public sealed class RlqV02DvVocabularyUnresolvablePerturbationTests
{
    // H and L anchor rows in the fixture (Q1=6, Q2=7, Q3=8 anchor, Q4=13).
    private static readonly int[] AnchorRows = [6, 7, 8, 13];

    // Substring present in every dv-vocabulary-unresolvable CheckResult, absent in not-conformant.
    private const string UnresolvableSubstring = "could not be resolved from the empty template";

    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-rlqv02-dvvocab", Guid.NewGuid().ToString("N"));

    private static RiskLevelQuestionValidationV02Task BuildTask() =>
        new(
            new ClosedXmlExcelStructureReader(
                NullLogger<ClosedXmlExcelStructureReader>.Instance),
            NullLogger<RiskLevelQuestionValidationV02Task>.Instance);

    private static async Task<ValidationReport> RunAsync(
        string currentPath, string templatePath, string previousPath,
        string configPath, string reportPath, string dir)
    {
        var result = await BuildTask().ExecuteAsync(
            new TaskExecutionContext(
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
            },
            CancellationToken.None);

        result.Succeeded.Should().BeTrue(
            "task must succeed; errors: {0}",
            string.Join("; ", result.Messages.Select(m => m.Text)));

        return ValidationReportSerializer.Deserialize(await File.ReadAllTextAsync(reportPath));
    }

    // Post-processes a copy of the factory template: replaces H DV on each anchor row with
    // "=NonExistentListName" (NamedRange List). DvRangeRefResolver calls
    // ResolveDefinedNameValues("NonExistentListName") → null (name not in workbook) →
    // AnswerDvListValues stays null → DvConformanceEvaluator returns UnresolvableList → finding fires.
    private static void WriteTemplateWithUnresolvableHDv(string path)
    {
        RlqV02BaselineFactory.WriteTemplate(path);
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.First();
        foreach (var row in AnchorRows)
            ws.Cell(row, "H").CreateDataValidation().List("=NonExistentListName");
        wb.Save();
    }

    // Post-processes a copy of the factory template: replaces L DV on each anchor row with
    // "=NonExistentListName" (NamedRange List). Same mechanism as H above but for column L.
    // Mirrors WriteTemplateWithUnresolvableLDv in RlqV02ConfiguredTriggerInDvListPerturbationTests.
    private static void WriteTemplateWithUnresolvableLDv(string path)
    {
        RlqV02BaselineFactory.WriteTemplate(path);
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.First();
        foreach (var row in AnchorRows)
            ws.Cell(row, "L").CreateDataValidation().List("=NonExistentListName");
        wb.Save();
    }

    /// <summary>
    /// Scenario 1: template H DV is a NamedRange List referencing an undefined name →
    /// AnswerDvListValues null for each aligned template question → one InputConformance finding
    /// per present H value (4 questions, all H values present: 1,2,3,4) → findings at H6/H7/H8/H13.
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

            RlqV02BaselineFactory.WriteCurrent(currentPath);
            // Template H DV replaced with unresolvable NamedRange List on all anchor rows.
            // Current H values (1,2,3,4) are present and non-blank; L DV stays inline "Yes,No".
            WriteTemplateWithUnresolvableHDv(templatePath);
            RlqV02BaselineFactory.WritePrevious(previousPath);
            await File.WriteAllTextAsync(configPath, RlqV02BaselineFactory.SyntheticConfigJson);

            var report = await RunAsync(currentPath, templatePath, previousPath, configPath, reportPath, dir);

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
    /// Scenario 2 — coexistence guard: template L DV unresolvable (same "=NonExistentListName"
    /// construction as the Rule-2 ConfiguredTrigger test). Two distinct surfaces fire:
    /// (a) Exactly one ConfigConsistency/Fatal at CellAddresses "L" — Rule-2 dv-list-unresolvable,
    ///     once per column. UNCHANGED by chunk (a); confirmed still present.
    /// (b) InputConformance/Error at L{row} per aligned present row — DvConformanceCell
    ///     dv-vocabulary-unresolvable (BL-025 new surface). L="No" on all 4 rows → 4 per-row findings.
    /// The two surfaces are distinct by Check (ConfigConsistency vs InputConformance) and
    /// cardinality (once-per-column vs once-per-row). Non-vacuity on (b): if 0 questions
    /// parsed, Contain fails.
    /// </summary>
    [Fact]
    public async Task MaterialChangeL_ListUnresolvable_CoexistsWithConfigConsistencyFinding()
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

            RlqV02BaselineFactory.WriteCurrent(currentPath);
            // Template L DV: "=NonExistentListName" on all anchor rows.
            // Rule-2 fires once per column (ConfigConsistency); DvConformanceCell fires per row (InputConformance).
            WriteTemplateWithUnresolvableLDv(templatePath);
            RlqV02BaselineFactory.WritePrevious(previousPath);
            await File.WriteAllTextAsync(configPath, RlqV02BaselineFactory.SyntheticConfigJson);

            var report = await RunAsync(currentPath, templatePath, previousPath, configPath, reportPath, dir);

            // (a) Rule-2: exactly one ConfigConsistency finding at column "L" (unchanged from pre-BL-025).
            var ccFindings = report.Findings
                .Where(f => f.Check == ValidationCheck.ConfigConsistency)
                .ToList();

            ccFindings.Should().HaveCount(1,
                "Rule-2 dv-list-unresolvable fires exactly once per column (not per row); actual: {0}",
                string.Join("; ", ccFindings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            ccFindings[0].CellAddresses.Should().Be("L",
                "the config-level finding references the column letter only, not a specific row");

            // (b) DvConformanceCell: per-row InputConformance finding(s) at L{row} with the new surface.
            // Proves coexistence — these are a different Check from (a) and a different cardinality.
            var unresolvableFindings = report.Findings
                .Where(f => f.Check == ValidationCheck.InputConformance
                         && f.CheckResult.Contains(UnresolvableSubstring))
                .ToList();

            unresolvableFindings.Should().Contain(
                f => f.CellAddresses == "L6",
                "x1 (row 6) L DV is unresolvable; dv-vocabulary-unresolvable must fire at L6; " +
                "actual unresolvable InputConformance findings: {0}",
                string.Join("; ", unresolvableFindings.Select(f =>
                    $"[{f.Evaluation}] @ {f.CellAddresses}: {f.CheckResult}")));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    /// <summary>
    /// Scenario 3 (clean-guard): unmodified v02 baseline workbooks (H DV = WholeNumber, L DV =
    /// inline List "Yes,No" — both resolvable). The dv-vocabulary-unresolvable surface must NOT fire.
    /// Non-vacuity for this guard is established by scenarios 1 and 2 above, which use the identical
    /// fixture geometry and confirm 4 questions are parsed and aligned.
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

            RlqV02BaselineFactory.WriteCurrent(currentPath);
            RlqV02BaselineFactory.WriteTemplate(templatePath);
            RlqV02BaselineFactory.WritePrevious(previousPath);
            await File.WriteAllTextAsync(configPath, RlqV02BaselineFactory.SyntheticConfigJson);

            var report = await RunAsync(currentPath, templatePath, previousPath, configPath, reportPath, dir);

            // H DV is WholeNumber (not a List → evaluator never reaches UnresolvableList).
            // L DV is inline "Yes,No" (ClassifySource → Inline → AnswerDvListValues resolved immediately).
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
