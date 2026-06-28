using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Presentation;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using ItrqTool.Tasks.RiskLevelQuestionValidationV02;
using ItrqTool.Integration.Tests.WorksheetStructure;

namespace ItrqTool.Integration.Tests.RlqInject;

/// <summary>
/// End-to-end trial of the RLQ inject chain through the workflow engine:
/// <c>StaticFileSource(previous v01) + StaticFileSource(current v02 template) →
/// RiskLevelQuestionInject_v01_to_v02 → StaticFileSink</c>, every task resolved
/// via the production composition root (real readers/writers, no mocks).
///
/// The fixture pair exercises every arm of the H→G type-compatibility policy and the
/// K→J / O→P write logic. Questions are placed in section 1 (rows 4–15) of the
/// production SectionRows ("3:4-21"). Sections 2–4 (rows 23–71) have no questions.
///
/// Scenarios (v01 anchor row / v02 anchor row):
///   X1 row 4  — (1) equal:       v01 H=WholeNumber 3,  v02 H=WholeNumber → G4=3.0 (numeric).
///   X2 row 5  — (2) widen:       v01 H=WholeNumber 5,  v02 H=Decimal    → G5=5.0 + Warning.
///   X3 rows 6–8 — (3) narrow:    v01 H=Decimal 4.5,    v02 H=WholeNumber → G6=4.5 + Warning.
///   X5 rows 9–11 — (5) multi-row K→J: v01 K9/10/11 written to v02 J9/10/11.
///   X4 row 12 — (4) mismatch:    v01 H=List "Yes",     v02 H=WholeNumber → G12 SKIPPED + Error;
///               K→J and O→P ARE still written (continue-not-abort).
///   X6 rows 13–15(v01)/13–14(v02) — (6) row-count mismatch: 2 J writes + Warning.
///   X9 row 15 (v02 only) — (7) non-Agree: row left entirely untouched.
/// </summary>
public sealed class RlqInjectEndToEndWorkflowTests
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
    public async Task InjectWorkflow_RunsEndToEnd_AllPolicyArmsAsserted()
    {
        var solutionRoot = FindSolutionRoot();
        var configsRoot  = Path.Combine(solutionRoot.FullName, "configs");

        var v01ConfigAbs    = Path.Combine(configsRoot, "rlq-v01-validation-config.json");
        var v02ConfigAbs    = Path.Combine(configsRoot, "rlq-v02-validation-config.json");
        var injectConfigAbs = Path.Combine(configsRoot, "rlq-inject-config.json");

        var v01Config = ConfigLoader.Load<RlqV01Config>(
            await File.ReadAllTextAsync(v01ConfigAbs), c => c.Validate());
        var v02Config = ConfigLoader.Load<RlqV02Config>(
            await File.ReadAllTextAsync(v02ConfigAbs), c => c.Validate());

        var sheetName = v01Config.SheetName; // "IT Risk Level Questions"

        // ── Build fixtures ──────────────────────────────────────────────────────
        var workbooksDir = Path.Combine(AppContext.BaseDirectory, "trial-workbooks", "rlq-inject");
        Directory.CreateDirectory(workbooksDir);
        var previousPath = Path.Combine(workbooksDir, "rlq_previous_response.xlsx");
        var currentPath  = Path.Combine(workbooksDir, "rlq_current_template.xlsx");
        WritePreviousV01(previousPath, sheetName, v01Config);
        WriteCurrentV02(currentPath,  sheetName, v02Config);

        // ── Stage configs (production files; config filenames match the inject config's references)
        var configsOut = Path.Combine(AppContext.BaseDirectory, "configs");
        Directory.CreateDirectory(configsOut);
        File.Copy(injectConfigAbs, Path.Combine(configsOut, "rlq-inject-config.json"),         overwrite: true);
        File.Copy(v01ConfigAbs,    Path.Combine(configsOut, "rlq-v01-validation-config.json"), overwrite: true);
        File.Copy(v02ConfigAbs,    Path.Combine(configsOut, "rlq-v02-validation-config.json"), overwrite: true);

        // ── Workflow engine ─────────────────────────────────────────────────────
        var workflowsDir = Path.Combine(Path.GetTempPath(),
            "ItrqTool-rlq-inject-wf-" + Guid.NewGuid().ToString("N"));
        var workflowDataRoot = Path.Combine(Path.GetTempPath(),
            "ItrqTool-rlq-inject-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workflowsDir);
        Directory.CreateDirectory(workflowDataRoot);

        var sinkDestination = Path.Combine(
            AppContext.BaseDirectory, "inject-output", "rlq-inject", "rlq_injected_current.xlsx");
        try { File.Delete(sinkDestination); } catch (IOException) { }

        try
        {
            File.Copy(
                Path.Combine(solutionRoot.FullName, "workflows", "rlq-inject-trial.json"),
                Path.Combine(workflowsDir, "rlq-inject-trial.json"));

            var services = new ServiceCollection();
            services.AddItrqToolServices(workflowsDir, workflowDataRoot);
            using var sp = services.BuildServiceProvider();

            var loader     = sp.GetRequiredService<IWorkflowLoader>();
            var loadResult = loader.LoadAll();
            loadResult.Failures.Should().BeEmpty("rlq-inject-trial workflow JSON must load without errors");
            var workflow = loadResult.Workflows.Single(w => w.Id == "rlq-inject-trial");

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
            msgs.Should().Contain(m => m.Text.Contains("Inject complete"),
                "the inject task must emit its outcome summary");

            // ── Open the sink-placed workbook and assert every policy arm ───────
            using var wb = new XLWorkbook(sinkDestination);
            var ws = wb.Worksheet(sheetName);

            string G = v02Config.PreviousAnswerColumn;      // "G"
            string J = v02Config.PreviousExplanationColumn; // "J"
            string P = v02Config.ProvidedByColumn;          // "P"

            string GetStr(string col, int row) => ws.Cell($"{col}{row}").GetString();

            // (1) X1 row 4 — equal: WholeNumber → WholeNumber, G4 must be numeric 3.
            ws.Cell($"{G}4").DataType.Should().Be(XLDataType.Number,
                "equal WholeNumber→WholeNumber typed write must produce a Number-typed cell");
            ws.Cell($"{G}4").GetValue<double>().Should().Be(3.0);
            msgs.Should().NotContain(
                m => (m.Text.Contains("widened") || m.Text.Contains("narrowed") || m.Text.Contains("incompatible"))
                     && m.Text.Contains("xref x1"),
                "the equal arm (X1) must emit no answer-type-compatibility message");

            // (2) X2 row 5 — widen: WholeNumber → Decimal, G5 must be numeric 5 + Warning.
            ws.Cell($"{G}5").DataType.Should().Be(XLDataType.Number,
                "widen typed write must produce a Number-typed cell");
            ws.Cell($"{G}5").GetValue<double>().Should().Be(5.0);
            msgs.Should().Contain(m => m.Severity == MessageSeverity.Warning && m.Text.Contains("widened"),
                "widen case must emit a Warning containing 'widened'");

            // (3) X3 rows 6–8 — narrow: Decimal 4.5 → WholeNumber, G6=4.5 (not rounded) + Warning.
            ws.Cell($"{G}6").DataType.Should().Be(XLDataType.Number,
                "narrow typed write must produce a Number-typed cell");
            ws.Cell($"{G}6").GetValue<double>().Should().Be(4.5,
                "narrow write must be as-is, NOT rounded");
            msgs.Should().Contain(m => m.Severity == MessageSeverity.Warning && m.Text.Contains("narrowed"),
                "narrow case must emit a Warning containing 'narrowed'");
            // K→J for X3 anchor + continuation rows
            GetStr(J, 6).Should().Be("k3a");
            GetStr(J, 7).Should().Be("k3b");
            GetStr(J, 8).Should().Be("k3c");
            // O→P for X3 anchor row
            GetStr(P, 6).Should().Be("OU3");

            // (5) X5 rows 9–11 — multi-row K→J: all three K values written to J.
            GetStr(J, 9).Should().Be("k5a");
            GetStr(J, 10).Should().Be("k5b");
            GetStr(J, 11).Should().Be("k5c");
            GetStr(P, 9).Should().Be("OU5");

            // (4) X4 row 12 — mismatch: G12 NOT written; K→J and O→P still written (continue-not-abort).
            ws.Cell($"{G}12").IsEmpty().Should().BeTrue(
                "incompatible answer type must cause the G write to be skipped");
            msgs.Should().Contain(m => m.Severity == MessageSeverity.Error && m.Text.Contains("incompatible"),
                "mismatch case must emit an Error containing 'incompatible'");
            GetStr(J, 12).Should().Be("k4a",
                "K→J must still be written even when the G write is skipped (continue-not-abort)");
            GetStr(P, 12).Should().Be("OU4",
                "O→P must still be written even when the G write is skipped");

            // (6) X6 — explanation row-count mismatch: v01 has 3 rows, v02 has 2 → 2 J writes + Warning.
            GetStr(J, 13).Should().Be("k6a");
            GetStr(J, 14).Should().Be("k6b");
            ws.Cell($"{J}15").IsEmpty().Should().BeTrue(
                "no J15 write: the extra v01 row has no v02 counterpart");
            msgs.Where(m => m.Severity == MessageSeverity.Warning
                            && m.Text.Contains("explanation row count mismatch"))
                .Should().HaveCount(1,
                    "row-count mismatch must emit EXACTLY ONE Warning (the fixture has a single mismatch question, X6)");

            // (7) X9 row 15 (v02 only) — non-Agree: nothing written.
            ws.Cell($"{G}15").IsEmpty().Should().BeTrue("non-Agree question must be left untouched (G)");
            ws.Cell($"{J}15").IsEmpty().Should().BeTrue("non-Agree question must be left untouched (J)");
            ws.Cell($"{P}15").IsEmpty().Should().BeTrue("non-Agree question must be left untouched (P)");
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

    // v01 previous-response workbook. Seven test questions in section 1 (rows 4–15),
    // section header at row 3. H values and DV types drive the type-compatibility scenarios.
    private static void WritePreviousV01(string path, string sheetName, RlqV01Config cfg)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);
        StructureHeaderStamper.Stamp(ws, "rlq", "v01");

        // Section header (production SectionRows: section 1 header at row 3)
        ws.Cell(3, cfg.TextColumn).Value = "Test Section";

        // X1 (row 4) — equal: WholeNumber H=3
        WriteV01Anchor(ws, cfg, row: 4, xref: "x1", text: "X1 text", providedBy: "OU1", curExp: null);
        ws.Cell(4, cfg.AnswerColumn).Value = 3;
        ws.Cell(4, cfg.AnswerColumn).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);

        // X2 (row 5) — widen: WholeNumber H=5 (target v02 H=Decimal)
        WriteV01Anchor(ws, cfg, row: 5, xref: "x2", text: "X2 text", providedBy: null, curExp: null);
        ws.Cell(5, cfg.AnswerColumn).Value = 5;
        ws.Cell(5, cfg.AnswerColumn).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);

        // X3 (rows 6–8) — narrow: Decimal H=4.5 (target v02 H=WholeNumber); 3 K values
        WriteV01Anchor(ws, cfg, row: 6, xref: "x3", text: "X3 text", providedBy: "OU3", curExp: "k3a");
        ws.Cell(6, cfg.AnswerColumn).Value = 4.5;
        ws.Cell(6, cfg.AnswerColumn).CreateDataValidation().Decimal.EqualOrGreaterThan(0);
        ws.Cell(7, cfg.XrefIdColumn).Value             = "x3";
        ws.Cell(7, cfg.CurrentExplanationColumn).Value = "k3b";
        ws.Cell(8, cfg.XrefIdColumn).Value             = "x3";
        ws.Cell(8, cfg.CurrentExplanationColumn).Value = "k3c";
        MergeV01(ws, cfg, anchorRow: 6, lastRow: 8);

        // X5 (rows 9–11) — multi-row K→J: 3 K values + O payload
        WriteV01Anchor(ws, cfg, row: 9, xref: "x5", text: "X5 text", providedBy: "OU5", curExp: "k5a");
        ws.Cell(9, cfg.AnswerColumn).Value = 2;
        ws.Cell(9, cfg.AnswerColumn).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
        ws.Cell(10, cfg.XrefIdColumn).Value             = "x5";
        ws.Cell(10, cfg.CurrentExplanationColumn).Value = "k5b";
        ws.Cell(11, cfg.XrefIdColumn).Value             = "x5";
        ws.Cell(11, cfg.CurrentExplanationColumn).Value = "k5c";
        MergeV01(ws, cfg, anchorRow: 9, lastRow: 11);

        // X4 (row 12) — mismatch: List H="Yes" (target v02 H=WholeNumber)
        //                K→J and O→P must fire even though G is skipped
        WriteV01Anchor(ws, cfg, row: 12, xref: "x4", text: "X4 text", providedBy: "OU4", curExp: "k4a");
        ws.Cell(12, cfg.AnswerColumn).Value = "Yes";
        ws.Cell(12, cfg.AnswerColumn).CreateDataValidation().List("\"Yes,No\"");

        // X6 (rows 13–15) — row-count mismatch: v01 has 3 rows, v02 will have 2
        WriteV01Anchor(ws, cfg, row: 13, xref: "x6", text: "X6 text", providedBy: "OU6", curExp: "k6a");
        ws.Cell(13, cfg.AnswerColumn).Value = 1;
        ws.Cell(13, cfg.AnswerColumn).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
        ws.Cell(14, cfg.XrefIdColumn).Value             = "x6";
        ws.Cell(14, cfg.CurrentExplanationColumn).Value = "k6b";
        ws.Cell(15, cfg.XrefIdColumn).Value             = "x6";
        ws.Cell(15, cfg.CurrentExplanationColumn).Value = "k6c";
        MergeV01(ws, cfg, anchorRow: 13, lastRow: 15);

        wb.SaveAs(path);
    }

    // v02 current-template workbook. G/J/P intentionally blank (inject targets).
    // H has DV applied (no value) to drive the target-type category lookup.
    // X9 (row 15) exists only in v02 — no matching XrefId in v01 → non-Agree.
    private static void WriteCurrentV02(string path, string sheetName, RlqV02Config cfg)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);
        StructureHeaderStamper.Stamp(ws, "rlq", "v02");

        ws.Cell(3, cfg.TextColumn).Value = "Test Section";

        // X1 (row 4) — equal target: WholeNumber DV
        WriteV02Anchor(ws, cfg, row: 4, xref: "x1", text: "X1 text");
        ws.Cell(4, cfg.AnswerColumn).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);

        // X2 (row 5) — widen target: Decimal DV
        WriteV02Anchor(ws, cfg, row: 5, xref: "x2", text: "X2 text");
        ws.Cell(5, cfg.AnswerColumn).CreateDataValidation().Decimal.EqualOrGreaterThan(0);

        // X3 (rows 6–8) — narrow target: WholeNumber DV (3 rows match v01's 3)
        WriteV02Anchor(ws, cfg, row: 6, xref: "x3", text: "X3 text");
        ws.Cell(6, cfg.AnswerColumn).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
        ws.Cell(7, cfg.XrefIdColumn).Value = "x3";
        ws.Cell(8, cfg.XrefIdColumn).Value = "x3";
        MergeV02(ws, cfg, anchorRow: 6, lastRow: 8);

        // X5 (rows 9–11) — multi-row K→J target (3 rows)
        WriteV02Anchor(ws, cfg, row: 9, xref: "x5", text: "X5 text");
        ws.Cell(9, cfg.AnswerColumn).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
        ws.Cell(10, cfg.XrefIdColumn).Value = "x5";
        ws.Cell(11, cfg.XrefIdColumn).Value = "x5";
        MergeV02(ws, cfg, anchorRow: 9, lastRow: 11);

        // X4 (row 12) — mismatch target: WholeNumber DV
        WriteV02Anchor(ws, cfg, row: 12, xref: "x4", text: "X4 text");
        ws.Cell(12, cfg.AnswerColumn).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);

        // X6 (rows 13–14) — row-count mismatch target: 2 rows (v01 has 3)
        WriteV02Anchor(ws, cfg, row: 13, xref: "x6", text: "X6 text");
        ws.Cell(13, cfg.AnswerColumn).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);
        ws.Cell(14, cfg.XrefIdColumn).Value = "x6";
        MergeV02(ws, cfg, anchorRow: 13, lastRow: 14);

        // X9 (row 15) — non-Agree: v02-only (no "x9" in v01)
        WriteV02Anchor(ws, cfg, row: 15, xref: "x9", text: "X9 text");
        ws.Cell(15, cfg.AnswerColumn).CreateDataValidation().WholeNumber.EqualOrGreaterThan(0);

        wb.SaveAs(path);
    }

    // Sets the once-per-question v01 columns on the anchor row:
    // QuestionNumber (=xref), TextColumn, XrefId, ProvidedBy (O), CurrentExplanation (K on anchor).
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

    // Sets the once-per-question v02 columns on the anchor row:
    // QuestionNumber (=xref), TextColumn, XrefId.
    // G/J/P intentionally blank (inject targets).
    private static void WriteV02Anchor(
        IXLWorksheet ws, RlqV02Config cfg, int row, string xref, string text)
    {
        ws.Cell(row, cfg.QuestionNumberColumn).Value = xref;
        ws.Cell(row, cfg.TextColumn).Value           = text;
        ws.Cell(row, cfg.XrefIdColumn).Value         = xref;
    }

    // Merges the once-per-question v01 columns (C/D/E/F/G/H/L/O) over the question group.
    // XrefId (Q) and explanation triplet (I/J/K) are NOT merged.
    private static void MergeV01(IXLWorksheet ws, RlqV01Config cfg, int anchorRow, int lastRow)
    {
        foreach (var col in new[]
        {
            cfg.QuestionNumberColumn, cfg.TextColumn,           cfg.GuidanceColumn,
            cfg.RequestedTypeColumn,  cfg.PreviousAnswerColumn, cfg.AnswerColumn,
            cfg.MaterialChangeColumn, cfg.ProvidedByColumn
        })
        {
            ws.Range($"{col}{anchorRow}:{col}{lastRow}").Merge();
        }
    }

    // Merges the once-per-question v02 columns (C/D/E/F/G/H/L/M/P) over the question group.
    // XrefId (R) and explanation triplet (I/J/K) are NOT merged.
    private static void MergeV02(IXLWorksheet ws, RlqV02Config cfg, int anchorRow, int lastRow)
    {
        foreach (var col in new[]
        {
            cfg.QuestionNumberColumn, cfg.TextColumn,           cfg.GuidanceColumn,
            cfg.RequestedTypeColumn,  cfg.PreviousAnswerColumn, cfg.AnswerColumn,
            cfg.MaterialChangeColumn, cfg.HowExplanationColumn, cfg.ProvidedByColumn
        })
        {
            ws.Range($"{col}{anchorRow}:{col}{lastRow}").Merge();
        }
    }
}
