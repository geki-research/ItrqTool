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
/// End-to-end trial of the GD inject EXPLANATION / STRUCTURE arms through the workflow engine:
/// <c>StaticFileSource(previous v01) + StaticFileSource(current v02 template) →
/// GeneralDataInject_v01_to_v02 → StaticFileSink</c>, every task resolved via the production
/// composition root (real readers/writers, no mocks) — the same harness the chunk-4a policy e2e
/// (<see cref="GdInjectPolicyE2ETests"/>) uses. Where 4a varied the H→G type policy on ONE question,
/// this varies the EXPLANATION row geometry and the non-Agree / per-answer-grain structure.
///
/// Five arms, each its OWN production section so the per-question row geometry is independent of the
/// others' spans (anchors start at the section's FirstDataRow):
///   • Arm 1 — section 1 (rows 4–9):  Agree q1, one answer, TWO explanation rows BOTH sides
///             → J4 = v01 K@row1, J5 = v01 K@row2 (both written in order); NO message.
///   • Arm 2 — section 2 (rows 11–43): Agree q2, one answer, v01 2 rows / v02 1 row
///             → overlap (1) written to J11; surplus dropped (J12 empty); EXACTLY ONE Warning.
///   • Arm 3 — section 3 (rows 45–64): q3 same qid, DIVERGED OriginalText (v01≠v02)
///             → SameXrefIdTextDiverged: G/J/P at its answer row ALL EMPTY; EXACTLY ONE Warning.
///   • Arm 4 — section 4 (rows 66–91): q4 present in v02 ONLY (qid absent from v01)
///             → Neither: G/J/P EMPTY; NO message.
///   • Arm 5 — section 5 (rows 93–120): Agree q5, TWO answers — d1 matched on both sides, d2 in v02 only
///             → d1 G/J/P written (concrete); d2 G/J/P EMPTY (untouched); NO message for d2.
///
/// Whole-run invariants: M (how-explanation) is NEVER written (all M anchors empty), and the inject
/// task SUCCEEDS (policy Warnings are not task failures). The message set is asserted EXACTLY:
/// { one "row count mismatch" Warning (arm 2), one "left untouched" Warning (arm 3) }, zero Errors.
/// </summary>
public sealed class GdInjectStructureArmsE2ETests
{
    private const string SheetName = "General Data";

    // Production section coordinates (from gd-v0x-validation-config.json) — one arm per section.
    private const int Sec1Header = 3,  Sec1First = 4;
    private const int Sec2Header = 10, Sec2First = 11;
    private const int Sec3Header = 44, Sec3First = 45;
    private const int Sec4Header = 65, Sec4First = 66;
    private const int Sec5Header = 92, Sec5First = 93;

    private const string Sec1Name = "Entities in scope/contact details";
    private const string Sec2Name = "Staff";
    private const string Sec3Name = "Financials (please refer to the Glossary for this section)";
    private const string Sec4Name = "Oversight";
    private const string Sec5Name = "IT Environment";

    // ── Arm 1: Agree, one answer, 2 explanation rows BOTH sides (position-aligned multi-row K→J). ──
    private static readonly GdInjectQuestionSpec Q1 = new(
        Qid: "q1", V01Text: "Q1 text", V02Text: "Q1 text", InV01: true, InV02: true,
        SectionHeaderRow: Sec1Header, SectionName: Sec1Name, FirstDataRow: Sec1First,
        Answers: [new("a1", "WholeNumber", 5.0, "WholeNumber", "OU1", ["m1a", "m1b"])]);

    // ── Arm 2: Agree, one answer, v01 2 rows / v02 1 row (explanation row-count mismatch, v01>v02). ──
    private static readonly GdInjectQuestionSpec Q2 = new(
        Qid: "q2", V01Text: "Q2 text", V02Text: "Q2 text", InV01: true, InV02: true,
        SectionHeaderRow: Sec2Header, SectionName: Sec2Name, FirstDataRow: Sec2First,
        Answers: [new("a1", "WholeNumber", 8.0, "WholeNumber", "OU2", ["m2a", "m2b"], V02ExplanationCount: 1)]);

    // ── Arm 3: same qid, divergent OriginalText (→ SameXrefIdTextDiverged → left untouched). ──
    private static readonly GdInjectQuestionSpec Q3 = new(
        Qid: "q3", V01Text: "Q3 text v01", V02Text: "Q3 text v02", InV01: true, InV02: true,
        SectionHeaderRow: Sec3Header, SectionName: Sec3Name, FirstDataRow: Sec3First,
        Answers: [new("a1", "WholeNumber", 4.0, "WholeNumber", "OU3", ["m3a"])]);

