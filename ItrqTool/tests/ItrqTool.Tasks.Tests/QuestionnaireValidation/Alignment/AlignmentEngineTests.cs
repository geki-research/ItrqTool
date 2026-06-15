using FluentAssertions;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Alignment;

public sealed class AlignmentEngineTests
{
    // A minimal question type carrying only the six IAlignmentIdentity fields the
    // generic engine reads. Reference type (record class) — the engine relies on
    // object identity (ReferenceEquals) for the Agree outcome.
    private sealed record Question(
        int RowNumber,
        string? XrefId,
        string OriginalText,
        string QuestionText,
        string SectionName,
        string? QuestionNumber) : IAlignmentIdentity;

    // Caller convenience: supply C#-level defaults so each test overrides only the
    // fields it cares about. OriginalText defaults to QuestionText when null.
    private static Question Q(
        int rowNumber = 1,
        string? xrefId = null,
        string questionText = "",
        string? originalText = null,
        string sectionName = "Section",
        string? questionNumber = null)
        => new(rowNumber, xrefId, originalText ?? questionText, questionText, sectionName, questionNumber);

    private static readonly IReadOnlyList<Question> None = [];

    // Distinct, mutually low-similarity texts so cross-year outcomes never depend
    // on Hungarian tie-breaking (lesson 73).
    private const string TextRisk     = "What is your overall risk appetite for the year?";
    private const string TextCredit   = "Describe the credit exposure limits in place.";
    private const string TextLiquid   = "Outline liquidity buffers held against stress.";
    private const string TextBanana   = "Banana cucumber zzz totally unrelated wording.";

    // ── Within-year ───────────────────────────────────────────────────────────

    [Fact]
    public void WithinYear_CleanMatch_JoinedByXrefId_NoShift_NoMismatch()
    {
        var current  = new[] { Q(rowNumber: 10, xrefId: "X1", questionText: TextRisk, originalText: "1. " + TextRisk) };
        var template = new[] { Q(rowNumber: 10, xrefId: "X1", questionText: TextRisk, originalText: "1. " + TextRisk) };

        var result = AlignmentEngine.Align(current, template, None);

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

        var a = AlignmentEngine.Align(current, template, None).Aligned.Single();

        a.WithinYear.Should().Be(WithinYearJoin.JoinedByXrefId);
        a.RowShifted.Should().BeTrue();
        a.TextMismatched.Should().BeFalse();
    }

    [Fact]
    public void WithinYear_TextMismatched_WhenOriginalTextDiffers()
    {
        var current  = new[] { Q(rowNumber: 10, xrefId: "X1", originalText: "current text") };
        var template = new[] { Q(rowNumber: 10, xrefId: "X1", originalText: "template text") };

        var a = AlignmentEngine.Align(current, template, None).Aligned.Single();

        a.WithinYear.Should().Be(WithinYearJoin.JoinedByXrefId);
        a.RowShifted.Should().BeFalse();
        a.TextMismatched.Should().BeTrue();
    }

    [Fact]
    public void WithinYear_AddedInResponse_WhenCurrentKeyAbsentFromTemplate()
    {
        var current  = new[] { Q(xrefId: "NEW") };
        var template = new[] { Q(xrefId: "OLD") };

        var a = AlignmentEngine.Align(current, template, None).Aligned.Single();

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

        var result = AlignmentEngine.Align(current, template, None);

        result.WithinYearRemoved.Should().ContainSingle().Which.Should().BeSameAs(template[0]);
    }

    // ── Malformed keys ──────────────────────────────────────────────────────────

