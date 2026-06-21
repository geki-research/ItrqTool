using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Checks;

// Coverage for WithinYearStructureCheck<T>: the within-year structure extension reproducing
// CLQ Phase 2 (removed) + Phase 3 AddedInResponse. Two descriptors
// (structure.question-removed, structure.question-added; both Error, Structure). FILTER-FREE:
// it never consults alignment.MalformedKeys (the gate handles malformed keys upstream), and it
// excludes row-shift (JoinedByXrefId — that is finding 3b). Sheet-agnostic: uses a minimal
// local IAlignmentIdentity record; no dependency on the Clq/Rlq harnesses.
public sealed class WithinYearStructureCheckTests
{
    // ── Local test fixtures ──────────────────────────────────────────────────────

    private sealed record CheckTestQuestion(
        int RowNumber,
        string? XrefId = "X1",
        string OriginalText = "orig",
        string QuestionText = "What?",
        string SectionName = "Section",
        string? QuestionNumber = "1",
        string? ProvidedBy = "TestOU") : IAlignmentIdentity;

    private const string Column = "Q";

    private static WithinYearStructureCheck<CheckTestQuestion> Primitive(
        FindingEvaluation removedDefault = FindingEvaluation.Error,
        FindingEvaluation addedDefault   = FindingEvaluation.Error) =>
        new(q => q.ProvidedBy, Column, removedDefault, addedDefault);

    private static AlignmentResult<CheckTestQuestion> Result(
        IReadOnlyList<AlignedQuestion<CheckTestQuestion>>? aligned = null,
        IReadOnlyList<CheckTestQuestion>? removed = null,
        IReadOnlyList<MalformedKey>? malformed = null) =>
        new(
            aligned   ?? Array.Empty<AlignedQuestion<CheckTestQuestion>>(),
            removed   ?? Array.Empty<CheckTestQuestion>(),
            malformed ?? Array.Empty<MalformedKey>());

    private static AlignedQuestion<CheckTestQuestion> Aligned(
        CheckTestQuestion current,
        WithinYearJoin withinYear,
        CheckTestQuestion? templateMatch = null,
        bool rowShifted = false) =>
        new(
            Current: current,
            WithinYear: withinYear,
            TemplateMatch: templateMatch,
            RowShifted: rowShifted,
            TextMismatched: false,
            CrossYear: CrossYearOutcome.Neither,
            PreviousMatch: null,
            XrefIdCounterpart: null,
            MatcherCandidate: null,
            MatcherBaseScore: null);

    private static FindingEmitter Emitter(
        WithinYearStructureCheck<CheckTestQuestion> primitive,
        IReadOnlyDictionary<string, FindingEvaluation>? overrides = null) =>
        new(
            overrides ?? new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(primitive.Descriptors));

    // ── Tests ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Descriptors_ExactlyTwo_RemovedThenAdded_ErrorStructure()
    {
        var primitive = Primitive();

        primitive.Descriptors.Should().HaveCount(2);
        primitive.Descriptors.Select(d => d.Id).Should()
            .Equal("structure.question-removed", "structure.question-added");
        primitive.Descriptors.Should().AllSatisfy(d =>
        {
            d.DefaultEvaluation.Should().Be(FindingEvaluation.Error);
            d.Check.Should().Be(ValidationCheck.Structure);
            d.Description.Should().NotBeNullOrWhiteSpace();
        });
    }

