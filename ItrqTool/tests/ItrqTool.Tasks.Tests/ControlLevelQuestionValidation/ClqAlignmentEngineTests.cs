using FluentAssertions;
using ItrqTool.Tasks.ControlLevelQuestionValidation;
using Xunit;

namespace ItrqTool.Tasks.Tests.ControlLevelQuestionValidation;

public sealed class ClqAlignmentEngineTests
{
    // 18-arg positional record with no defaults — this helper supplies C#-level
    // defaults so each test overrides only the fields it cares about.
    // (Caller convenience: sectionName is exposed before chapterName; the new(...)
    // argument order below keeps the positional record order ChapterName, SectionName correct.)
    private static InternalClqQuestion Q(
        int rowNumber = 1,
        string? xrefId = null,
        string? questionNumber = null,
        string questionText = "",
        string? originalText = null,        // defaults to questionText when null
        string sectionName = "Section",
        string chapterName = "Chapter",
        string? guidance = null, string? previousAnswer = null, string? answer = null,
        string? strengths = null, string? weaknesses = null, string? providedBy = null,
        string? answerDvType = null, string? answerDvFormula = null, string? answerDvOperator = null,
        string? answerDvFormula2 = null, bool numberFormatUnrecognized = false)
        => new InternalClqQuestion(
            rowNumber, xrefId, questionNumber, questionText, originalText ?? questionText,
            chapterName, sectionName, guidance, previousAnswer, answer, strengths, weaknesses,
            providedBy, answerDvType, answerDvFormula, answerDvOperator, answerDvFormula2, numberFormatUnrecognized);

    private static readonly IReadOnlyList<InternalClqQuestion> None = [];

    // ── Within-year ───────────────────────────────────────────────────────────

    [Fact]
    public void WithinYear_CleanMatch_JoinedByXrefId_NoShift_NoMismatch()
    {
        var current  = new[] { Q(rowNumber: 10, xrefId: "X1", questionText: "Risk appetite?", originalText: "1. Risk appetite?") };
        var template = new[] { Q(rowNumber: 10, xrefId: "X1", questionText: "Risk appetite?", originalText: "1. Risk appetite?") };

        var result = ClqAlignmentEngine.Align(current, template, None);

        var a = result.Aligned.Should().ContainSingle().Subject;
        a.WithinYear.Should().Be(WithinYearJoin.JoinedByXrefId);
        a.TemplateMatch.Should().BeSameAs(template[0]);
        a.RowShifted.Should().BeFalse();
        a.TextMismatched.Should().BeFalse();
    }

    [Fact]
    public void WithinYear_RowShifted_WhenTemplateRowDiffers()
    {
        var current  = new[] { Q(rowNumber: 12, xrefId: "X1", originalText: "same text") };
        var template = new[] { Q(rowNumber: 10, xrefId: "X1", originalText: "same text") };

        var a = ClqAlignmentEngine.Align(current, template, None).Aligned.Single();

        a.WithinYear.Should().Be(WithinYearJoin.JoinedByXrefId);
        a.RowShifted.Should().BeTrue();
        a.TextMismatched.Should().BeFalse();
    }

    [Fact]
    public void WithinYear_TextMismatched_WhenOriginalTextDiffers()
    {
        var current  = new[] { Q(rowNumber: 10, xrefId: "X1", originalText: "current text") };
        var template = new[] { Q(rowNumber: 10, xrefId: "X1", originalText: "template text") };

        var a = ClqAlignmentEngine.Align(current, template, None).Aligned.Single();

        a.WithinYear.Should().Be(WithinYearJoin.JoinedByXrefId);
        a.RowShifted.Should().BeFalse();
        a.TextMismatched.Should().BeTrue();
    }

    [Fact]
    public void WithinYear_RowShifted_And_TextMismatched_BothTrue()
    {
        var current  = new[] { Q(rowNumber: 12, xrefId: "X1", originalText: "current text") };
        var template = new[] { Q(rowNumber: 10, xrefId: "X1", originalText: "template text") };

        var a = ClqAlignmentEngine.Align(current, template, None).Aligned.Single();

        a.WithinYear.Should().Be(WithinYearJoin.JoinedByXrefId);
        a.RowShifted.Should().BeTrue();
        a.TextMismatched.Should().BeTrue();
    }

