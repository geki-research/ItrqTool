using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Presentation;
using ItrqTool.Integration.Tests.ClqV01;
using ItrqTool.Integration.Tests.ClqV02;
using ItrqTool.Tasks.ControlLevelQuestionValidationV01;
using ItrqTool.Tasks.ControlLevelQuestionValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Config;

namespace ItrqTool.Integration.Tests.ClqInject;

/// <summary>
/// End-to-end trial of the whole CLQ inject chain through the workflow engine:
/// <c>StaticFileSource(previous v02) + StaticFileSource(current v01 template) →
/// ControlLevelQuestionInject_v02_to_v01 → StaticFileSink</c>, every task resolved
/// via the production composition root (real <c>ClosedXmlExcelStructureReader</c>,
/// real <c>ClosedXmlTemplateWriter</c>, real source/sink — no mocks).
///
/// Fixtures are built from the shared baseline factories so the v01 current questions
/// and v02 previous questions share XrefIds (Q001..Q193) and identical question text,
/// which makes the cross-format aligner produce <c>Agree</c> matches. Six question rows
/// are then perturbed to drive the representative spread (a)–(e):
///   • row 6  — (b) carry-forward does NOT fire (stability "Yes"): F/G/M only, H/I/J blank.
///   • row 7  — (a) carry-forward fires (stability "No"): F/G/M and H/I/J populated.
///   • row 8  — (c) merge both strengths + weaknesses → exact G string.
///   • row 9  — (c) strengths-only merge → exact G string.
///   • row 10 — (d) provided-by injected into M.
///   • row 11 — (e) current question with no v02 counterpart (unique xref + distinct text)
///              → left entirely untouched.
/// </summary>
public sealed class ClqInjectEndToEndWorkflowTests
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
    public async Task InjectWorkflow_RunsSourceInjectSinkEndToEnd_OutputLandsAtSinkWithInjectedCells()
    {
        var solutionRoot = FindSolutionRoot();
        var configsRoot = Path.Combine(solutionRoot.FullName, "configs");

        // ── Load the three configs (current v01, previous v02, inject knobs) ──────
        var v01ConfigAbs    = Path.Combine(configsRoot, "clq-v01-validation-config.json");
        var v02ConfigAbs    = Path.Combine(configsRoot, "clq-v02-validation-config.json");
        var injectConfigAbs = Path.Combine(configsRoot, "clq-inject-config.json");

        var v01Config = ConfigLoader.Load<ClqV01Config>(
            await File.ReadAllTextAsync(v01ConfigAbs), c => c.Validate());
        var v02Config = ConfigLoader.Load<ControlLevelQuestionValidationV02Config>(
            await File.ReadAllTextAsync(v02ConfigAbs), c => c.Validate());

        var sheetName = v01Config.SheetName;

        // ── Build fixtures from the shared baseline factories ─────────────────────
        // Current = v01 EMPTY template (D/E/N populated, F/G/H/I/J/M blank).
        // Previous = v02 FILLED previous-year response.
        var v01Trio = ClqV01BaselineFactory.Build(v01Config);
        var v02Trio = ClqV02BaselineFactory.Build(v02Config);

        // (e) — current row 11 gets a unique xref + distinct text so it has no v02
        // counterpart; the aligner must classify it Neither and leave it untouched.
        var currentQuestions = v01Trio.Template.Questions
            .Select(q => q.RowNumber == 11
                ? q with
                {
                    XrefId = "QX011",
                    OriginalText =
                        "ZZZ unmatched sentinel question with no counterpart in the previous response."
                }
                : q)
            .ToList();
        var currentDescriptor = v01Trio.Template with { Questions = currentQuestions };

        // Perturb the v02 previous response to drive (a)–(d).
        var previousQuestions = v02Trio.Previous.Questions
            .Select(q => q.RowNumber switch
            {
                // (b) no carry-forward: stability "Yes" → F/G/M only.
                6  => q with
                {
                    Answer = "4", Strengths = "S6 baseline", Weaknesses = "W6 baseline",
                    ProvidedBy = "Org6", AnswerStability = "Yes"
                },
                // (a) carry-forward fires: stability "No" → F/G/M and H/I/J.
                7  => q with
                {
                    Answer = "3", Strengths = "S7 carry", Weaknesses = "W7 carry",
                    ProvidedBy = "Org7", AnswerStability = "No"
                },
                // (c) merge both strengths + weaknesses.
                8  => q with
                {
                    Answer = "2", Strengths = "S8", Weaknesses = "W8", AnswerStability = "Yes"
                },
                // (c) strengths-only merge.
                9  => q with
                {
                    Strengths = "S9", Weaknesses = null, AnswerStability = "Yes"
                },
                // (d) provided-by carried into M.
                10 => q with { ProvidedBy = "Org10" },
                _  => q
            })
            .ToList();
        var previousDescriptor = v02Trio.Previous with { Questions = previousQuestions };

        // ── Stage the two input workbooks under BaseDirectory so StaticFileSource
        //    resolves them via their relative sourcePath ("trial-workbooks/clq-inject/…").
        var workbooksDir = Path.Combine(AppContext.BaseDirectory, "trial-workbooks", "clq-inject");
        Directory.CreateDirectory(workbooksDir);
        var currentPath  = Path.Combine(workbooksDir, "clq_current_template.xlsx");
        var previousPath = Path.Combine(workbooksDir, "clq_previous_response.xlsx");
        ClqV01WorkbookWriter.Write(currentPath,  sheetName, currentDescriptor);
        ClqV02WorkbookWriter.Write(previousPath, sheetName, previousDescriptor);

        // ── Stage the three configs under BaseDirectory/configs so the inject task
        //    opens clq-inject-config.json (relative to CWD == BaseDirectory) and
        //    resolves the two referenced validation configs relative to ITS directory.
        var configsOut = Path.Combine(AppContext.BaseDirectory, "configs");
        Directory.CreateDirectory(configsOut);
        File.Copy(injectConfigAbs, Path.Combine(configsOut, "clq-inject-config.json"),       overwrite: true);
        File.Copy(v01ConfigAbs,    Path.Combine(configsOut, "clq-v01-validation-config.json"), overwrite: true);
        File.Copy(v02ConfigAbs,    Path.Combine(configsOut, "clq-v02-validation-config.json"), overwrite: true);

        // ── Workflow engine setup ─────────────────────────────────────────────────
        var workflowsDir = Path.Combine(Path.GetTempPath(),
            "ItrqTool-inject-wf-" + Guid.NewGuid().ToString("N"));
        var workflowDataRoot = Path.Combine(Path.GetTempPath(),
            "ItrqTool-inject-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workflowsDir);
        Directory.CreateDirectory(workflowDataRoot);

        // The sink writes the final deliverable here — OUTSIDE the session working
        // directory — proving the sink (not the inject task) owns final placement.
        var sinkDestination = Path.Combine(
            AppContext.BaseDirectory, "inject-output", "clq-inject", "clq_injected_current.xlsx");
        try { File.Delete(sinkDestination); } catch (IOException) { }

        try
        {
            // Copy the committed trial workflow JSON to the temp workflows directory.
            File.Copy(
                Path.Combine(solutionRoot.FullName, "workflows", "clq-inject-trial.json"),
                Path.Combine(workflowsDir, "clq-inject-trial.json"));

            var services = new ServiceCollection();
            services.AddItrqToolServices(workflowsDir, workflowDataRoot);
            using var sp = services.BuildServiceProvider();

            var loader     = sp.GetRequiredService<IWorkflowLoader>();
            var loadResult = loader.LoadAll();
            loadResult.Failures.Should().BeEmpty("inject trial workflow JSON must load without errors");
            var workflow = loadResult.Workflows.Single(w => w.Id == "clq-inject-trial");

            var factory = sp.GetRequiredService<WorkflowSessionFactory>();
            var session = factory.Create(workflow);

            // ── Run all four tasks (source, source, inject, sink) ─────────────────
            TaskResult? injectResult = null;
            while (session.Status != WorkflowSessionStatus.Completed)
            {
                var currentNode = workflow.Nodes[session.CurrentIndex];
                var result = await session.RunCurrentTaskAsync();
                result.Succeeded.Should().BeTrue(
                    "task '{0}' must succeed; messages: {1}", currentNode.Id,
                    string.Join("; ", result.Messages.Select(m => m.Text)));

                if (currentNode.Id == "inject") injectResult = result;

                if (session.Status != WorkflowSessionStatus.Completed)
                    session.Status.Should().Be(WorkflowSessionStatus.AwaitingReview);
            }

            session.Status.Should().Be(WorkflowSessionStatus.Completed);

            // The inject task wrote only to the working dir; the sink owns placement.
            File.Exists(Path.Combine(session.WorkingDirectory, "injected.xlsx")).Should().BeTrue(
                "the inject task writes its populated workbook into the session working directory");
            File.Exists(sinkDestination).Should().BeTrue(
                "the StaticFileSink must place the final deliverable at destinationFolder/destinationFileName");
            Path.GetDirectoryName(sinkDestination).Should().NotBe(session.WorkingDirectory,
                "the sink destination must be outside the session working directory");

            injectResult.Should().NotBeNull();
            injectResult!.Messages.Should().Contain(m => m.Text.Contains("Inject complete"),
                "the inject task should emit its outcome summary");

            // ── Open the sink-placed workbook and assert the injected cells ───────
            using var wb = new XLWorkbook(sinkDestination);
            var ws = wb.Worksheet(sheetName);

            string F = v01Config.PreviousAnswerColumn;      // F
            string G = v01Config.PreviousExplanationColumn; // G
            string H = v01Config.AnswerColumn;              // H
            string I = v01Config.StrengthsColumn;           // I
            string J = v01Config.WeaknessesColumn;          // J
            string M = v01Config.ProvidedByColumn;          // M

            string Cell(string col, int row) => ws.Cell($"{col}{row}").GetString();

            // (b) row 6 — carry-forward does NOT fire: reference cells only, no answer block.
            Cell(F, 6).Should().Be("4");
            Cell(G, 6).Should().Be("Strengths:\nS6 baseline\n\n\nWeaknesses:\nW6 baseline");
            Cell(M, 6).Should().Be("Org6");
            Cell(H, 6).Should().BeEmpty();
            Cell(I, 6).Should().BeEmpty();
            Cell(J, 6).Should().BeEmpty();

            // (a) row 7 — carry-forward fires: reference AND answer block populated.
            Cell(F, 7).Should().Be("3");
            Cell(G, 7).Should().Be("Strengths:\nS7 carry\n\n\nWeaknesses:\nW7 carry");
            Cell(M, 7).Should().Be("Org7");
            Cell(H, 7).Should().Be("3");
            Cell(I, 7).Should().Be("S7 carry");
            Cell(J, 7).Should().Be("W7 carry");

            // (c) row 8 — merge both strengths + weaknesses (the {nl}{nl}{nl} → 3 newlines).
            Cell(G, 8).Should().Be("Strengths:\nS8\n\n\nWeaknesses:\nW8");

            // (c) row 9 — strengths-only merge; no carry-forward (stability "Yes").
            Cell(G, 9).Should().Be("Strengths:\nS9");
            Cell(H, 9).Should().BeEmpty();
            Cell(I, 9).Should().BeEmpty();
            Cell(J, 9).Should().BeEmpty();

            // (d) row 10 — provided-by injected into M.
            Cell(M, 10).Should().Be("Org10");

            // (e) row 11 — unmatched current question: everything left untouched.
            Cell(F, 11).Should().BeEmpty();
            Cell(G, 11).Should().BeEmpty();
            Cell(H, 11).Should().BeEmpty();
            Cell(I, 11).Should().BeEmpty();
            Cell(J, 11).Should().BeEmpty();
            Cell(M, 11).Should().BeEmpty();
        }
        finally
        {
            try { Directory.Delete(workflowsDir,     recursive: true); } catch (IOException) { }
            try { Directory.Delete(workflowDataRoot, recursive: true); } catch (IOException) { }
            try { Directory.Delete(workbooksDir,     recursive: true); } catch (IOException) { }
            try { File.Delete(sinkDestination); } catch (IOException) { }
        }
    }

    /// <summary>
    /// A stability-gated carry-forward whose source answer violates the target v01 H List DV
    /// ("1,2,3,4", stamped on every row by <see cref="ClqV01WorkbookWriter"/>'s default) must be
    /// SKIPPED by <see cref="ItrqTool.Tasks.Shared.InjectionValueGuard"/>, not written — the task
    /// still succeeds (skip is non-fatal) and surfaces an Error message naming the row.
    /// Row 12 (section "5:6-13") is used to avoid touching rows 6-11's existing perturbations.
    /// </summary>
    [Fact]
    public async Task InjectWorkflow_CarryForwardSourceViolatesTargetDv_SkipsHCellAndSurfacesErrorMessage()
    {
        var solutionRoot = FindSolutionRoot();
        var configsRoot = Path.Combine(solutionRoot.FullName, "configs");

        var v01ConfigAbs    = Path.Combine(configsRoot, "clq-v01-validation-config.json");
        var v02ConfigAbs    = Path.Combine(configsRoot, "clq-v02-validation-config.json");
        var injectConfigAbs = Path.Combine(configsRoot, "clq-inject-config.json");

        var v01Config = ConfigLoader.Load<ClqV01Config>(
            await File.ReadAllTextAsync(v01ConfigAbs), c => c.Validate());
        var v02Config = ConfigLoader.Load<ControlLevelQuestionValidationV02Config>(
            await File.ReadAllTextAsync(v02ConfigAbs), c => c.Validate());

        var sheetName = v01Config.SheetName;

        var v01Trio = ClqV01BaselineFactory.Build(v01Config);
        var v02Trio = ClqV02BaselineFactory.Build(v02Config);

        var currentDescriptor = v01Trio.Template;

        // Row 12 — carry-forward fires (stability "No") but the source answer "9" is not a
        // member of the target v01 H List DV → the guard must Skip the H write.
        var previousQuestions = v02Trio.Previous.Questions
            .Select(q => q.RowNumber == 12
                ? q with
                {
                    Answer = "9", Strengths = "S12 skip", Weaknesses = "W12 skip",
                    ProvidedBy = "Org12", AnswerStability = "No"
                }
                : q)
            .ToList();
        var previousDescriptor = v02Trio.Previous with { Questions = previousQuestions };

        // NOTE: the committed clq-inject-trial.json hardcodes both the StaticFileSource
        // sourcePath ("trial-workbooks/clq-inject/…") and the StaticFileSink destinationFolder
        // ("inject-output/clq-inject") — this workflow JSON is shared, frozen production
        // config and must not be duplicated or edited for this test, so this test reuses the
        // SAME relative paths as the sibling Fact above. Safe: xUnit runs Facts within one
        // class/collection sequentially, never in parallel with each other.
        var workbooksDir = Path.Combine(AppContext.BaseDirectory, "trial-workbooks", "clq-inject");
        Directory.CreateDirectory(workbooksDir);
        var currentPath  = Path.Combine(workbooksDir, "clq_current_template.xlsx");
        var previousPath = Path.Combine(workbooksDir, "clq_previous_response.xlsx");
        ClqV01WorkbookWriter.Write(currentPath,  sheetName, currentDescriptor);
        ClqV02WorkbookWriter.Write(previousPath, sheetName, previousDescriptor);

        var configsOut = Path.Combine(AppContext.BaseDirectory, "configs");
        Directory.CreateDirectory(configsOut);
        File.Copy(injectConfigAbs, Path.Combine(configsOut, "clq-inject-config.json"),       overwrite: true);
        File.Copy(v01ConfigAbs,    Path.Combine(configsOut, "clq-v01-validation-config.json"), overwrite: true);
        File.Copy(v02ConfigAbs,    Path.Combine(configsOut, "clq-v02-validation-config.json"), overwrite: true);

        var workflowsDir = Path.Combine(Path.GetTempPath(),
            "ItrqTool-inject-wf-skip-" + Guid.NewGuid().ToString("N"));
        var workflowDataRoot = Path.Combine(Path.GetTempPath(),
            "ItrqTool-inject-data-skip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workflowsDir);
        Directory.CreateDirectory(workflowDataRoot);

        var sinkDestination = Path.Combine(
            AppContext.BaseDirectory, "inject-output", "clq-inject", "clq_injected_current.xlsx");
        try { File.Delete(sinkDestination); } catch (IOException) { }

        try
        {
            File.Copy(
                Path.Combine(solutionRoot.FullName, "workflows", "clq-inject-trial.json"),
                Path.Combine(workflowsDir, "clq-inject-trial.json"));

            var services = new ServiceCollection();
            services.AddItrqToolServices(workflowsDir, workflowDataRoot);
            using var sp = services.BuildServiceProvider();

            var loader     = sp.GetRequiredService<IWorkflowLoader>();
            var loadResult = loader.LoadAll();
            loadResult.Failures.Should().BeEmpty("inject trial workflow JSON must load without errors");
            var workflow = loadResult.Workflows.Single(w => w.Id == "clq-inject-trial");

            var factory = sp.GetRequiredService<WorkflowSessionFactory>();
            var session = factory.Create(workflow);

            TaskResult? injectResult = null;
            while (session.Status != WorkflowSessionStatus.Completed)
            {
                var currentNode = workflow.Nodes[session.CurrentIndex];
                var result = await session.RunCurrentTaskAsync();
                result.Succeeded.Should().BeTrue(
                    "task '{0}' must succeed even when a carry-forward answer is skipped (non-fatal); messages: {1}",
                    currentNode.Id, string.Join("; ", result.Messages.Select(m => m.Text)));

                if (currentNode.Id == "inject") injectResult = result;

                if (session.Status != WorkflowSessionStatus.Completed)
                    session.Status.Should().Be(WorkflowSessionStatus.AwaitingReview);
            }

            session.Status.Should().Be(WorkflowSessionStatus.Completed);

            injectResult.Should().NotBeNull();
            injectResult!.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error &&
                m.Text.Contains("Row 12") &&
                m.Text.Contains("does not conform to the target data-validation rule"),
                "the guard's skip reason for row 12's non-conforming carry-forward answer must surface as an Error");

            using var wb = new XLWorkbook(sinkDestination);
            var ws = wb.Worksheet(sheetName);
            string H = v01Config.AnswerColumn;
            ws.Cell($"{H}12").GetString().Should().BeEmpty(
                "the guard must skip the H write for a source answer that violates the target's List DV");
        }
        finally
        {
            try { Directory.Delete(workflowsDir,     recursive: true); } catch (IOException) { }
            try { Directory.Delete(workflowDataRoot, recursive: true); } catch (IOException) { }
            try { Directory.Delete(workbooksDir,     recursive: true); } catch (IOException) { }
            try { File.Delete(sinkDestination); } catch (IOException) { }
        }
    }
}
