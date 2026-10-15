using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.RiskLevelQuestionInjectED;
using ItrqTool.Tasks.WorksheetStructure;

namespace ItrqTool.Tasks.Tests.RiskLevelQuestionInjectED;

/// <summary>
/// Task-level tests for <see cref="RiskLevelQuestionInjectEDV01ToV02Task"/> through ExecuteAsync.
/// Tasks.Tests has no Infrastructure reference, so the reader is a hand-written in-memory fake
/// (one cell map per workbook, serving both ReadRows and ReadCells) and the writer and the
/// worksheet-structure mediator are NSubstitute substitutes. Config files are real JSON in a temp
/// directory with the ED letters; the workbook paths are empty placeholder files (the task only
/// checks they exist — all content comes from the fake reader).
/// </summary>
public sealed class RiskLevelQuestionInjectEDV01ToV02TaskTests
{
    private const string Sheet = "IT Risk Level Questions";

    // ── fake reader ───────────────────────────────────────────────────────────

    private sealed class Workbook
    {
        public Dictionary<string, ExcelCellStructure> Cells { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Workbook Text(string address, string text)
        {
            Cells[address] = Get(address) with { TextValue = text, NativeValue = text };
            return this;
        }

        public Workbook Number(string address, string text, double native, string? dvType)
        {
            Cells[address] = Get(address) with { TextValue = text, NativeValue = native, DataValidationType = dvType };
            return this;
        }

        public Workbook Dv(string address, string dvType)
        {
            Cells[address] = Get(address) with { DataValidationType = dvType };
            return this;
        }

        private ExcelCellStructure Get(string address)
            => Cells.TryGetValue(address, out var c) ? c : new ExcelCellStructure(null, null, null, null);
    }

    private sealed class FakeReader(Dictionary<string, Workbook> byPath) : IExcelStructureReader
    {
        public IReadOnlyList<ExcelRowStructure> ReadRows(string filePath, string sheetName)
            => byPath[filePath].Cells
                .Select(kv => (Addr: Split(kv.Key), Cell: kv.Value))
                .GroupBy(x => x.Addr.Row)
                .OrderBy(g => g.Key)
                .Select(g => new ExcelRowStructure(g.Key,
                    g.ToDictionary(x => x.Addr.Col, x => x.Cell, StringComparer.OrdinalIgnoreCase)))
                .ToList();

        public IReadOnlyDictionary<string, ExcelCellStructure> ReadCells(
            string filePath, string sheetName, IReadOnlyList<string> a1Ranges)
        {
            var result = new Dictionary<string, ExcelCellStructure>(StringComparer.OrdinalIgnoreCase);
            foreach (var range in a1Ranges)
            {
                var parts = range.Split(':');
                var (c1, r1) = Split(parts[0]);
                var (c2, r2) = Split(parts.Length > 1 ? parts[1] : parts[0]);
                foreach (var (addr, cell) in byPath[filePath].Cells)
                {
                    var (c, r) = Split(addr);
                    if (string.Compare(c, c1, StringComparison.OrdinalIgnoreCase) >= 0 &&
                        string.Compare(c, c2, StringComparison.OrdinalIgnoreCase) <= 0 &&
                        r >= r1 && r <= r2)
                        result[addr.ToUpperInvariant()] = cell;
                }
            }
            return result;
        }

        public IReadOnlyList<string>? ResolveDefinedNameValues(string filePath, string sheetName, string name) => null;

        public IReadOnlyList<string> GetWorksheetNames(string filePath) => [Sheet];

        private static (string Col, int Row) Split(string address)
        {
            var i = 0;
            while (i < address.Length && char.IsLetter(address[i])) i++;
            return (address[..i].ToUpperInvariant(), int.Parse(address[i..]));
        }
    }

    // ── fixture: temp dir, configs, workbooks ─────────────────────────────────

