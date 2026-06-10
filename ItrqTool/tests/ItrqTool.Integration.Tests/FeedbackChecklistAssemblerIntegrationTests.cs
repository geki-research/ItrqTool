using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Infrastructure.Excel;
using ItrqTool.Tasks;
using ItrqTool.Tasks.Validation;

namespace ItrqTool.Integration.Tests;

/// <summary>
/// End-to-end exercise of <see cref="FeedbackChecklistAssemblerTask"/> against the real
/// <see cref="ClosedXmlFeedbackChecklistWriter"/> and the committed checklist template:
/// flatten ≥2 findings JSON files spanning ≥2 sheets, populate the template, reload, and
/// assert the rows landed in the configured columns in input order with DV/CF preserved.
/// </summary>
public sealed class FeedbackChecklistAssemblerIntegrationTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-assembler-integration", Guid.NewGuid().ToString("N"));

    private static string WriteFindings(string dir, string fileName, ValidationReport report)
    {
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, ValidationReportSerializer.Serialize(report));
        return path;
    }

    [Fact]
    public async Task ExecuteAsync_RealWriter_FlattensFindingsIntoTemplateAcrossSheets()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            // Two findings files spanning two sheets.
            var f1 = WriteFindings(dir, "clq.json", new ValidationReport(
                Sheet: "Control Level Questions",
                TaskType: "ControlLevelValidation",
                Findings:
                [
                    new ValidationFinding(ValidationCheck.Deviation, FindingEvaluation.Error,
                        "C12", "1.1", "What is risk?", "Yes/No", "OrgUnit A", "Value 'Maybe' not allowed."),
                    new ValidationFinding(ValidationCheck.MissingResponse, FindingEvaluation.Warning,
                        "C13", "1.2", "Explain mitigation.", "Free text", null, "Cell is empty.")
                ]));
            var f2 = WriteFindings(dir, "gd.json", new ValidationReport(
                Sheet: "General Data",
                TaskType: "GeneralDataValidation",
                Findings:
                [
                    new ValidationFinding(ValidationCheck.Structure, FindingEvaluation.Fatal,
                        "A1", null, null, null, null, "Header row missing.")
                ]));

            // Config: relative template path (exercises AppContext.BaseDirectory resolution).
            var configPath = Path.Combine(dir, "assembler-config.json");
            File.WriteAllText(configPath, """
            {
              "templatePath": "templates/feedback-checklist-template.xlsx",
              "sheetName": "Checklist",
              "dataStartRow": 2,
              "columnMap": {
                "Counter": "A", "Worksheet": "B", "QuestionNumber": "C", "CellAddresses": "D",
                "QuestionText": "E", "RequestedData": "F", "ProvidedBy": "G",
                "Evaluation": "H", "CheckResult": "I"
              }
            }
            """);

            var output = Path.Combine(dir, "feedback-checklist.xlsx");
            var task = new FeedbackChecklistAssemblerTask(
                new ClosedXmlFeedbackChecklistWriter(),
                NullLogger<FeedbackChecklistAssemblerTask>.Instance);

            var ctx = new TaskExecutionContext(
                TaskId: "assemble",
                InputPaths: new Dictionary<string, string> { ["findings1"] = f1, ["findings2"] = f2 },
                OutputPaths: new Dictionary<string, string> { ["checklist"] = output },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["configurationFullFilename"] = configPath
                }
            };

            var result = await task.ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            File.Exists(output).Should().BeTrue();

            using var wb = new XLWorkbook(output);
            var ws = wb.Worksheet("Checklist");

            // Row 2 — first finding (input order: findings1, then findings2).
            ws.Cell("A2").GetString().Should().Be("1");
            ws.Cell("B2").GetString().Should().Be("Control Level Questions");
            ws.Cell("C2").GetString().Should().Be("1.1");
            ws.Cell("D2").GetString().Should().Be("C12");
            ws.Cell("E2").GetString().Should().Be("What is risk?");
            ws.Cell("F2").GetString().Should().Be("Yes/No");
            ws.Cell("G2").GetString().Should().Be("OrgUnit A");
            ws.Cell("H2").GetString().Should().Be("Error");
            ws.Cell("I2").GetString().Should().Be("Value 'Maybe' not allowed.");

            // Row 3 — second finding, same sheet.
            ws.Cell("A3").GetString().Should().Be("2");
            ws.Cell("B3").GetString().Should().Be("Control Level Questions");
            ws.Cell("H3").GetString().Should().Be("Warning");
            ws.Cell("I3").GetString().Should().Be("Cell is empty.");

            // Row 4 — finding from the second input/sheet, counter continued.
            ws.Cell("A4").GetString().Should().Be("3");
            ws.Cell("B4").GetString().Should().Be("General Data");
            ws.Cell("D4").GetString().Should().Be("A1");
            ws.Cell("H4").GetString().Should().Be("Fatal");
            ws.Cell("I4").GetString().Should().Be("Header row missing.");

            // Optional fields absent on the General Data finding stay blank.
            ws.Cell("C4").IsEmpty().Should().BeTrue();
            ws.Cell("G4").IsEmpty().Should().BeTrue();

            // Template structure survives the populate.
            ws.DataValidations.Count().Should().Be(1, "template has 1 data-validation rule");
            ws.ConditionalFormats.Count().Should().Be(4, "template has 4 conditional-format rules");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
