using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks;
using ItrqTool.Tasks.Validation;

namespace ItrqTool.Tasks.Tests;

public sealed class FeedbackChecklistAssemblerTaskTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-assembler-task-tests", Guid.NewGuid().ToString("N"));

    private static FeedbackChecklistAssemblerTask MakeTask(IFeedbackChecklistWriter? writer = null)
    {
        var w = writer ?? Substitute.For<IFeedbackChecklistWriter>();
        return new FeedbackChecklistAssemblerTask(w, NullLogger<FeedbackChecklistAssemblerTask>.Instance);
    }

    private static TaskExecutionContext MakeCtx(
        string dir,
        IReadOnlyDictionary<string, string> inputs,
        string configPath,
        string outputFile = "checklist.xlsx")
        => new(
            TaskId: "assemble",
            InputPaths: inputs,
            OutputPaths: new Dictionary<string, string> { ["checklist"] = Path.Combine(dir, outputFile) },
            Logger: NullLogger.Instance,
            WorkingDirectory: dir)
        {
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["configurationFullFilename"] = configPath
            }
        };

    // A config whose template points at a real file so the task's existence check passes.
    // (The injected writer is a substitute, so the template is never actually opened.)
    private static string WriteConfig(string dir, string? templatePath = null, int dataStartRow = 2)
    {
        var template = templatePath ?? Path.Combine(dir, "template.xlsx");
        if (templatePath is null) File.WriteAllText(template, "stub"); // existence only
        var json = $$"""
        {
          "templatePath": {{System.Text.Json.JsonSerializer.Serialize(template)}},
          "sheetName": "Checklist",
          "dataStartRow": {{dataStartRow}},
          "columnMap": {
            "Counter": "A", "Worksheet": "B", "QuestionNumber": "C", "CellAddresses": "D",
            "QuestionText": "E", "RequestedData": "F", "ProvidedBy": "G",
            "Evaluation": "H", "CheckResult": "I"
          }
        }
        """;
        var path = Path.Combine(dir, "config.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string WriteFindings(string dir, string fileName, ValidationReport report)
    {
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, ValidationReportSerializer.Serialize(report));
        return path;
    }

    private static ValidationFinding Finding(
        string cellAddresses,
        string checkResult,
        FindingEvaluation evaluation = FindingEvaluation.Error,
        string? questionNumber = null,
        string? questionText = null,
        string? requestedData = null,
        string? providedBy = null)
        => new(
            Check:          ValidationCheck.Deviation,
            Evaluation:     evaluation,
            CellAddresses:  cellAddresses,
            QuestionNumber: questionNumber,
            QuestionText:   questionText,
            RequestedData:  requestedData,
            ProvidedBy:     providedBy,
            CheckResult:    checkResult);

    // ── happy path: single input, fields map through, counter starts at 1 ─────

    [Fact]
    public async Task ExecuteAsync_SingleInput_MapsFieldsAndAssignsCounter()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var configPath = WriteConfig(dir);
            var findings = WriteFindings(dir, "f1.json", new ValidationReport(
                Sheet: "Control Level Questions",
                TaskType: "ControlLevelValidation",
                Findings:
                [
                    Finding("C12", "Value 'Maybe' not allowed.", FindingEvaluation.Error,
                        questionNumber: "1.1", questionText: "What is risk?",
                        requestedData: "Yes/No", providedBy: "OrgUnit A"),
                    Finding("C13", "Cell is empty.", FindingEvaluation.Warning)
                ]));

            IReadOnlyList<FeedbackChecklistRow>? captured = null;
            var writer = Substitute.For<IFeedbackChecklistWriter>();
            writer.When(w => w.Populate(Arg.Any<IReadOnlyList<FeedbackChecklistRow>>(),
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<FeedbackChecklistWriterOptions>()))
                .Do(ci => captured = ci.ArgAt<IReadOnlyList<FeedbackChecklistRow>>(0));

            var ctx = MakeCtx(dir, new Dictionary<string, string> { ["findings1"] = findings }, configPath);

            var result = await MakeTask(writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            captured.Should().NotBeNull();
            var rows = captured!;
            rows.Should().HaveCount(2);

            var first = rows[0];
            first.Counter.Should().Be(1);
            first.Worksheet.Should().Be("Control Level Questions");
            first.QuestionNumber.Should().Be("1.1");
            first.CellAddresses.Should().Be("C12");
            first.QuestionText.Should().Be("What is risk?");
            first.RequestedData.Should().Be("Yes/No");
            first.ProvidedBy.Should().Be("OrgUnit A");
            first.Evaluation.Should().Be(FindingEvaluation.Error);
            first.CheckResult.Should().Be("Value 'Maybe' not allowed.");

            rows[1].Counter.Should().Be(2);
            rows[1].Evaluation.Should().Be(FindingEvaluation.Warning);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── multi-input concatenation order + running counter across ≥2 sheets ────

    [Fact]
    public async Task ExecuteAsync_MultipleInputs_PreservesOrderAndContinuesCounter()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var configPath = WriteConfig(dir);
            var f1 = WriteFindings(dir, "f1.json", new ValidationReport(
                Sheet: "Sheet A", TaskType: "A",
                Findings: [Finding("A1", "a1"), Finding("A2", "a2")]));
            var f2 = WriteFindings(dir, "f2.json", new ValidationReport(
                Sheet: "Sheet B", TaskType: "B",
                Findings: [Finding("B1", "b1")]));

            IReadOnlyList<FeedbackChecklistRow>? captured = null;
            var writer = Substitute.For<IFeedbackChecklistWriter>();
            writer.When(w => w.Populate(Arg.Any<IReadOnlyList<FeedbackChecklistRow>>(),
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<FeedbackChecklistWriterOptions>()))
                .Do(ci => captured = ci.ArgAt<IReadOnlyList<FeedbackChecklistRow>>(0));

            // Deliberately add keys out of order to prove ordinal key ordering drives emission.
            var inputs = new Dictionary<string, string> { ["findings2"] = f2, ["findings1"] = f1 };
            var ctx = MakeCtx(dir, inputs, configPath);

            var result = await MakeTask(writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            captured.Should().NotBeNull();
            var rows = captured!;
            rows.Select(r => r.Counter).Should().Equal(1, 2, 3);
            rows.Select(r => r.Worksheet).Should().Equal("Sheet A", "Sheet A", "Sheet B");
            rows.Select(r => r.CheckResult).Should().Equal("a1", "a2", "b1");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── zero findings: still populates (no rows) + Info message + Succeeded ────

    [Fact]
    public async Task ExecuteAsync_ZeroFindings_PopulatesNoRowsWithInfoMessage()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var configPath = WriteConfig(dir);
            var f1 = WriteFindings(dir, "f1.json", new ValidationReport(
                Sheet: "Empty", TaskType: "E", Findings: []));

            IReadOnlyList<FeedbackChecklistRow>? captured = null;
            var populateCalls = 0;
            var writer = Substitute.For<IFeedbackChecklistWriter>();
            writer.When(w => w.Populate(Arg.Any<IReadOnlyList<FeedbackChecklistRow>>(),
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<FeedbackChecklistWriterOptions>()))
                .Do(ci => { populateCalls++; captured = ci.ArgAt<IReadOnlyList<FeedbackChecklistRow>>(0); });

            var ctx = MakeCtx(dir, new Dictionary<string, string> { ["findings1"] = f1 }, configPath);

            var result = await MakeTask(writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            populateCalls.Should().Be(1, "the checklist is still produced when there are no findings");
            captured.Should().NotBeNull();
            captured!.Should().BeEmpty();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Info && m.Text.Contains("No findings"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── options carry sheet name + start row + column map from config ─────────

    [Fact]
    public async Task ExecuteAsync_PassesConfiguredOptionsToWriter()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var configPath = WriteConfig(dir, dataStartRow: 5);
            var f1 = WriteFindings(dir, "f1.json", new ValidationReport(
                Sheet: "S", TaskType: "T", Findings: [Finding("A1", "x")]));

            FeedbackChecklistWriterOptions? options = null;
            var writer = Substitute.For<IFeedbackChecklistWriter>();
            writer.When(w => w.Populate(Arg.Any<IReadOnlyList<FeedbackChecklistRow>>(),
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<FeedbackChecklistWriterOptions>()))
                .Do(ci => options = ci.ArgAt<FeedbackChecklistWriterOptions>(3));

            var ctx = MakeCtx(dir, new Dictionary<string, string> { ["findings1"] = f1 }, configPath);

            var result = await MakeTask(writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            options.Should().NotBeNull();
            options!.SheetName.Should().Be("Checklist");
            options.DataStartRow.Should().Be(5);
            options.ColumnMap.Should().ContainKey(ChecklistColumn.Counter)
                .WhoseValue.Should().Be("A");
            options.ColumnMap.Should().ContainKey(ChecklistColumn.CheckResult)
                .WhoseValue.Should().Be("I");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── output path comes from OutputPaths["checklist"] ───────────────────────

    [Fact]
    public async Task ExecuteAsync_WritesToConfiguredOutputPath()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var configPath = WriteConfig(dir);
            var f1 = WriteFindings(dir, "f1.json", new ValidationReport(
                Sheet: "S", TaskType: "T", Findings: [Finding("A1", "x")]));

            string? outputPath = null;
            var writer = Substitute.For<IFeedbackChecklistWriter>();
            writer.When(w => w.Populate(Arg.Any<IReadOnlyList<FeedbackChecklistRow>>(),
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<FeedbackChecklistWriterOptions>()))
                .Do(ci => outputPath = ci.ArgAt<string>(2));

            var ctx = MakeCtx(dir, new Dictionary<string, string> { ["findings1"] = f1 }, configPath,
                outputFile: "my-checklist.xlsx");

            var result = await MakeTask(writer).ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            outputPath.Should().Be(Path.Combine(dir, "my-checklist.xlsx"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── failure: missing config parameter ─────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingConfigParameter_FailsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var ctx = new TaskExecutionContext(
                TaskId: "assemble",
                InputPaths: new Dictionary<string, string>(),
                OutputPaths: new Dictionary<string, string> { ["checklist"] = Path.Combine(dir, "out.xlsx") },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir); // no Parameters

            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains("configurationFullFilename"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── failure: config file missing on disk ──────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ConfigFileMissing_FailsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var ctx = MakeCtx(dir, new Dictionary<string, string>(),
                Path.Combine(dir, "no-such-config.json"));

            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Error);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── failure: config is literal 'null' ─────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ConfigLiteralNull_FailsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var configPath = Path.Combine(dir, "config.json");
            File.WriteAllText(configPath, "null");
            var ctx = MakeCtx(dir, new Dictionary<string, string>(), configPath);

            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains("null"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── failure: config is unparseable JSON ───────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ConfigUnparseable_FailsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var configPath = Path.Combine(dir, "config.json");
            File.WriteAllText(configPath, "{ this is not valid json ");
            var ctx = MakeCtx(dir, new Dictionary<string, string>(), configPath);

            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains("parsed"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── failure: config under-specified (empty column map) ────────────────────

    [Fact]
    public async Task ExecuteAsync_ConfigEmptyColumnMap_FailsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var template = Path.Combine(dir, "template.xlsx");
            File.WriteAllText(template, "stub");
            var configPath = Path.Combine(dir, "config.json");
            File.WriteAllText(configPath, $$"""
            {
              "templatePath": {{System.Text.Json.JsonSerializer.Serialize(template)}},
              "sheetName": "Checklist",
              "dataStartRow": 2,
              "columnMap": {}
            }
            """);
            var ctx = MakeCtx(dir, new Dictionary<string, string>(), configPath);

            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains("columnMap"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── failure: template not found ───────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_TemplateMissing_FailsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var configPath = WriteConfig(dir, templatePath: Path.Combine(dir, "absent-template.xlsx"));
            var ctx = MakeCtx(dir, new Dictionary<string, string>(), configPath);

            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains("template"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── failure: an input findings file is missing ────────────────────────────

    [Fact]
    public async Task ExecuteAsync_InputFindingsMissing_FailsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var configPath = WriteConfig(dir);
            var ctx = MakeCtx(dir,
                new Dictionary<string, string> { ["findings1"] = Path.Combine(dir, "gone.json") },
                configPath);

            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains("findings1"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── failure: an input findings file is malformed JSON ─────────────────────

    [Fact]
    public async Task ExecuteAsync_InputFindingsMalformed_FailsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var configPath = WriteConfig(dir);
            var bad = Path.Combine(dir, "bad.json");
            File.WriteAllText(bad, "{ not a report ");
            var ctx = MakeCtx(dir,
                new Dictionary<string, string> { ["findings1"] = bad }, configPath);

            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m =>
                m.Severity == MessageSeverity.Error && m.Text.Contains("findings1"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── failure: input findings file is literal 'null' ────────────────────────

    [Fact]
    public async Task ExecuteAsync_InputFindingsLiteralNull_FailsWithError()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var configPath = WriteConfig(dir);
            var nullFile = Path.Combine(dir, "null.json");
            File.WriteAllText(nullFile, "null");
            var ctx = MakeCtx(dir,
                new Dictionary<string, string> { ["findings1"] = nullFile }, configPath);

            var result = await MakeTask().ExecuteAsync(ctx, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Messages.Should().Contain(m => m.Severity == MessageSeverity.Error);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── cancellation propagates ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var configPath = WriteConfig(dir);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var ctx = MakeCtx(dir, new Dictionary<string, string>(), configPath);

            Func<Task> act = () => MakeTask().ExecuteAsync(ctx, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
