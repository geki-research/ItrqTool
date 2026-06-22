using FluentAssertions;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation;

// Own coverage for the post-DV-patch entry point ValidationPipeline.RunFromParsed<T>.
// Proves: (1) Run and RunFromParsed are behaviour-equivalent at the unit level — running
// the same inputs end-to-end through Run yields a finding list IDENTICAL to reading+parsing+
// DV-patching the inputs by hand and calling RunFromParsed directly; (2) RunFromParsed works
// standalone over hand-built parsed lists, both clean (zero findings) and minimally perturbed
// (the exact expected finding). Self-contained: a local FakeReader, a local PipeTestQuestion
// record, stub baseline delegates, and real RequiredInputCell / FrozenConstraintCell extensions.
public sealed class RunFromParsedTests
{
    // ── Local IExcelStructureReader implementation ───────────────────────────────

    private sealed class FakeReader : IExcelStructureReader
    {
        private readonly Dictionary<string, IReadOnlyList<ExcelRowStructure>> _rows;
        private readonly Dictionary<string, IReadOnlyDictionary<string, ExcelCellStructure>> _cells;

        public FakeReader(
            Dictionary<string, IReadOnlyList<ExcelRowStructure>> rows,
            Dictionary<string, IReadOnlyDictionary<string, ExcelCellStructure>>? cells = null)
        {
            _rows = rows;
            _cells = cells ?? [];
        }

        public IReadOnlyList<ExcelRowStructure> ReadRows(string filePath, string sheetName)
            => _rows.TryGetValue(filePath, out var r) ? r : Array.Empty<ExcelRowStructure>();

        public IReadOnlyDictionary<string, ExcelCellStructure> ReadCells(
            string filePath, string sheetName, IReadOnlyList<string> a1Ranges)
            => _cells.TryGetValue(filePath, out var c) ? c : new Dictionary<string, ExcelCellStructure>(StringComparer.Ordinal);

        public IReadOnlyList<string>? ResolveDefinedNameValues(string filePath, string sheetName, string name)
            => null;
    }

    // ── Local IAlignmentIdentity record ──────────────────────────────────────────

    private sealed record PipeTestQuestion(
        int RowNumber,
        string? XrefId,
        string OriginalText,
        string QuestionText,
        string SectionName,
        string? QuestionNumber,
        string? Stability,
        string? ProvidedBy,
        string? DvType,
        string? DvOp,
        string? DvFormula,
        string? DvFormula2) : IAlignmentIdentity;

    // ── Layout + factory ──────────────────────────────────────────────────────────

    // Chapter at row 1, section at row 2, questions from row 3–99, all names from "C".
    private static readonly QuestionnaireLayout Layout = LayoutParser.Parse(
        ["1"], ["2:3-99"], "C", "C", "C");

    // Record factory: reads identity from "C" (text) and "D" (XrefId); payload from "K"/"L".
    private static readonly Func<QuestionRowContext, PipeTestQuestion> RecordFactory =
        ctx => new PipeTestQuestion(
            RowNumber: ctx.RowNumber,
            XrefId: CellText(ctx.Row, "D"),
            OriginalText: CellText(ctx.Row, "C") ?? "",
            QuestionText: CellText(ctx.Row, "C") ?? "",
            SectionName: ctx.SectionName,
            QuestionNumber: null,
            Stability: CellText(ctx.Row, "K"),
            ProvidedBy: CellText(ctx.Row, "L"),
            DvType: null, DvOp: null, DvFormula: null, DvFormula2: null);

    private static string? CellText(ExcelRowStructure row, string col)
        => row.CellsByColumn.TryGetValue(col, out var c) ? c.TextValue : null;

    // ── Row builders ─────────────────────────────────────────────────────────────

    private static ExcelRowStructure MakeRow(int rowNumber, params (string Col, string? Text)[] cells)
        => new(rowNumber,
            cells.ToDictionary(c => c.Col, c => new ExcelCellStructure(c.Text, null, null, null)));

    // One chapter + section + question at row 3.
    private static IReadOnlyList<ExcelRowStructure> OneQuestionRows(string xrefId = "X1", string? stability = null)
        => new[]
        {
            MakeRow(1, ("C", "Chapter")),
            MakeRow(2, ("C", "Section")),
            stability is null
                ? MakeRow(3, ("C", "Test question"), ("D", xrefId))
                : MakeRow(3, ("C", "Test question"), ("D", xrefId), ("K", stability)),
        };

    // ── Path constants ────────────────────────────────────────────────────────────