    [Fact]
    public void Malformed_BlankCurrentKey_NotEvaluatedBothAxes_PlusBlankEntry()
    {
        var current = new[] { Q(rowNumber: 7, xrefId: null, questionText: "anything") };

        var result = AlignmentEngine.Align(current, None, None);

        var a = result.Aligned.Single();
        a.WithinYear.Should().Be(WithinYearJoin.NotEvaluatedMalformedKey);
        a.CrossYear.Should().Be(CrossYearOutcome.NotEvaluatedMalformedKey);
        a.TemplateMatch.Should().BeNull();
        a.PreviousMatch.Should().BeNull();
        a.XrefIdCounterpart.Should().BeNull();
        a.MatcherCandidate.Should().BeNull();
        a.MatcherBaseScore.Should().BeNull();

        result.MalformedKeys.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new MalformedKey(ValidationWorkbook.CurrentResponse, 7, null, MalformedKeyReason.Blank));
    }

    [Fact]
    public void Malformed_DuplicateCurrentKey_BothRowsMalformed()
    {
        var current = new[]
        {
            Q(rowNumber: 3, xrefId: "DUP"),
            Q(rowNumber: 8, xrefId: "DUP"),
        };

        var result = AlignmentEngine.Align(current, None, None);

        result.Aligned.Should().OnlyContain(a =>
            a.WithinYear == WithinYearJoin.NotEvaluatedMalformedKey &&
            a.CrossYear == CrossYearOutcome.NotEvaluatedMalformedKey);

        result.MalformedKeys.Should().BeEquivalentTo(new[]
        {
            new MalformedKey(ValidationWorkbook.CurrentResponse, 3, "DUP", MalformedKeyReason.Duplicate),
            new MalformedKey(ValidationWorkbook.CurrentResponse, 8, "DUP", MalformedKeyReason.Duplicate),
        });
    }

    [Fact]
    public void Malformed_KeysTaggedWithCorrectWorkbook()
    {
        var current  = new[] { Q(rowNumber: 1, xrefId: null) };
        var template = new[] { Q(rowNumber: 2, xrefId: null) };
        var previous = new[] { Q(rowNumber: 3, xrefId: null) };

        var result = AlignmentEngine.Align(current, template, previous);

        result.MalformedKeys.Should().BeEquivalentTo(new[]
        {
            new MalformedKey(ValidationWorkbook.CurrentResponse, 1, null, MalformedKeyReason.Blank),
            new MalformedKey(ValidationWorkbook.EmptyTemplate, 2, null, MalformedKeyReason.Blank),
            new MalformedKey(ValidationWorkbook.PreviousResponse, 3, null, MalformedKeyReason.Blank),
        });
    }

    [Fact]
    public void Malformed_PreviousDuplicateKey_ExcludedFromMatcherAndXrefIndex()
    {
        var current  = new[] { Q(xrefId: "P", questionText: TextRisk) };
        var previous = new[]
        {
            Q(rowNumber: 4, xrefId: "P", questionText: TextRisk),
            Q(rowNumber: 5, xrefId: "P", questionText: TextRisk),
        };

        var result = AlignmentEngine.Align(current, None, previous);

        var a = result.Aligned.Single();
        a.CrossYear.Should().Be(CrossYearOutcome.Neither);
        a.MatcherCandidate.Should().BeNull();
        a.XrefIdCounterpart.Should().BeNull();

        result.MalformedKeys.Should().BeEquivalentTo(new[]
        {
            new MalformedKey(ValidationWorkbook.PreviousResponse, 4, "P", MalformedKeyReason.Duplicate),
            new MalformedKey(ValidationWorkbook.PreviousResponse, 5, "P", MalformedKeyReason.Duplicate),
        });
    }

    // ── Cross-year reconciliation — one test per row of the locked table ────────

    [Fact]
    public void CrossYear_Case1_Agree_KeyAndTextOnSamePrevious()
    {
        var current  = new[] { Q(xrefId: "X1", questionText: TextRisk) };
        var previous = new[] { Q(xrefId: "X1", questionText: TextRisk) };

        var a = AlignmentEngine.Align(current, None, previous).Aligned.Single();

        a.CrossYear.Should().Be(CrossYearOutcome.Agree);
        a.PreviousMatch.Should().BeSameAs(previous[0]);
        a.XrefIdCounterpart.Should().BeSameAs(a.PreviousMatch);
        a.MatcherCandidate.Should().BeSameAs(previous[0]);
        a.MatcherBaseScore.Should().BeApproximately(1.0, 1e-9); // BASE score
    }

    [Fact]
    public void CrossYear_Case2_XrefIdConflict_TextMatchesA_ButKeyPointsToB()
    {
        var prevA = Q(rowNumber: 10, xrefId: "A", questionText: TextRisk);
        var prevB = Q(rowNumber: 11, xrefId: "B", questionText: TextBanana);
        var previous = new[] { prevA, prevB };

        // current text confidently matches prevA, but its key points to prevB.
        var current = new[] { Q(xrefId: "B", questionText: TextRisk) };

        var a = AlignmentEngine.Align(current, None, previous).Aligned.Single();

        a.CrossYear.Should().Be(CrossYearOutcome.XrefIdConflict);
        a.PreviousMatch.Should().BeNull();
        a.MatcherCandidate.Should().BeSameAs(prevA);
        a.XrefIdCounterpart.Should().BeSameAs(prevB);
        a.MatcherCandidate.Should().NotBeSameAs(a.XrefIdCounterpart);
    }

    [Fact]
    public void CrossYear_Case3_NewXrefId_WithTextualLookalike_IsFlaggedForAttention()
    {
        var prevA = Q(xrefId: "A", questionText: TextRisk);
        var previous = new[] { prevA };

        // NEW key absent from previous, but textually identical to prevA.
        var current = new[] { Q(xrefId: "NEW", questionText: TextRisk) };

        var a = AlignmentEngine.Align(current, None, previous).Aligned.Single();

        a.CrossYear.Should().Be(CrossYearOutcome.NewXrefIdWithLookalike);
        a.PreviousMatch.Should().BeNull();
        a.XrefIdCounterpart.Should().BeNull();
        a.MatcherCandidate.Should().BeSameAs(prevA);
        a.MatcherBaseScore.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void CrossYear_Case4_SameXrefId_TextDiverged_IsFlaggedForAttention()
    {
        var prevA = Q(xrefId: "A", questionText: TextCredit);
        var previous = new[] { prevA };

        // Shares the key "A" but text is too dissimilar to match.
        var current = new[] { Q(xrefId: "A", questionText: TextBanana) };

        var a = AlignmentEngine.Align(current, None, previous).Aligned.Single();

        a.CrossYear.Should().Be(CrossYearOutcome.SameXrefIdTextDiverged);
        a.PreviousMatch.Should().BeNull(); // NOT auto-used as a baseline
        a.XrefIdCounterpart.Should().BeSameAs(prevA);
    }

    [Fact]
    public void CrossYear_Case5_Neither_NoKeyCounterpart_NoTextMatch()
    {
        var prevA = Q(xrefId: "A", questionText: TextRisk);
        var previous = new[] { prevA };

        // current1 claims prevA (Agree); current2 is an orphan with a new key and
        // unrelated text — prevA is already taken, so current2 has no matcher pick.
        var current = new[]
        {
            Q(rowNumber: 1, xrefId: "A",   questionText: TextRisk),
            Q(rowNumber: 2, xrefId: "NEW", questionText: TextBanana),
        };

        var aligned = AlignmentEngine.Align(current, None, previous).Aligned;

        var orphan = aligned.Single(x => x.Current.RowNumber == 2);
        orphan.CrossYear.Should().Be(CrossYearOutcome.Neither);
        orphan.XrefIdCounterpart.Should().BeNull();
        orphan.MatcherCandidate.Should().BeNull();
    }

    // ── Base-vs-adjusted reporting ──────────────────────────────────────────────

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

        var a = AlignmentEngine.Align(current, None, previous).Aligned.Single();

        // Bonus pushed it over threshold → confident match → Agree (key also matches).
        a.CrossYear.Should().Be(CrossYearOutcome.Agree);
        a.MatcherBaseScore.Should().BeApproximately(0.4, 1e-9);
        a.MatcherBaseScore.Should().BeLessThan(0.5); // base alone would NOT have matched
    }

    // ── Equivalence guard — multi-question scenario pinned to known v01 behaviour ─

    [Fact]
    public void EquivalenceGuard_MixedScenario_OutcomesPinnedToV01Behaviour()
    {
        // Three previous questions with mutually distinct (low-similarity) text.
        var prevRisk   = Q(rowNumber: 10, xrefId: "R", questionText: TextRisk);
        var prevCredit = Q(rowNumber: 11, xrefId: "C", questionText: TextCredit);
        var prevLiquid = Q(rowNumber: 12, xrefId: "L", questionText: TextLiquid);
        var previous   = new[] { prevRisk, prevCredit, prevLiquid };

        var current = new[]
        {
            // 1: key R + same text  → Agree (PreviousMatch = prevRisk)
            Q(rowNumber: 20, xrefId: "R", questionText: TextRisk),
            // 2: key C + heavily diverged text → SameXrefIdTextDiverged
            Q(rowNumber: 21, xrefId: "C", questionText: TextBanana),
            // 3: brand-new key, brand-new unrelated text → Neither (L stays free but
            //    is too dissimilar to be a confident match)
            Q(rowNumber: 22, xrefId: "NEW", questionText: "Quux frobnitz wibble plover xyzzy."),
        };

        var aligned = AlignmentEngine.Align(current, None, previous).Aligned;

        var r1 = aligned.Single(a => a.Current.RowNumber == 20);
        r1.CrossYear.Should().Be(CrossYearOutcome.Agree);
        r1.PreviousMatch.Should().BeSameAs(prevRisk);

        var r2 = aligned.Single(a => a.Current.RowNumber == 21);
        r2.CrossYear.Should().Be(CrossYearOutcome.SameXrefIdTextDiverged);
        r2.PreviousMatch.Should().BeNull();
        r2.XrefIdCounterpart.Should().BeSameAs(prevCredit);

        var r3 = aligned.Single(a => a.Current.RowNumber == 22);
        r3.CrossYear.Should().Be(CrossYearOutcome.Neither);
        r3.PreviousMatch.Should().BeNull();
        r3.XrefIdCounterpart.Should().BeNull();

        // PreviousMatch is non-null ONLY on Agree across the whole batch.
        aligned.Where(a => a.PreviousMatch is not null)
               .Should().OnlyContain(a => a.CrossYear == CrossYearOutcome.Agree);
    }
}
