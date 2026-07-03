using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Presentation;
using ItrqTool.Tasks.ControlLevelQuestionValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Config;

namespace ItrqTool.Integration.Tests.ClqV02;

/// <summary>
/// End-to-end test that runs the committed <c>clq-v02-validation-trial.json</c> workflow
/// through the workflow engine over the three-stability-finding scenario, then emits
/// the three workbooks and the assembled checklist to <c>&lt;repo&gt;/trial-output/clq-v02/</c>
/// for the human live trial.
/// </summary>
public sealed class ClqV02EndToEndWorkflowTests
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
    public async Task TrialWorkflow_StabilityScenario_WorkflowSucceedsAndChecklistHasFindings()
    {
        var solutionRoot = FindSolutionRoot();

        // ── Load config to build baseline ─────────────────────────────────────────
        var configAbsPath = Path.Combine(
            solutionRoot.FullName, "configs", "clq-v02-validation-config.json");
        var configJson = await File.ReadAllTextAsync(configAbsPath);
        var config = ConfigLoader.Load<ControlLevelQuestionValidationV02Config>(
            configJson, c => c.Validate());

        // ── Build the three-stability-finding scenario ────────────────────────────
        var trio = ClqV02BaselineFactory.Build(config);
        var perturbedQuestions = trio.Current.Questions
            .Select(q => q.RowNumber switch
            {
                7 => q with { AnswerStability = null },       // → missing @ K7 (Error)
                8 => q with { AnswerStability = "Maybe" },    // → not-in-allowed-set @ K8 (Fatal)
                _ => q
            })
            .ToList();
        var perturbedCurrent = trio.Current with { Questions = perturbedQuestions };
        var stabilityDvOverrides = new Dictionary<int, string> { [9] = "\"Yes\"" }; // → rule-changed @ K9 (Error)

        // ── Stage workbooks under BaseDirectory so StaticFileSource can resolve them
        //    via their relative sourcePath ("trial-workbooks/clq-v02/…").
        var workbooksDir = Path.Combine(AppContext.BaseDirectory, "trial-workbooks", "clq-v02");
        Directory.CreateDirectory(workbooksDir);
        var currentPath  = Path.Combine(workbooksDir, "clq_current_response.xlsx");
        var templatePath = Path.Combine(workbooksDir, "clq_empty_template.xlsx");
        var previousPath = Path.Combine(workbooksDir, "clq_previous_response.xlsx");
        ClqV02WorkbookWriter.Write(currentPath,  config.SheetName, perturbedCurrent,
            stabilityDvOverrides: stabilityDvOverrides);
        ClqV02WorkbookWriter.Write(templatePath, config.SheetName, trio.Template);
        ClqV02WorkbookWriter.Write(previousPath, config.SheetName, trio.Previous);

        // ── Stage configs under BaseDirectory so tasks can open them by their
        //    relative configurationFullFilename ("configs/…") against CWD == BaseDirectory.
        var configsOut = Path.Combine(AppContext.BaseDirectory, "configs");
        Directory.CreateDirectory(configsOut);
        File.Copy(configAbsPath,
            Path.Combine(configsOut, "clq-v02-validation-config.json"), overwrite: true);
        File.Copy(
            Path.Combine(solutionRoot.FullName, "configs", "feedback-checklist-assembler-config.json"),
            Path.Combine(configsOut, "feedback-checklist-assembler-config.json"), overwrite: true);

        // ── Workflow engine setup ─────────────────────────────────────────────────
        var workflowsDir    = Path.Combine(Path.GetTempPath(),
            "ItrqTool-e2e-wf-" + Guid.NewGuid().ToString("N"));
        var workflowDataRoot = Path.Combine(Path.GetTempPath(),
            "ItrqTool-e2e-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workflowsDir);
        Directory.CreateDirectory(workflowDataRoot);

        try
        {
            // Copy the committed trial workflow JSON to the temp workflows directory.
            File.Copy(
                Path.Combine(solutionRoot.FullName, "workflows", "clq-v02-validation-trial.json"),
                Path.Combine(workflowsDir, "clq-v02-validation-trial.json"));

            var services = new ServiceCollection();
            services.AddItrqToolServices(workflowsDir, workflowDataRoot);
            using var sp = services.BuildServiceProvider();

            var loader     = sp.GetRequiredService<IWorkflowLoader>();
            var loadResult = loader.LoadAll();
            loadResult.Failures.Should().BeEmpty("trial workflow JSON must load without errors");
            var workflow = loadResult.Workflows.Single(w => w.HierarchicalPath == "clq-v02-validation-trial");

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

            // ── Assert checklist XLSX is produced and non-trivially populated ─────
            var checklistPath = Path.Combine(
                session.WorkingDirectory, "clq-v02-feedback-checklist.xlsx");
            File.Exists(checklistPath).Should().BeTrue(
                "FeedbackChecklistAssembler must produce the checklist file");
            new FileInfo(checklistPath).Length.Should().BeGreaterThan(1024,
                "checklist must be a real Excel workbook, not an empty file");

            using var wb = new XLWorkbook(checklistPath);
            var ws = wb.Worksheet("Checklist");

            // First finding must be in row 2 (dataStartRow = 2).
            ws.Cell("A2").GetString().Should().NotBeEmpty(
                "at least one finding must be written to the checklist");

            // Three stability findings (K7 missing, K8 not-in-set, K9 rule-changed) and no others.
            var dataRows = ws.RowsUsed().Skip(1).ToList();

            dataRows.Should().HaveCount(3,
                "exactly the three stability findings should reach the checklist; rows: {0}",
                string.Join(" | ", dataRows.Select(r =>
                    $"D={r.Cell("D").GetString()} H={r.Cell("H").GetString()} I={r.Cell("I").GetString()}")));

            foreach (var addr in new[] { "K7", "K8", "K9" })
                dataRows.Should().Contain(r => r.Cell("D").GetString() == addr,
                    $"finding at {addr} must appear in the checklist CellAddresses column (D)");

            dataRows.Should().Contain(r => r.Cell("H").GetString() == "Fatal",
                "the not-in-allowed-set finding (K8) must appear as Fatal in the Evaluation column (H)");

            // ── Emit artifacts to trial-output for human inspection ───────────────
            var trialOutputDir = Path.Combine(
                solutionRoot.FullName, "trial-output", "clq-v02");
            Directory.CreateDirectory(trialOutputDir);
            File.Copy(currentPath,
                Path.Combine(trialOutputDir, "clq_current_response.xlsx"),  overwrite: true);
            File.Copy(templatePath,
                Path.Combine(trialOutputDir, "clq_empty_template.xlsx"),    overwrite: true);
            File.Copy(previousPath,
                Path.Combine(trialOutputDir, "clq_previous_response.xlsx"), overwrite: true);
            File.Copy(checklistPath,
                Path.Combine(trialOutputDir, "clq-v02-feedback-checklist.xlsx"), overwrite: true);
        }
        finally
        {
            try { Directory.Delete(workflowsDir,    recursive: true); } catch (IOException) { }
            try { Directory.Delete(workflowDataRoot, recursive: true); } catch (IOException) { }
        }
    }
}