    private const string Cur  = "current.xlsx";
    private const string Tmpl = "template.xlsx";
    private const string Prev = "previous.xlsx";

    // ── Descriptor + baseline helpers ────────────────────────────────────────────

    private static readonly FindingDescriptor BaselineDescriptor =
        new("test.baseline.finding", FindingEvaluation.Warning, ValidationCheck.Structure, "Stub.");

    private static readonly Func<AlignmentResult<PipeTestQuestion>, FindingEmitter, IReadOnlyList<ValidationFinding>>
        EmittingBaseline =
            (alignment, emitter) => alignment.Aligned
                .Select(aq => emitter.Emit("test.baseline.finding",
                    $"C{aq.Current.RowNumber}", aq.Current.QuestionNumber, aq.Current.QuestionText,
                    null, null, "stub baseline"))
                .ToList();

    private static readonly Func<AlignmentResult<PipeTestQuestion>, FindingEmitter, IReadOnlyList<ValidationFinding>>
        SilentBaseline = (_, __) => Array.Empty<ValidationFinding>();

    // Stability extension: RequiredInputCell on column K, allowed ["Yes","No"].
    private static RequiredInputCell<PipeTestQuestion> StabilityRequired() =>
        new(q => q.Stability, q => q.ProvidedBy,
            role: "stability", column: "K",
            allowed: ["Yes", "No"]);

    // Stability DV role: patches DvType/DvOp/DvFormula/DvFormula2 from column K.
    private static (string Column, Func<PipeTestQuestion, ExcelCellStructure, PipeTestQuestion> ApplyDv)
        StabilityDvRole =>
        ("K", (q, cell) => q with
        {
            DvType    = cell.DataValidationType,
            DvOp      = cell.DataValidationOperator,
            DvFormula = cell.DataValidationFormula,
            DvFormula2 = cell.DataValidationFormula2,
        });

    // Stability constraint extension: FrozenConstraintCell on column K.
    private static FrozenConstraintCell<PipeTestQuestion> StabilityConstraint() =>
        new(q => q.DvType, q => q.DvOp, q => q.DvFormula, q => q.DvFormula2,
            q => q.ProvidedBy, role: "stability", column: "K");

