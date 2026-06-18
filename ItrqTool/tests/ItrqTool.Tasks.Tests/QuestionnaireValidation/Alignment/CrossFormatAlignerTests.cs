using FluentAssertions;
using ItrqTool.Tasks.ControlLevelQuestionValidationV01;
using ItrqTool.Tasks.ControlLevelQuestionValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Alignment;

/// <summary>
/// Cross-format aligner coverage. Section A aligns TWO DIFFERENT concrete types —
/// <see cref="ClqV01Question"/> (current) against <see cref="ClqV02Question"/>
/// (previous) — to prove the generic cross-format matcher reconciles across formats.
/// Section B is the drift-defense equivalence guard against AlignmentEngine.
/// </summary>
public sealed class CrossFormatAlignerTests
{
    // Distinct, mutually low-similarity texts so cross-year outcomes never depend
    // on Hungarian tie-breaking (lesson 73).
    private const string TextRisk    = "What is your overall risk appetite for the year?";
    private const string TextCredit  = "Describe the credit exposure limits in place.";
    private const string TextLiquid  = "Outline liquidity buffers held against stress.";
    private const string TextBanana  = "Banana cucumber zzz totally unrelated wording.";
    private const string TextOrphan  = "Quux frobnitz wibble plover xyzzy nonsense line.";

