using System.IO;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using ItrqTool.Domain;
using ItrqTool.Infrastructure;
using ItrqTool.Infrastructure.Excel;
using ItrqTool.Integration.Tests.WorksheetStructure;
using ItrqTool.Tasks.RiskLevelQuestionInject;
using ItrqTool.Tasks.RiskLevelQuestionInjectED;

namespace ItrqTool.Integration.Tests.RlqEDInject;

/// <summary>
/// Shared fixture plumbing for the closing RLQ ED inject end-to-end tests (C4): isolated temp
/// directories, header-stamped workbooks, and the ED and standard inject tasks constructed the way
/// InjectWrongVersionTests constructs them — real ClosedXML reader and writer, the production
/// structure mediator, and the committed configs from the solution root. No substitutes.
/// </summary>
internal static class RlqEDWorkbookBuilder
{
    public const string SheetName = "IT Risk Level Questions";

    public static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Solution root (.slnx) not found above test output directory.");
    }

    public static string EdInjectConfigPath() =>
        Path.Combine(SolutionRoot(), "configs", "rlq-ed-inject-config.json");

    public static string StandardInjectConfigPath() =>
        Path.Combine(SolutionRoot(), "configs", "rlq-inject-config.json");

    /// <summary>A fresh, isolated working directory: <c>%TEMP%/ItrqTool-rlq-ed-c4-&lt;tag&gt;-&lt;guid&gt;</c>.</summary>
    public static string NewWorkDir(string tag)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ItrqTool-rlq-ed-c4-{tag}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Creates a workbook with one worksheet named <see cref="SheetName"/> whose header row is
    /// stamped from the committed schema <paramref name="questionnaire"/>-<paramref name="version"/>.
    /// </summary>
    public static XLWorkbook NewStampedWorkbook(string questionnaire, string version, out IXLWorksheet ws)
    {
        var wb = new XLWorkbook();
        ws = wb.Worksheets.Add(SheetName);
        StructureHeaderStamper.Stamp(ws, questionnaire, version);
        return wb;
    }

    /// <summary>Saves a header-only workbook (no data rows) — enough for the structure gate.</summary>
    public static void SaveHeaderOnly(string path, string questionnaire, string version)
    {
        using var wb = NewStampedWorkbook(questionnaire, version, out _);
        wb.SaveAs(path);
    }

    public static RiskLevelQuestionInjectEDV01ToV02Task EdTask()
    {
        var reader = new ClosedXmlExcelStructureReader(NullLogger<ClosedXmlExcelStructureReader>.Instance);
        return new RiskLevelQuestionInjectEDV01ToV02Task(
            reader, new ClosedXmlTemplateWriter(), StructureGateTestSupport.Mediator(reader));
    }

    public static RiskLevelQuestionInjectV01ToV02Task StandardTask()
    {
        var reader = new ClosedXmlExcelStructureReader(NullLogger<ClosedXmlExcelStructureReader>.Instance);
        return new RiskLevelQuestionInjectV01ToV02Task(
            reader, new ClosedXmlTemplateWriter(), StructureGateTestSupport.Mediator(reader));
    }

    public static TaskExecutionContext Context(
        string dir, string previousPath, string currentPath, string outputPath, string configPath)
        => new(
            TaskId: "inject",
            InputPaths: new Dictionary<string, string>
            {
                ["previousResponse"] = previousPath,
                ["currentTemplate"]  = currentPath,
            },
            OutputPaths: new Dictionary<string, string> { ["output"] = outputPath },
            Logger: NullLogger.Instance,
            WorkingDirectory: dir)
        {
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["configurationFullFilename"] = configPath,
            }
        };
}