    private sealed class Fixture : IDisposable
    {
        public string Dir { get; } =
            Path.Combine(Path.GetTempPath(), "ItrqTool-rlq-ed-task-tests", Guid.NewGuid().ToString("N"));
        public string InjectConfigPath => Path.Combine(Dir, "rlq-ed-inject-config.json");
        public string PreviousPath => Path.Combine(Dir, "previous.xlsx");
        public string CurrentPath => Path.Combine(Dir, "current.xlsx");
        public string OutputPath => Path.Combine(Dir, "output.xlsx");

        public Workbook Previous { get; } = new();
        public Workbook Current { get; } = new();

        public IWorksheetStructureMediator Mediator { get; } = Substitute.For<IWorksheetStructureMediator>();
        public IExcelTemplateWriter Writer { get; } = Substitute.For<IExcelTemplateWriter>();
        public IReadOnlyList<CellWriteEntry>? Written { get; private set; }

        public Fixture()
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(PreviousPath, "");
            File.WriteAllText(CurrentPath, "");

            Mediator.Verify(Arg.Any<string>(), Arg.Any<WorksheetSchemaRef>())
                .Returns(WorksheetStructureResult.Match());
            Writer.When(w => w.Populate(Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<CellWriteEntry>>(), Arg.Any<string>()))
                .Do(ci => Written = ci.ArgAt<IReadOnlyList<CellWriteEntry>>(2));
        }

        public void WriteConfigs(
            string targetHoldingDv = "K",
            string targetEBOeDv = "L",
            bool writeV02Config = true)
        {
            File.WriteAllText(InjectConfigPath, $$"""
                {
                  "CurrentConfigFilename": "rlq-ed-v02-validation-config.json",
                  "PreviousConfigFilename": "rlq-ed-v01-validation-config.json",
                  "SourceHoldingAnswerColumn": "K",
                  "SourceEBOeAnswerColumn": "L",
                  "TargetHoldingAnswerColumn": "H",
                  "TargetEBOeAnswerColumn": "I",
                  "TargetHoldingDvColumn": "{{targetHoldingDv}}",
                  "TargetEBOeDvColumn": "{{targetEBOeDv}}"
                }
                """);

            File.WriteAllText(Path.Combine(Dir, "rlq-ed-v01-validation-config.json"), $$"""
                {
                  "QuestionNumberColumn": "C", "TextColumn": "D", "GuidanceColumn": "E",
                  "RequestedTypeColumn": "F", "PreviousAnswerColumn": "G", "AnswerColumn": "J",
                  "RequestedExplanationColumn": "M", "PreviousExplanationColumn": "N",
                  "CurrentExplanationColumn": "O", "MaterialChangeColumn": "P",
                  "ProvidedByColumn": "S", "XrefIdColumn": "U",
                  "SheetName": "{{Sheet}}",
                  "SectionRows": ["3:4-10"],
                  "DeviationThreshold": 0.25,
                  "SeverityOverrides": {}
                }
                """);

            if (writeV02Config)
                File.WriteAllText(Path.Combine(Dir, "rlq-ed-v02-validation-config.json"), $$"""
                    {
                      "QuestionNumberColumn": "C", "TextColumn": "D", "GuidanceColumn": "E",
                      "RequestedTypeColumn": "F", "PreviousAnswerColumn": "G", "AnswerColumn": "J",
                      "RequestedExplanationColumn": "M", "PreviousExplanationColumn": "N",
                      "CurrentExplanationColumn": "O", "MaterialChangeColumn": "P",
                      "HowExplanationColumn": "Q", "ProvidedByColumn": "T", "XrefIdColumn": "V",
                      "SheetName": "{{Sheet}}",
                      "SectionRows": ["3:4-10"],
                      "DeviationThreshold": 0.25,
                      "MaterialChangeExplanationTriggers": ["Yes"],
                      "SeverityOverrides": {}
                    }
                    """);
        }

