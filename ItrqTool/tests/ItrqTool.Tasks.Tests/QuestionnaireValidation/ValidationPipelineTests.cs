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

// F coverage: ValidationPipeline.Run<T> end-to-end wiring — parse×3, patch×3 per role,
// align, catalogue assembly (baseline+extension descriptors), override-key validation,
// emitter construction, baseline-then-extensions combination. Self-contained: a local
// FakeReader (no NSubstitute), a local PipeTestQuestion record, a stub RunBaseline
// delegate, and real RequiredInputCell / FrozenConstraintCell extension instances.
public sealed class ValidationPipelineTests
{
    // ── Local IExcelStructureReader implementations ──────────────────────────────

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

        public IReadOnlyList<string> GetWorksheetNames(string filePath)
            => Array.Empty<string>();
    }

    private sealed class ThrowingReader : IExcelStructureReader
    {
        public IReadOnlyList<ExcelRowStructure> ReadRows(string filePath, string sheetName)
            => throw new InvalidOperationException("Simulated reader failure.");

        public IReadOnlyDictionary<string, ExcelCellStructure> ReadCells(
            string filePath, string sheetName, IReadOnlyList<string> a1Ranges)
            => throw new InvalidOperationException("Simulated reader failure.");

        public IReadOnlyList<string>? ResolveDefinedNameValues(string filePath, string sheetName, string name)
            => throw new InvalidOperationException("Simulated reader failure.");

        public IReadOnlyList<string> GetWorksheetNames(string filePath)
            => throw new InvalidOperationException("Simulated reader failure.");
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

    // One chapter + section + question at row 3 (shared across most tests).
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

    // ── Shared runner ─────────────────────────────────────────────────────────────

    private static IReadOnlyList<ValidationFinding> Run(
        IExcelStructureReader reader,
        ValidationPipelineProfile<PipeTestQuestion> profile,
        IReadOnlyDictionary<string, FindingEvaluation>? overrides = null,
        ICollection<TaskMessage>? messages = null)
        => ValidationPipeline.Run(
            reader, Cur, Tmpl, Prev, profile,
            overrides ?? new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            messages ?? new List<TaskMessage>(),
            CancellationToken.None);

    // ── Tests ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parses_Aligns_Combines_Empty_WhenNothingFires()
    {
        // All workbooks have no question rows; stub baseline returns nothing; no extensions.
        var reader = new FakeReader(new() { [Cur] = [], [Tmpl] = [], [Prev] = [] });
        var profile = new ValidationPipelineProfile<PipeTestQuestion>(
            SheetName: "Sheet1",
            Layout: Layout,
            RecordFactory: RecordFactory,
            DvRoles: [],
            BaselineDescriptors: [BaselineDescriptor],
            RunBaseline: SilentBaseline,
            Extensions: []);

        var findings = Run(reader, profile);

        findings.Should().BeEmpty();
    }

    [Fact]
    public void BaselineFindings_Surface_First()
    {
        // One question matched across all three workbooks; EmittingBaseline emits one finding.
        var rows = OneQuestionRows();
        var reader = new FakeReader(new() { [Cur] = rows, [Tmpl] = rows, [Prev] = rows });
        var profile = new ValidationPipelineProfile<PipeTestQuestion>(
            SheetName: "Sheet1",
            Layout: Layout,
            RecordFactory: RecordFactory,
            DvRoles: [],
            BaselineDescriptors: [BaselineDescriptor],
            RunBaseline: EmittingBaseline,
            Extensions: []);

        var findings = Run(reader, profile);

        findings.Should().HaveCount(1);
        findings[0].Check.Should().Be(ValidationCheck.Structure);
        findings[0].CheckResult.Should().Be("stub baseline");
    }

    [Fact]
    public void ExtensionFindings_Surface_AfterBaseline()
    {
        // One question; Stability column blank → RequiredInputCell emits missing finding.
        // Baseline is silent. Extension finding appears (after the empty baseline block).
        var rows = OneQuestionRows(stability: null);   // K column absent → Stability = null
        var reader = new FakeReader(new() { [Cur] = rows, [Tmpl] = rows, [Prev] = rows });
        var profile = new ValidationPipelineProfile<PipeTestQuestion>(
            SheetName: "Sheet1",
            Layout: Layout,
            RecordFactory: RecordFactory,
            DvRoles: [],
            BaselineDescriptors: [BaselineDescriptor],
            RunBaseline: SilentBaseline,
            Extensions: [StabilityRequired()]);

        var findings = Run(reader, profile);

        findings.Should().HaveCount(1);
        findings[0].Check.Should().Be(ValidationCheck.MissingResponse);
        findings[0].CellAddresses.Should().Be("K3");
    }

    [Fact]
    public void Catalogue_Includes_ExtensionDescriptors()
    {
        // A SeverityOverrides entry on the extension's id overrides the default (Error → Fatal),
        // proving extension descriptors are registered in the catalogue before override validation.
        var rows = OneQuestionRows(stability: null);
        var reader = new FakeReader(new() { [Cur] = rows, [Tmpl] = rows, [Prev] = rows });
        var profile = new ValidationPipelineProfile<PipeTestQuestion>(
            SheetName: "Sheet1",
            Layout: Layout,
            RecordFactory: RecordFactory,
            DvRoles: [],
            BaselineDescriptors: [BaselineDescriptor],
            RunBaseline: SilentBaseline,
            Extensions: [StabilityRequired()]);
        var overrides = new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal)
        {
            ["input-cell.stability.missing"] = FindingEvaluation.Fatal,
        };

        var findings = Run(reader, profile, overrides);

        findings.Should().HaveCount(1);
        findings[0].Evaluation.Should().Be(FindingEvaluation.Fatal);
    }

    [Fact]
    public void UnknownSeverityOverrideKey_Throws()
    {
        // A SeverityOverrides key present in neither baseline nor extension descriptors
        // → ConfigException naming the unknown key.
        var reader = new FakeReader(new() { [Cur] = [], [Tmpl] = [], [Prev] = [] });
        var profile = new ValidationPipelineProfile<PipeTestQuestion>(
            SheetName: "Sheet1",
            Layout: Layout,
            RecordFactory: RecordFactory,
            DvRoles: [],
            BaselineDescriptors: [BaselineDescriptor],
            RunBaseline: SilentBaseline,
            Extensions: []);
        var overrides = new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal)
        {
            ["no.such.finding"] = FindingEvaluation.Warning,
        };

        var act = () => Run(reader, profile, overrides);

        act.Should().Throw<ConfigException>().WithMessage("*no.such.finding*");
    }

    [Fact]
    public void DvRole_Patches_And_FeedsConstraintCheck()
    {
        // DvRole is applied to column K; template has "WholeNumber", current has "List".
        // FrozenConstraintCell detects the change and emits a finding.
        var rows = OneQuestionRows();   // question at row 3 with XrefId "X1"
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
            RunBaseline: SilentBaseline,
            Extensions: [StabilityConstraint()]);

        var findings = Run(reader, profile);

        findings.Should().HaveCount(1);
        findings[0].Check.Should().Be(ValidationCheck.FrozenConstraint);
        findings[0].CellAddresses.Should().Be("K3");
    }

    [Fact]
    public void DuplicateDescriptorId_ThrowsAtCatalogueBuild()
    {
        // Baseline declares "input-cell.stability.missing"; RequiredInputCell with role "stability"
        // also produces id "input-cell.stability.missing" → FindingCatalogue throws ArgumentException.
        var dupDescriptor = new FindingDescriptor(
            "input-cell.stability.missing", FindingEvaluation.Warning, ValidationCheck.Structure, "Dup.");
        var reader = new FakeReader(new() { [Cur] = [], [Tmpl] = [], [Prev] = [] });
        var profile = new ValidationPipelineProfile<PipeTestQuestion>(
            SheetName: "Sheet1",
            Layout: Layout,
            RecordFactory: RecordFactory,
            DvRoles: [],
            BaselineDescriptors: [dupDescriptor],
            RunBaseline: SilentBaseline,
            Extensions: [StabilityRequired()]);

        var act = () => Run(reader, profile);

        act.Should().Throw<ArgumentException>().WithMessage("*Duplicate*");
    }

    [Fact]
    public void ReadRowsFailure_Propagates()
    {
        // ThrowingReader throws on ReadRows → the exception propagates unchanged out of Run.
        var reader = new ThrowingReader();
        var profile = new ValidationPipelineProfile<PipeTestQuestion>(
            SheetName: "Sheet1",
            Layout: Layout,
            RecordFactory: RecordFactory,
            DvRoles: [],
            BaselineDescriptors: [BaselineDescriptor],
            RunBaseline: SilentBaseline,
            Extensions: []);

        var act = () => Run(reader, profile);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Simulated reader failure*");
    }
}
