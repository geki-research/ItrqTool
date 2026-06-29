using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Infrastructure;
using ItrqTool.Infrastructure.Excel;
using ItrqTool.Tasks.ControlLevelQuestionInject;
using ItrqTool.Tasks.GeneralDataInject;
using ItrqTool.Tasks.RiskLevelQuestionInject;

namespace ItrqTool.Integration.Tests.WorksheetStructure;

/// <summary>
/// Proves that both inject tasks halt loudly (Succeeded=false, Error message) when a
/// wrong-version input workbook is supplied. The gate runs before parse, so the workbooks
/// need only a correctly-named worksheet with a stamped header row — no data rows required.
/// </summary>
public sealed class InjectWrongVersionTests
{
    private static string TestWorkDir(string sub) =>
        Path.Combine(Path.GetTempPath(), $"ItrqTool-inject-wrong-version-{sub}", Guid.NewGuid().ToString("N"));

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Solution root (.slnx) not found above test output directory.");
    }

    [Fact]
    public async Task RlqV01ToV02Inject_SourceWrongVersion_HaltsWithStructureError()
    {
        var injectConfigPath = Path.Combine(SolutionRoot(), "configs", "rlq-inject-config.json");
        const string sheetName = "IT Risk Level Questions";
        var dir = TestWorkDir("rlq-source-wrong");
        Directory.CreateDirectory(dir);
        try
        {
            var sourcePath = Path.Combine(dir, "source.xlsx");
            var targetPath = Path.Combine(dir, "target.xlsx");
            var outputPath = Path.Combine(dir, "output.xlsx");

            // SOURCE stamped wrong: rlq-v02 (must be v01)
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add(sheetName);
                StructureHeaderStamper.Stamp(ws, "rlq", "v02");
                wb.SaveAs(sourcePath);
            }
            // TARGET stamped correct: rlq-v02
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add(sheetName);
                StructureHeaderStamper.Stamp(ws, "rlq", "v02");
                wb.SaveAs(targetPath);
            }

            var reader = new ClosedXmlExcelStructureReader(
                NullLogger<ClosedXmlExcelStructureReader>.Instance);
            var task = new RiskLevelQuestionInjectV01ToV02Task(
                reader,
                new ClosedXmlTemplateWriter(),
                StructureGateTestSupport.Mediator(reader));

            var ctx = new TaskExecutionContext(
                TaskId: "inject",
                InputPaths: new Dictionary<string, string>
                {
                    ["previousResponse"] = sourcePath,
                    ["currentTemplate"]  = targetPath,
                },
                OutputPaths: new Dictionary<string, string> { ["output"] = outputPath },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["configurationFullFilename"] = injectConfigPath,
                }
            };

            var result = await task.ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse("wrong-version source must halt the inject task");
            result.Messages.Should().ContainSingle(m =>
                m.Severity == MessageSeverity.Error &&
                m.Text.Contains("rlq-v01"),
                "the error must name the expected source version rlq-v01");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task ClqV02ToV01Inject_TargetWrongVersion_HaltsWithStructureError()
    {
        var injectConfigPath = Path.Combine(SolutionRoot(), "configs", "clq-inject-config.json");
        const string sheetName = "IT Risk Control Self-Assessment";
        var dir = TestWorkDir("clq-target-wrong");
        Directory.CreateDirectory(dir);
        try
        {
            var sourcePath = Path.Combine(dir, "source.xlsx");
            var targetPath = Path.Combine(dir, "target.xlsx");
            var outputPath = Path.Combine(dir, "output.xlsx");

            // SOURCE stamped correct: clq-v02
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add(sheetName);
                StructureHeaderStamper.Stamp(ws, "clq", "v02");
                wb.SaveAs(sourcePath);
            }
            // TARGET stamped wrong: clq-v02 (must be clq-v01)
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add(sheetName);
                StructureHeaderStamper.Stamp(ws, "clq", "v02");
                wb.SaveAs(targetPath);
            }

            var reader = new ClosedXmlExcelStructureReader(
                NullLogger<ClosedXmlExcelStructureReader>.Instance);
            var task = new ControlLevelQuestionInjectV02ToV01Task(
                reader,
                new ClosedXmlTemplateWriter(),
                StructureGateTestSupport.Mediator(reader));

            var ctx = new TaskExecutionContext(
                TaskId: "inject",
                InputPaths: new Dictionary<string, string>
                {
                    ["previousResponse"] = sourcePath,
                    ["currentTemplate"]  = targetPath,
                },
                OutputPaths: new Dictionary<string, string> { ["output"] = outputPath },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["configurationFullFilename"] = injectConfigPath,
                }
            };

            var result = await task.ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse("wrong-version target must halt the inject task");
            result.Messages.Should().ContainSingle(m =>
                m.Severity == MessageSeverity.Error &&
                m.Text.Contains("clq-v01"),
                "the error must name the expected target version clq-v01");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task GdV01ToV02Inject_SourceWrongVersion_HaltsWithStructureError()
    {
        var injectConfigPath = Path.Combine(SolutionRoot(), "configs", "gd-inject-config.json");
        const string sheetName = "General Data";
        var dir = TestWorkDir("gd-source-wrong");
        Directory.CreateDirectory(dir);
        try
        {
            var sourcePath = Path.Combine(dir, "source.xlsx");
            var targetPath = Path.Combine(dir, "target.xlsx");
            var outputPath = Path.Combine(dir, "output.xlsx");

            // SOURCE stamped wrong: gd-v02 (must be v01)
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add(sheetName);
                StructureHeaderStamper.Stamp(ws, "gd", "v02");
                wb.SaveAs(sourcePath);
            }
            // TARGET stamped correct: gd-v02
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add(sheetName);
                StructureHeaderStamper.Stamp(ws, "gd", "v02");
                wb.SaveAs(targetPath);
            }

            var reader = new ClosedXmlExcelStructureReader(
                NullLogger<ClosedXmlExcelStructureReader>.Instance);
            var task = new GeneralDataInjectV01ToV02Task(
                reader,
                new ClosedXmlTemplateWriter(),
                StructureGateTestSupport.Mediator(reader));

            var ctx = new TaskExecutionContext(
                TaskId: "inject",
                InputPaths: new Dictionary<string, string>
                {
                    ["previousResponse"] = sourcePath,
                    ["currentTemplate"]  = targetPath,
                },
                OutputPaths: new Dictionary<string, string> { ["output"] = outputPath },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["configurationFullFilename"] = injectConfigPath,
                }
            };

            var result = await task.ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse("wrong-version source must halt the inject task");
            result.Messages.Should().ContainSingle(m =>
                m.Severity == MessageSeverity.Error &&
                m.Text.Contains("gd-v01"),
                "the error must name the expected source version gd-v01");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task GdV01ToV02Inject_TargetWrongVersion_HaltsWithStructureError()
    {
        var injectConfigPath = Path.Combine(SolutionRoot(), "configs", "gd-inject-config.json");
        const string sheetName = "General Data";
        var dir = TestWorkDir("gd-target-wrong");
        Directory.CreateDirectory(dir);
        try
        {
            var sourcePath = Path.Combine(dir, "source.xlsx");
            var targetPath = Path.Combine(dir, "target.xlsx");
            var outputPath = Path.Combine(dir, "output.xlsx");

            // SOURCE stamped correct: gd-v01
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add(sheetName);
                StructureHeaderStamper.Stamp(ws, "gd", "v01");
                wb.SaveAs(sourcePath);
            }
            // TARGET stamped wrong: gd-v01 (must be gd-v02)
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add(sheetName);
                StructureHeaderStamper.Stamp(ws, "gd", "v01");
                wb.SaveAs(targetPath);
            }

            var reader = new ClosedXmlExcelStructureReader(
                NullLogger<ClosedXmlExcelStructureReader>.Instance);
            var task = new GeneralDataInjectV01ToV02Task(
                reader,
                new ClosedXmlTemplateWriter(),
                StructureGateTestSupport.Mediator(reader));

            var ctx = new TaskExecutionContext(
                TaskId: "inject",
                InputPaths: new Dictionary<string, string>
                {
                    ["previousResponse"] = sourcePath,
                    ["currentTemplate"]  = targetPath,
                },
                OutputPaths: new Dictionary<string, string> { ["output"] = outputPath },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["configurationFullFilename"] = injectConfigPath,
                }
            };

            var result = await task.ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse("wrong-version target must halt the inject task");
            result.Messages.Should().ContainSingle(m =>
                m.Severity == MessageSeverity.Error &&
                m.Text.Contains("gd-v02"),
                "the error must name the expected target version gd-v02");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
