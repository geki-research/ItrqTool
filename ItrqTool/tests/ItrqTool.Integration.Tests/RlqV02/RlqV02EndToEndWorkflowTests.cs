using System.IO;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Presentation;
using ItrqTool.Tasks.RiskLevelQuestionValidationV02;
using ItrqTool.Tasks.Validation;

namespace ItrqTool.Integration.Tests.RlqV02;

/// <summary>
/// End-to-end baseline-zero test: runs a fixture-matching trial workflow (synthetic config
/// SectionRows 5:6-10,12:13-13 aligned to the factory fixture) through the workflow engine
/// against three synthetic RLQ workbooks, asserts the session reaches <c>Completed</c>, the
/// validation report contains zero findings, and <c>FeedbackChecklistAssembler</c> produces
/// a checklist .xlsx.
/// <para>
/// The zero-findings result is NON-VACUOUS: an inline non-vacuity guard (see below) asserts
/// exactly 4 questions are parsed from the factory workbooks using the same synthetic config —
/// proving all 11 v02 checks are silent on a clean, structurally-correct workbook trio, not
/// merely that no questions were found.  This is the standing over-fire guard for chunks 3b/3c/3d.
/// </para>
/// <para>
/// WHY a fixture-matching workflow JSON, not the committed production workflow: the committed
/// <c>rlq-v02-validation-trial.json</c> has SectionRows <c>["3:4-21","22:23-42","43:44-58","59:60-71"]</c>
/// (production ranges) while the factory fixture places data at rows 5–13 (section headers at
/// 5 and 12).  Running the production workflow against this fixture leaves the parser with no
/// matching section, so it skips every row and returns 0 questions — a vacuous zero that cannot
/// verify any check.  The fixture-matching workflow below uses
/// SectionRows <c>["5:6-10","12:13-13"]</c> via the synthetic config
/// (<c>RlqV02BaselineFactory.SyntheticConfigJson</c>) so the parser reaches all 4 questions.
/// </para>
/// </summary>
public sealed class RlqV02EndToEndWorkflowTests
{
    // Fixture-matching trial workflow JSON.
    // configurationFullFilename points at the synthetic config (rlq-v02-validation-trial-config.json,
    // SectionRows 5:6-10,12:13-13) NOT the committed production config (SectionRows 3:4-21,...).
    // The workflow id / task-type / input-output keys / sourcePaths are identical to the
    // committed production workflow; only the config filename differs.
    private const string TrialWorkflowJson = """
        {
          "id": "rlq-v02-validation-trial",
          "name": "RLQ v02 — Validation Trial",
          "tasks": [
            {
              "id": "load-current-response",
              "type": "StaticFileSource",
              "inputs": {},
              "outputs": { "output": "rlq_current_response.xlsx" },
              "parameters": {
                "sourcePath": "trial-workbooks/rlq-v02/rlq_current_response.xlsx"
              }
            },
            {
              "id": "load-empty-template",
              "type": "StaticFileSource",
              "inputs": {},
              "outputs": { "output": "rlq_empty_template.xlsx" },
              "parameters": {
                "sourcePath": "trial-workbooks/rlq-v02/rlq_empty_template.xlsx"
              }
            },
            {
              "id": "load-previous-response",
              "type": "StaticFileSource",
              "inputs": {},
              "outputs": { "output": "rlq_previous_response.xlsx" },
              "parameters": {
                "sourcePath": "trial-workbooks/rlq-v02/rlq_previous_response.xlsx"
              }
            },
            {
              "id": "validate",
              "type": "RiskLevelQuestionValidation_v02",
              "inputs": {
                "currentResponse": "load-current-response.output",
                "emptyTemplate": "load-empty-template.output",
                "previousResponse": "load-previous-response.output"
              },
              "outputs": { "report": "rlq-v02-validation-report.json" },
              "parameters": {
                "configurationFullFilename": "configs/rlq-v02-validation-trial-config.json"
              }
            },
            {
              "id": "assemble",
              "type": "FeedbackChecklistAssembler",
              "inputs": { "findings01": "validate.report" },
              "outputs": { "checklist": "rlq-v02-feedback-checklist.xlsx" },
              "parameters": {
                "configurationFullFilename": "configs/feedback-checklist-assembler-config.json"
              }
            }
          ]
        }
        """;

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
        //    via their relative sourcePath ("trial-workbooks/rlq-v02/…").
        var workbooksDir = Path.Combine(AppContext.BaseDirectory, "trial-workbooks", "rlq-v02");
        Directory.CreateDirectory(workbooksDir);
        var currentPath  = Path.Combine(workbooksDir, "rlq_current_response.xlsx");
        var templatePath = Path.Combine(workbooksDir, "rlq_empty_template.xlsx");
        var previousPath = Path.Combine(workbooksDir, "rlq_previous_response.xlsx");
        RlqV02BaselineFactory.WriteCurrent(currentPath);
        RlqV02BaselineFactory.WriteTemplate(templatePath);
        RlqV02BaselineFactory.WritePrevious(previousPath);

        // ── Stage configs under BaseDirectory so tasks can open them by their
        //    relative configurationFullFilename ("configs/…") against CWD.
        var configsOut = Path.Combine(AppContext.BaseDirectory, "configs");
        Directory.CreateDirectory(configsOut);

