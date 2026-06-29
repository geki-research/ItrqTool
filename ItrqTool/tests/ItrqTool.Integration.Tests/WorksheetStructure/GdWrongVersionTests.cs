using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Infrastructure;
using ItrqTool.Tasks;
using ItrqTool.Tasks.Validation;

namespace ItrqTool.Integration.Tests.WorksheetStructure;

/// <summary>
/// Proves the StructureGate fires correctly through the GD_v01 validation path when the current
/// workbook's header row is blank: the task still Succeeds (Mismatch is a data finding, not a
/// failure), the gate finding folds into the report, and the task returns Succeeded=true.
/// Template and previous carry correct schema headers (stamped), so only the current mismatches —
/// one file, one Fatal finding.
/// Uses a minimal header-only inline fixture (3 tiny workbooks) — does NOT touch the frozen
/// GdV01BaselineFactory engine fixtures.
/// </summary>
public sealed class GdWrongVersionTests
{
    private const string SheetName = "General Data";

    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), $"ItrqTool-gd-wrong-version-{Guid.NewGuid():N}");

    /// <summary>Writes a minimal GD workbook with only the "General Data" sheet.
    /// If <paramref name="stampHeader"/> is true the schema canonical headers are stamped at
    /// row 2; if false the header row is left blank, causing a Mismatch.</summary>
    private static void WriteMinimalGdWorkbook(string path, bool stampHeader)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);
        if (stampHeader)
            StructureHeaderStamper.Stamp(ws, "gd", "v01");
        wb.SaveAs(path);
    }

    private static GdV01MinimalConfig MakeMinimalConfig(string configPath)
    {
        // Minimal config with a single section — just enough for the task to parse without errors.
        // The StructureGate fires BEFORE parse, so section content does not matter for this test.
        var json = """
            {
              "QuestionNumberColumn": "C",
              "TextColumn": "D",
              "GuidanceColumn": "E",
              "RequestedTypeColumn": "F",
              "PreviousAnswerColumn": "G",
              "AnswerColumn": "H",
              "RequestedExplanationColumn": "I",
              "PreviousExplanationColumn": "J",
              "CurrentExplanationColumn": "K",
              "MaterialChangeColumn": "L",
              "ProvidedByColumn": "O",
              "XrefIdColumn": "Q",
              "SheetName": "General Data",
              "Sections": [
                {
                  "HeaderRow": 3,
                  "FirstDataRow": 4,
                  "LastDataRow": 9,
                  "ExpectedName": "Section A",
                  "MaterialChangeRequired": false
                }
              ],
              "DeviationThreshold": 0.25,
              "SeverityOverrides": {}
            }
            """;
        File.WriteAllText(configPath, json);
        return new GdV01MinimalConfig(configPath);
    }

    private sealed record GdV01MinimalConfig(string Path);

    [Fact]
    public async Task GdV01Task_CurrentWithBlankHeaders_ExactlyOneStructureFatal_TaskSucceeds()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var configPath   = Path.Combine(dir, "config.json");
            var reportPath   = Path.Combine(dir, "report.json");

            // current: blank header row → Mismatch on all non-blank schema columns
            WriteMinimalGdWorkbook(currentPath,  stampHeader: false);
            // template + previous: correct schema headers → Match
            WriteMinimalGdWorkbook(templatePath, stampHeader: true);
            WriteMinimalGdWorkbook(previousPath, stampHeader: true);
            MakeMinimalConfig(configPath);

            var reader = new ClosedXmlExcelStructureReader(
                NullLogger<ClosedXmlExcelStructureReader>.Instance);
            var task = new GeneralDataValidationV01Task(
                reader,
                StructureGateTestSupport.Mediator(reader),
                NullLogger<GeneralDataValidationV01Task>.Instance);

            var ctx = new TaskExecutionContext(
                TaskId: "validate",
                InputPaths: new Dictionary<string, string>
                {
                    ["currentResponse"]  = currentPath,
                    ["emptyTemplate"]    = templatePath,
                    ["previousResponse"] = previousPath,
                },
                OutputPaths: new Dictionary<string, string> { ["report"] = reportPath },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["configurationFullFilename"] = configPath,
                }
            };

            var result = await task.ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue("structure mismatch is a data finding, not a task failure");

            var report = ValidationReportSerializer.Deserialize(await File.ReadAllTextAsync(reportPath));
            report.Findings.Should().ContainSingle(f =>
                f.Check == ValidationCheck.Structure &&
                f.Evaluation == FindingEvaluation.Fatal &&
                f.CheckResult.StartsWith("structure.unexpected-worksheet-structure"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