    // ── current builder: ClqV01Question carrying only the identity fields that matter ──
    private static ClqV01Question Cur(
        int rowNumber = 1,
        string? xrefId = null,
        string questionText = "",
        string? questionNumber = null,
        string sectionName = "Section")
        => new(
            RowNumber: rowNumber,
            XrefId: xrefId,
            QuestionNumber: questionNumber,
            QuestionText: questionText,
            OriginalText: questionText,
            ChapterName: "Chapter",
            SectionName: sectionName,
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

    // ── previous builder: ClqV02Question (a DIFFERENT concrete type) ──
    private static ClqV02Question Prev(
        int rowNumber = 1,
        string? xrefId = null,
        string questionText = "",
        string? questionNumber = null,
        string sectionName = "Section")
        => new(
            RowNumber: rowNumber,
            XrefId: xrefId,
            QuestionNumber: questionNumber,
            QuestionText: questionText,
            OriginalText: questionText,
            ChapterName: "Chapter",
            SectionName: sectionName,
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
            NumberFormatUnrecognized: false,
            AnswerStability: null,
            AnswerStabilityDvType: null,
            AnswerStabilityDvFormula: null,
            AnswerStabilityDvOperator: null,
            AnswerStabilityDvFormula2: null);

    private static readonly IReadOnlyList<ClqV02Question> NoPrev = [];

    // ════════════════════════════════════════════════════════════════════════════
    // Section A — one test per outcome, V01 current × V02 previous (cross-format)
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Agree_KeyAndTextOnSamePrevious_PreviousIsTheMatch()
    {
        var prev = Prev(xrefId: "X1", questionText: TextRisk);
        var current  = new[] { Cur(xrefId: "X1", questionText: TextRisk) };
        var previous = new[] { prev };

        var match = CrossFormatAligner.Align(current, previous).Matches.Should().ContainSingle().Subject;

        match.Outcome.Should().Be(CrossYearOutcome.Agree);
        match.Previous.Should().BeSameAs(prev);
        match.XrefIdCounterpart.Should().BeSameAs(prev);
        match.MatcherCandidate.Should().BeSameAs(prev);
        match.MatcherBaseScore.Should().BeApproximately(1.0, 1e-9); // BASE score
    }

    [Fact]
    public void XrefIdConflict_TextMatchesPrevA_ButKeyPointsToPrevB()
    {
        var prevA = Prev(rowNumber: 10, xrefId: "A", questionText: TextRisk);
        var prevB = Prev(rowNumber: 11, xrefId: "B", questionText: TextBanana);
        var previous = new[] { prevA, prevB };

        // current text confidently matches prevA, but its key points to prevB.
        var current = new[] { Cur(xrefId: "B", questionText: TextRisk) };

        var match = CrossFormatAligner.Align(current, previous).Matches.Single();

        match.Outcome.Should().Be(CrossYearOutcome.XrefIdConflict);
        match.Previous.Should().BeNull();
        match.MatcherCandidate.Should().BeSameAs(prevA);
        match.XrefIdCounterpart.Should().BeSameAs(prevB);
        match.MatcherCandidate.Should().NotBeSameAs(match.XrefIdCounterpart);
    }

    [Fact]
    public void NewXrefIdWithLookalike_NewKey_ButConfidentTextualTwinExists()
    {
        var prevA = Prev(xrefId: "A", questionText: TextRisk);
        var previous = new[] { prevA };

        // NEW key absent from previous, but textually identical to prevA.
        var current = new[] { Cur(xrefId: "NEW", questionText: TextRisk) };

        var match = CrossFormatAligner.Align(current, previous).Matches.Single();

        match.Outcome.Should().Be(CrossYearOutcome.NewXrefIdWithLookalike);
        match.Previous.Should().BeNull();
        match.XrefIdCounterpart.Should().BeNull();
        match.MatcherCandidate.Should().BeSameAs(prevA);
        match.MatcherBaseScore.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void SameXrefIdTextDiverged_SharedKey_ButTextBelowThreshold()
    {
        var prevA = Prev(xrefId: "A", questionText: TextCredit);
        var previous = new[] { prevA };

        // Shares the key "A" but text is too dissimilar to match.
        var current = new[] { Cur(xrefId: "A", questionText: TextBanana) };

        var match = CrossFormatAligner.Align(current, previous).Matches.Single();

        match.Outcome.Should().Be(CrossYearOutcome.SameXrefIdTextDiverged);
        match.Previous.Should().BeNull(); // NOT auto-used as an injection source
        match.XrefIdCounterpart.Should().BeSameAs(prevA);
    }

    [Fact]
    public void Neither_NoKeyCounterpart_NoTextMatch()
    {
        var prevA = Prev(xrefId: "A", questionText: TextRisk);
        var previous = new[] { prevA };

        // current1 claims prevA (Agree); current2 is an orphan with a new key and
        // unrelated text — prevA is already taken, so current2 has no matcher pick.
        var current = new[]
        {
            Cur(rowNumber: 1, xrefId: "A",   questionText: TextRisk),
            Cur(rowNumber: 2, xrefId: "NEW", questionText: TextOrphan),
        };

        var matches = CrossFormatAligner.Align(current, previous).Matches;

        var orphan = matches.Single(x => x.Current.RowNumber == 2);
        orphan.Outcome.Should().Be(CrossYearOutcome.Neither);
        orphan.Previous.Should().BeNull();
        orphan.XrefIdCounterpart.Should().BeNull();
        orphan.MatcherCandidate.Should().BeNull();
    }

    [Fact]
    public void NotEvaluatedMalformedKey_BlankCurrentKey_PlusMalformedEntry()
    {
        var current = new[] { Cur(rowNumber: 7, xrefId: null, questionText: TextRisk) };

        var result = CrossFormatAligner.Align(current, NoPrev);

        var match = result.Matches.Single();
        match.Outcome.Should().Be(CrossYearOutcome.NotEvaluatedMalformedKey);
        match.Previous.Should().BeNull();
        match.XrefIdCounterpart.Should().BeNull();
        match.MatcherCandidate.Should().BeNull();
        match.MatcherBaseScore.Should().BeNull();

        result.MalformedKeys.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(
                new MalformedKey(ValidationWorkbook.CurrentResponse, 7, null, MalformedKeyReason.Blank));
    }

    [Fact]
    public void NotEvaluatedMalformedKey_DuplicateCurrentKey_BothRowsMalformed()
    {
        var current = new[]
        {
            Cur(rowNumber: 3, xrefId: "DUP", questionText: TextRisk),
            Cur(rowNumber: 8, xrefId: "DUP", questionText: TextCredit),
        };

        var result = CrossFormatAligner.Align(current, NoPrev);

        result.Matches.Should().OnlyContain(m => m.Outcome == CrossYearOutcome.NotEvaluatedMalformedKey);

        result.MalformedKeys.Should().BeEquivalentTo(new[]
        {
            new MalformedKey(ValidationWorkbook.CurrentResponse, 3, "DUP", MalformedKeyReason.Duplicate),
            new MalformedKey(ValidationWorkbook.CurrentResponse, 8, "DUP", MalformedKeyReason.Duplicate),
        });
    }

    [Fact]
    public void MalformedKeys_TaggedWithCorrectWorkbook_CurrentAndPrevious()
    {
        var current  = new[] { Cur(rowNumber: 1, xrefId: null) };
        var previous = new[] { Prev(rowNumber: 3, xrefId: null) };

        var result = CrossFormatAligner.Align(current, previous);

        result.MalformedKeys.Should().BeEquivalentTo(new[]
        {
            new MalformedKey(ValidationWorkbook.CurrentResponse, 1, null, MalformedKeyReason.Blank),
            new MalformedKey(ValidationWorkbook.PreviousResponse, 3, null, MalformedKeyReason.Blank),
        });
    }

    [Fact]
    public void MatcherBaseScore_IsBase_NotBonusAdjusted_AcrossFormats()
    {
        // Base text similarity is 0.4 (6 of 10 chars differ). Section + number
        // bonuses (+0.10 each) push the ADJUSTED score to 0.6, over the 0.5
        // threshold — but the reported MatcherBaseScore must be the BASE 0.4.
        var previous = new[]
        {
            Prev(xrefId: "X1", questionNumber: "1.2", sectionName: "Sec A", questionText: "abcdefghij"),
        };
        var current = new[]
        {
            Cur(xrefId: "X1", questionNumber: "1.2", sectionName: "Sec A", questionText: "abcdxyzqwv"),
        };

        var match = CrossFormatAligner.Align(current, previous).Matches.Single();

        // Bonus pushed it over threshold → confident match → Agree (key also matches).
        match.Outcome.Should().Be(CrossYearOutcome.Agree);
        match.MatcherBaseScore.Should().BeApproximately(0.4, 1e-9);
        match.MatcherBaseScore.Should().BeLessThan(0.5); // base alone would NOT have matched
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Section B — equivalence guard: SAME-type run through both engines must agree
    //   on the cross-year outcome sequence and the Agree previous-matches.
    // ════════════════════════════════════════════════════════════════════════════

    // A minimal same-type question for the guard — reference record carrying the six
    // IAlignmentIdentity fields, so AlignmentEngine.Align<T> and CrossFormatAligner
    // .Align<T, T> see identical inputs.
    private sealed record GuardQ(
        int RowNumber,
        string? XrefId,
        string OriginalText,
        string QuestionText,
        string SectionName,
        string? QuestionNumber) : IAlignmentIdentity;

    private static GuardQ G(int rowNumber, string? xrefId, string questionText, string sectionName = "Section")
        => new(rowNumber, xrefId, questionText, questionText, sectionName, null);

    [Fact]
    public void EquivalenceGuard_CrossYearOutcomesAndAgreeMatches_IdenticalToAlignmentEngine()
    {
        var prevRisk   = G(10, "R", TextRisk);
        var prevCredit = G(11, "C", TextCredit);
        var prevLiquid = G(12, "L", TextLiquid);
        var previous   = new[] { prevRisk, prevCredit, prevLiquid };

        var current = new[]
        {
            G(20, "R",   TextRisk),     // → Agree on prevRisk
            G(21, "C",   TextBanana),   // → SameXrefIdTextDiverged (shares C, text diverged)
            G(22, "NEW", TextOrphan),   // → Neither (new key, no confident twin)
            G(23, null,  TextLiquid),   // → NotEvaluatedMalformedKey (blank key)
        };

        // The template arm of Align<T> does not affect cross-year outcomes — pass an
        // arbitrary (empty) template; the guard compares only the cross-year fields.
        IReadOnlyList<GuardQ> emptyTemplate = [];

        var engine = AlignmentEngine.Align(current, emptyTemplate, previous).Aligned;
        var cross  = CrossFormatAligner.Align(current, previous).Matches;

        // Same count, same input order.
        cross.Should().HaveCount(engine.Count);

        // Cross-year outcome sequence identical.
        cross.Select(c => c.Outcome)
             .Should().Equal(engine.Select(e => e.CrossYear));

        // Agree previous-matches identical (by object identity), per current question.
        for (int i = 0; i < engine.Count; i++)
        {
            cross[i].Current.Should().BeSameAs(engine[i].Current);
            cross[i].Previous.Should().BeSameAs(engine[i].PreviousMatch);
        }

        // Sanity: the scenario actually exercises Agree (non-null match) on row 20.
        cross.Single(c => c.Current.RowNumber == 20).Previous.Should().BeSameAs(prevRisk);
        cross.Where(c => c.Previous is not null)
             .Should().OnlyContain(c => c.Outcome == CrossYearOutcome.Agree);
    }
}
