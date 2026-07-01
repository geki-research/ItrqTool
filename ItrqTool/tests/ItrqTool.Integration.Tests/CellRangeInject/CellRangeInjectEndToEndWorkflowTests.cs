using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Presentation;

namespace ItrqTool.Integration.Tests.CellRangeInject;

/// <summary>
/// End-to-end trial of the CellRangeInject chain through the workflow engine:
/// <c>StaticFileSource(source) + StaticFileSource(targetTemplate) →
/// CellRangeInject → StaticFileSink</c>, every task resolved via the production
/// composition root (real readers/writers, no mocks).
///
/// Fact 1 — happy SHIFT e2e: B2:B4→G2:G4 (3×1 range) + D2→I2 (single cell).
///   Proves shift wiring, native-type preservation (numeric as double, text as string),
///   and that the target cell's number format is preserved by the write.
///
/// Fact 2 — dimension-mismatch FAIL-LOUD: B2:B4→G2:G5 (source 3×1, target 4×1).
///   Proves the task halts with Succeeded=false, the session status becomes Failed,
///   StaticFileSink never runs, and no output file is placed.
/// </summary>
public sealed class CellRangeInjectEndToEndWorkflowTests
{
    private const string SheetName = "RLE";

    private static DirectoryInfo FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException(
            "Solution root (.slnx) not found above test output directory.");
    }

    [Fact]
    public async Task ShiftInject_RunsEndToEnd_NativeTypes_NumberFormatPreserved()
    {
        var solutionRoot = FindSolutionRoot();

        // ── Build fixtures ──────────────────────────────────────────────────────
        var workbooksDir = Path.Combine(AppContext.BaseDirectory, "trial-workbooks", "cell-range-inject");
        Directory.CreateDirectory(workbooksDir);
        var sourcePath   = Path.Combine(workbooksDir, "source.xlsx");
        var templatePath = Path.Combine(workbooksDir, "target_template.xlsx");
        WriteSourceWorkbook(sourcePath);
        WriteTargetTemplateWorkbook(templatePath);

        // ── Workflow engine ─────────────────────────────────────────────────────
        var workflowsDir = Path.Combine(Path.GetTempPath(),
            "ItrqTool-cri-wf-" + Guid.NewGuid().ToString("N"));
        var workflowDataRoot = Path.Combine(Path.GetTempPath(),
            "ItrqTool-cri-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workflowsDir);
        Directory.CreateDirectory(workflowDataRoot);

        var sinkDestination = Path.Combine(
            AppContext.BaseDirectory, "inject-output", "cell-range-inject", "cell_range_injected.xlsx");
        try { File.Delete(sinkDestination); } catch (IOException) { }

        try
        {
            File.Copy(
                Path.Combine(solutionRoot.FullName, "workflows", "cell-range-inject-trial.json"),
                Path.Combine(workflowsDir, "cell-range-inject-trial.json"));

            var services = new ServiceCollection();
            services.AddItrqToolServices(workflowsDir, workflowDataRoot);
            using var sp = services.BuildServiceProvider();

            var loader     = sp.GetRequiredService<IWorkflowLoader>();
            var loadResult = loader.LoadAll();
            loadResult.Failures.Should().BeEmpty("cell-range-inject-trial workflow JSON must load without errors");
            var workflow = loadResult.Workflows.Single(w => w.Id == "cell-range-inject-trial");

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

            injectResult.Should().NotBeNull();
            injectResult!.Messages.Should().Contain(m => m.Text.Contains("Injected"),
                "the inject task must emit its outcome summary");

            File.Exists(sinkDestination).Should().BeTrue(
                "the StaticFileSink must place the final deliverable at destinationFolder/destinationFileName");

            // ── Open the sink-placed workbook and assert shift + native types + format-preservation ──
            using var wb = new XLWorkbook(sinkDestination);
            var ws = wb.Worksheet(SheetName);

            // Shift B2:B4 → G2:G4: numeric values written as native type (double).
            ws.Cell("G2").DataType.Should().Be(XLDataType.Number,
                "shift B2→G2 must produce a Number-typed cell");
            ws.Cell("G2").GetValue<double>().Should().Be(10.0,
                "B2 value 10 must be carried to G2");
            ws.Cell("G3").DataType.Should().Be(XLDataType.Number,
                "shift B3→G3 must produce a Number-typed cell");
            ws.Cell("G3").GetValue<double>().Should().Be(20.5,
                "B3 value 20.5 must be carried to G3");
            ws.Cell("G4").DataType.Should().Be(XLDataType.Number,
                "shift B4→G4 must produce a Number-typed cell");
            ws.Cell("G4").GetValue<double>().Should().Be(30.0,
                "B4 value 30 must be carried to G4");

            // Number format on G2 must be preserved from the target template.
            ws.Cell("G2").Style.NumberFormat.Format.Should().Be("0.00",
                "the target cell's '0.00' number format must be preserved after the inject write");

            // Shift D2 → I2: text value.
            ws.Cell("I2").GetString().Should().Be("hello",
                "shift D2→I2 must carry the text value");
        }
        finally
        {
            try { Directory.Delete(workflowsDir,     recursive: true); } catch (IOException) { }
            try { Directory.Delete(workflowDataRoot, recursive: true); } catch (IOException) { }
            try { Directory.Delete(workbooksDir,     recursive: true); } catch (IOException) { }
            try { File.Delete(sinkDestination); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task DimensionMismatch_FailLoud_InjectFails_SinkNeverRuns_NoOutputFile()
    {
        // ── Build fixtures (isolated subdirectory for parallel-safety) ──────────
        var workbooksDir = Path.Combine(AppContext.BaseDirectory,
            "trial-workbooks", "cell-range-inject-mm");
        Directory.CreateDirectory(workbooksDir);
        var sourcePath   = Path.Combine(workbooksDir, "source.xlsx");
        var templatePath = Path.Combine(workbooksDir, "target_template.xlsx");
        WriteSourceWorkbook(sourcePath);
        WriteTargetTemplateWorkbook(templatePath);

        // ── Dynamic mismatch workflow JSON (B2:B4→G2:G5 = source 3×1, target 4×1) ──
        var workflowsDir = Path.Combine(Path.GetTempPath(),
            "ItrqTool-cri-mm-wf-" + Guid.NewGuid().ToString("N"));
        var workflowDataRoot = Path.Combine(Path.GetTempPath(),
            "ItrqTool-cri-mm-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workflowsDir);
        Directory.CreateDirectory(workflowDataRoot);

        var sinkDestination = Path.Combine(
            AppContext.BaseDirectory, "inject-output", "cell-range-inject-mm", "cell_range_mismatch.xlsx");
        try { File.Delete(sinkDestination); } catch (IOException) { }

        const string mismatchWorkflowId = "cell-range-inject-trial-mm";
        const string mismatchJson = """
            {
              "id": "cell-range-inject-trial-mm",
              "name": "CellRangeInject Mismatch Trial",
              "tasks": [
                {
                  "id": "load-source",
                  "type": "StaticFileSource",
                  "inputs": {},
                  "outputs": { "output": "cri_mm_source.xlsx" },
                  "parameters": { "sourcePath": "trial-workbooks/cell-range-inject-mm/source.xlsx" }
                },
                {
                  "id": "load-target",
                  "type": "StaticFileSource",
                  "inputs": {},
                  "outputs": { "output": "cri_mm_target.xlsx" },
                  "parameters": { "sourcePath": "trial-workbooks/cell-range-inject-mm/target_template.xlsx" }
                },
                {
                  "id": "inject",
                  "type": "CellRangeInject",
                  "inputs": { "source": "load-source.output", "targetTemplate": "load-target.output" },
                  "outputs": { "output": "cri_mm_injected.xlsx" },
                  "parameters": {
                    "sourceSheetName": "RLE",
                    "targetSheetName": "RLE",
                    "mappings": "B2:B4->G2:G5"
                  }
                },
                {
                  "id": "sink",
                  "type": "StaticFileSink",
                  "inputs": { "input": "inject.output" },
                  "outputs": {},
                  "parameters": {
                    "destinationFolder": "inject-output/cell-range-inject-mm",
                    "destinationFileName": "cell_range_mismatch.xlsx"
                  }
                }
              ]
            }
            """;
        File.WriteAllText(
            Path.Combine(workflowsDir, "cell-range-inject-trial-mm.json"), mismatchJson);

        try
        {
            var services = new ServiceCollection();
            services.AddItrqToolServices(workflowsDir, workflowDataRoot);
            using var sp = services.BuildServiceProvider();

            var loader     = sp.GetRequiredService<IWorkflowLoader>();
            var loadResult = loader.LoadAll();
            loadResult.Failures.Should().BeEmpty("mismatch workflow JSON must load without errors");
            var workflow = loadResult.Workflows.Single(w => w.Id == mismatchWorkflowId);

            var factory = sp.GetRequiredService<WorkflowSessionFactory>();
            var session = factory.Create(workflow);

            // ── Run tasks until session halts (inject will fail, sink must not run) ──
            TaskResult? injectResult = null;
            bool sinkRan = false;
            while (session.Status is WorkflowSessionStatus.ReadyToRun
                                  or WorkflowSessionStatus.AwaitingReview)
            {
                var currentNode = workflow.Nodes[session.CurrentIndex];
                var result = await session.RunCurrentTaskAsync();
                if (currentNode.Id == "inject") injectResult = result;
                if (currentNode.Id == "sink")   sinkRan = true;
            }

            session.Status.Should().Be(WorkflowSessionStatus.Failed,
                "session must halt when CellRangeInject returns Succeeded=false");

            injectResult.Should().NotBeNull("CellRangeInject task must have run");
            injectResult!.Succeeded.Should().BeFalse(
                "CellRangeInject must return Succeeded=false on dimension mismatch");
            injectResult.Messages.Should().Contain(
                m => m.Severity == MessageSeverity.Error
                  && m.Text.Contains("3x1") && m.Text.Contains("4x1"),
                "the Error message must describe the source-vs-target dimension mismatch");

            sinkRan.Should().BeFalse(
                "StaticFileSink must not run after CellRangeInject returns Succeeded=false");
            File.Exists(sinkDestination).Should().BeFalse(
                "no output file must be placed when the inject step fails");
        }
        finally
        {
            try { Directory.Delete(workflowsDir,     recursive: true); } catch (IOException) { }
            try { Directory.Delete(workflowDataRoot, recursive: true); } catch (IOException) { }
            try { Directory.Delete(workbooksDir,     recursive: true); } catch (IOException) { }
            try { File.Delete(sinkDestination); } catch (IOException) { }
        }
    }

    // ── DV-aware gating e2e (BL-053 P3/P4a): real ClosedXmlExcelStructureReader/Writer, no mocks ──
    // Inline List (G2/G3), WholeNumber (G4/G5), and same-sheet range-ref List (G6/G7, backing K1:K2)
    // target DV rules; each pair's odd row conforms (→ inject), even row violates NotConformant
    // (→ skip + Error, per InjectionValueGuard.SkipSeverity Model B).

    [Fact]
    public async Task DvGatedInject_RunsEndToEnd_ConformingInjectsViolatingSkipsWithErrors()
    {
        var workbooksDir = Path.Combine(AppContext.BaseDirectory, "trial-workbooks", "cell-range-inject-dv");
        Directory.CreateDirectory(workbooksDir);
        var sourcePath   = Path.Combine(workbooksDir, "source.xlsx");
        var templatePath = Path.Combine(workbooksDir, "target_template.xlsx");
        WriteDvSourceWorkbook(sourcePath);
        WriteDvTargetTemplateWorkbook(templatePath);

        var workflowsDir = Path.Combine(Path.GetTempPath(),
            "ItrqTool-cri-dv-wf-" + Guid.NewGuid().ToString("N"));
        var workflowDataRoot = Path.Combine(Path.GetTempPath(),
            "ItrqTool-cri-dv-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workflowsDir);
        Directory.CreateDirectory(workflowDataRoot);

        var sinkDestination = Path.Combine(
            AppContext.BaseDirectory, "inject-output", "cell-range-inject-dv", "cell_range_dv_injected.xlsx");
        try { File.Delete(sinkDestination); } catch (IOException) { }

        const string dvWorkflowId = "cell-range-inject-trial-dv";
        const string dvJson = """
            {
              "id": "cell-range-inject-trial-dv",
              "name": "CellRangeInject DV-Gated Trial",
              "tasks": [
                {
                  "id": "load-source",
                  "type": "StaticFileSource",
                  "inputs": {},
                  "outputs": { "output": "cri_dv_source.xlsx" },
                  "parameters": { "sourcePath": "trial-workbooks/cell-range-inject-dv/source.xlsx" }
                },
                {
                  "id": "load-target",
                  "type": "StaticFileSource",
                  "inputs": {},
                  "outputs": { "output": "cri_dv_target.xlsx" },
                  "parameters": { "sourcePath": "trial-workbooks/cell-range-inject-dv/target_template.xlsx" }
                },
                {
                  "id": "inject",
                  "type": "CellRangeInject",
                  "inputs": { "source": "load-source.output", "targetTemplate": "load-target.output" },
                  "outputs": { "output": "cri_dv_injected.xlsx" },
                  "parameters": {
                    "sourceSheetName": "RLE",
                    "targetSheetName": "RLE",
                    "mappings": "B2->G2;B3->G3;B4->G4;B5->G5;B6->G6;B7->G7"
                  }
                },
                {
                  "id": "sink",
                  "type": "StaticFileSink",
                  "inputs": { "input": "inject.output" },
                  "outputs": {},
                  "parameters": {
                    "destinationFolder": "inject-output/cell-range-inject-dv",
                    "destinationFileName": "cell_range_dv_injected.xlsx"
                  }
                }
              ]
            }
            """;
        File.WriteAllText(
            Path.Combine(workflowsDir, "cell-range-inject-trial-dv.json"), dvJson);

        try
        {
            var services = new ServiceCollection();
            services.AddItrqToolServices(workflowsDir, workflowDataRoot);
            using var sp = services.BuildServiceProvider();

            var loader     = sp.GetRequiredService<IWorkflowLoader>();
            var loadResult = loader.LoadAll();
            loadResult.Failures.Should().BeEmpty("DV-gated trial workflow JSON must load without errors");
            var workflow = loadResult.Workflows.Single(w => w.Id == dvWorkflowId);

            var factory = sp.GetRequiredService<WorkflowSessionFactory>();
            var session = factory.Create(workflow);

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

            injectResult.Should().NotBeNull();
            injectResult!.Messages.Should().Contain(m => m.Text.Contains("Injected 3 cell"),
                "3 of the 6 mapped pairs conform and must be injected");
            injectResult.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.StartsWith("G3:") && m.Text.Contains("does not conform"),
                "the inline-List non-member must be skipped with an Error");
            injectResult.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.StartsWith("G5:") && m.Text.Contains("does not conform"),
                "the WholeNumber violator must be skipped with an Error");
            injectResult.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.StartsWith("G7:") && m.Text.Contains("does not conform"),
                "the range-ref-List non-member must be skipped with an Error");

            File.Exists(sinkDestination).Should().BeTrue(
                "the StaticFileSink must place the final deliverable even though some pairs were skipped");

            using var wb = new XLWorkbook(sinkDestination);
            var ws = wb.Worksheet(SheetName);

            ws.Cell("G2").GetString().Should().Be("Yes", "the conforming inline-List member must be injected");
            ws.Cell("G3").IsEmpty().Should().BeTrue("the inline-List non-member must be omitted, not written");

            ws.Cell("G4").GetValue<double>().Should().Be(5.0, "the conforming WholeNumber value must be injected");
            ws.Cell("G5").IsEmpty().Should().BeTrue("the WholeNumber violator must be omitted, not written");

            ws.Cell("G6").GetString().Should().Be("Alpha", "the conforming range-ref-List member must be injected");
            ws.Cell("G7").IsEmpty().Should().BeTrue("the range-ref-List non-member must be omitted, not written");
        }
        finally
        {
            try { Directory.Delete(workflowsDir,     recursive: true); } catch (IOException) { }
            try { Directory.Delete(workflowDataRoot, recursive: true); } catch (IOException) { }
            try { Directory.Delete(workbooksDir,     recursive: true); } catch (IOException) { }
            try { File.Delete(sinkDestination); } catch (IOException) { }
        }
    }

    // UnresolvableList target (all-blank backing range) end-to-end: proves the OTHER Model-B
    // severity branch (Warning) survives through the real reader/writer, complementing the
    // NotConformant/Error branches proven above (lesson 138 — both branches, not just one).
    [Fact]
    public async Task DvGatedInject_RunsEndToEnd_UnresolvableListSkipsWithWarning()
    {
        var workbooksDir = Path.Combine(AppContext.BaseDirectory, "trial-workbooks", "cell-range-inject-dv-unresolvable");
        Directory.CreateDirectory(workbooksDir);
        var sourcePath   = Path.Combine(workbooksDir, "source.xlsx");
        var templatePath = Path.Combine(workbooksDir, "target_template.xlsx");
        WriteDvSourceWorkbook(sourcePath);
        WriteDvTargetTemplateWorkbook(templatePath);

        var workflowsDir = Path.Combine(Path.GetTempPath(),
            "ItrqTool-cri-dv-ur-wf-" + Guid.NewGuid().ToString("N"));
        var workflowDataRoot = Path.Combine(Path.GetTempPath(),
            "ItrqTool-cri-dv-ur-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workflowsDir);
        Directory.CreateDirectory(workflowDataRoot);

        var sinkDestination = Path.Combine(
            AppContext.BaseDirectory, "inject-output", "cell-range-inject-dv-unresolvable", "cell_range_dv_unresolvable_injected.xlsx");
        try { File.Delete(sinkDestination); } catch (IOException) { }

        const string dvWorkflowId = "cell-range-inject-trial-dv-unresolvable";
        const string dvJson = """
            {
              "id": "cell-range-inject-trial-dv-unresolvable",
              "name": "CellRangeInject DV-Gated UnresolvableList Trial",
              "tasks": [
                {
                  "id": "load-source",
                  "type": "StaticFileSource",
                  "inputs": {},
                  "outputs": { "output": "cri_dv_ur_source.xlsx" },
                  "parameters": { "sourcePath": "trial-workbooks/cell-range-inject-dv-unresolvable/source.xlsx" }
                },
                {
                  "id": "load-target",
                  "type": "StaticFileSource",
                  "inputs": {},
                  "outputs": { "output": "cri_dv_ur_target.xlsx" },
                  "parameters": { "sourcePath": "trial-workbooks/cell-range-inject-dv-unresolvable/target_template.xlsx" }
                },
                {
                  "id": "inject",
                  "type": "CellRangeInject",
                  "inputs": { "source": "load-source.output", "targetTemplate": "load-target.output" },
                  "outputs": { "output": "cri_dv_ur_injected.xlsx" },
                  "parameters": {
                    "sourceSheetName": "RLE",
                    "targetSheetName": "RLE",
                    "mappings": "B8->G8"
                  }
                },
                {
                  "id": "sink",
                  "type": "StaticFileSink",
                  "inputs": { "input": "inject.output" },
                  "outputs": {},
                  "parameters": {
                    "destinationFolder": "inject-output/cell-range-inject-dv-unresolvable",
                    "destinationFileName": "cell_range_dv_unresolvable_injected.xlsx"
                  }
                }
              ]
            }
            """;
        File.WriteAllText(
            Path.Combine(workflowsDir, "cell-range-inject-trial-dv-unresolvable.json"), dvJson);

        try
        {
            var services = new ServiceCollection();
            services.AddItrqToolServices(workflowsDir, workflowDataRoot);
            using var sp = services.BuildServiceProvider();

            var loader     = sp.GetRequiredService<IWorkflowLoader>();
            var loadResult = loader.LoadAll();
            loadResult.Failures.Should().BeEmpty("DV-gated UnresolvableList trial workflow JSON must load without errors");
            var workflow = loadResult.Workflows.Single(w => w.Id == dvWorkflowId);

            var factory = sp.GetRequiredService<WorkflowSessionFactory>();
            var session = factory.Create(workflow);

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

            injectResult.Should().NotBeNull();
            injectResult!.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Warning && m.Text.StartsWith("G8:") && m.Text.Contains("could not be resolved"),
                "an UnresolvableList target (all-blank backing range) must be skipped with a Warning");

            File.Exists(sinkDestination).Should().BeTrue(
                "the StaticFileSink must place the final deliverable even though the pair was skipped");

            using var wb = new XLWorkbook(sinkDestination);
            var ws = wb.Worksheet(SheetName);

            ws.Cell("G8").IsEmpty().Should().BeTrue("the UnresolvableList target must be omitted, not written");
        }
        finally
        {
            try { Directory.Delete(workflowsDir,     recursive: true); } catch (IOException) { }
            try { Directory.Delete(workflowDataRoot, recursive: true); } catch (IOException) { }
            try { Directory.Delete(workbooksDir,     recursive: true); } catch (IOException) { }
            try { File.Delete(sinkDestination); } catch (IOException) { }
        }
    }

    // Source workbook: sheet "RLE" — B2/B4/B6 conform to their mapped target's DV rule, B3/B5/B7 violate it;
    // B8 targets an UnresolvableList (all-blank backing range).
    private static void WriteDvSourceWorkbook(string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);

        ws.Cell("B2").Value = "Yes";     // inline List member
        ws.Cell("B3").Value = "Maybe";   // inline List non-member
        ws.Cell("B4").Value = 5;         // WholeNumber, within 1..10
        ws.Cell("B5").Value = 99;        // WholeNumber, out of bound
        ws.Cell("B6").Value = "Alpha";   // range-ref List member (K1:K2)
        ws.Cell("B7").Value = "Gamma";   // range-ref List non-member
        ws.Cell("B8").Value = "Anything"; // range-ref List, backing range K3:K4 all-blank

        wb.SaveAs(path);
    }

    // Target template workbook: sheet "RLE" — G2/G3 inline List, G4/G5 WholeNumber(1..10),
    // G6/G7 range-ref List backed by K1:K2 ("Alpha","Beta"), G8 range-ref List backed by
    // K3:K4 (left blank — UnresolvableList).
    private static void WriteDvTargetTemplateWorkbook(string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);

        ws.Cell("G2").CreateDataValidation().List("\"Yes,No\"");
        ws.Cell("G3").CreateDataValidation().List("\"Yes,No\"");

        ws.Cell("G4").CreateDataValidation().WholeNumber.Between(1, 10);
        ws.Cell("G5").CreateDataValidation().WholeNumber.Between(1, 10);

        ws.Cell("K1").Value = "Alpha";
        ws.Cell("K2").Value = "Beta";
        ws.Cell("G6").CreateDataValidation().List(ws.Range("K1:K2"));
        ws.Cell("G7").CreateDataValidation().List(ws.Range("K1:K2"));

        ws.Cell("G8").CreateDataValidation().List(ws.Range("K3:K4"));

        wb.SaveAs(path);
    }

    // ── Fixture builders ────────────────────────────────────────────────────────

    // Source workbook: sheet "RLE" with B2:B4 numeric and D2 text.
    private static void WriteSourceWorkbook(string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);

        ws.Cell("B2").Value = 10;
        ws.Cell("B3").Value = 20.5;
        ws.Cell("B4").Value = 30;
        ws.Cell("D2").Value = "hello";

        wb.SaveAs(path);
    }

    // Target template workbook: sheet "RLE" with G2 carrying "0.00" number format
    // to prove ExcelStyleHelper preserves the target cell's format after the inject write.
    // G3, G4, I2 are plain (no explicit format).
    private static void WriteTargetTemplateWorkbook(string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);

        ws.Cell("G2").Style.NumberFormat.Format = "0.00";

        wb.SaveAs(path);
    }
}
