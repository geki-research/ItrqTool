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

namespace ItrqTool.Integration.Tests.RlqV02;

/// <summary>
/// Perturbation tests for Rule 2 (ConfiguredTriggerInDvList check): the configured trigger
/// value(s) in MaterialChangeExplanationTriggers must appear in column L's template DV
/// vocabulary. Each test asserts by the Check == ConfigConsistency SUBSET only —
/// ignoring all other checks (lessons 83/112).
/// </summary>
public sealed class RlqV02ConfiguredTriggerInDvListPerturbationTests
{
    // L-column anchor rows in the fixture (Q1=6, Q2=7, Q3=8 anchor, Q4=13).
    private static readonly int[] LAnchorRows = [6, 7, 8, 13];

    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-rlqv02-ctdl", Guid.NewGuid().ToString("N"));

    private static RiskLevelQuestionValidationV02Task BuildTask() =>
        new(
            new ClosedXmlExcelStructureReader(
                NullLogger<ClosedXmlExcelStructureReader>.Instance),
            NullLogger<RiskLevelQuestionValidationV02Task>.Instance);

    private static async Task<ValidationReport> RunAsync(
        string currentPath, string templatePath, string previousPath,
        string configPath, string reportPath, string dir)
    {
        var result = await BuildTask().ExecuteAsync(
            new TaskExecutionContext(
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
                },
            },
            CancellationToken.None);

        result.Succeeded.Should().BeTrue(
            "task must succeed; errors: {0}",
            string.Join("; ", result.Messages.Select(m => m.Text)));

