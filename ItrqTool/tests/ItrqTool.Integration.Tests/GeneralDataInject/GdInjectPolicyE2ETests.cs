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

namespace ItrqTool.Integration.Tests.GeneralDataInject;

/// <summary>
/// End-to-end trial of the GD inject H→G DV-CONFORMANCE GUARD policy through the workflow engine:
/// <c>StaticFileSource(previous v01) + StaticFileSource(current v02 template) →
/// GeneralDataInject_v01_to_v02 → StaticFileSink</c>, every task resolved via the production
/// composition root (real readers/writers, no mocks) — the same harness the chunk-3 happy-path
/// e2e uses, here exercising all four H→G guard arms on ONE Agree question with four answers.
///
/// One Agree question (qid "q1", same OriginalText on both books) with answers a1–a4 in section 1
/// (rows 4–9, header row 3 = "Entities in scope/contact details"), one answer per row (anchors
/// 4/5/6/7). Each answer pairs by AnswerId and carries a v01 K explanation + a v01 O provided-by,
/// so the K→J and O→P writes fire on EVERY matched answer regardless of the H arm. Every target H
/// cell in this fixture carries a real "≥ 0" DV bound (<c>GdInjectWorkbookWriter.ApplyDv</c>), so
/// <c>InjectionValueGuard</c> evaluates real DV conformance, not a category-only comparison:
///
///   a1 (row 4) equal:       v01 H WholeNumber 5   ↔ v02 H WholeNumber → G4 = 5 (Number), NO message.
///   a2 (row 5) widen:       v01 H WholeNumber 7   ↔ v02 H Decimal     → G5 = 7 (Number) + ONE Info.
///   a3 (row 6) narrow:      v01 H Decimal 3.5     ↔ v02 H WholeNumber → G6 EMPTY (skipped — "3.5"
///                           does not conform to WholeNumber) + ONE Error.
///   a4 (row 7) list source: v01 H List "yes"      ↔ v02 H WholeNumber → G7 EMPTY (skipped — "yes"
///                           does not conform to WholeNumber) + ONE Error;
///                           K→J and O→P STILL written on BOTH skip arms (guard Skip skips ONLY the
///                           G write, continue-not-abort).
///
/// M (how-explanation) is never written; neither Error fails the task; the message set is asserted
/// EXACTLY: { one widen Info (a2), two does-not-conform Errors (a3, a4) }, none for the equal arm,
/// zero Warnings.
/// </summary>
public sealed class GdInjectPolicyE2ETests
{
    private const string SheetName = "General Data";
    private const string SectionHeader = "Entities in scope/contact details";
    private const int SectionHeaderRow = 3;
    private const int FirstDataRow = 4;
    private const string Qid = "q1";
    private const string QuestionText = "Q1 text";

    private static readonly IReadOnlyList<GdInjectAnswerSpec> Answers =
    [
        new("a1", "WholeNumber", 5.0, "WholeNumber", "OU1", ["k1"]), // equal
        new("a2", "WholeNumber", 7.0, "Decimal",     "OU2", ["k2"]), // widen
        new("a3", "Decimal",     3.5, "WholeNumber", "OU3", ["k3"]), // narrow — does not conform
        new("a4", "List",        "yes", "WholeNumber", "OU4", ["k4"]), // list source — does not conform
    ];

