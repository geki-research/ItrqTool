using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Presentation;
using ItrqTool.Tasks.Validation;

namespace ItrqTool.Integration.Tests.GdV01;

/// <summary>
/// End-to-end tests for the GD v01 validation trial workflow. Drives
/// <c>StaticFileSource×3 → GeneralDataValidation_v01 → FeedbackChecklistAssembler</c>
/// through the production composition root and asserts report content, checklist
/// content (wiring proof), and HALT propagation into the deserialized report.
/// </summary>
public sealed class GdV01EndToEndWorkflowTests
{
    private static DirectoryInfo FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException(
            "Solution root (.slnx) not found above test output directory.");
    }

    private static (string workflowsDir, string workflowDataRoot) MakeTempDirs()
    {
        var wf   = Path.Combine(Path.GetTempPath(), "ItrqTool-gdv01-e2e-wf-"   + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(Path.GetTempPath(), "ItrqTool-gdv01-e2e-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(wf);
        Directory.CreateDirectory(data);
        return (wf, data);
    }

    private static void StageWorkbooks(string workbooksDir,
        out string currentPath, out string templatePath, out string previousPath)
    {
        Directory.CreateDirectory(workbooksDir);
        currentPath  = Path.Combine(workbooksDir, "gd_current_response.xlsx");
        templatePath = Path.Combine(workbooksDir, "gd_empty_template.xlsx");
        previousPath = Path.Combine(workbooksDir, "gd_previous_response.xlsx");
        GdV01BaselineFactory.WriteCurrent(currentPath);
        GdV01BaselineFactory.WriteTemplate(templatePath);
        GdV01BaselineFactory.WritePrevious(previousPath);
    }

    private static async Task StageConfigs(string configsOut, DirectoryInfo solutionRoot)
    {
        Directory.CreateDirectory(configsOut);
        await File.WriteAllTextAsync(
            Path.Combine(configsOut, "gd-v01-validation-trial-config.json"),
            GdV01BaselineFactory.SyntheticConfigJson);
        File.Copy(
            Path.Combine(solutionRoot.FullName, "configs", "feedback-checklist-assembler-config.json"),
            Path.Combine(configsOut, "feedback-checklist-assembler-config.json"), overwrite: true);
    }

    private static async Task<WorkflowSession> BuildSession(
        DirectoryInfo solutionRoot, string workflowsDir, string workflowDataRoot)
    {
        File.Copy(
            Path.Combine(solutionRoot.FullName, "workflows", "gd-v01-validation-trial.json"),
            Path.Combine(workflowsDir, "gd-v01-validation-trial.json"));

        var services = new ServiceCollection();
        services.AddItrqToolServices(workflowsDir, workflowDataRoot);
        var sp = services.BuildServiceProvider();

        var loader     = sp.GetRequiredService<IWorkflowLoader>();
        var loadResult = loader.LoadAll();
        loadResult.Failures.Should().BeEmpty("trial workflow JSON must load without errors");
        var workflow = loadResult.Workflows.Single(w => w.HierarchicalPath == "gd-v01-validation-trial");

        var factory = sp.GetRequiredService<WorkflowSessionFactory>();
        return await Task.FromResult(factory.Create(workflow));
    }

    private static async Task AdvanceToCompleted(WorkflowSession session)
    {
        while (session.Status != WorkflowSessionStatus.Completed)
        {
            var result = await session.RunCurrentTaskAsync();
            result.Succeeded.Should().BeTrue(
                "task must succeed; messages: {0}",
                string.Join("; ", result.Messages.Select(m => m.Text)));

            if (session.Status != WorkflowSessionStatus.Completed)
                session.Status.Should().Be(WorkflowSessionStatus.AwaitingReview);
        }
        session.Status.Should().Be(WorkflowSessionStatus.Completed);
    }

    // ── Fact 1 — baseline-zero / happy path ──────────────────────────────────

    [Fact]
    public async Task TrialWorkflow_BaselineZero_ReportEmptyAndChecklistExists()
    {
        var solutionRoot = FindSolutionRoot();
        var (workflowsDir, workflowDataRoot) = MakeTempDirs();

        StageWorkbooks(
            Path.Combine(AppContext.BaseDirectory, "trial-workbooks", "gd-v01"),
            out var currentPath, out var templatePath, out var previousPath);
        await StageConfigs(Path.Combine(AppContext.BaseDirectory, "configs"), solutionRoot);

        try
        {
            var session = await BuildSession(solutionRoot, workflowsDir, workflowDataRoot);
            await AdvanceToCompleted(session);

            // ── Report ────────────────────────────────────────────────────────
            var reportPath = Path.Combine(session.WorkingDirectory, "gd-v01-validation-report.json");
            File.Exists(reportPath).Should().BeTrue(
                "GeneralDataValidation_v01 must produce the report file");

            var report = ValidationReportSerializer.Deserialize(
                await File.ReadAllTextAsync(reportPath));

            report.Findings.Should().BeEmpty(
                "baseline-zero: clean synthetic trio must yield zero findings");
            report.Halted.Should().BeNull(
                "no gate halt on a clean trio");
            report.Sheet.Should().Be("General Data",
                "the report must reflect the GD sheet name from the synthetic config");
            report.TaskType.Should().Be("GeneralDataValidation_v01",
                "the report must record the correct task type");

            // ── Checklist ─────────────────────────────────────────────────────
            var checklistPath = Path.Combine(session.WorkingDirectory, "gd-v01-feedback-checklist.xlsx");
            File.Exists(checklistPath).Should().BeTrue(
                "FeedbackChecklistAssembler must produce the checklist file");
            new FileInfo(checklistPath).Length.Should().BeGreaterThan(1024,
                "checklist must be a real Excel workbook, not an empty file");

            // ── Emit artifacts ────────────────────────────────────────────────
            var trialOutputDir = Path.Combine(solutionRoot.FullName, "trial-output", "gd-v01");
            Directory.CreateDirectory(trialOutputDir);
            File.Copy(currentPath,
                Path.Combine(trialOutputDir, "gd_current_response.xlsx"),  overwrite: true);
            File.Copy(templatePath,
                Path.Combine(trialOutputDir, "gd_empty_template.xlsx"),    overwrite: true);
            File.Copy(previousPath,
                Path.Combine(trialOutputDir, "gd_previous_response.xlsx"), overwrite: true);
            File.Copy(checklistPath,
                Path.Combine(trialOutputDir, "gd-v01-feedback-checklist.xlsx"), overwrite: true);
        }
        finally
        {
            try { Directory.Delete(workflowsDir,     recursive: true); } catch (IOException) { }
            try { Directory.Delete(workflowDataRoot, recursive: true); } catch (IOException) { }
        }
    }

    // ── Fact 2 — seeded: non-vacuity proof + checklist wiring proof ──────────

    [Fact]
    public async Task TrialWorkflow_SeededHMissing_ReportExactSetAndChecklistContainsRow()
    {
        var solutionRoot = FindSolutionRoot();
        var (workflowsDir, workflowDataRoot) = MakeTempDirs();

        StageWorkbooks(
            Path.Combine(AppContext.BaseDirectory, "trial-workbooks", "gd-v01"),
            out _, out _, out _);
        await StageConfigs(Path.Combine(AppContext.BaseDirectory, "configs"), solutionRoot);

        // Reopen current and blank H4 (Q1's answer cell — G-CO anchor row 4).
        // Value-only mutate: DV intact, data deleted — mirrors GdV01RequiredInputPerturbationTests.
        var currentPath = Path.Combine(
            AppContext.BaseDirectory, "trial-workbooks", "gd-v01", "gd_current_response.xlsx");
        using (var wb = new XLWorkbook(currentPath))
        {
            wb.Worksheets.First().Cell(4, "H").Value = "";
            wb.Save();
        }

        try
        {
            var session = await BuildSession(solutionRoot, workflowsDir, workflowDataRoot);
            await AdvanceToCompleted(session);

            // ── Report exact-set (non-vacuity proof) ──────────────────────────
            var reportPath = Path.Combine(session.WorkingDirectory, "gd-v01-validation-report.json");
            var report = ValidationReportSerializer.Deserialize(
                await File.ReadAllTextAsync(reportPath));

            GdV01Assert.Exactly(report.Findings,
                new GdV01ExpectedFinding(
                    ValidationCheck.MissingResponse, FindingEvaluation.Error,
                    "H4", "not provided"));

            // ── Checklist wiring proof (E2 unique value) ──────────────────────
            var checklistPath = Path.Combine(session.WorkingDirectory, "gd-v01-feedback-checklist.xlsx");
            File.Exists(checklistPath).Should().BeTrue(
                "FeedbackChecklistAssembler must produce the checklist file");

            using var checklist = new XLWorkbook(checklistPath);
            var ws = checklist.Worksheet("Checklist");
            var dataRows = ws.RowsUsed().Skip(1).ToList();

            dataRows.Should().Contain(
                r => r.Cell("D").GetString() == "H4",
                "the seeded MissingResponse finding at H4 must appear in the CellAddresses column (D)");
            dataRows.Should().Contain(
                r => r.Cell("B").GetString() == "General Data",
                "the GD sheet name must appear in the Worksheet column (B)");
            dataRows.Should().Contain(
                r => r.Cell("H").GetString() == "Error",
                "the MissingResponse finding must appear as Error in the Evaluation column (H)");
        }
        finally
        {
            try { Directory.Delete(workflowsDir,     recursive: true); } catch (IOException) { }
            try { Directory.Delete(workflowDataRoot, recursive: true); } catch (IOException) { }
        }
    }

    // ── Fact 3 — HALT propagation through task → serialization round-trip ────

    [Fact]
    public async Task TrialWorkflow_BlankXrefId_ReportHaltedTrueAndFatalFindingPresent()
    {
        var solutionRoot = FindSolutionRoot();
        var (workflowsDir, workflowDataRoot) = MakeTempDirs();

        StageWorkbooks(
            Path.Combine(AppContext.BaseDirectory, "trial-workbooks", "gd-v01"),
            out _, out _, out _);
        await StageConfigs(Path.Combine(AppContext.BaseDirectory, "configs"), solutionRoot);

        // Reopen current and blank Q4 (Q1's XrefId cell) — mirrors
        // GdV01GateHaltTests.BlankXrefId_HaltsGate_ExactlyOneStructureFatalAtQ4.
        var currentPath = Path.Combine(
            AppContext.BaseDirectory, "trial-workbooks", "gd-v01", "gd_current_response.xlsx");
        using (var wb = new XLWorkbook(currentPath))
        {
            wb.Worksheets.First().Cell(4, "Q").Value = "";
            wb.Save();
        }

        try
        {
            var session = await BuildSession(solutionRoot, workflowsDir, workflowDataRoot);
            // The task SUCCEEDS even when halted — it writes the report and returns Succeeded:true.
            await AdvanceToCompleted(session);

            // ── Halted round-trip proof ───────────────────────────────────────
            var reportPath = Path.Combine(session.WorkingDirectory, "gd-v01-validation-report.json");
            var report = ValidationReportSerializer.Deserialize(
                await File.ReadAllTextAsync(reportPath));

            report.Halted.Should().BeTrue(
                "a blank XrefId halts the identity-integrity gate; the task must map Halted→true and round-trip it");

            report.Findings.Should().ContainSingle(
                f => f.CellAddresses == "Q4" &&
                     f.Check        == ValidationCheck.Structure &&
                     f.Evaluation   == FindingEvaluation.Fatal &&
                     f.CheckResult.Contains("blank", StringComparison.Ordinal),
                "exactly one blank-key Fatal/Structure finding at Q4 must survive serialization round-trip");
        }
        finally
        {
            try { Directory.Delete(workflowsDir,     recursive: true); } catch (IOException) { }
            try { Directory.Delete(workflowDataRoot, recursive: true); } catch (IOException) { }
        }
    }
}