    // ── Arm 4: present in v02 only (qid absent from v01 → Neither → no message). ──
    private static readonly GdInjectQuestionSpec Q4 = new(
        Qid: "q4", V01Text: "Q4 text", V02Text: "Q4 text", InV01: false, InV02: true,
        SectionHeaderRow: Sec4Header, SectionName: Sec4Name, FirstDataRow: Sec4First,
        Answers: [new("a1", "WholeNumber", 0.0, "WholeNumber", "OU4", ["m4a"])]);

    // ── Arm 5: Agree, two answers — d1 matched both sides; d2 present in v02 only (unmatched by AnswerId). ──
    private static readonly GdInjectQuestionSpec Q5 = new(
        Qid: "q5", V01Text: "Q5 text", V02Text: "Q5 text", InV01: true, InV02: true,
        SectionHeaderRow: Sec5Header, SectionName: Sec5Name, FirstDataRow: Sec5First,
        Answers:
        [
            new("d1", "WholeNumber", 3.0, "WholeNumber", "OU5d1", ["m5a"]),                 // both sides
            new("d2", "WholeNumber", 0.0, "WholeNumber", "OU5d2", ["m5b"], InV01: false),   // v02 only
        ]);

    private static readonly IReadOnlyList<GdInjectQuestionSpec> Questions = [Q1, Q2, Q3, Q4, Q5];