        // One question (xref ED-1, rows 4-5) in both workbooks: answer 3 / Holding 1 / EBOe 2,
        // two current-explanation rows, provided-by "OU-Prev". No target DV.
        public void OneQuestion()
        {
            Previous.Text("D3", "Section A")
                .Text("C4", "1.1").Text("D4", "How is risk assessed?").Text("U4", "ED-1").Text("U5", "ED-1")
                .Number("J4", "3", 3.0, "WholeNumber")
                .Number("K4", "1", 1.0, "WholeNumber")
                .Number("L4", "2", 2.0, "WholeNumber")
                .Text("O4", "expl-1").Text("O5", "expl-2")
                .Text("S4", "OU-Prev");

            Current.Text("D3", "Section A")
                .Text("C4", "1.1").Text("D4", "How is risk assessed?").Text("V4", "ED-1").Text("V5", "ED-1");
        }

        public RiskLevelQuestionInjectEDV01ToV02Task Task()
            => new(new FakeReader(new Dictionary<string, Workbook>(StringComparer.OrdinalIgnoreCase)
            {
                [PreviousPath] = Previous,
                [CurrentPath] = Current,
            }), Writer, Mediator);

        public TaskExecutionContext Ctx(
            string? configPath = null,
            bool withConfigParam = true,
            bool withPrevious = true)
        {
            var inputs = new Dictionary<string, string> { ["currentTemplate"] = CurrentPath };
            if (withPrevious) inputs["previousResponse"] = PreviousPath;

            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (withConfigParam) parameters["configurationFullFilename"] = configPath ?? InjectConfigPath;

            return new TaskExecutionContext(
                TaskId: "rlq-ed-inject",
                InputPaths: inputs,
                OutputPaths: new Dictionary<string, string> { ["output"] = OutputPath },
                Logger: NullLogger.Instance,
                WorkingDirectory: Dir)
            {
                Parameters = parameters
            };
        }

        public void Dispose()
        {
            try { Directory.Delete(Dir, recursive: true); } catch (IOException) { }
        }
    }

    private static CellWriteEntry? Cell(IReadOnlyList<CellWriteEntry>? cells, int row, string column)
        => cells?.SingleOrDefault(e => e.Row == row && e.Column == column);

    // ── T1. TaskType ──────────────────────────────────────────────────────────

    [Fact]
    public void TaskType_IsExactWorkflowType()
    {
        using var f = new Fixture();
        f.Task().TaskType.Should().Be("RiskLevelQuestionInjectED_v01_to_v02");
    }

    // ── T2–T5. parameter / input / config-file failures (standard texts) ──────

