using FluentAssertions;
using ItrqTool.Domain;
using ItrqTool.Tasks.ControlLevelQuestionInject;
using ItrqTool.Tasks.ControlLevelQuestionValidationV01;
using ItrqTool.Tasks.ControlLevelQuestionValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using Xunit;

namespace ItrqTool.Tasks.Tests.ControlLevelQuestionInject;

/// <summary>
/// Pure unit tests for <see cref="ClqInjectMapper"/> — no Excel, no mocks. Each test
/// constructs the question records and a <see cref="CrossFormatMatch{TCur,TPrev}"/>
/// directly and asserts the emitted (cells, messages).
/// </summary>
public sealed class ClqInjectMapperTests
{
    // ── builders ──────────────────────────────────────────────────────────────

    // Standard production-shaped v01 column map: F/G/H/I/J/M.
    private static ClqV01Config StandardConfig() => new()
    {
        TextColumn = "C",
        GuidanceColumn = "D",
        PreviousAnswerColumn = "F",
        PreviousExplanationColumn = "G",
        AnswerColumn = "H",
        StrengthsColumn = "I",
        WeaknessesColumn = "J",
        ProvidedByColumn = "M",
        XrefIdColumn = "N",
    };

    private static ClqInjectConfig Inject(bool carryForward, string trigger = "No") => new()
    {
        CurrentConfigFilename = "current.json",
        PreviousConfigFilename = "previous.json",
        CarryForwardEnabled = carryForward,
        StabilityTriggerToken = trigger,
        ExplanationMergeSeparator = "{nl}{nl}{nl}",
        ExplanationStrengthsPrefix = "Strengths:{nl}",
        ExplanationWeaknessesPrefix = "Weaknesses:{nl}",
    };

    private static ClqV01Question Current(int row = 10, string? xref = "CLQ-1.1") => new(
        RowNumber: row,
        XrefId: xref,
        QuestionNumber: "1.1",
        QuestionText: "How is access controlled?",
        OriginalText: "1.1 How is access controlled?",
        ChapterName: "Access",
        SectionName: "Identity",
        Guidance: null,
        PreviousAnswer: null,
        Answer: null,
        Strengths: null,
        Weaknesses: null,
        ProvidedBy: null,
        AnswerDvType: null,
        AnswerDvFormula: null,
        AnswerDvOperator: null,
        AnswerDvFormula2: null,
        NumberFormatUnrecognized: false);

    private static ClqV02Question Previous(
        string? answer = "3",
        string? strengths = "Robust SSO and MFA enforced.",
        string? weaknesses = "No periodic access review.",
        string? providedBy = "Alice Auditee",
        string? stability = "No") => new(
        RowNumber: 99,
        XrefId: "CLQ-1.1",
        QuestionNumber: "1.1",
        QuestionText: "How is access controlled?",
        OriginalText: "1.1 How is access controlled?",
        ChapterName: "Access",
        SectionName: "Identity",
        Guidance: null,
        PreviousAnswer: null,
        Answer: answer,
        Strengths: strengths,
        Weaknesses: weaknesses,
        ProvidedBy: providedBy,
        AnswerDvType: null,
        AnswerDvFormula: null,
        AnswerDvOperator: null,
        AnswerDvFormula2: null,
        NumberFormatUnrecognized: false,
        AnswerStability: stability,
        AnswerStabilityDvType: null,
        AnswerStabilityDvFormula: null,
        AnswerStabilityDvOperator: null,
        AnswerStabilityDvFormula2: null);

    private static CrossFormatMatch<ClqV01Question, ClqV02Question> AgreeMatch(
        ClqV01Question c, ClqV02Question p)
        => new(c, CrossYearOutcome.Agree, p, p, p, 0.95);

    private static CrossFormatMatch<ClqV01Question, ClqV02Question> AmbiguousMatch(
        ClqV01Question c, CrossYearOutcome outcome)
        => new(c, outcome, null, null, null, null);

    private static (IReadOnlyList<CellWriteEntry> cells, IReadOnlyList<TaskMessage> messages) MapOne(
        CrossFormatMatch<ClqV01Question, ClqV02Question> match,
        ClqInjectConfig inject,
        ClqV01Config? config = null)
    {
        var result = new CrossFormatAlignmentResult<ClqV01Question, ClqV02Question>([match], []);
        return ClqInjectMapper.Map(result, inject, config ?? StandardConfig());
    }

