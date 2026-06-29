using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Presentation;
using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.GeneralDataValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Integration.Tests.WorksheetStructure;

namespace ItrqTool.Integration.Tests.GeneralDataInject;

/// <summary>
/// Minimal happy-path end-to-end trial of the GD inject chain through the workflow engine:
/// <c>StaticFileSource(previous v01) + StaticFileSource(current v02 template) →
/// GeneralDataInject_v01_to_v02 → StaticFileSink</c>, every task resolved via the production
/// composition root (real readers/writers, no mocks).
///
/// ONE Agree question with one answer, in section 1 (rows 4–9, header at row 3 = "Entities in
/// scope/contact details"). The v01 answer is WholeNumber and the v02 answer DV is WholeNumber too,
/// so the H→G type-compatibility policy fires its EQUAL arm. The single answer drives all three
/// reference writes — H→G (typed), K→J (text), O→P (text) — and proves M (how-explanation) is
/// never written (reference injection writes only previous-* columns).
///
/// The full per-answer-row-varied policy-arm suite is a later chunk; this is the wiring/gate proof.
/// </summary>
public sealed class GdInjectEndToEndWorkflowTests
{
    private const string SheetName = "General Data";
    private const string SectionHeader = "Entities in scope/contact details";

    private static DirectoryInfo FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException(
            "Solution root (.slnx) not found above test output directory.");
    }

    [Fact]
    public async Task InjectWorkflow_RunsEndToEnd_HappyPath_GJPWritten_MUntouched()
    {
        var solutionRoot = FindSolutionRoot();
        var configsRoot  = Path.Combine(solutionRoot.FullName, "configs");

        var v01ConfigAbs    = Path.Combine(configsRoot, "gd-v01-validation-config.json");
        var v02ConfigAbs    = Path.Combine(configsRoot, "gd-v02-validation-config.json");
        var injectConfigAbs = Path.Combine(configsRoot, "gd-inject-config.json");

        var v01Config = ConfigLoader.Load<GdV01Config>(
            await File.ReadAllTextAsync(v01ConfigAbs), c => c.Validate());
        var v02Config = ConfigLoader.Load<GdV02Config>(
            await File.ReadAllTextAsync(v02ConfigAbs), c => c.Validate());

        // ── Build fixtures ──────────────────────────────────────────────────────
        var workbooksDir = Path.Combine(AppContext.BaseDirectory, "trial-workbooks", "gd-inject");
        Directory.CreateDirectory(workbooksDir);
        var previousPath = Path.Combine(workbooksDir, "gd_previous_response.xlsx");
        var currentPath  = Path.Combine(workbooksDir, "gd_current_template.xlsx");
        WritePreviousV01(previousPath, v01Config);
        WriteCurrentV02(currentPath,  v02Config);

        // ── Stage configs (production files; filenames match the inject config's references) ──
        var configsOut = Path.Combine(AppContext.BaseDirectory, "configs");
        Directory.CreateDirectory(configsOut);
        File.Copy(injectConfigAbs, Path.Combine(configsOut, "gd-inject-config.json"),         overwrite: true);
        File.Copy(v01ConfigAbs,    Path.Combine(configsOut, "gd-v01-validation-config.json"), overwrite: true);
        File.Copy(v02ConfigAbs,    Path.Combine(configsOut, "gd-v02-validation-config.json"), overwrite: true);

        // ── Workflow engine ─────────────────────────────────────────────────────
        var workflowsDir = Path.Combine(Path.GetTempPath(),
            "ItrqTool-gd-inject-wf-" + Guid.NewGuid().ToString("N"));
        var workflowDataRoot = Path.Combine(Path.GetTempPath(),
            "ItrqTool-gd-inject-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workflowsDir);
        Directory.CreateDirectory(workflowDataRoot);

        var sinkDestination = Path.Combine(
            AppContext.BaseDirectory, "inject-output", "gd-inject", "gd_injected_current.xlsx");
        try { File.Delete(sinkDestination); } catch (IOException) { }

        try
        {
            File.Copy(
                Path.Combine(solutionRoot.FullName, "workflows", "gd-inject-trial.json"),
                Path.Combine(workflowsDir, "gd-inject-trial.json"));

            var services = new ServiceCollection();
            services.AddItrqToolServices(workflowsDir, workflowDataRoot);
            using var sp = services.BuildServiceProvider();

            var loader     = sp.GetRequiredService<IWorkflowLoader>();
            var loadResult = loader.LoadAll();
            loadResult.Failures.Should().BeEmpty("gd-inject-trial workflow JSON must load without errors");
            var workflow = loadResult.Workflows.Single(w => w.Id == "gd-inject-trial");

            var factory = sp.GetRequiredService<WorkflowSessionFactory>();
            var session = factory.Create(workflow);

            // ── Run all four tasks (source, source, inject, sink) ───────────────
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

            File.Exists(Path.Combine(session.WorkingDirectory, "injected.xlsx")).Should().BeTrue(
                "the inject task must write its populated workbook into the session working directory");
            File.Exists(sinkDestination).Should().BeTrue(
                "the StaticFileSink must place the final deliverable at destinationFolder/destinationFileName");

            injectResult.Should().NotBeNull();
            injectResult!.Succeeded.Should().BeTrue();
            injectResult.Messages.Should().Contain(m => m.Text.Contains("Inject complete"),
                "the inject task must emit its outcome summary");

            // ── Open the sink-placed workbook and assert the three writes + M untouched ──
            using var wb = new XLWorkbook(sinkDestination);
            var ws = wb.Worksheet(SheetName);

            string G = v02Config.PreviousAnswerColumn;      // "G" ← v01 H (answer)
            string J = v02Config.PreviousExplanationColumn; // "J" ← v01 K (current explanation)
            string P = v02Config.ProvidedByColumn;          // "P" ← v01 O (provided-by)
            string M = v02Config.HowExplanationColumn;      // "M" — must stay empty (not an inject target)

            // H→G — equal arm: WholeNumber → WholeNumber, written TYPED.
            ws.Cell($"{G}4").DataType.Should().Be(XLDataType.Number,
                "equal WholeNumber→WholeNumber typed write must produce a Number-typed cell");
            ws.Cell($"{G}4").GetValue<double>().Should().Be(7.0);

            // K→J — the v01 current-explanation written as text.
            ws.Cell($"{J}4").GetString().Should().Be("expl-prev");

            // O→P — the v01 provided-by written as text.
            ws.Cell($"{P}4").GetString().Should().Be("Alice");

            // Non-vacuity (lesson 128): the three target cells must be NON-EMPTY — the inject fired.
            ws.Cell($"{G}4").IsEmpty().Should().BeFalse("the H→G write must have fired");
            ws.Cell($"{J}4").IsEmpty().Should().BeFalse("the K→J write must have fired");
            ws.Cell($"{P}4").IsEmpty().Should().BeFalse("the O→P write must have fired");

            // M is NEVER written — reference injection writes only previous-* columns.
            ws.Cell($"{M}4").IsEmpty().Should().BeTrue(
                "M (how-explanation) is not an inject target and must stay empty");
        }
        finally
        {
            try { Directory.Delete(workflowsDir,     recursive: true); } catch (IOException) { }
            try { Directory.Delete(workflowDataRoot, recursive: true); } catch (IOException) { }
            try { Directory.Delete(workbooksDir,     recursive: true); } catch (IOException) { }
            try { File.Delete(sinkDestination); } catch (IOException) { }
        }
    }

    // ── Fixture builders ────────────────────────────────────────────────────────

    // v01 previous-response workbook: one question (q1) with one answer (a1) in section 1, row 4.
    // H=WholeNumber 7 drives the equal arm; K=current-explanation and O=provided-by drive K→J / O→P.
    private static void WritePreviousV01(string path, GdV01Config cfg)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);
        StructureHeaderStamper.Stamp(ws, "gd", "v01");

        ws.Cell(3, cfg.TextColumn).Value = SectionHeader;

        ws.Cell(4, cfg.QuestionNumberColumn).Value      = "1";
        ws.Cell(4, cfg.TextColumn).Value                = "Q1 text";
        ws.Cell(4, cfg.XrefIdColumn).Value              = "q1:a1";
        ws.Cell(4, cfg.AnswerColumn).Value              = 7;
        ws.Cell(4, cfg.AnswerColumn).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
        ws.Cell(4, cfg.CurrentExplanationColumn).Value  = "expl-prev";
        ws.Cell(4, cfg.ProvidedByColumn).Value          = "Alice";

        wb.SaveAs(path);
    }

    // v02 current-template workbook: same q1/a1 identity + OriginalText (→ Agree). G/J/P/M blank
    // (inject targets / not-a-target). H carries a WholeNumber DV (no value) so the target-type
    // category lookup yields "WholeNumber" matching the source.
    private static void WriteCurrentV02(string path, GdV02Config cfg)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);
        StructureHeaderStamper.Stamp(ws, "gd", "v02");

        ws.Cell(3, cfg.TextColumn).Value = SectionHeader;

        ws.Cell(4, cfg.QuestionNumberColumn).Value = "1";
        ws.Cell(4, cfg.TextColumn).Value           = "Q1 text";
        ws.Cell(4, cfg.XrefIdColumn).Value         = "q1:a1";
        ws.Cell(4, cfg.AnswerColumn).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);

        wb.SaveAs(path);
    }
}