    [Fact]
    public void OneRemoved_EmitsOneRemovedFinding()
    {
        var primitive = Primitive();
        var removed = new CheckTestQuestion(RowNumber: 13, XrefId: "x4");
        var findings = primitive.Run(Result(removed: new[] { removed }), Emitter(primitive));

        var f = findings.Should().ContainSingle().Subject;
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Error);
        f.CellAddresses.Should().Be("Q13");
        f.ProvidedBy.Should().BeNull("removed findings are template-side: no responder");
        f.CheckResult.Should().Contain("absent from the response").And.Contain("x4");
    }

    [Fact]
    public void OneAdded_EmitsOneAddedFinding_WithProvidedBy()
    {
        var primitive = Primitive();
        var current = new CheckTestQuestion(RowNumber: 13, XrefId: "x5", ProvidedBy: "UnitA");
        var aligned = new[] { Aligned(current, WithinYearJoin.AddedInResponse) };
        var findings = primitive.Run(Result(aligned: aligned), Emitter(primitive));

        var f = findings.Should().ContainSingle().Subject;
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Error);
        f.CellAddresses.Should().Be("Q13");
        f.ProvidedBy.Should().Be("UnitA");
        f.CheckResult.Should().Contain("absent from the empty template").And.Contain("x5");
    }

    [Fact]
    public void JoinedByXrefId_IncludingRowShifted_ProducesNoFinding()
    {
        // Row-shift is finding 3b — JoinedByXrefId rows (shifted or not) yield nothing here.
        var primitive = Primitive();
        var clean   = new CheckTestQuestion(RowNumber: 6,  XrefId: "x1");
        var shifted = new CheckTestQuestion(RowNumber: 14, XrefId: "x4");
        var tmpl    = new CheckTestQuestion(RowNumber: 13, XrefId: "x4");
        var aligned = new[]
        {
            Aligned(clean,   WithinYearJoin.JoinedByXrefId, templateMatch: clean,   rowShifted: false),
            Aligned(shifted, WithinYearJoin.JoinedByXrefId, templateMatch: tmpl,    rowShifted: true),
        };

        primitive.Run(Result(aligned: aligned), Emitter(primitive)).Should().BeEmpty();
    }

    [Fact]
    public void NotEvaluatedMalformedKey_ProducesNoFinding()
    {
        var primitive = Primitive();
        var current = new CheckTestQuestion(RowNumber: 7, XrefId: null);
        var aligned = new[] { Aligned(current, WithinYearJoin.NotEvaluatedMalformedKey) };

        primitive.Run(Result(aligned: aligned), Emitter(primitive)).Should().BeEmpty();
    }

    [Fact]
    public void EmptyAlignment_NoFindings()
    {
        var primitive = Primitive();
        primitive.Run(Result(), Emitter(primitive)).Should().BeEmpty();
    }

    [Fact]
    public void OrderPreserved_AllRemovedThenAllAdded_EachInInputOrder()
    {
        var primitive = Primitive();
        var removed = new[]
        {
            new CheckTestQuestion(RowNumber: 13, XrefId: "x4"),
            new CheckTestQuestion(RowNumber: 20, XrefId: "x9"),
        };
        var aligned = new[]
        {
            Aligned(new CheckTestQuestion(RowNumber: 6,  XrefId: "x5"), WithinYearJoin.AddedInResponse),
            Aligned(new CheckTestQuestion(RowNumber: 30, XrefId: "x6"), WithinYearJoin.AddedInResponse),
        };

        var findings = primitive.Run(Result(aligned: aligned, removed: removed), Emitter(primitive));

        findings.Should().HaveCount(4);
        findings.Select(f => f.CellAddresses).Should().Equal("Q13", "Q20", "Q6", "Q30");
    }

    [Fact]
    public void NoFilter_RemovedEmittedDespiteCoincidingMalformedKeysRow()
    {
        // NO-FILTER pin: the check ignores alignment.MalformedKeys entirely. A removed entry
        // whose RowNumber coincides with a malformed-key row is STILL emitted — proving the
        // parked 3a row-based suppression filter is NOT reintroduced (the gate, not this
        // check, handles malformed keys upstream).
        var primitive = Primitive();
        var removed = new[] { new CheckTestQuestion(RowNumber: 13, XrefId: "x4") };
        var malformed = new[]
        {
            new MalformedKey(ValidationWorkbook.EmptyTemplate, 13, null, MalformedKeyReason.Blank),
        };

        var findings = primitive.Run(Result(removed: removed, malformed: malformed), Emitter(primitive));

        findings.Should().ContainSingle().Which.CellAddresses.Should().Be("Q13");
    }

    [Fact]
    public void CtorEvaluationDefaults_FlowIntoEmittedFindings()
    {
        var primitive = Primitive(
            removedDefault: FindingEvaluation.Warning,
            addedDefault:   FindingEvaluation.Information);
        var removed = new[] { new CheckTestQuestion(RowNumber: 13, XrefId: "x4") };
        var aligned = new[]
        {
            Aligned(new CheckTestQuestion(RowNumber: 6, XrefId: "x5"), WithinYearJoin.AddedInResponse),
        };

        var findings = primitive.Run(Result(aligned: aligned, removed: removed), Emitter(primitive));

        findings.Should().HaveCount(2);
        findings.Single(f => f.CheckResult.Contains("absent from the response"))
            .Evaluation.Should().Be(FindingEvaluation.Warning);
        findings.Single(f => f.CheckResult.Contains("absent from the empty template"))
            .Evaluation.Should().Be(FindingEvaluation.Information);
    }

    [Fact]
    public void Guards_ThrowOnNullSelectorOrEmptyColumn()
    {
        var nullSelector = () => new WithinYearStructureCheck<CheckTestQuestion>(null!, Column);
        var emptyColumn  = () => new WithinYearStructureCheck<CheckTestQuestion>(q => q.ProvidedBy, "");
        var blankColumn  = () => new WithinYearStructureCheck<CheckTestQuestion>(q => q.ProvidedBy, "   ");

        nullSelector.Should().Throw<ArgumentNullException>();
        emptyColumn.Should().Throw<ArgumentException>();
        blankColumn.Should().Throw<ArgumentException>();
    }
}