    [Fact]
    public async Task MissingConfigParameter_FailsWithStandardText()
    {
        using var f = new Fixture();

        var result = await f.Task().ExecuteAsync(f.Ctx(withConfigParam: false), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Messages.Should().ContainSingle().Which.Text.Should().Be(
            "Required parameter missing or empty: configurationFullFilename.");
    }

    [Fact]
    public async Task MissingInputKey_FailsWithStandardText()
    {
        using var f = new Fixture();
        f.WriteConfigs();

        var result = await f.Task().ExecuteAsync(f.Ctx(withPrevious: false), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Messages.Should().ContainSingle().Which.Text.Should().Be(
            "Required input missing or empty: previousResponse.");
    }

    [Fact]
    public async Task InjectConfigNotFound_FailsWithStandardText()
    {
        using var f = new Fixture();
        var missing = Path.Combine(f.Dir, "nope.json");

        var result = await f.Task().ExecuteAsync(f.Ctx(configPath: missing), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Messages.Should().ContainSingle().Which.Text.Should().Be(
            $"File not found: configurationFullFilename: {missing}");
    }

    [Fact]
    public async Task ReferencedConfigNotFound_FailsNamingTheEdFilename()
    {
        using var f = new Fixture();
        f.WriteConfigs(writeV02Config: false);

        var result = await f.Task().ExecuteAsync(f.Ctx(), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Messages.Should().ContainSingle().Which.Text.Should().Be(
            "Referenced config file(s) not found: CurrentConfigFilename: " +
            Path.Combine(f.Dir, "rlq-ed-v02-validation-config.json"));
    }

    // ── T6. invalid ED config (A3 pair) → Configuration error ─────────────────

    [Fact]
    public async Task InvalidEdConfig_DvColumnEqualsWriteColumn_FailsWithConfigurationError()
    {
        using var f = new Fixture();
        f.WriteConfigs(targetHoldingDv: "H");

        var result = await f.Task().ExecuteAsync(f.Ctx(), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Messages.Should().ContainSingle().Which.Text.Should().Be(
            $"Configuration error ({f.InjectConfigPath}): Configuration is invalid: " +
            "TargetHoldingAnswerColumn and TargetHoldingDvColumn must be distinct.");
    }

    // ── T7. column-letter collision → one Error, nothing else called ──────────

    [Fact]
    public async Task CollisionWithValidationConfig_FailsBeforeGateAndWrite()
    {
        using var f = new Fixture();
        f.WriteConfigs(targetEBOeDv: "T");
        f.OneQuestion();

        var result = await f.Task().ExecuteAsync(f.Ctx(), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Messages.Should().ContainSingle().Which.Should().Match<TaskMessage>(m =>
            m.Severity == MessageSeverity.Error &&
            m.Text == $"Configuration error ({f.InjectConfigPath}): Column letter collision: " +
                      "RlqEDInjectConfig.TargetEBOeDvColumn 'T' equals RlqV02Config.ProvidedByColumn.");
        f.Mediator.ReceivedCalls().Should().BeEmpty();
        f.Writer.ReceivedCalls().Should().BeEmpty();
    }

    // ── T8. structure-gate refs: rlq-ed v01 / v02, never rlq ──────────────────

    [Fact]
    public async Task StructureGate_UsesRlqEdSchemaRefs()
    {
        using var f = new Fixture();
        f.WriteConfigs();
        f.OneQuestion();

        var result = await f.Task().ExecuteAsync(f.Ctx(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        f.Mediator.Received(1).Verify(f.PreviousPath, new WorksheetSchemaRef("rlq-ed", "v01"));
        f.Mediator.Received(1).Verify(f.CurrentPath, new WorksheetSchemaRef("rlq-ed", "v02"));
        f.Mediator.DidNotReceive().Verify(Arg.Any<string>(),
            Arg.Is<WorksheetSchemaRef>(s => s.Questionnaire == "rlq"));
        f.Mediator.ReceivedCalls().Should().HaveCount(2);
    }

    [Fact]
    public async Task StructureGate_Mismatch_FailsAndDoesNotWrite()
    {
        using var f = new Fixture();
        f.WriteConfigs();
        f.OneQuestion();
        f.Mediator.Verify(f.CurrentPath, new WorksheetSchemaRef("rlq-ed", "v02"))
            .Returns(WorksheetStructureResult.Mismatch(
            [
                new ValidationFinding(ValidationCheck.Structure, FindingEvaluation.Fatal,
                    "A1", null, null, null, null, "Header mismatch at A1."),
            ]));

        var result = await f.Task().ExecuteAsync(f.Ctx(), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Messages.Should().ContainSingle().Which.Should().Match<TaskMessage>(m =>
            m.Severity == MessageSeverity.Error && m.Text == "Header mismatch at A1.");
        f.Writer.ReceivedCalls().Should().BeEmpty();
    }

    // ── T9. cancellation propagates ───────────────────────────────────────────

    [Fact]
    public async Task PreCancelledToken_ThrowsOperationCanceled()
    {
        using var f = new Fixture();
        f.WriteConfigs();
        f.OneQuestion();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => f.Task().ExecuteAsync(f.Ctx(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        f.Writer.ReceivedCalls().Should().BeEmpty();
    }

    // ── T10. success: G/H/I from J/K/L, N per explanation row, T ──────────────

    [Fact]
    public async Task Success_WritesAllArmsExplanationsAndProvidedBy()
    {
        using var f = new Fixture();
        f.WriteConfigs();
        f.OneQuestion();

        var result = await f.Task().ExecuteAsync(f.Ctx(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        f.Writer.Received(1).Populate(f.CurrentPath, Sheet, Arg.Any<IReadOnlyList<CellWriteEntry>>(), f.OutputPath);
        f.Written.Should().BeEquivalentTo(new[]
        {
            new CellWriteEntry(4, "G", "3", 3.0),
            new CellWriteEntry(4, "H", "1", 1.0),
            new CellWriteEntry(4, "I", "2", 2.0),
            new CellWriteEntry(4, "N", "expl-1"),
            new CellWriteEntry(5, "N", "expl-2"),
            new CellWriteEntry(4, "T", "OU-Prev"),
        });
        result.Messages.Should().ContainSingle().Which.Should().Match<TaskMessage>(m =>
            m.Severity == MessageSeverity.Info &&
            m.Text == "Inject complete: 1 confident match(es) injected, 0 ambiguous (left untouched), " +
                      "0 unmatched. Wrote 6 cell(s) to output.xlsx.");
    }

    // ── T11. A3: each arm's DV is read from the v02 CURRENT column, not the write column ──

    [Theory]
    [InlineData("J", "G", "answer", true)]
    [InlineData("K", "H", "answer (Holding)", true)]
    [InlineData("L", "I", "answer (EBOe)", true)]
    [InlineData("J", "G", "answer", false)]
    [InlineData("K", "H", "answer (Holding)", false)]
    [InlineData("L", "I", "answer (EBOe)", false)]
    public async Task DvRule_IsReadFromCurrentColumn(string column, string writeColumn, string label, bool dvOnCurrentColumn)
    {
        using var f = new Fixture();
        f.WriteConfigs();
        f.OneQuestion();
        f.Previous.Number($"{column}4", "3.5", 3.5, "Decimal");                       // v01 source = same letter
        f.Current.Dv($"{(dvOnCurrentColumn ? column : writeColumn)}4", "WholeNumber");

        var result = await f.Task().ExecuteAsync(f.Ctx(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        if (dvOnCurrentColumn)
        {
            Cell(f.Written, 4, writeColumn).Should().BeNull();
            result.Messages.Where(m => m.Severity == MessageSeverity.Error).Should().ContainSingle()
                .Which.Text.Should().StartWith($"[{label}] Row 4 (xref ED-1): value '3.5' does not conform");
        }
        else
        {
            Cell(f.Written, 4, writeColumn)!.TypedValue.Should().Be(3.5);
            result.Messages.Should().NotContain(m => m.Severity == MessageSeverity.Error);
        }
    }

    // ── T12. every anchor row gets its own target DV entry ────────────────────

    [Fact]
    public async Task TargetDv_IsPerAnchorRow()
    {
        using var f = new Fixture();
        f.WriteConfigs();
        f.Previous.Text("D3", "Section A")
            .Text("D4", "How is risk assessed?").Text("U4", "ED-1").Number("K4", "3.5", 3.5, "Decimal")
            .Text("D6", "Who approves the remediation plan?").Text("U6", "ED-2").Number("K6", "3.5", 3.5, "Decimal");
        f.Current.Text("D3", "Section A")
            .Text("D4", "How is risk assessed?").Text("V4", "ED-1")
            .Text("D6", "Who approves the remediation plan?").Text("V6", "ED-2")
            .Dv("K6", "WholeNumber");                                                  // second question only

        var result = await f.Task().ExecuteAsync(f.Ctx(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        Cell(f.Written, 4, "H")!.TypedValue.Should().Be(3.5);
        Cell(f.Written, 6, "H").Should().BeNull();
        result.Messages.Where(m => m.Severity == MessageSeverity.Error).Should().ContainSingle()
            .Which.Text.Should().StartWith("[answer (Holding)] Row 6 (xref ED-2): value '3.5' does not conform");
    }
}
