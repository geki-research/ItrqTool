using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Presentation;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.RiskLevelQuestionInjectED;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using ItrqTool.Tasks.RiskLevelQuestionValidationV02;
using ItrqTool.Integration.Tests.WorksheetStructure;

namespace ItrqTool.Integration.Tests.RlqEDInject;

/// <summary>
/// End-to-end trial of the RLQ ED inject chain through the workflow engine, cloned from
/// RlqInjectEndToEndWorkflowTests: <c>StaticFileSource(previous ED v01) + StaticFileSource(current
/// ED v02 template) → RiskLevelQuestionInjectED_v01_to_v02 → StaticFileSink</c>, every task resolved
/// via the production composition root (real readers/writers, no mocks), with the committed ED
/// configs, schemas and trial workflow.
///
/// Happy path only. Two questions in section 1 of the production SectionRows ("3:4-21"), both Agree
/// (identical text and XrefId):
///   Q1 rows 4–6 — answer 3 / Holding 1 / EBOe 2, three explanation rows, provided-by "OU-ED1".
///   Q2 row 7    — answer 10 / Holding 4 / EBOe 6, one explanation row, provided-by "OU-ED2".
/// v02 J/K/L carry a permissive WholeNumber DV (0–1000), so every value conforms: the run emits no
/// Warning or Error. The v02 current columns J/K/L stay blank (never written).
/// </summary>
public sealed class RlqEDInjectEndToEndWorkflowTests
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
    public async Task EdInjectWorkflow_RunsEndToEnd_HappyPath()
    {
        var solutionRoot = FindSolutionRoot();
        var configsRoot  = Path.Combine(solutionRoot.FullName, "configs");

        var v01ConfigAbs    = Path.Combine(configsRoot, "rlq-ed-v01-columns-config.json");
        var v02ConfigAbs    = Path.Combine(configsRoot, "rlq-ed-v02-columns-config.json");
        var injectConfigAbs = Path.Combine(configsRoot, "rlq-ed-inject-config.json");

        var v01Config = ConfigLoader.Load<RlqV01Config>(
            await File.ReadAllTextAsync(v01ConfigAbs), c => c.Validate());
        var v02Config = ConfigLoader.Load<RlqV02Config>(
            await File.ReadAllTextAsync(v02ConfigAbs), c => c.Validate());
        var edConfig = ConfigLoader.Load<RlqEDInjectConfig>(
            await File.ReadAllTextAsync(injectConfigAbs), c => c.Validate());

        var sheetName = v01Config.SheetName; // "IT Risk Level Questions"

        // ── Build fixtures ──────────────────────────────────────────────────────
        var workbooksDir = Path.Combine(AppContext.BaseDirectory, "trial-workbooks", "rlq-ed-inject");
        Directory.CreateDirectory(workbooksDir);
        var previousPath = Path.Combine(workbooksDir, "rlq_ed_previous_response.xlsx");
        var currentPath  = Path.Combine(workbooksDir, "rlq_ed_current_template.xlsx");
        WritePreviousV01(previousPath, sheetName, v01Config, edConfig);
        WriteCurrentV02(currentPath,  sheetName, v02Config, edConfig);

        // ── Stage configs (production files; config filenames match the inject config's references)
        var configsOut = Path.Combine(AppContext.BaseDirectory, "configs");
        Directory.CreateDirectory(configsOut);
        File.Copy(injectConfigAbs, Path.Combine(configsOut, "rlq-ed-inject-config.json"),     overwrite: true);
        File.Copy(v01ConfigAbs,    Path.Combine(configsOut, "rlq-ed-v01-columns-config.json"), overwrite: true);
        File.Copy(v02ConfigAbs,    Path.Combine(configsOut, "rlq-ed-v02-columns-config.json"), overwrite: true);

        // ── Workflow engine ─────────────────────────────────────────────────────
        var workflowsDir = Path.Combine(Path.GetTempPath(),
            "ItrqTool-rlq-ed-inject-wf-" + Guid.NewGuid().ToString("N"));
        var workflowDataRoot = Path.Combine(Path.GetTempPath(),
            "ItrqTool-rlq-ed-inject-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workflowsDir);
        Directory.CreateDirectory(workflowDataRoot);

        var sinkDestination = Path.Combine(
            AppContext.BaseDirectory, "inject-output", "rlq-ed-inject", "rlq_ed_injected_current.xlsx");
        try { File.Delete(sinkDestination); } catch (IOException) { }

        try
        {
            File.Copy(
                Path.Combine(solutionRoot.FullName, "workflows", "rlq-ed-inject-trial.json"),
                Path.Combine(workflowsDir, "rlq-ed-inject-trial.json"));

            var services = new ServiceCollection();
            services.AddItrqToolServices(workflowsDir, workflowDataRoot);
            using var sp = services.BuildServiceProvider();

            var loader     = sp.GetRequiredService<IWorkflowLoader>();
            var loadResult = loader.LoadAll();
            loadResult.Failures.Should().BeEmpty("rlq-ed-inject-trial workflow JSON must load without errors");
            var workflow = loadResult.Workflows.Single(w => w.HierarchicalPath == "rlq-ed-inject-trial");

            // ── The inject node's type resolves to the ED task (C2b review N-4) ──
            var injectNode = workflow.Nodes.Single(n => n.Id == "inject");
            injectNode.TaskType.Should().Be("RiskLevelQuestionInjectED_v01_to_v02");
            sp.GetRequiredService<ITaskRegistry>().FindTask(injectNode.TaskType)
                .Should().BeOfType<RiskLevelQuestionInjectEDV01ToV02Task>();

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
            var msgs = injectResult!.Messages;
            msgs.Should().Contain(m => m.Text.Contains("Inject complete") && m.Text.Contains("Wrote 12 cell(s)"),
                "the inject task must emit its outcome summary (6 answer + 4 explanation + 2 provided-by cells)");
            msgs.Should().NotContain(m => m.Severity == MessageSeverity.Warning || m.Severity == MessageSeverity.Error,
                "every value conforms to its permissive target DV and both questions are identical Agrees");

            // ── Open the sink-placed workbook ───────────────────────────────────
            using var wb = new XLWorkbook(sinkDestination);
            var ws = wb.Worksheet(sheetName);

            string G = v02Config.PreviousAnswerColumn;      // "G"
            string H = edConfig.TargetHoldingAnswerColumn;  // "H"
            string I = edConfig.TargetEBOeAnswerColumn;     // "I"
            string N = v02Config.PreviousExplanationColumn; // "N"
            string T = v02Config.ProvidedByColumn;          // "T"

            void AssertNumber(string col, int row, double expected)
            {
                ws.Cell($"{col}{row}").DataType.Should().Be(XLDataType.Number, $"{col}{row} is a typed numeric write");
                ws.Cell($"{col}{row}").GetValue<double>().Should().Be(expected, $"{col}{row}");
            }

            // Typed answer arms: G/H/I from v01 J/K/L.
            AssertNumber(G, 4, 3.0);
            AssertNumber(H, 4, 1.0);
            AssertNumber(I, 4, 2.0);
            AssertNumber(G, 7, 10.0);
            AssertNumber(H, 7, 4.0);
            AssertNumber(I, 7, 6.0);

            // Explanations per row: N from v01 O.
            ws.Cell($"{N}4").GetString().Should().Be("e1a");
            ws.Cell($"{N}5").GetString().Should().Be("e1b");
            ws.Cell($"{N}6").GetString().Should().Be("e1c");
            ws.Cell($"{N}7").GetString().Should().Be("e2a");

            // Provided-by: T from v01 S.
            ws.Cell($"{T}4").GetString().Should().Be("OU-ED1");
            ws.Cell($"{T}7").GetString().Should().Be("OU-ED2");

            // Never written: the v02 current answer columns J/K/L keep the template's (blank) values.
            foreach (var col in new[] { v02Config.AnswerColumn, edConfig.TargetHoldingDvColumn, edConfig.TargetEBOeDvColumn })
                for (var row = 4; row <= 7; row++)
                    ws.Cell($"{col}{row}").IsEmpty().Should().BeTrue($"{col}{row} is a v02 current column and is never written");
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

    // ED v01 previous-response workbook. Two questions in section 1, section header at row 3.
    private static void WritePreviousV01(string path, string sheetName, RlqV01Config cfg, RlqEDInjectConfig ed)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);
        StructureHeaderStamper.Stamp(ws, "rlq-ed", "v01");

        ws.Cell(3, cfg.TextColumn).Value = "Test Section";

        // Q1 (rows 4–6): answer 3 / Holding 1 / EBOe 2, three explanation rows.
        WriteV01Anchor(ws, cfg, row: 4, xref: "ed1", text: "ED1 text", providedBy: "OU-ED1", curExp: "e1a");
        WriteV01Answers(ws, cfg, ed, row: 4, answer: 3, holding: 1, eboe: 2);
        ws.Cell(5, cfg.XrefIdColumn).Value             = "ed1";
        ws.Cell(5, cfg.CurrentExplanationColumn).Value = "e1b";
        ws.Cell(6, cfg.XrefIdColumn).Value             = "ed1";
        ws.Cell(6, cfg.CurrentExplanationColumn).Value = "e1c";
        MergeV01(ws, cfg, ed, anchorRow: 4, lastRow: 6);

        // Q2 (row 7): answer 10 / Holding 4 / EBOe 6, one explanation row.
        WriteV01Anchor(ws, cfg, row: 7, xref: "ed2", text: "ED2 text", providedBy: "OU-ED2", curExp: "e2a");
        WriteV01Answers(ws, cfg, ed, row: 7, answer: 10, holding: 4, eboe: 6);

        wb.SaveAs(path);
    }

    // ED v02 current-template workbook. G/H/I, N and T intentionally blank (inject targets); J/K/L
    // carry a permissive WholeNumber DV (no value) — the DV the ED task judges each arm against.
    private static void WriteCurrentV02(string path, string sheetName, RlqV02Config cfg, RlqEDInjectConfig ed)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);
        StructureHeaderStamper.Stamp(ws, "rlq-ed", "v02");

        ws.Cell(3, cfg.TextColumn).Value = "Test Section";

        WriteV02Anchor(ws, cfg, ed, row: 4, xref: "ed1", text: "ED1 text");
        ws.Cell(5, cfg.XrefIdColumn).Value = "ed1";
        ws.Cell(6, cfg.XrefIdColumn).Value = "ed1";
        MergeV02(ws, cfg, ed, anchorRow: 4, lastRow: 6);

        WriteV02Anchor(ws, cfg, ed, row: 7, xref: "ed2", text: "ED2 text");

        wb.SaveAs(path);
    }

    // Sets the once-per-question v01 columns on the anchor row:
    // QuestionNumber (=xref), TextColumn, XrefId, ProvidedBy (S), CurrentExplanation (O on anchor).
    private static void WriteV01Anchor(
        IXLWorksheet ws, RlqV01Config cfg, int row, string xref, string text,
        string? providedBy, string? curExp)
    {
        ws.Cell(row, cfg.QuestionNumberColumn).Value = xref;
        ws.Cell(row, cfg.TextColumn).Value           = text;
        ws.Cell(row, cfg.XrefIdColumn).Value         = xref;
        if (providedBy != null) ws.Cell(row, cfg.ProvidedByColumn).Value          = providedBy;
        if (curExp    != null) ws.Cell(row, cfg.CurrentExplanationColumn).Value   = curExp;
    }

    // The three v01 current answers (J / K / L), each a whole number under a WholeNumber DV.
    private static void WriteV01Answers(
        IXLWorksheet ws, RlqV01Config cfg, RlqEDInjectConfig ed, int row, int answer, int holding, int eboe)
    {
        foreach (var (col, value) in new[]
                 {
                     (cfg.AnswerColumn, answer),
                     (ed.SourceHoldingAnswerColumn, holding),
                     (ed.SourceEBOeAnswerColumn, eboe),
                 })
        {
            ws.Cell(row, col).Value = value;
            ws.Cell(row, col).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
        }
    }

    // Sets the once-per-question v02 columns on the anchor row (QuestionNumber, TextColumn, XrefId)
    // and the permissive WholeNumber DV on the three current answer columns (J / K / L).
    private static void WriteV02Anchor(
        IXLWorksheet ws, RlqV02Config cfg, RlqEDInjectConfig ed, int row, string xref, string text)
    {
        ws.Cell(row, cfg.QuestionNumberColumn).Value = xref;
        ws.Cell(row, cfg.TextColumn).Value           = text;
        ws.Cell(row, cfg.XrefIdColumn).Value         = xref;
        foreach (var col in new[] { cfg.AnswerColumn, ed.TargetHoldingDvColumn, ed.TargetEBOeDvColumn })
            ws.Cell(row, col).CreateDataValidation().WholeNumber.Between(0, 1000);
    }

    // Merges the once-per-question v01 columns over the question group: C/D/E/F/G, the ED previous
    // answers H/I, the three current answers J/K/L, P and S. XrefId (U) and the explanation
    // triplet (M/N/O) are NOT merged.
    private static void MergeV01(IXLWorksheet ws, RlqV01Config cfg, RlqEDInjectConfig ed, int anchorRow, int lastRow)
    {
        foreach (var col in new[]
        {
            cfg.QuestionNumberColumn, cfg.TextColumn,           cfg.GuidanceColumn,
            cfg.RequestedTypeColumn,  cfg.PreviousAnswerColumn, "H", "I",
            cfg.AnswerColumn,         ed.SourceHoldingAnswerColumn, ed.SourceEBOeAnswerColumn,
            cfg.MaterialChangeColumn, cfg.ProvidedByColumn
        })
        {
            ws.Range($"{col}{anchorRow}:{col}{lastRow}").Merge();
        }
    }

    // Merges the once-per-question v02 columns over the question group: C/D/E/F/G, the ED previous
    // answers H/I, the three current answers J/K/L, P, Q and T. XrefId (V) and the explanation
    // triplet (M/N/O) are NOT merged.
    private static void MergeV02(IXLWorksheet ws, RlqV02Config cfg, RlqEDInjectConfig ed, int anchorRow, int lastRow)
    {
        foreach (var col in new[]
        {
            cfg.QuestionNumberColumn, cfg.TextColumn,           cfg.GuidanceColumn,
            cfg.RequestedTypeColumn,  cfg.PreviousAnswerColumn,
            ed.TargetHoldingAnswerColumn, ed.TargetEBOeAnswerColumn,
            cfg.AnswerColumn,         ed.TargetHoldingDvColumn, ed.TargetEBOeDvColumn,
            cfg.MaterialChangeColumn, cfg.HowExplanationColumn, cfg.ProvidedByColumn
        })
        {
            ws.Range($"{col}{anchorRow}:{col}{lastRow}").Merge();
        }
    }
}