    private static IReadOnlyDictionary<string, FindingEvaluation> NoOverrides
        => new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal);

    // Mirrors the read+parse+DV-patch half of Run<T> exactly, so a test can feed the result
    // into RunFromParsed and prove the two halves recombine to the same output.
    private static (IReadOnlyList<PipeTestQuestion> Current,
                    IReadOnlyList<PipeTestQuestion> Template,
                    IReadOnlyList<PipeTestQuestion> Previous) ReadParsePatch(
        IExcelStructureReader reader,
        ValidationPipelineProfile<PipeTestQuestion> profile,
        ICollection<TaskMessage> messages)
    {
        var current  = QuestionParser.Parse(reader.ReadRows(Cur,  profile.SheetName), profile.Layout, profile.RecordFactory, messages);
        var template = QuestionParser.Parse(reader.ReadRows(Tmpl, profile.SheetName), profile.Layout, profile.RecordFactory, messages);
        var previous = QuestionParser.Parse(reader.ReadRows(Prev, profile.SheetName), profile.Layout, profile.RecordFactory, messages);

        foreach (var (column, applyDv) in profile.DvRoles)
        {
            current  = DvPatcher.Patch(reader, Cur,  profile.SheetName, column, current,  applyDv);
            template = DvPatcher.Patch(reader, Tmpl, profile.SheetName, column, template, applyDv);
            previous = DvPatcher.Patch(reader, Prev, profile.SheetName, column, previous, applyDv);
        }

        return (current, template, previous);
    }

    // ── Test 1 — equivalence ────────────────────────────────────────────────────────

    [Fact]
    public void RunFromParsed_Matches_Run_FindingForFinding()
    {
        // A profile that exercises every limb: a DV role, a baseline that emits one finding per
        // aligned question, AND a FrozenConstraintCell extension that fires on a DV change. The
        // resulting finding list (baseline first, then extension) is order-sensitive, so an exact
        // sequence-equal of Run's output vs the hand-assembled RunFromParsed output proves the split
        // is behaviour-preserving — not merely the same set.
        var rows = OneQuestionRows();   // question at row 3, XrefId "X1"
        var tmplCell = new ExcelCellStructure(null, "WholeNumber", null, null);
        var curCell  = new ExcelCellStructure(null, "List", null, null);
        var reader = new FakeReader(
            rows:  new() { [Cur] = rows, [Tmpl] = rows, [Prev] = rows },
            cells: new()
            {
                [Cur]  = new Dictionary<string, ExcelCellStructure>(StringComparer.Ordinal) { ["K3"] = curCell  },
                [Tmpl] = new Dictionary<string, ExcelCellStructure>(StringComparer.Ordinal) { ["K3"] = tmplCell },
                [Prev] = new Dictionary<string, ExcelCellStructure>(StringComparer.Ordinal) { ["K3"] = curCell  },
            });
        var profile = new ValidationPipelineProfile<PipeTestQuestion>(
            SheetName: "Sheet1",
            Layout: Layout,
            RecordFactory: RecordFactory,
            DvRoles: [StabilityDvRole],
            BaselineDescriptors: [BaselineDescriptor],
            RunBaseline: EmittingBaseline,
            Extensions: [StabilityConstraint()]);

        // (a) End-to-end through Run.
        var viaRun = ValidationPipeline.Run(
            reader, Cur, Tmpl, Prev, profile, NoOverrides,
            new List<TaskMessage>(), CancellationToken.None);

        // (b) Read+parse+DV-patch by hand, then call RunFromParsed directly.
        var (current, template, previous) = ReadParsePatch(reader, profile, new List<TaskMessage>());
        var viaRunFromParsed = ValidationPipeline.RunFromParsed(
            current, template, previous, profile, NoOverrides,
            new List<TaskMessage>(), CancellationToken.None);

        // Records give value equality — Equal asserts same fields AND same order.
        viaRunFromParsed.Should().Equal(viaRun);
        viaRun.Should().HaveCount(2);                                  // baseline + constraint
        viaRun[0].Check.Should().Be(ValidationCheck.Structure);       // baseline first
        viaRun[1].Check.Should().Be(ValidationCheck.FrozenConstraint);// extension after
    }

    // ── Test 2 — standalone clean ────────────────────────────────────────────────────

    [Fact]
    public void RunFromParsed_Standalone_Clean_NoFindings()
    {
        // Hand-built parsed lists, identical across all three workbooks, silent baseline,
        // no extensions → nothing fires.
        var q = new PipeTestQuestion(
            RowNumber: 3, XrefId: "X1", OriginalText: "Q", QuestionText: "Q",
            SectionName: "Section", QuestionNumber: null, Stability: "Yes", ProvidedBy: null,
            DvType: null, DvOp: null, DvFormula: null, DvFormula2: null);
        var list = new[] { q };
        var profile = new ValidationPipelineProfile<PipeTestQuestion>(
            SheetName: "Sheet1",
            Layout: Layout,
            RecordFactory: RecordFactory,
            DvRoles: [],
            BaselineDescriptors: [BaselineDescriptor],
            RunBaseline: SilentBaseline,
            Extensions: []);

        var findings = ValidationPipeline.RunFromParsed(
            list, list, list, profile, NoOverrides,
            new List<TaskMessage>(), CancellationToken.None);

        findings.Should().BeEmpty();
    }

    // ── Test 3 — standalone perturbed ────────────────────────────────────────────────

    [Fact]
    public void RunFromParsed_Standalone_Perturbed_EmitsExactFinding()
    {
        // Hand-built parsed lists with the current-year Stability cell blank → the
        // RequiredInputCell extension emits exactly one MissingResponse finding at K3.
        var current = new[]
        {
            new PipeTestQuestion(
                RowNumber: 3, XrefId: "X1", OriginalText: "Q", QuestionText: "Q",
                SectionName: "Section", QuestionNumber: null, Stability: null, ProvidedBy: null,
                DvType: null, DvOp: null, DvFormula: null, DvFormula2: null),
        };
        var template = new[]
        {
            current[0] with { Stability = "Yes" },
        };
        var previous = template;
        var profile = new ValidationPipelineProfile<PipeTestQuestion>(
            SheetName: "Sheet1",
            Layout: Layout,
            RecordFactory: RecordFactory,
            DvRoles: [],
            BaselineDescriptors: [BaselineDescriptor],
            RunBaseline: SilentBaseline,
            Extensions: [StabilityRequired()]);

        var findings = ValidationPipeline.RunFromParsed(
            current, template, previous, profile, NoOverrides,
            new List<TaskMessage>(), CancellationToken.None);

        findings.Should().HaveCount(1);
        findings[0].Check.Should().Be(ValidationCheck.MissingResponse);
        findings[0].CellAddresses.Should().Be("K3");
    }
}