        return ValidationReportSerializer.Deserialize(await File.ReadAllTextAsync(reportPath));
    }

    // Returns the synthetic config JSON with the specified trigger values replacing the
    // default ["Yes"]. All other fields mirror RlqV02BaselineFactory.SyntheticConfigJson exactly.
    private static string ConfigJsonWithTriggers(params string[] triggers)
    {
        var triggerList = string.Join(", ", triggers.Select(t => $"\"{t}\""));
        return $$"""
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
              "HowExplanationColumn": "M",
              "ProvidedByColumn": "P",
              "XrefIdColumn": "R",
              "SheetName": "IT Risk Level Questions",
              "SectionRows": ["5:6-10", "12:13-13"],
              "DeviationThreshold": 0.25,
              "MaterialChangeExplanationTriggers": [{{triggerList}}],
              "SeverityOverrides": {}
            }
            """;
    }

    // Writes the template with an UNRESOLVABLE L DV: a NamedRange DV referencing a name
    // that is not defined in the workbook. Post-processes the factory template so the
    // FROZEN baseline factory is not modified.
    //
    // Mechanism: "=NonExistentListName" → DvListParser classifies as NamedRange →
    // DvRangeRefResolver calls ResolveDefinedNameValues("NonExistentListName") → null
    // (name absent) → MaterialChangeDvListValues stays null → dv-list-unresolvable fires.
    private static void WriteTemplateWithUnresolvableLDv(string path)
    {
        RlqV02BaselineFactory.WriteTemplate(path);
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.First();
        foreach (var row in LAnchorRows)
            ws.Cell(row, "L").CreateDataValidation().List("=NonExistentListName");
        wb.Save();
    }

    // Writes the template with a RESOLVABLE named-range L DV. Adds a "Lists" sheet
    // (A1="Yes", A2="No") and a workbook-scoped name "MaterialChangeList" → Lists!A1:A2,
    // then replaces each anchor row's L DV with "=MaterialChangeList".
    //
    // After the DvRangeRefResolver L pass, ResolveDefinedNameValues("MaterialChangeList")
    // returns ["Yes","No"] → MaterialChangeDvListValues stamped → trigger "Yes" is in list
    // → no ConfigConsistency finding (proves the resolver L pass resolves non-inline DVs,
    // lesson 125).
    private static void WriteTemplateWithResolvableNamedRangeLDv(string path)
    {
        RlqV02BaselineFactory.WriteTemplate(path);
        using var wb = new XLWorkbook(path);
        var listsWs = wb.Worksheets.Add("Lists");
        listsWs.Cell("A1").Value = "Yes";
        listsWs.Cell("A2").Value = "No";
        wb.NamedRanges.Add("MaterialChangeList", listsWs.Range("A1:A2"));
        var ws = wb.Worksheets.First(w => w.Name == RlqV02BaselineFactory.SheetName);
        foreach (var row in LAnchorRows)
            ws.Cell(row, "L").CreateDataValidation().List("=MaterialChangeList");
        wb.Save();
    }

    /// <summary>
    /// Test 1: configured trigger "Maybe" ∉ template L DV vocabulary {Yes,No} →
    /// exactly one config.material-change-explanation.trigger-not-in-dv-list (Fatal, column L).
    /// Template has inline "Yes,No" DV (resolves to ["Yes","No"] via the ApplyDv lambda).
    /// </summary>
    [Fact]
    public async Task TriggerNotInDvList_Maybe_NotInYesNo_OneFatalFinding()
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

            RlqV02BaselineFactory.WriteCurrent(currentPath);
            RlqV02BaselineFactory.WriteTemplate(templatePath);
            RlqV02BaselineFactory.WritePrevious(previousPath);
            // Config with triggers = ["Maybe"] — "Maybe" ∉ {Yes,No}.
            await File.WriteAllTextAsync(configPath, ConfigJsonWithTriggers("Maybe"));

            var report = await RunAsync(currentPath, templatePath, previousPath, configPath, reportPath, dir);

            var ccFindings = report.Findings
                .Where(f => f.Check == ValidationCheck.ConfigConsistency)
                .ToList();

            ccFindings.Should().HaveCount(1,
                "exactly one ConfigConsistency finding expected " +
                "(trigger 'Maybe' not in template list {Yes,No}); actual: {0}",
                string.Join("; ", ccFindings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            ccFindings[0].Evaluation.Should().Be(FindingEvaluation.Fatal);
            ccFindings[0].CellAddresses.Should().Be("L",
                "config-level finding references the column letter only, not a specific row");
            ccFindings[0].CheckResult.Should().Contain("'Maybe' is not a member",
                "message must identify the offending trigger value");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    /// <summary>
    /// Test 2 (clean-guard): default trigger "Yes" ∈ {Yes,No} →
    /// ConfigConsistency subset is empty (no trigger-not-in-dv-list finding).
    /// </summary>
    [Fact]
    public async Task TriggerInDvList_Yes_InYesNo_NoConfigConsistencyFinding()
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

            RlqV02BaselineFactory.WriteCurrent(currentPath);
            RlqV02BaselineFactory.WriteTemplate(templatePath);
            RlqV02BaselineFactory.WritePrevious(previousPath);
            // Default config: triggers = ["Yes"] — "Yes" ∈ {Yes,No}.
            await File.WriteAllTextAsync(configPath, RlqV02BaselineFactory.SyntheticConfigJson);

            var report = await RunAsync(currentPath, templatePath, previousPath, configPath, reportPath, dir);

            report.Findings
                .Where(f => f.Check == ValidationCheck.ConfigConsistency)
                .Should().BeEmpty(
                    "trigger 'Yes' is in {Yes,No} — no ConfigConsistency finding expected; actual: {0}",
                    string.Join("; ", report.Findings
                        .Where(f => f.Check == ValidationCheck.ConfigConsistency)
                        .Select(f =>
                            $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    /// <summary>
    /// Test 3: template L DV is a NamedRange referencing an undefined name →
    /// DvRangeRefResolver L pass leaves MaterialChangeDvListValues null for all aligned
    /// template questions → exactly one config.material-change-explanation.dv-list-unresolvable
    /// (Fatal, column L). Exercises the genuine unresolvable path past the resolver.
    /// </summary>
    [Fact]
    public async Task DvListUnresolvable_TemplateNamedRangeNotDefined_OneFatalFinding()
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

            RlqV02BaselineFactory.WriteCurrent(currentPath);
            // Template L DV: "=NonExistentListName" (NamedRange, name not defined) →
            // resolver returns null → anyTemplateAligned=true, repList=null → dv-list-unresolvable.
            WriteTemplateWithUnresolvableLDv(templatePath);
            RlqV02BaselineFactory.WritePrevious(previousPath);
            // Default config: triggers = ["Yes"].
            await File.WriteAllTextAsync(configPath, RlqV02BaselineFactory.SyntheticConfigJson);

            var report = await RunAsync(currentPath, templatePath, previousPath, configPath, reportPath, dir);

            var ccFindings = report.Findings
                .Where(f => f.Check == ValidationCheck.ConfigConsistency)
                .ToList();

            ccFindings.Should().HaveCount(1,
                "exactly one ConfigConsistency finding expected " +
                "(template L DV references undefined name 'NonExistentListName'); actual: {0}",
                string.Join("; ", ccFindings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));

            ccFindings[0].Evaluation.Should().Be(FindingEvaluation.Fatal);
            ccFindings[0].CellAddresses.Should().Be("L",
                "config-level finding references the column letter only, not a specific row");
            ccFindings[0].CheckResult.Should().Contain("could not be resolved",
                "message must state the vocabulary could not be resolved from the template");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    /// <summary>
    /// Test 4 (clean-guard): template L DV is a RESOLVABLE named-range (MaterialChangeList →
    /// Lists!A1:A2 = {Yes,No}) → DvRangeRefResolver L pass stamps MaterialChangeDvListValues =
    /// ["Yes","No"] → trigger "Yes" ∈ {Yes,No} → no ConfigConsistency finding.
    /// Proves the DvRangeRefResolver L pass resolves non-inline named-range DVs (lesson 125).
    /// </summary>
    [Fact]
    public async Task ResolvableNamedRangeLDv_TriggerYes_NoDvListUnresolvableFinding()
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

            RlqV02BaselineFactory.WriteCurrent(currentPath);
            // Template L DV: "=MaterialChangeList" (NamedRange, defined → Lists!A1:A2 = {Yes,No}) →
            // resolver stamps ["Yes","No"] → trigger "Yes" ∈ list → no ConfigConsistency finding.
            WriteTemplateWithResolvableNamedRangeLDv(templatePath);
            RlqV02BaselineFactory.WritePrevious(previousPath);
            // Default config: triggers = ["Yes"] — "Yes" ∈ resolved {Yes,No}.
            await File.WriteAllTextAsync(configPath, RlqV02BaselineFactory.SyntheticConfigJson);

            var report = await RunAsync(currentPath, templatePath, previousPath, configPath, reportPath, dir);

            report.Findings
                .Where(f => f.Check == ValidationCheck.ConfigConsistency)
                .Should().BeEmpty(
                    "named-range DV 'MaterialChangeList' resolves to {Yes,No} via the DvRangeRefResolver " +
                    "L pass; trigger 'Yes' ∈ list — no ConfigConsistency finding expected; actual: {0}",
                    string.Join("; ", report.Findings
                        .Where(f => f.Check == ValidationCheck.ConfigConsistency)
                        .Select(f =>
                            $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