    [Fact]
    public void WithinYear_AddedInResponse_WhenCurrentKeyAbsentFromTemplate()
    {
        var current  = new[] { Q(xrefId: "NEW") };
        var template = new[] { Q(xrefId: "OLD") };

        var a = ClqAlignmentEngine.Align(current, template, None).Aligned.Single();

        a.WithinYear.Should().Be(WithinYearJoin.AddedInResponse);
        a.TemplateMatch.Should().BeNull();
        a.RowShifted.Should().BeFalse();
        a.TextMismatched.Should().BeFalse();
    }

    [Fact]
    public void WithinYear_Removed_WhenTemplateValidKeyAbsentFromResponse()
    {
        var current  = new[] { Q(xrefId: "C1") };
        var template = new[] { Q(rowNumber: 5, xrefId: "T1") };

        var result = ClqAlignmentEngine.Align(current, template, None);

        result.WithinYearRemoved.Should().ContainSingle().Which.Should().BeSameAs(template[0]);
    }

    // ── Malformed keys ──────────────────────────────────────────────────────────

    [Fact]
    public void Malformed_BlankCurrentKey_NotEvaluatedBothAxes_PlusBlankEntry()
    {
        var current = new[] { Q(rowNumber: 7, xrefId: null, questionText: "anything") };

        var result = ClqAlignmentEngine.Align(current, None, None);

        var a = result.Aligned.Single();
        a.WithinYear.Should().Be(WithinYearJoin.NotEvaluatedMalformedKey);
        a.CrossYear.Should().Be(CrossYearOutcome.NotEvaluatedMalformedKey);
        a.TemplateMatch.Should().BeNull();
        a.PreviousMatch.Should().BeNull();
        a.XrefIdCounterpart.Should().BeNull();
        a.MatcherCandidate.Should().BeNull();
        a.MatcherBaseScore.Should().BeNull();

        result.MalformedKeys.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new MalformedKey(ClqWorkbook.CurrentResponse, 7, null, MalformedKeyReason.Blank));
    }

    [Fact]
    public void Malformed_DuplicateCurrentKey_BothRowsMalformed()
    {
        var current = new[]
        {
            Q(rowNumber: 3, xrefId: "DUP"),
            Q(rowNumber: 8, xrefId: "DUP"),
        };

        var result = ClqAlignmentEngine.Align(current, None, None);

        result.Aligned.Should().OnlyContain(a =>
            a.WithinYear == WithinYearJoin.NotEvaluatedMalformedKey &&
            a.CrossYear == CrossYearOutcome.NotEvaluatedMalformedKey);

        result.MalformedKeys.Should().BeEquivalentTo(new[]
        {
            new MalformedKey(ClqWorkbook.CurrentResponse, 3, "DUP", MalformedKeyReason.Duplicate),
            new MalformedKey(ClqWorkbook.CurrentResponse, 8, "DUP", MalformedKeyReason.Duplicate),
        });
    }

    [Fact]
    public void Malformed_TemplateKeyExcludedFromIndex_CurrentReadsAddedInResponse()
    {
        var current  = new[] { Q(xrefId: "DUP") };
        var template = new[]
        {
            Q(rowNumber: 4, xrefId: "DUP"),
            Q(rowNumber: 5, xrefId: "DUP"),
        };

        var result = ClqAlignmentEngine.Align(current, template, None);

        // Template's duplicated "DUP" is not a valid key, so the current row cannot join.
        result.Aligned.Single().WithinYear.Should().Be(WithinYearJoin.AddedInResponse);

        result.MalformedKeys.Should().BeEquivalentTo(new[]
        {
            new MalformedKey(ClqWorkbook.EmptyTemplate, 4, "DUP", MalformedKeyReason.Duplicate),
            new MalformedKey(ClqWorkbook.EmptyTemplate, 5, "DUP", MalformedKeyReason.Duplicate),
        });
    }

    [Fact]
    public void Malformed_PreviousKeyExcludedFromMatcherAndXrefIndex()
    {
        var current  = new[] { Q(xrefId: "P", questionText: "What is your risk appetite?") };
        var previous = new[]
        {
            Q(rowNumber: 4, xrefId: "P", questionText: "What is your risk appetite?"),
            Q(rowNumber: 5, xrefId: "P", questionText: "What is your risk appetite?"),
        };

        var result = ClqAlignmentEngine.Align(current, None, previous);

        // Both previous rows have the duplicated key "P" → excluded from both the
        // matcher inputs and the XrefId index, so the current row is an orphan.
        var a = result.Aligned.Single();
        a.CrossYear.Should().Be(CrossYearOutcome.Neither);
        a.MatcherCandidate.Should().BeNull();
        a.XrefIdCounterpart.Should().BeNull();

        result.MalformedKeys.Should().BeEquivalentTo(new[]
        {
            new MalformedKey(ClqWorkbook.PreviousResponse, 4, "P", MalformedKeyReason.Duplicate),
            new MalformedKey(ClqWorkbook.PreviousResponse, 5, "P", MalformedKeyReason.Duplicate),
        });
    }

    [Fact]
    public void Malformed_KeysTaggedWithCorrectWorkbook()
    {
        var current  = new[] { Q(rowNumber: 1, xrefId: null) };
        var template = new[] { Q(rowNumber: 2, xrefId: null) };
        var previous = new[] { Q(rowNumber: 3, xrefId: null) };

        var result = ClqAlignmentEngine.Align(current, template, previous);

        result.MalformedKeys.Should().BeEquivalentTo(new[]
        {
            new MalformedKey(ClqWorkbook.CurrentResponse, 1, null, MalformedKeyReason.Blank),
            new MalformedKey(ClqWorkbook.EmptyTemplate, 2, null, MalformedKeyReason.Blank),
            new MalformedKey(ClqWorkbook.PreviousResponse, 3, null, MalformedKeyReason.Blank),
        });
    }

    // ── Cross-year reconciliation — one test per row of the locked table ────────

    [Fact]
    public void CrossYear_Case1_Agree_KeyAndTextOnSamePrevious()
    {
        var current  = new[] { Q(xrefId: "X1", questionText: "What is your risk appetite?") };
        var previous = new[] { Q(xrefId: "X1", questionText: "What is your risk appetite?") };

        var a = ClqAlignmentEngine.Align(current, None, previous).Aligned.Single();

        a.CrossYear.Should().Be(CrossYearOutcome.Agree);
        a.PreviousMatch.Should().BeSameAs(previous[0]);
        a.XrefIdCounterpart.Should().BeSameAs(a.PreviousMatch);
        a.MatcherCandidate.Should().BeSameAs(previous[0]);
        a.MatcherBaseScore.Should().BeApproximately(1.0, 1e-9); // BASE score
    }

    [Fact]
    public void CrossYear_Case2_XrefIdConflict_TextMatchesA_ButKeyPointsToB()
    {
        var prevA = Q(rowNumber: 10, xrefId: "A", questionText: "What is your risk appetite?");
        var prevB = Q(rowNumber: 11, xrefId: "B", questionText: "Totally unrelated banana cucumber zzz wording.");
        var previous = new[] { prevA, prevB };

        // current text confidently matches prevA, but its key points to prevB.
        var current = new[] { Q(xrefId: "B", questionText: "What is your risk appetite?") };

        var a = ClqAlignmentEngine.Align(current, None, previous).Aligned.Single();

        a.CrossYear.Should().Be(CrossYearOutcome.XrefIdConflict);
        a.PreviousMatch.Should().BeNull();
        a.MatcherCandidate.Should().BeSameAs(prevA);
        a.XrefIdCounterpart.Should().BeSameAs(prevB);
        a.MatcherCandidate.Should().NotBeSameAs(a.XrefIdCounterpart);
    }

    [Fact]
    public void CrossYear_NewXrefId_WithTextualLookalike_IsFlaggedForAttention()
    {
        var prevA = Q(xrefId: "A", questionText: "What is your risk appetite?");
        var previous = new[] { prevA };

        // NEW key absent from previous, but textually near-identical to prevA.
        var current = new[] { Q(xrefId: "NEW", questionText: "What is your risk appetite?") };

        var a = ClqAlignmentEngine.Align(current, None, previous).Aligned.Single();

        a.CrossYear.Should().Be(CrossYearOutcome.NewXrefIdWithLookalike);
        a.CrossYear.Should().NotBe(CrossYearOutcome.Agree);
        a.CrossYear.Should().NotBe(CrossYearOutcome.XrefIdConflict);
        a.CrossYear.Should().NotBe(CrossYearOutcome.Neither);
        a.PreviousMatch.Should().BeNull();
        a.XrefIdCounterpart.Should().BeNull();
        a.MatcherCandidate.Should().BeSameAs(prevA);
        a.MatcherBaseScore.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void CrossYear_SameXrefId_TextDiverged_IsFlaggedForAttention_NotAgreeNorConflict()
    {
        var prevA = Q(xrefId: "A", questionText: "What is your risk appetite for credit exposure?");
        var previous = new[] { prevA };

        // Shares the key "A" but text is too dissimilar to match.
        var current = new[] { Q(xrefId: "A", questionText: "Banana.") };

        var a = ClqAlignmentEngine.Align(current, None, previous).Aligned.Single();

        a.CrossYear.Should().Be(CrossYearOutcome.SameXrefIdTextDiverged);
        a.CrossYear.Should().NotBe(CrossYearOutcome.Agree);
        a.CrossYear.Should().NotBe(CrossYearOutcome.XrefIdConflict);
        a.CrossYear.Should().NotBe(CrossYearOutcome.Neither);
        a.PreviousMatch.Should().BeNull(); // NOT auto-used as a baseline
        a.XrefIdCounterpart.Should().BeSameAs(prevA);
    }

    [Fact]
    public void CrossYear_Case5_Neither_NoKeyCounterpart_NoTextMatch()
    {
        var prevA = Q(xrefId: "A", questionText: "What is your risk appetite?");
        var previous = new[] { prevA };

        // current1 claims prevA (Agree); current2 is an orphan with a new key and
        // unrelated text — prevA is already taken, so current2 has no matcher pick.
        var current = new[]
        {
            Q(rowNumber: 1, xrefId: "A",   questionText: "What is your risk appetite?"),
            Q(rowNumber: 2, xrefId: "NEW", questionText: "Banana cucumber zzz unrelated wording."),
        };

        var aligned = ClqAlignmentEngine.Align(current, None, previous).Aligned;

        var orphan = aligned.Single(x => x.Current.RowNumber == 2);
        orphan.CrossYear.Should().Be(CrossYearOutcome.Neither);
        orphan.XrefIdCounterpart.Should().BeNull();
        orphan.MatcherCandidate.Should().BeNull();
    }

    // ── Base-vs-adjusted reporting (lesson 11) ───────────────────────────────────

    [Fact]
    public void CrossYear_MatcherBaseScore_IsBase_NotBonusAdjusted()
    {
        // Base text similarity is 0.4 (6 of 10 chars differ). Section + number
        // bonuses (+0.10 each) push the ADJUSTED score to 0.6, over the 0.5
        // threshold — but the reported MatcherBaseScore must be the BASE 0.4.
        var previous = new[]
        {
            Q(xrefId: "X1", questionNumber: "1.2", sectionName: "Sec A", questionText: "abcdefghij"),
        };
        var current = new[]
        {
            Q(xrefId: "X1", questionNumber: "1.2", sectionName: "Sec A", questionText: "abcdxyzqwv"),
        };

        var a = ClqAlignmentEngine.Align(current, None, previous).Aligned.Single();

        // Bonus pushed it over threshold → confident match → Agree (key also matches).
        a.CrossYear.Should().Be(CrossYearOutcome.Agree);
        a.MatcherBaseScore.Should().BeApproximately(0.4, 1e-9);
        a.MatcherBaseScore.Should().BeLessThan(0.5); // base alone would NOT have matched
    }
}
