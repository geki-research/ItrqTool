using System.IO;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Presentation;
using ItrqTool.Tasks.Validation;

namespace ItrqTool.Integration.Tests.RlqV01;

/// <summary>
/// End-to-end baseline-zero test: runs the committed <c>rlq-v01-validation-trial.json</c>
/// workflow through the workflow engine against three synthetic RLQ workbooks, asserts the
/// session reaches <c>Completed</c>, the validation report contains zero findings, and the
/// <c>FeedbackChecklistAssembler</c> produces a checklist .xlsx.
/// <para>
/// This is the first e2e of the RLQ task's execute-path (read → RlqV01QuestionParser →
/// DV-patch → RunFromParsed → serialize → FeedbackChecklistAssembler → checklist). All
/// existing src files are consumed read-only; no 1a/1b/core/CLQ file is modified.
/// </para>
/// </summary>
public sealed class RlqV01EndToEndWorkflowTests
{
    private static DirectoryInfo FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException(
            "Solution root (.slnx) not found above test output directory.");
    }

    [Fact]
    public async Task TrialWorkflow_BaselineZero_WorkflowSucceedsAndReportHasNoFindings()
    {
        var solutionRoot = FindSolutionRoot();

        // ── Stage workbooks under BaseDirectory so StaticFileSource resolves them
        //    via their relative sourcePath ("trial-workbooks/rlq-v01/…").
        var workbooksDir = Path.Combine(AppContext.BaseDirectory, "trial-workbooks", "rlq-v01");
        Directory.CreateDirectory(workbooksDir);
        var currentPath  = Path.Combine(workbooksDir, "rlq_current_response.xlsx");
        var templatePath = Path.Combine(workbooksDir, "rlq_empty_template.xlsx");
        var previousPath = Path.Combine(workbooksDir, "rlq_previous_response.xlsx");
        RlqV01BaselineFactory.WriteCurrent(currentPath);
        RlqV01BaselineFactory.WriteTemplate(templatePath);
        RlqV01BaselineFactory.WritePrevious(previousPath);

        // ── Stage configs under BaseDirectory so tasks can open them by their
        //    relative configurationFullFilename ("configs/…") against CWD == BaseDirectory.
        var configsOut = Path.Combine(AppContext.BaseDirectory, "configs");
        Directory.CreateDirectory(configsOut);

        // Synthetic RLQ config — generated at test runtime; NOT a committed production asset.
        await File.WriteAllTextAsync(
            Path.Combine(configsOut, "rlq-v01-validation-trial-config.json"),
            RlqV01BaselineFactory.SyntheticConfigJson);

        // FeedbackChecklistAssembler config — copy from repo (same as CLQ-v02 trial).
        File.Copy(
            Path.Combine(solutionRoot.FullName, "configs", "feedback-checklist-assembler-config.json"),
            Path.Combine(configsOut, "feedback-checklist-assembler-config.json"), overwrite: true);

        // ── Workflow engine setup ─────────────────────────────────────────────────
        var workflowsDir    = Path.Combine(Path.GetTempPath(),
            "ItrqTool-rlq-e2e-wf-" + Guid.NewGuid().ToString("N"));
        var workflowDataRoot = Path.Combine(Path.GetTempPath(),
            "ItrqTool-rlq-e2e-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workflowsDir);
        Directory.CreateDirectory(workflowDataRoot);

        try
        {
            File.Copy(
                Path.Combine(solutionRoot.FullName, "workflows", "rlq-v01-validation-trial.json"),
                Path.Combine(workflowsDir, "rlq-v01-validation-trial.json"));

            var services = new ServiceCollection();
            services.AddItrqToolServices(workflowsDir, workflowDataRoot);
            using var sp = services.BuildServiceProvider();

            var loader     = sp.GetRequiredService<IWorkflowLoader>();
            var loadResult = loader.LoadAll();
            loadResult.Failures.Should().BeEmpty("trial workflow JSON must load without errors");
            var workflow = loadResult.Workflows.Single(w => w.HierarchicalPath == "rlq-v01-validation-trial");

            var factory = sp.GetRequiredService<WorkflowSessionFactory>();
            var session = factory.Create(workflow);

            // ── Run all five tasks ────────────────────────────────────────────────
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

            // ── Assert validation report exists, is valid JSON, and has zero findings ──
            var reportPath = Path.Combine(session.WorkingDirectory, "rlq-v01-validation-report.json");
            File.Exists(reportPath).Should().BeTrue(
                "RiskLevelQuestionValidation_v01 must produce the report file");

            var reportJson = await File.ReadAllTextAsync(reportPath);
            var report = ValidationReportSerializer.Deserialize(reportJson);

            report.Findings.Should().BeEmpty(
                "baseline-zero: the RLQ_v01 profile carries an empty finding catalogue; " +
                "no findings are expected against a consistent synthetic workbook trio");

            report.Sheet.Should().Be("IT Risk Level Questions",
                "the report must reflect the RLQ sheet name from the synthetic config");
            report.TaskType.Should().Be("RiskLevelQuestionValidation_v01",
                "the report must record the correct task type");

            // ── Assert FeedbackChecklistAssembler produced the checklist .xlsx ────
            var checklistPath = Path.Combine(session.WorkingDirectory, "rlq-v01-feedback-checklist.xlsx");
            File.Exists(checklistPath).Should().BeTrue(
                "FeedbackChecklistAssembler must produce the checklist file");
            new FileInfo(checklistPath).Length.Should().BeGreaterThan(1024,
                "checklist must be a real Excel workbook, not an empty file");

            // ── Emit artifacts to trial-output for human inspection ───────────────
            var trialOutputDir = Path.Combine(solutionRoot.FullName, "trial-output", "rlq-v01");
            Directory.CreateDirectory(trialOutputDir);
            File.Copy(currentPath,
                Path.Combine(trialOutputDir, "rlq_current_response.xlsx"),  overwrite: true);
            File.Copy(templatePath,
                Path.Combine(trialOutputDir, "rlq_empty_template.xlsx"),    overwrite: true);
            File.Copy(previousPath,
                Path.Combine(trialOutputDir, "rlq_previous_response.xlsx"), overwrite: true);
            File.Copy(checklistPath,
                Path.Combine(trialOutputDir, "rlq-v01-feedback-checklist.xlsx"), overwrite: true);
        }
        finally
        {
            try { Directory.Delete(workflowsDir,    recursive: true); } catch (IOException) { }
            try { Directory.Delete(workflowDataRoot, recursive: true); } catch (IOException) { }
        }
    }
}