    private static string? Cell(IReadOnlyList<CellWriteEntry> cells, int row, string column)
        => cells.SingleOrDefault(e => e.Row == row && e.Column == column)?.Value;

    // ── 1. Agree, carry-forward DISABLED → F, G, M; NOT H/I/J ─────────────────

    [Fact]
    public void Agree_CarryForwardDisabled_WritesReferenceColumnsOnly()
    {
        var c = Current();
        var p = Previous();

        var (cells, messages) = MapOne(AgreeMatch(c, p), Inject(carryForward: false));

        Cell(cells, 10, "F").Should().Be("3");
        Cell(cells, 10, "G").Should().Be("Strengths:\nRobust SSO and MFA enforced.\n\n\nWeaknesses:\nNo periodic access review.");
        Cell(cells, 10, "M").Should().Be("Alice Auditee");

        cells.Should().NotContain(e => e.Column == "H");
        cells.Should().NotContain(e => e.Column == "I");
        cells.Should().NotContain(e => e.Column == "J");
        messages.Should().BeEmpty();
    }

    // ── 2. Agree, carry-forward ENABLED, stability == trigger → F/G/M + H/I/J ─

    [Fact]
    public void Agree_CarryForwardEnabled_StabilityTriggers_WritesAllColumns()
    {
        var c = Current();
        var p = Previous(stability: "No");

        var (cells, messages) = MapOne(AgreeMatch(c, p), Inject(carryForward: true, trigger: "No"));

        Cell(cells, 10, "F").Should().Be("3");
        Cell(cells, 10, "G").Should().NotBeNullOrEmpty();
        Cell(cells, 10, "M").Should().Be("Alice Auditee");
        Cell(cells, 10, "H").Should().Be("3");
        Cell(cells, 10, "I").Should().Be("Robust SSO and MFA enforced.");
        Cell(cells, 10, "J").Should().Be("No periodic access review.");
        messages.Should().BeEmpty();
    }

    // ── 3. Agree, carry-forward ENABLED, stability != trigger → F/G/M only ────

    [Fact]
    public void Agree_CarryForwardEnabled_StabilityDoesNotTrigger_WritesReferenceColumnsOnly()
    {
        var c = Current();
        var p = Previous(stability: "Yes");

        var (cells, _) = MapOne(AgreeMatch(c, p), Inject(carryForward: true, trigger: "No"));

        Cell(cells, 10, "F").Should().Be("3");
        Cell(cells, 10, "M").Should().Be("Alice Auditee");
        cells.Should().NotContain(e => e.Column == "H");
        cells.Should().NotContain(e => e.Column == "I");
        cells.Should().NotContain(e => e.Column == "J");
    }

    // ── 4. Agree, carry-forward ENABLED, stability null → F/G/M only ──────────

    [Fact]
    public void Agree_CarryForwardEnabled_StabilityNull_WritesReferenceColumnsOnly()
    {
        var c = Current();
        var p = Previous(stability: null);

        var (cells, _) = MapOne(AgreeMatch(c, p), Inject(carryForward: true, trigger: "No"));

        Cell(cells, 10, "F").Should().Be("3");
        cells.Should().NotContain(e => e.Column == "H");
        cells.Should().NotContain(e => e.Column == "I");
        cells.Should().NotContain(e => e.Column == "J");
    }

    // ── 5. Merge rendering (exact strings) ────────────────────────────────────

    [Fact]
    public void Merge_BothPresent_RendersBothSegmentsWithTripleNewlineSeparator()
    {
        var p = Previous(strengths: "S", weaknesses: "W");

        var (cells, _) = MapOne(AgreeMatch(Current(), p), Inject(carryForward: false));

        Cell(cells, 10, "G").Should().Be("Strengths:\nS\n\n\nWeaknesses:\nW");
    }

    [Fact]
    public void Merge_StrengthsOnly_RendersStrengthsSegment()
    {
        var p = Previous(strengths: "S", weaknesses: null);

        var (cells, _) = MapOne(AgreeMatch(Current(), p), Inject(carryForward: false));

        Cell(cells, 10, "G").Should().Be("Strengths:\nS");
    }