    private static DirectoryInfo FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException(
            "Solution root (.slnx) not found above test output directory.");
    }

    [Fact]
    public async Task InjectWorkflow_RunsEndToEnd_AllAnswerTypePolicyArmsAsserted()
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

        // ── Build fixtures (four-answer Agree question; per-answer H DV-types per side) ──
        var workbooksDir = Path.Combine(AppContext.BaseDirectory, "trial-workbooks", "gd-inject");
        Directory.CreateDirectory(workbooksDir);
        var previousPath = Path.Combine(workbooksDir, "gd_previous_response.xlsx");
        var currentPath  = Path.Combine(workbooksDir, "gd_current_template.xlsx");
        GdInjectWorkbookWriter.WritePrevious(
            previousPath, v01Config, SectionHeaderRow, SectionHeader, FirstDataRow, Qid, QuestionText, Answers);
        GdInjectWorkbookWriter.WriteCurrent(
            currentPath, v02Config, SectionHeaderRow, SectionHeader, FirstDataRow, Qid, QuestionText, Answers);

        // ── Stage configs (production files; filenames match the inject config's references) ──
        var configsOut = Path.Combine(AppContext.BaseDirectory, "configs");
        Directory.CreateDirectory(configsOut);
        File.Copy(injectConfigAbs, Path.Combine(configsOut, "gd-inject-config.json"),         overwrite: true);
        File.Copy(v01ConfigAbs,    Path.Combine(configsOut, "gd-v01-validation-config.json"), overwrite: true);
        File.Copy(v02ConfigAbs,    Path.Combine(configsOut, "gd-v02-validation-config.json"), overwrite: true);

        // ── Workflow engine ─────────────────────────────────────────────────────
        var workflowsDir = Path.Combine(Path.GetTempPath(),
            "ItrqTool-gd-inject-policy-wf-" + Guid.NewGuid().ToString("N"));
        var workflowDataRoot = Path.Combine(Path.GetTempPath(),
            "ItrqTool-gd-inject-policy-data-" + Guid.NewGuid().ToString("N"));
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
            var workflow = loadResult.Workflows.Single(w => w.HierarchicalPath == "gd-inject-trial");

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
            File.Exists(sinkDestination).Should().BeTrue(
                "the StaticFileSink must place the final deliverable at destinationFolder/destinationFileName");

            injectResult.Should().NotBeNull();
            // The incompatible Error does NOT fail the task — a partial-deliverable inject still ships.
            injectResult!.Succeeded.Should().BeTrue("type-policy Warning/Error messages must not fail the task");
            var msgs = injectResult.Messages;
            msgs.Should().Contain(m => m.Text.Contains("Inject complete"),
                "the inject task must emit its outcome summary");

            // ── Resolve the inject target/non-target columns from config ──
            string G = v02Config.PreviousAnswerColumn;      // "G" ← v01 H (answer)
            string J = v02Config.PreviousExplanationColumn; // "J" ← v01 K (current explanation)
            string P = v02Config.ProvidedByColumn;          // "P" ← v01 O (provided-by)
            string M = v02Config.HowExplanationColumn;      // "M" — never an inject target

            var anchors = GdInjectWorkbookWriter.AnchorRows(FirstDataRow, Answers);
            int a1 = anchors[0], a2 = anchors[1], a3 = anchors[2], a4 = anchors[3];

            // ── Open the sink-placed workbook ──
            using var wb = new XLWorkbook(sinkDestination);
            var ws = wb.Worksheet(SheetName);

            // a1 equal — WholeNumber → WholeNumber: G written TYPED (Number) == 5.
            ws.Cell($"{G}{a1}").DataType.Should().Be(XLDataType.Number,
                "equal WholeNumber→WholeNumber typed write must produce a Number-typed cell");
            ws.Cell($"{G}{a1}").GetValue<double>().Should().Be(5.0);

            // a2 widen — WholeNumber → Decimal: G written TYPED (Number) == 7 + Info.
            ws.Cell($"{G}{a2}").DataType.Should().Be(XLDataType.Number,
                "widen typed write must produce a Number-typed cell");
            ws.Cell($"{G}{a2}").GetValue<double>().Should().Be(7.0);

            // a3 narrow — Decimal 3.5 does not conform to the target WholeNumber DV rule: G SKIPPED (empty).
            ws.Cell($"{G}{a3}").IsEmpty().Should().BeTrue(
                "a value that does not conform to the target DV rule must cause the G write to be skipped");

            // a4 list source — "yes" does not conform to the target WholeNumber DV rule: G SKIPPED (empty).
            ws.Cell($"{G}{a4}").IsEmpty().Should().BeTrue(
                "a value that does not conform to the target DV rule must cause the G write to be skipped");

            // K→J fires on EVERY matched answer (incl. a3/a4 — the continue-not-abort proof).
            ws.Cell($"{J}{a1}").GetString().Should().Be("k1");
            ws.Cell($"{J}{a2}").GetString().Should().Be("k2");
            ws.Cell($"{J}{a3}").GetString().Should().Be("k3",
                "K→J must still be written for the does-not-conform narrow answer (only the G write is skipped)");
            ws.Cell($"{J}{a4}").GetString().Should().Be("k4",
                "K→J must still be written for the does-not-conform list-source answer (only the G write is skipped)");

            // O→P fires on EVERY matched answer (incl. a3/a4).
            ws.Cell($"{P}{a1}").GetString().Should().Be("OU1");
            ws.Cell($"{P}{a2}").GetString().Should().Be("OU2");
            ws.Cell($"{P}{a3}").GetString().Should().Be("OU3",
                "O→P must still be written for the does-not-conform narrow answer");
            ws.Cell($"{P}{a4}").GetString().Should().Be("OU4",
                "O→P must still be written for the does-not-conform list-source answer");

            // M is NEVER written — reference injection writes only previous-* columns.
            foreach (var row in anchors)
                ws.Cell($"{M}{row}").IsEmpty().Should().BeTrue(
                    $"M (how-explanation) at row {row} is not an inject target and must stay empty");

            // ── EXACT message set: one widen Info (a2), two does-not-conform Errors (a3, a4) ──
            // (excludes the task's own "Inject complete" outcome-summary Info)
            var infos    = msgs.Where(m => m.Severity == MessageSeverity.Info && !m.Text.Contains("Inject complete")).ToList();
            var warnings = msgs.Where(m => m.Severity == MessageSeverity.Warning).ToList();
            var errors   = msgs.Where(m => m.Severity == MessageSeverity.Error).ToList();

            infos.Should().HaveCount(1, "exactly one type-policy Info (widen a2) — none for the equal arm");
            infos.Should().ContainSingle(m => m.Text.Contains("widened"), "the a2 widen Info");

            warnings.Should().BeEmpty("the DV-conformance guard produces no Warning in this fixture");

            errors.Should().HaveCount(2,
                "exactly two type-policy Errors (narrow a3 + list-source a4, both does-not-conform)");
            errors.Should().ContainSingle(m => m.Text.Contains("answer a3") && m.Text.Contains("does not conform"),
                "the a3 narrow does-not-conform Error");
            errors.Should().ContainSingle(m => m.Text.Contains("answer a4") && m.Text.Contains("does not conform"),
                "the a4 list-source does-not-conform Error");

            // The equal arm (a1) must produce NO type-compatibility message.
            msgs.Should().NotContain(m => m.Text.Contains("answer a1"),
                "the equal arm (a1) must emit no answer-type-compatibility message");
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
