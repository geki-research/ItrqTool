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

namespace ItrqTool.Integration.Tests.RlqV02;

/// <summary>
/// Perturbation tests for Rule 1 (ConditionalRequirement check): M (HowExplanation) is
/// conditionally required when L (MaterialChange) holds a configured trigger value ("Yes").
/// Each test asserts by the Check == ConditionalRequirement SUBSET of findings only —
/// ignoring all other checks (lessons 83/112).
/// </summary>
public sealed class RlqV02ConditionalRequirementPerturbationTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-rlqv02-condreq", Guid.NewGuid().ToString("N"));

    private static RiskLevelQuestionValidationV02Task BuildTask()
    {
        var reader = new ClosedXmlExcelStructureReader(NullLogger<ClosedXmlExcelStructureReader>.Instance);
        return new(reader, StructureGateTestSupport.Mediator(reader),
            NullLogger<RiskLevelQuestionValidationV02Task>.Instance);
    }

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

    /// <summary>
    /// Test 1: L="Yes" ∧ M blank → exactly one ConditionalRequirement Error at M anchor.
    /// Perturbs Q1 (anchor row 6, single-row question).
    /// </summary>
    [Fact]
    public async Task LYes_MBlank_SingleRow_OneErrorAtMAnchor()
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

            // Perturb Q1 (row 6): set L6="Yes" (trigger met), clear M6 (target blank).
            using (var wb = new XLWorkbook(currentPath))
            {
                var ws = wb.Worksheets.First();
                ws.Cell(6, "L").Value = "Yes";
                ws.Cell(6, "M").Value = "";
                wb.Save();
            }

            var report = await RunAsync(currentPath, templatePath, previousPath, configPath, reportPath, dir);

            var crFindings = report.Findings
                .Where(f => f.Check == ValidationCheck.ConditionalRequirement)
                .ToList();

            crFindings.Should().HaveCount(1,
                "exactly one ConditionalRequirement finding expected; actual: {0}",
                string.Join("; ", crFindings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            crFindings[0].Evaluation.Should().Be(FindingEvaluation.Error);
            crFindings[0].CellAddresses.Should().Be("M6",
                "finding must reference the M column anchor of Q1");
            crFindings[0].CheckResult.Should().Contain("is blank",
                "check result must describe the blank target cell");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    /// <summary>
    /// Test 2: L="Yes" ∧ M present → no ConditionalRequirement finding
    /// (trigger met but target is satisfied).
    /// Perturbs Q1 (anchor row 6, single-row question).
    /// </summary>
    [Fact]
    public async Task LYes_MPresent_SingleRow_NoFindings()
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

            // Perturb Q1 (row 6): set L6="Yes" (trigger met); M6 stays non-blank (factory wrote it).
            using (var wb = new XLWorkbook(currentPath))
            {
                var ws = wb.Worksheets.First();
                ws.Cell(6, "L").Value = "Yes";
                wb.Save();
            }

            var report = await RunAsync(currentPath, templatePath, previousPath, configPath, reportPath, dir);

            var crFindings = report.Findings
                .Where(f => f.Check == ValidationCheck.ConditionalRequirement)
                .ToList();

            crFindings.Should().BeEmpty(
                "no ConditionalRequirement finding expected when trigger is met but M is present; " +
                "actual: {0}",
                string.Join("; ", crFindings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    /// <summary>
    /// Test 3: L="No" ∧ M blank → no ConditionalRequirement finding (trigger not met).
    /// Perturbs Q1 (anchor row 6, single-row question).
    /// </summary>
    [Fact]
    public async Task LNo_MBlank_SingleRow_NoFindings()
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

            // Perturb Q1 (row 6): L6 stays "No" (trigger not met), clear M6 (target blank).
            using (var wb = new XLWorkbook(currentPath))
            {
                var ws = wb.Worksheets.First();
                ws.Cell(6, "M").Value = "";
                wb.Save();
            }

            var report = await RunAsync(currentPath, templatePath, previousPath, configPath, reportPath, dir);

            var crFindings = report.Findings
                .Where(f => f.Check == ValidationCheck.ConditionalRequirement)
                .ToList();

            crFindings.Should().BeEmpty(
                "no ConditionalRequirement finding expected when L is 'No' (trigger not met); " +
                "actual: {0}",
                string.Join("; ", crFindings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    /// <summary>
    /// Test 4: L="Yes" ∧ M blank on Q3 (anchor row 8, continuation rows 9–10; L and M are
    /// merged once-per-question) → exactly one ConditionalRequirement finding at M8, NOT one
    /// per continuation row. Proves the finding is emitted once per question at the anchor.
    /// </summary>
    [Fact]
    public async Task LYes_MBlank_MultiRowQuestion_OneFindingAtAnchorNotPerRow()
    {
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

            RlqV02BaselineFactory.WriteCurrent(currentPath);
            RlqV02BaselineFactory.WriteTemplate(templatePath);
            RlqV02BaselineFactory.WritePrevious(previousPath);
            await File.WriteAllTextAsync(configPath, RlqV02BaselineFactory.SyntheticConfigJson);

            // Perturb Q3 (anchor row 8): set L8="Yes" (trigger met), clear M8 (once-per-question
            // merged cell — its anchor value drives the parsed MaterialChange/HowExplanation fields).
            using (var wb = new XLWorkbook(currentPath))
            {
                var ws = wb.Worksheets.First();
                ws.Cell(Q3AnchorRow, "L").Value = "Yes";
                ws.Cell(Q3AnchorRow, "M").Value = "";
                wb.Save();
            }

            var report = await RunAsync(currentPath, templatePath, previousPath, configPath, reportPath, dir);

            var crFindings = report.Findings
                .Where(f => f.Check == ValidationCheck.ConditionalRequirement)
                .ToList();

            crFindings.Should().HaveCount(1,
                "exactly one ConditionalRequirement finding expected for Q3 " +
                "(multi-row question collapses to one parsed record at the anchor); actual: {0}",
                string.Join("; ", crFindings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            crFindings[0].Evaluation.Should().Be(FindingEvaluation.Error);
            crFindings[0].CellAddresses.Should().Be($"M{Q3AnchorRow}",
                "finding must reference the M column anchor row of Q3, not a continuation row");
            crFindings[0].CheckResult.Should().Contain("is blank",
                "check result must describe the blank target cell");

            report.Findings.Should().NotContain(
                f => f.Check == ValidationCheck.ConditionalRequirement
                  && f.CellAddresses == $"M{Q3ContRow1}",
                "continuation row {0} must not generate a separate ConditionalRequirement finding",
                Q3ContRow1);
            report.Findings.Should().NotContain(
                f => f.Check == ValidationCheck.ConditionalRequirement
                  && f.CellAddresses == $"M{Q3ContRow2}",
                "continuation row {0} must not generate a separate ConditionalRequirement finding",
                Q3ContRow2);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    /// <summary>
    /// Test 5 (clean-guard): no perturbation → ConditionalRequirement subset is empty.
    /// All factory L values are "No", so the trigger never fires.
    /// Complements the 3a baseline-zero e2e guard.
    /// </summary>
    [Fact]
    public async Task CleanBaseline_NoConditionalRequirementFindings()
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

            var crFindings = report.Findings
                .Where(f => f.Check == ValidationCheck.ConditionalRequirement)
                .ToList();

            crFindings.Should().BeEmpty(
                "clean baseline must produce no ConditionalRequirement findings " +
                "(all L values are 'No' in the factory); actual: {0}",
                string.Join("; ", crFindings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