        // Synthetic RLQ v02 config — ACTIVE: written here and referenced by TrialWorkflowJson.
        // SectionRows ["5:6-10","12:13-13"] match the fixture layout (headers at rows 5 and 12).
        await File.WriteAllTextAsync(
            Path.Combine(configsOut, "rlq-v02-validation-trial-config.json"),
            RlqV02BaselineFactory.SyntheticConfigJson);

        // FeedbackChecklistAssembler config — copy from repo (same as CLQ/RLQ v01 trials).
        File.Copy(
            Path.Combine(solutionRoot.FullName, "configs", "feedback-checklist-assembler-config.json"),
            Path.Combine(configsOut, "feedback-checklist-assembler-config.json"), overwrite: true);

        // ── Workflow engine setup ─────────────────────────────────────────────────
        var workflowsDir    = Path.Combine(Path.GetTempPath(),
            "ItrqTool-rlqv02-e2e-wf-" + Guid.NewGuid().ToString("N"));
        var workflowDataRoot = Path.Combine(Path.GetTempPath(),
            "ItrqTool-rlqv02-e2e-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workflowsDir);
        Directory.CreateDirectory(workflowDataRoot);

        try
        {
            // Write the fixture-matching trial workflow (see TrialWorkflowJson constant above).
            // This differs from the committed production workflow only in configurationFullFilename:
            // here it references the synthetic config; the production workflow references the
            // production config (SectionRows 3:4-21,...) which would parse 0 questions against
            // this fixture — a vacuous zero that cannot verify any check.
            await File.WriteAllTextAsync(
                Path.Combine(workflowsDir, "rlq-v02-validation-trial.json"),
                TrialWorkflowJson);

            var services = new ServiceCollection();
            services.AddItrqToolServices(workflowsDir, workflowDataRoot);
            using var sp = services.BuildServiceProvider();

            var loader     = sp.GetRequiredService<IWorkflowLoader>();
            var loadResult = loader.LoadAll();
            loadResult.Failures.Should().BeEmpty("trial workflow JSON must load without errors");
            var workflow = loadResult.Workflows.Single(w => w.Id == "rlq-v02-validation-trial");

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
            var reportPath = Path.Combine(session.WorkingDirectory, "rlq-v02-validation-report.json");
            File.Exists(reportPath).Should().BeTrue(
                "RiskLevelQuestionValidation_v02 must produce the report file");

            var reportJson = await File.ReadAllTextAsync(reportPath);
            var report = ValidationReportSerializer.Deserialize(reportJson);

            report.Findings.Should().BeEmpty(
                "baseline-zero: clean synthetic v02 workbooks must produce no findings " +
                "— this is the standing over-fire guard for chunks 3b/3c/3d");

            report.Sheet.Should().Be("IT Risk Level Questions",
                "the report must reflect the RLQ sheet name from the config");
            report.TaskType.Should().Be("RiskLevelQuestionValidation_v02",
                "the report must record the correct task type");

            // ── Non-vacuity guard ──────────────────────────────────────────────────────
            // Prove the zero-findings result above is real, not vacuous: assert the synthetic
            // config (SectionRows 5:6-10,12:13-13) parses exactly 4 questions from the same
            // current workbook the validate task consumed.  The DI-resolved IExcelStructureReader
            // is the same reader the task uses, so this is an end-to-end trace of the parse path.
            // Zero parsed questions would make the zero-findings assertion meaningless; this guard
            // fails the test if that happens.
            var structureReader = sp.GetRequiredService<IExcelStructureReader>();
            var syntheticConfig = RlqV02BaselineFactory.Config();
            var profileLayout   = RlqV02Profile.Build(syntheticConfig).Layout;
            var proofRows       = structureReader.ReadRows(currentPath, syntheticConfig.SheetName);
            var proofQuestions  = RlqV02QuestionParser.Parse(
                proofRows, profileLayout, syntheticConfig, new List<TaskMessage>());
            proofQuestions.Should().HaveCount(4,
                "non-vacuity guard: the synthetic config (SectionRows 5:6-10,12:13-13) must " +
                "parse exactly 4 questions from the factory workbook — proves the zero-findings " +
                "above is a real over-fire result, not a vacuous skip-all-rows result");

            // ── Assert FeedbackChecklistAssembler produced the checklist .xlsx ────
            var checklistPath = Path.Combine(session.WorkingDirectory, "rlq-v02-feedback-checklist.xlsx");
            File.Exists(checklistPath).Should().BeTrue(
                "FeedbackChecklistAssembler must produce the checklist file");
            new FileInfo(checklistPath).Length.Should().BeGreaterThan(1024,
                "checklist must be a real Excel workbook, not an empty file");

            // ── Emit artifacts to trial-output for human inspection ───────────────
            var trialOutputDir = Path.Combine(solutionRoot.FullName, "trial-output", "rlq-v02");
            Directory.CreateDirectory(trialOutputDir);
            File.Copy(currentPath,
                Path.Combine(trialOutputDir, "rlq_current_response.xlsx"),  overwrite: true);
            File.Copy(templatePath,
                Path.Combine(trialOutputDir, "rlq_empty_template.xlsx"),    overwrite: true);
            File.Copy(previousPath,
                Path.Combine(trialOutputDir, "rlq_previous_response.xlsx"), overwrite: true);
            File.Copy(checklistPath,
                Path.Combine(trialOutputDir, "rlq-v02-feedback-checklist.xlsx"), overwrite: true);
        }
        finally
        {
            try { Directory.Delete(workflowsDir,     recursive: true); } catch (IOException) { }
            try { Directory.Delete(workflowDataRoot,  recursive: true); } catch (IOException) { }
        }
    }
}
