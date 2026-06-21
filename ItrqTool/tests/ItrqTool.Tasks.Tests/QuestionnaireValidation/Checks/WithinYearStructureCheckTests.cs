using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Checks;

// Coverage for WithinYearStructureCheck<T>: the within-year structure extension reproducing
// CLQ Phase 2 (removed) + Phase 3 AddedInResponse + order-based row-shift. Three descriptors
// (structure.question-removed, structure.question-added, structure.question-row-shifted; all
// Error, Structure). FILTER-FREE: it never consults alignment.MalformedKeys (the gate handles
// malformed keys upstream). Row-shift is order-based (template-rank sequence vs current order),
// not absolute-row. Sheet-agnostic: uses a minimal local IAlignmentIdentity record; no
// dependency on the Clq/Rlq harnesses.
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
        FindingEvaluation removedDefault   = FindingEvaluation.Error,
        FindingEvaluation addedDefault     = FindingEvaluation.Error,
        FindingEvaluation rowShiftDefault  = FindingEvaluation.Error) =>
        new(q => q.ProvidedBy, Column, removedDefault, addedDefault, rowShiftDefault);

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

    // ── Row-shift fixtures ─────────────────────────────────────────────────────────
    // Build matched (JoinedByXrefId) questions in CURRENT order from a template-rank sequence.
    // XrefId encodes the rank ("x{rank}"); template RowNumber = rank*10 so ascending template
    // row == ascending rank; current RowNumber = 100+position (distinct). The flagged set is
    // asserted by XrefId, in emission (current) order.
    private static AlignedQuestion<CheckTestQuestion>[] MatchedByRanks(params int[] ranksInCurrentOrder) =>
        ranksInCurrentOrder
            .Select((rank, pos) => Aligned(
                new CheckTestQuestion(RowNumber: 100 + pos, XrefId: $"x{rank}"),
                WithinYearJoin.JoinedByXrefId,
                templateMatch: new CheckTestQuestion(RowNumber: rank * 10, XrefId: $"x{rank}")))
            .ToArray();

    private static IReadOnlyList<ValidationFinding> RowShifts(IReadOnlyList<ValidationFinding> findings) =>
        findings
            .Where(f => f.CheckResult.StartsWith("Question (identity key '", StringComparison.Ordinal))
            .ToList();

    private static string FlaggedXref(ValidationFinding f)
    {
        const string marker = "identity key '";
        int start = f.CheckResult.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        int end = f.CheckResult.IndexOf('\'', start);
        return f.CheckResult.Substring(start, end - start);
    }

    // ── Tests ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Descriptors_ExactlyThree_RemovedAddedRowShifted_ErrorStructure()
    {
        var primitive = Primitive();

        primitive.Descriptors.Should().HaveCount(3);
        primitive.Descriptors.Select(d => d.Id).Should()
            .Equal("structure.question-removed", "structure.question-added", "structure.question-row-shifted");
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
    public void JoinedByXrefId_AbsoluteRowShiftButSequenceOrderPreserved_ProducesNoFinding()
    {
        // ORDER-BASED proof: both matched questions sit on different absolute rows than the
        // template (RowShifted bool is true on the second), yet their CURRENT order matches
        // their TEMPLATE-RANK order (template rows 6 < 13 ⇒ ranks 1,2 in current order), so the
        // sequence is increasing and NOTHING is flagged. The absolute RowShifted bool is ignored.
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

    // ── Order-based row-shift ──────────────────────────────────────────────────────

    // The four canonical cases ARE the spec. Each drives the check with matched questions whose
    // template ranks, read in current order, form the given sequence; the rank-minimal LIS is
    // kept and its complement is flagged (in current order). Since every question is matched,
    // there are no removed/added findings — the entire result is row-shifts.
    [Theory]
    [InlineData(new[] { 1, 2, 3, 4 }, new string[0])]                 // in order → nothing
    [InlineData(new[] { 4, 1, 2, 3 }, new[] { "x4" })]               // x4 jumped to front
    [InlineData(new[] { 1, 3, 2, 4 }, new[] { "x3" })]               // tie-break: keep 1,2,4 → flag x3
    [InlineData(new[] { 4, 3, 2, 1 }, new[] { "x4", "x3", "x2" })]   // reversal: keep rank 1 only
    public void RowShift_CanonicalCases_FlagsTheRankMinimalComplement(
        int[] ranksInCurrentOrder, string[] expectedFlaggedInOrder)
    {
        var primitive = Primitive();
        var findings = primitive.Run(Result(aligned: MatchedByRanks(ranksInCurrentOrder)), Emitter(primitive));

        var shifts = RowShifts(findings);
        findings.Should().HaveCount(shifts.Count, "all questions are matched — only row-shift findings can fire");
        shifts.Select(FlaggedXref).Should().Equal(expectedFlaggedInOrder);
    }

    [Fact]
    public void RowShift_FewerThanTwoMatched_NeverFlags()
    {
        var primitive = Primitive();
        primitive.Run(Result(aligned: MatchedByRanks()), Emitter(primitive)).Should().BeEmpty();          // 0 matched
        RowShifts(primitive.Run(Result(aligned: MatchedByRanks(1)), Emitter(primitive))).Should().BeEmpty(); // 1 matched
    }

    [Fact]
    public void RowShift_ExcludesAddedRemovedAndMalformed_FromScope()
    {
        // The matched-only scope means added / malformed / removed questions can never be
        // flagged as row-shifted (but they DO still fire their own removed/added findings).
        var primitive = Primitive();
        var aligned = new[]
        {
            Aligned(new CheckTestQuestion(RowNumber: 100, XrefId: "x1"), WithinYearJoin.JoinedByXrefId,
                templateMatch: new CheckTestQuestion(RowNumber: 10, XrefId: "x1")),
            Aligned(new CheckTestQuestion(RowNumber: 101, XrefId: "add"), WithinYearJoin.AddedInResponse),
            Aligned(new CheckTestQuestion(RowNumber: 102, XrefId: null),  WithinYearJoin.NotEvaluatedMalformedKey),
            Aligned(new CheckTestQuestion(RowNumber: 103, XrefId: "x2"), WithinYearJoin.JoinedByXrefId,
                templateMatch: new CheckTestQuestion(RowNumber: 20, XrefId: "x2")),
        };
        var removed = new[] { new CheckTestQuestion(RowNumber: 30, XrefId: "x9") };

        var findings = primitive.Run(Result(aligned: aligned, removed: removed), Emitter(primitive));

        RowShifts(findings).Should().BeEmpty(
            "the two matched questions are in template order; added/malformed/removed are outside the matched-only scope");
        findings.Should().HaveCount(2, "the one added (add) and one removed (x9) findings still fire");
    }

    [Fact]
    public void RowShift_NeighbourText_NamesExpectedAndActualPredecessors()
    {
        var primitive = Primitive();

        // 1,3,2,4 → x3 flagged; expected predecessor x2 (rank 2), actual predecessor x1 (prior in current order).
        var shift132 = RowShifts(primitive.Run(Result(aligned: MatchedByRanks(1, 3, 2, 4)), Emitter(primitive)))
            .Should().ContainSingle().Subject;
        shift132.CheckResult.Should()
            .Contain("identity key 'x3'")
            .And.Contain("expected to follow 'x2'")
            .And.Contain("found following 'x1'");

        // 4,1,2,3 → x4 flagged at the FRONT of current order: expected predecessor x3 (rank 3),
        // actual predecessor is the start of the sequence (no current-order predecessor).
        var shift412 = RowShifts(primitive.Run(Result(aligned: MatchedByRanks(4, 1, 2, 3)), Emitter(primitive)))
            .Should().ContainSingle().Subject;
        shift412.CheckResult.Should()
            .Contain("identity key 'x4'")
            .And.Contain("expected to follow 'x3'")
            .And.Contain("found following the start of the sequence");
    }

    [Fact]
    public void RowShift_Finding_HasErrorStructureAddressAndProvidedBy()
    {
        var primitive = Primitive();
        var flagged = new CheckTestQuestion(RowNumber: 77, XrefId: "x3", ProvidedBy: "UnitZ");
        var aligned = new[]
        {
            Aligned(new CheckTestQuestion(RowNumber: 60, XrefId: "x1"), WithinYearJoin.JoinedByXrefId,
                templateMatch: new CheckTestQuestion(RowNumber: 10, XrefId: "x1")),
            Aligned(flagged, WithinYearJoin.JoinedByXrefId,
                templateMatch: new CheckTestQuestion(RowNumber: 30, XrefId: "x3")),
            Aligned(new CheckTestQuestion(RowNumber: 78, XrefId: "x2"), WithinYearJoin.JoinedByXrefId,
                templateMatch: new CheckTestQuestion(RowNumber: 20, XrefId: "x2")),
            Aligned(new CheckTestQuestion(RowNumber: 79, XrefId: "x4"), WithinYearJoin.JoinedByXrefId,
                templateMatch: new CheckTestQuestion(RowNumber: 40, XrefId: "x4")),
        };
        // Current-order ranks: x1(10)=1, x3(30)=3, x2(20)=2, x4(40)=4 → [1,3,2,4] → flag x3 @ row 77.

        var f = RowShifts(primitive.Run(Result(aligned: aligned), Emitter(primitive)))
            .Should().ContainSingle().Subject;
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Error);
        f.CellAddresses.Should().Be("Q77");
        f.ProvidedBy.Should().Be("UnitZ");
        f.QuestionText.Should().Be(flagged.QuestionText);
    }

    [Fact]
    public void RowShift_CtorDefault_FlowsIntoEmittedFinding()
    {
        var primitive = Primitive(rowShiftDefault: FindingEvaluation.Warning);

        // 2,1 → x2 flagged (keep rank 1).
        var f = RowShifts(primitive.Run(Result(aligned: MatchedByRanks(2, 1)), Emitter(primitive)))
            .Should().ContainSingle().Subject;
        f.Evaluation.Should().Be(FindingEvaluation.Warning);
        FlaggedXref(f).Should().Be("x2");
    }
}
