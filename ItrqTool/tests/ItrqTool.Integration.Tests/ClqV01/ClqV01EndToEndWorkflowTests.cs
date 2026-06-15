using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Presentation;
using ItrqTool.Tasks.ControlLevelQuestionValidation;

namespace ItrqTool.Integration.Tests.ClqV01;

/// <summary>
/// End-to-end test that runs the committed <c>clq-v01-validation-trial.json</c> workflow
/// through the workflow engine over the full 23-finding perturbed scenario, then emits
/// the three workbooks and the assembled checklist to <c>&lt;repo&gt;/trial-output/clq-v01/</c>
/// for the human live trial.
/// </summary>
public sealed class ClqV01EndToEndWorkflowTests
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
    public async Task TrialWorkflow_FullScenario_WorkflowSucceedsAndChecklistHasFindings()
    {
        var solutionRoot = FindSolutionRoot();

        // ── Load config to build baseline ─────────────────────────────────────────
        var configAbsPath = Path.Combine(
            solutionRoot.FullName, "configs", "clq-v01-validation-config.json");
        var configJson = await File.ReadAllTextAsync(configAbsPath);
        var config = ControlLevelQuestionValidationV01ConfigLoader.Load(configJson);

        // ── Build the full 23-finding perturbed scenario ──────────────────────────
        var baseline   = ClqV01BaselineFactory.Build(config);
        var structural = ClqV01TrialScenario.BuildStructuralPerturbations(baseline);
        var scenario   = ClqV01TrialScenario.Build(baseline, structural);

        // ── Stage workbooks under BaseDirectory so StaticFileSource can resolve them
        //    via their relative sourcePath ("trial-workbooks/clq-v01/…").
        var workbooksDir = Path.Combine(AppContext.BaseDirectory, "trial-workbooks", "clq-v01");
        Directory.CreateDirectory(workbooksDir);
        var currentPath  = Path.Combine(workbooksDir, "clq_current_response.xlsx");
        var templatePath = Path.Combine(workbooksDir, "clq_empty_template.xlsx");
        var previousPath = Path.Combine(workbooksDir, "clq_previous_response.xlsx");
        ClqV01WorkbookWriter.Write(currentPath,  config.SheetName, scenario.Trio.Current,
            scenario.CurrentAnswerDvOverrides);
        ClqV01WorkbookWriter.Write(templatePath, config.SheetName, scenario.Trio.Template);
        ClqV01WorkbookWriter.Write(previousPath, config.SheetName, scenario.Trio.Previous);

        // ── Stage configs under BaseDirectory so tasks can open them by their
        //    relative configurationFullFilename ("configs/…") against CWD == BaseDirectory.
        var configsOut = Path.Combine(AppContext.BaseDirectory, "configs");
        Directory.CreateDirectory(configsOut);
        File.Copy(configAbsPath,
            Path.Combine(configsOut, "clq-v01-validation-config.json"), overwrite: true);
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
                Path.Combine(solutionRoot.FullName, "workflows", "clq-v01-validation-trial.json"),
                Path.Combine(workflowsDir, "clq-v01-validation-trial.json"));

            var services = new ServiceCollection();
            services.AddItrqToolServices(workflowsDir, workflowDataRoot);
            using var sp = services.BuildServiceProvider();

            var loader     = sp.GetRequiredService<IWorkflowLoader>();
            var loadResult = loader.LoadAll();
            loadResult.Failures.Should().BeEmpty("trial workflow JSON must load without errors");
            var workflow = loadResult.Workflows.Single(w => w.Id == "clq-v01-validation-trial");

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
                session.WorkingDirectory, "clq-v01-feedback-checklist.xlsx");
            File.Exists(checklistPath).Should().BeTrue(
                "FeedbackChecklistAssembler must produce the checklist file");
            new FileInfo(checklistPath).Length.Should().BeGreaterThan(1024,
                "checklist must be a real Excel workbook, not an empty file");

            using var wb = new XLWorkbook(checklistPath);
            var ws = wb.Worksheet("Checklist");

            // First finding must be in row 2 (dataStartRow = 2).
            ws.Cell("A2").GetString().Should().NotBeEmpty(
                "at least one finding must be written to the checklist");

            // Spot-check: the H23 MissingResponse finding ("H23 is empty") must flow through.
            var dataRows = ws.RowsUsed().Skip(1).ToList();
            dataRows.Should().Contain(
                r => r.Cell("D").GetString().Contains("H23"),
                "the MissingResponse/Error finding at cell H23 must appear in the checklist CellAddresses column");

            // Spot-check: at least one Fatal evaluation must be present (row 30 + structural rows).
            dataRows.Should().Contain(
                r => r.Cell("H").GetString() == "Fatal",
                "at least one Fatal finding must appear in the Evaluation column");

            // ── Emit artifacts to trial-output for human inspection ───────────────
            var trialOutputDir = Path.Combine(
                solutionRoot.FullName, "trial-output", "clq-v01");
            Directory.CreateDirectory(trialOutputDir);
            File.Copy(currentPath,
                Path.Combine(trialOutputDir, "clq_current_response.xlsx"),  overwrite: true);
            File.Copy(templatePath,
                Path.Combine(trialOutputDir, "clq_empty_template.xlsx"),    overwrite: true);
            File.Copy(previousPath,
                Path.Combine(trialOutputDir, "clq_previous_response.xlsx"), overwrite: true);
            File.Copy(checklistPath,
                Path.Combine(trialOutputDir, "clq-v01-feedback-checklist.xlsx"), overwrite: true);
        }
        finally
        {
            try { Directory.Delete(workflowsDir,    recursive: true); } catch (IOException) { }
            try { Directory.Delete(workflowDataRoot, recursive: true); } catch (IOException) { }
        }
    }
}