    [Fact]
    public void Merge_WeaknessesOnly_RendersWeaknessesSegment()
    {
        var p = Previous(strengths: null, weaknesses: "W");

        var (cells, _) = MapOne(AgreeMatch(Current(), p), Inject(carryForward: false));

        Cell(cells, 10, "G").Should().Be("Weaknesses:\nW");
    }

    [Fact]
    public void Merge_BothBlank_OmitsGCell()
    {
        var p = Previous(strengths: "   ", weaknesses: null);

        var (cells, _) = MapOne(AgreeMatch(Current(), p), Inject(carryForward: false));

        cells.Should().NotContain(e => e.Column == "G");
    }

    // ── 6. Blank source omission ──────────────────────────────────────────────

    [Fact]
    public void BlankSources_OmitTheirCells()
    {
        var c = Current();
        // answer blank → no F (and no H even though CF would carry it); providedBy blank → no M.
        var p = Previous(answer: "   ", providedBy: null, strengths: "S", weaknesses: "W", stability: "No");

        var (cells, _) = MapOne(AgreeMatch(c, p), Inject(carryForward: true, trigger: "No"));

        cells.Should().NotContain(e => e.Column == "F");
        cells.Should().NotContain(e => e.Column == "M");
        cells.Should().NotContain(e => e.Column == "H"); // carry-forward answer also blank
        Cell(cells, 10, "G").Should().Be("Strengths:\nS\n\n\nWeaknesses:\nW");
        Cell(cells, 10, "I").Should().Be("S");
        Cell(cells, 10, "J").Should().Be("W");
    }

    // ── 7. Ambiguous outcomes → no cells + exactly one Warning ────────────────

    [Theory]
    [InlineData(CrossYearOutcome.XrefIdConflict)]
    [InlineData(CrossYearOutcome.NewXrefIdWithLookalike)]
    [InlineData(CrossYearOutcome.SameXrefIdTextDiverged)]
    public void Ambiguous_EmitsNoCellsAndOneWarning(CrossYearOutcome outcome)
    {
        var c = Current(row: 42, xref: "CLQ-3.1");

        var (cells, messages) = MapOne(AmbiguousMatch(c, outcome), Inject(carryForward: true));

        cells.Should().BeEmpty();
        messages.Should().ContainSingle();
        var msg = messages[0];
        msg.Severity.Should().Be(MessageSeverity.Warning);
        msg.Text.Should().Contain("42");
        msg.Text.Should().Contain("CLQ-3.1");
        msg.Text.Should().Contain(outcome.ToString());
    }

    // ── 8. Genuine new / structural → no cells, no warning ────────────────────

    [Theory]
    [InlineData(CrossYearOutcome.Neither)]
    [InlineData(CrossYearOutcome.NotEvaluatedMalformedKey)]
    public void NewOrStructural_EmitsNothing(CrossYearOutcome outcome)
    {
        var (cells, messages) = MapOne(AmbiguousMatch(Current(), outcome), Inject(carryForward: true));

        cells.Should().BeEmpty();
        messages.Should().BeEmpty();
    }

    // ── 9. Cell coordinates use c.RowNumber + currentConfig column letters ────

    [Fact]
    public void CellCoordinates_ComeFromCurrentRowAndConfigColumns()
    {
        // Distinctive, non-production column letters prove pass-through (no hardcoding).
        var cfg = new ClqV01Config
        {
            PreviousAnswerColumn = "P",
            PreviousExplanationColumn = "Q",
            AnswerColumn = "R",
            StrengthsColumn = "S",
            WeaknessesColumn = "T",
            ProvidedByColumn = "U",
        };
        var c = Current(row: 77);
        var p = Previous(stability: "No");

        var (cells, _) = MapOne(AgreeMatch(c, p), Inject(carryForward: true, trigger: "No"), cfg);

        cells.Should().OnlyContain(e => e.Row == 77);
        Cell(cells, 77, "P").Should().Be("3");                       // previous answer
        Cell(cells, 77, "Q").Should().NotBeNullOrEmpty();            // explanation
        Cell(cells, 77, "U").Should().Be("Alice Auditee");           // provided-by
        Cell(cells, 77, "R").Should().Be("3");                       // carried answer
        Cell(cells, 77, "S").Should().Be("Robust SSO and MFA enforced."); // carried strengths
        Cell(cells, 77, "T").Should().Be("No periodic access review.");   // carried weaknesses
    }
}