    private static DirectoryInfo FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException(
            "Solution root (.slnx) not found above test output directory.");
    }

    [Fact]
    public async Task InjectWorkflow_RunsEndToEnd_ExplanationAndStructureArmsAsserted()
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

        // ── Build fixtures (five arms, one question per production section) ──
        var workbooksDir = Path.Combine(AppContext.BaseDirectory, "trial-workbooks", "gd-inject");
        Directory.CreateDirectory(workbooksDir);
        var previousPath = Path.Combine(workbooksDir, "gd_previous_response.xlsx");
        var currentPath  = Path.Combine(workbooksDir, "gd_current_template.xlsx");
        GdInjectWorkbookWriter.WritePreviousMulti(previousPath, v01Config, Questions);
        GdInjectWorkbookWriter.WriteCurrentMulti(currentPath,  v02Config, Questions);

        // ── Stage configs (production files; filenames match the inject config's references) ──
        var configsOut = Path.Combine(AppContext.BaseDirectory, "configs");
        Directory.CreateDirectory(configsOut);
        File.Copy(injectConfigAbs, Path.Combine(configsOut, "gd-inject-config.json"),         overwrite: true);
        File.Copy(v01ConfigAbs,    Path.Combine(configsOut, "gd-v01-validation-config.json"), overwrite: true);
        File.Copy(v02ConfigAbs,    Path.Combine(configsOut, "gd-v02-validation-config.json"), overwrite: true);

        // ── Workflow engine ─────────────────────────────────────────────────────
        var workflowsDir = Path.Combine(Path.GetTempPath(),
            "ItrqTool-gd-inject-struct-wf-" + Guid.NewGuid().ToString("N"));
        var workflowDataRoot = Path.Combine(Path.GetTempPath(),
            "ItrqTool-gd-inject-struct-data-" + Guid.NewGuid().ToString("N"));
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
            File.Exists(sinkDestination).Should().BeTrue(
                "the StaticFileSink must place the final deliverable at destinationFolder/destinationFileName");

            injectResult.Should().NotBeNull();
            // Policy/structure Warnings must NOT fail the task — a partial-deliverable inject still ships.
            injectResult!.Succeeded.Should().BeTrue("structure-arm Warning messages must not fail the task");
            var msgs = injectResult.Messages;
            msgs.Should().Contain(m => m.Text.Contains("Inject complete"),
                "the inject task must emit its outcome summary");

            // ── Resolve inject target / non-target columns from config ──
            string G = v02Config.PreviousAnswerColumn;      // "G" ← v01 H (answer)
            string J = v02Config.PreviousExplanationColumn; // "J" ← v01 K (current explanation)
            string P = v02Config.ProvidedByColumn;          // "P" ← v01 O (provided-by)
            string M = v02Config.HowExplanationColumn;      // "M" — never an inject target

            using var wb = new XLWorkbook(sinkDestination);
            var ws = wb.Worksheet(SheetName);

            // ── Arm 1: multi-row K→J, position-aligned (BOTH rows written, in order) ──
            int q1Anchor = GdInjectWorkbookWriter.V02AnchorRows(Q1)[0]; // 4
            ws.Cell($"{G}{q1Anchor}").DataType.Should().Be(XLDataType.Number,
                "arm1 equal WholeNumber→WholeNumber typed write must produce a Number-typed cell");
            ws.Cell($"{G}{q1Anchor}").GetValue<double>().Should().Be(5.0);
            ws.Cell($"{J}{q1Anchor}").GetString().Should().Be("m1a", "arm1 K→J row 1 (position 0)");
            ws.Cell($"{J}{q1Anchor + 1}").GetString().Should().Be("m1b", "arm1 K→J row 2 (position 1)");
            ws.Cell($"{P}{q1Anchor}").GetString().Should().Be("OU1");

            // ── Arm 2: explanation row-count mismatch (overlap written, surplus dropped, ONE Warning) ──
            int q2Anchor = GdInjectWorkbookWriter.V02AnchorRows(Q2)[0]; // 11
            ws.Cell($"{G}{q2Anchor}").GetValue<double>().Should().Be(8.0, "arm2 equal-arm G write (incidental)");
            ws.Cell($"{J}{q2Anchor}").GetString().Should().Be("m2a", "arm2 overlap row written");
            ws.Cell($"{J}{q2Anchor + 1}").IsEmpty().Should().BeTrue(
                "arm2 surplus v01 explanation must be dropped — the v02 answer has only one row");
            ws.Cell($"{P}{q2Anchor}").GetString().Should().Be("OU2");
            msgs.Where(m => m.Severity == MessageSeverity.Warning && m.Text.Contains("row count mismatch"))
                .Should().HaveCount(1, "arm2 emits EXACTLY ONE explanation row-count-mismatch Warning");

            // ── Arm 3: SameXrefIdTextDiverged — G/J/P all EMPTY (untouched), ONE "left untouched" Warning ──
            int q3Anchor = GdInjectWorkbookWriter.V02AnchorRows(Q3)[0]; // 45
            ws.Cell($"{G}{q3Anchor}").IsEmpty().Should().BeTrue("arm3 diverged-text question: G untouched");
            ws.Cell($"{J}{q3Anchor}").IsEmpty().Should().BeTrue("arm3 diverged-text question: J untouched");
            ws.Cell($"{P}{q3Anchor}").IsEmpty().Should().BeTrue("arm3 diverged-text question: P untouched");
            msgs.Where(m => m.Severity == MessageSeverity.Warning && m.Text.Contains("left untouched"))
                .Should().HaveCount(1, "arm3 emits EXACTLY ONE 'left untouched' Warning (SameXrefIdTextDiverged)");

            // ── Arm 4: Neither (v02-only question) — G/J/P EMPTY, NO message ──
            int q4Anchor = GdInjectWorkbookWriter.V02AnchorRows(Q4)[0]; // 66
            ws.Cell($"{G}{q4Anchor}").IsEmpty().Should().BeTrue("arm4 v02-only question: G untouched");
            ws.Cell($"{J}{q4Anchor}").IsEmpty().Should().BeTrue("arm4 v02-only question: J untouched");
            ws.Cell($"{P}{q4Anchor}").IsEmpty().Should().BeTrue("arm4 v02-only question: P untouched");

            // ── Arm 5: multi-answer with an unmatched answer — d1 written, d2 untouched, NO message for d2 ──
            var q5Anchors = GdInjectWorkbookWriter.V02AnchorRows(Q5);
            int d1 = q5Anchors[0]; // 93
            int d2 = q5Anchors[1]; // 94
            ws.Cell($"{G}{d1}").DataType.Should().Be(XLDataType.Number, "arm5 d1 matched: typed G write");
            ws.Cell($"{G}{d1}").GetValue<double>().Should().Be(3.0);
            ws.Cell($"{J}{d1}").GetString().Should().Be("m5a", "arm5 d1 K→J");
            ws.Cell($"{P}{d1}").GetString().Should().Be("OU5d1", "arm5 d1 O→P");
            ws.Cell($"{G}{d2}").IsEmpty().Should().BeTrue("arm5 d2 (v02-only answer): G untouched");
            ws.Cell($"{J}{d2}").IsEmpty().Should().BeTrue("arm5 d2 (v02-only answer): J untouched");
            ws.Cell($"{P}{d2}").IsEmpty().Should().BeTrue("arm5 d2 (v02-only answer): P untouched");

            // ── Whole-run invariant: M (how-explanation) is NEVER written ──
            var allAnchors = new[] { q1Anchor, q1Anchor + 1, q2Anchor, q3Anchor, q4Anchor, d1, d2 };
            foreach (var row in allAnchors)
                ws.Cell($"{M}{row}").IsEmpty().Should().BeTrue(
                    $"M (how-explanation) at row {row} is not an inject target and must stay empty");

            // ── EXACT message set: exactly two Warnings (arm2 + arm3), zero Errors ──
            var warnings = msgs.Where(m => m.Severity == MessageSeverity.Warning).ToList();
            var errors   = msgs.Where(m => m.Severity == MessageSeverity.Error).ToList();
            warnings.Should().HaveCount(2,
                "exactly two structure-arm Warnings — arm2 row-count-mismatch + arm3 left-untouched");
            errors.Should().BeEmpty("no structure arm produces an Error");
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
