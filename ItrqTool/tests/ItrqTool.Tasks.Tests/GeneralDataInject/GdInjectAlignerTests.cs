using FluentAssertions;
using ItrqTool.Tasks.GeneralDataInject;
using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.GeneralDataValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using Xunit;

namespace ItrqTool.Tasks.Tests.GeneralDataInject;

// Coverage for GdInjectAligner.Align: the forked pure qid-join (current v02 ⟷ previous v01).
// All fixtures are in-memory; no file I/O, no ClosedXML. The aligner never reads answers, so
// the question builders carry empty answer lists.
public sealed class GdInjectAlignerTests
{
    private static GdV02Question Cur(
        string? xrefId,
        int rowNumber = 10,
        string originalText = "Text") =>
        new(RowNumber:      rowNumber,
            XrefId:         xrefId,
            OriginalText:   originalText,
            QuestionText:   originalText,
            SectionName:    "Section",
            QuestionNumber: "1",
            Answers:        Array.Empty<GdV02Answer>());

    private static GdV01Question Prev(
        string? xrefId,
        int rowNumber = 100,
        string originalText = "Text") =>
        new(RowNumber:      rowNumber,
            XrefId:         xrefId,
            OriginalText:   originalText,
            QuestionText:   originalText,
            SectionName:    "Section",
            QuestionNumber: "1",
            Answers:        Array.Empty<GdAnswer>());

    // The production default (GdInjectConfig.DefaultQidJoinSimilarityThreshold). Every test that
    // is not specifically about the threshold runs at it, so the fixtures read the way production
    // behaves. BLG-0077 widened Align's signature with this scalar.
    private const double Threshold = 0.50;

    private static CrossFormatAlignmentResult<GdV02Question, GdV01Question> Align(
        IReadOnlyList<GdV02Question> current,
        IReadOnlyList<GdV01Question> previous,
        double threshold = Threshold)
        => GdInjectAligner.Align(current, previous, threshold);

    // ── Agree — qid present + OriginalText equal ──────────────────────────────────

    [Fact]
    public void QidPresentAndTextEqual_Agree_PreviousSet()
    {
        var cur  = Cur("q1", originalText: "Same Text");
        var prev = Prev("q1", originalText: "Same Text");

        var result = Align(new[] { cur }, new[] { prev });

        var m = result.Matches.Should().ContainSingle().Subject;
        m.Outcome.Should().Be(CrossYearOutcome.Agree);
        m.Previous.Should().Be(prev);
        m.XrefIdCounterpart.Should().Be(prev);
        result.MalformedKeys.Should().BeEmpty();
    }

    // ── Agree — qid present + text differs but scores AT OR ABOVE the threshold ───
    //
    // REWRITTEN by BLG-0077 (authorized). Its OLD intent was "any non-identical text under one qid
    // diverges, so Previous is null" — the defect this work fixes. "New Text" vs "Old Text" scores
    // 0.6250, comfortably above the 0.50 default, so the pair is now the same question and its
    // previous values DO carry forward. The NEW intent is that assertion inverted deliberately:
    // above the threshold, a drifted wording agrees. The below-threshold half of the boundary moved
    // to the sibling test immediately below, so both sides stay covered.

    [Fact]
    public void QidPresentAndTextDivergedAboveThreshold_Agree_PreviousSet()
    {
        var cur  = Cur("q1", originalText: "New Text");
        var prev = Prev("q1", originalText: "Old Text");

        var result = Align(new[] { cur }, new[] { prev });

        var m = result.Matches.Should().ContainSingle().Subject;
        m.Outcome.Should().Be(CrossYearOutcome.Agree);
        m.Previous.Should().Be(prev);
        m.XrefIdCounterpart.Should().Be(prev);
        m.MatcherBaseScore.Should().BeApproximately(0.6250, 1e-9);
    }

    // ── SameXrefIdTextDiverged — qid present + text scores BELOW the threshold ────

    [Fact]
    public void QidPresentAndTextDivergedBelowThreshold_SameXrefIdTextDiverged_PreviousNull()
    {
        // Wholly unrelated wording — nothing a threshold at or below 0.50 would rescue.
        var cur  = Cur("q1", originalText: "Number of staff employed at year end");
        var prev = Prev("q1", originalText: "Zzzz");

        var result = Align(new[] { cur }, new[] { prev });

        var m = result.Matches.Should().ContainSingle().Subject;
        m.Outcome.Should().Be(CrossYearOutcome.SameXrefIdTextDiverged);
        m.Previous.Should().BeNull();
        m.XrefIdCounterpart.Should().Be(prev);
        m.MatcherBaseScore.Should().BeLessThan(Threshold);
    }

    // ── The boundary itself: >= agrees, just under diverges ──────────────────────

    [Fact]
    public void ScoreExactlyAtThreshold_Agrees()
    {
        // "abcd" vs "abxy": 2 of 4 characters substituted → score exactly 0.50.
        var cur  = Cur("q1", originalText: "abcd");
        var prev = Prev("q1", originalText: "abxy");

        var result = Align(new[] { cur }, new[] { prev }, threshold: 0.50);

        var m = result.Matches.Should().ContainSingle().Subject;
        m.MatcherBaseScore.Should().BeApproximately(0.50, 1e-9);
        m.Outcome.Should().Be(CrossYearOutcome.Agree, "the comparison is >=, so equal scores agree");
        m.Previous.Should().Be(prev);
    }

    [Fact]
    public void ScoreJustBelowThreshold_Diverges()
    {
        var cur  = Cur("q1", originalText: "abcd");
        var prev = Prev("q1", originalText: "abxy");

        // Same 0.50 pair, threshold nudged just above it.
        var result = Align(new[] { cur }, new[] { prev }, threshold: 0.5000001);

        var m = result.Matches.Should().ContainSingle().Subject;
        m.Outcome.Should().Be(CrossYearOutcome.SameXrefIdTextDiverged);
        m.Previous.Should().BeNull();
    }

    // ── Identity wins regardless of the threshold ────────────────────────────────

    [Fact]
    public void IdenticalText_Agrees_EvenAtMaximumThreshold()
    {
        var cur  = Cur("q1", originalText: "Same Text");
        var prev = Prev("q1", originalText: "Same Text");

        var result = Align(new[] { cur }, new[] { prev }, threshold: 1.0);

        var m = result.Matches.Should().ContainSingle().Subject;
        m.Outcome.Should().Be(CrossYearOutcome.Agree);
        m.Previous.Should().Be(prev);
    }

    // A threshold of 0.0 accepts every counterpart — the knob's permissive extreme.
    [Fact]
    public void ZeroThreshold_AcceptsEvenWhollyUnrelatedText()
    {
        var cur  = Cur("q1", originalText: "Number of staff employed at year end");
        var prev = Prev("q1", originalText: "Zzzz");

        var result = Align(new[] { cur }, new[] { prev }, threshold: 0.0);

        result.Matches.Should().ContainSingle().Subject
            .Outcome.Should().Be(CrossYearOutcome.Agree);
    }

    // ── Score normalisation vs identity — the two are deliberately different ──────

    [Fact]
    public void CaseOnlyDrift_ScoresOne_ButIsStillNotIdentical_SoStillAgrees()
    {
        // TextSimilarity normalises case, so this scores 1.0 and agrees. It is NOT ordinally
        // identical, though, which is what makes the mapper warn about it — see
        // GdInjectMapperTests.Agree_TextCaseOnlyDrift_StillWarns.
        var cur  = Cur("q1", originalText: "SAME TEXT");
        var prev = Prev("q1", originalText: "same text");

        var result = Align(new[] { cur }, new[] { prev });

        var m = result.Matches.Should().ContainSingle().Subject;
        m.Outcome.Should().Be(CrossYearOutcome.Agree);
        m.MatcherBaseScore.Should().BeApproximately(1.0, 1e-9);
        cur.OriginalText.Should().NotBe(prev.OriginalText, "the drift is real at the byte level");
    }

    // ── Null-text guard — must classify, never throw ──────────────────────────────

    [Fact]
    public void NullTexts_BothSides_AgreeWithoutThrowing()
    {
        var cur  = Cur("q1", originalText: null!);
        var prev = Prev("q1", originalText: null!);

        var act = () => Align(new[] { cur }, new[] { prev });

        var result = act.Should().NotThrow().Subject;
        result.Matches.Should().ContainSingle().Subject
            .Outcome.Should().Be(CrossYearOutcome.Agree, "two absent texts are ordinally equal, as before");
    }

    [Fact]
    public void NullTextOnOneSideOnly_DivergesWithoutThrowing()
    {
        var cur  = Cur("q1", originalText: null!);
        var prev = Prev("q1", originalText: "Real question text");

        var act = () => Align(new[] { cur }, new[] { prev });

        var result = act.Should().NotThrow().Subject;
        var m = result.Matches.Should().ContainSingle().Subject;
        m.Outcome.Should().Be(CrossYearOutcome.SameXrefIdTextDiverged);
        m.MatcherBaseScore.Should().Be(0.0, "one empty side scores 0.0, never a silent match");
    }

    // ── Neither — qid absent from previous ────────────────────────────────────────

    [Fact]
    public void QidAbsentFromPrevious_Neither_PreviousNull()
    {
        var cur = Cur("q99");

        var result = Align(new[] { cur }, Array.Empty<GdV01Question>());

        var m = result.Matches.Should().ContainSingle().Subject;
        m.Outcome.Should().Be(CrossYearOutcome.Neither);
        m.Previous.Should().BeNull();
        m.XrefIdCounterpart.Should().BeNull();
    }

    // ── NotEvaluatedMalformedKey — blank current qid → + MalformedKey ─────────────

    [Fact]
    public void BlankCurrentQid_NotEvaluatedMalformedKey_AndMalformedKeyEmitted()
    {
        var cur = Cur("   ", rowNumber: 7);

        var result = Align(new[] { cur }, Array.Empty<GdV01Question>());

        var m = result.Matches.Should().ContainSingle().Subject;
        m.Outcome.Should().Be(CrossYearOutcome.NotEvaluatedMalformedKey);
        m.Previous.Should().BeNull();

        result.MalformedKeys.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new MalformedKey(ValidationWorkbook.CurrentResponse, 7, "   ", MalformedKeyReason.Blank));
    }

    // ── NotEvaluatedMalformedKey — duplicate current qid → + MalformedKey per row ─

    [Fact]
    public void DuplicateCurrentQid_BothNotEvaluatedMalformedKey_AndMalformedKeysEmitted()
    {
        var cur1 = Cur("dup", rowNumber: 10);
        var cur2 = Cur("dup", rowNumber: 20);

        var result = Align(new[] { cur1, cur2 }, Array.Empty<GdV01Question>());

        result.Matches.Should().HaveCount(2);
        result.Matches.Should().AllSatisfy(m =>
            m.Outcome.Should().Be(CrossYearOutcome.NotEvaluatedMalformedKey));

        result.MalformedKeys.Should().HaveCount(2);
        result.MalformedKeys.Should().AllSatisfy(k =>
        {
            k.Workbook.Should().Be(ValidationWorkbook.CurrentResponse);
            k.Reason.Should().Be(MalformedKeyReason.Duplicate);
            k.XrefId.Should().Be("dup");
        });
        result.MalformedKeys.Select(k => k.RowNumber).Should().BeEquivalentTo(new[] { 10, 20 });
    }

    // ── Never emits XrefIdConflict / NewXrefIdWithLookalike (no Hungarian) ────────

    [Fact]
    public void TextTwins_DifferentQids_NeverConflictOrLookalike()
    {
        // Two current questions with identical text but different qids; previous has only q1's
        // text (under q1). A Hungarian matcher could pair q2's current text to q1's previous and
        // emit NewXrefIdWithLookalike / XrefIdConflict — the pure qid-join never does.
        var cur1 = Cur("q1", rowNumber: 10, originalText: "Identical Text");
        var cur2 = Cur("q2", rowNumber: 11, originalText: "Identical Text");
        var prev = Prev("q1", rowNumber: 100, originalText: "Identical Text");

        var result = Align(new[] { cur1, cur2 }, new[] { prev });

        result.Matches.Should().NotContain(m =>
            m.Outcome == CrossYearOutcome.XrefIdConflict ||
            m.Outcome == CrossYearOutcome.NewXrefIdWithLookalike);

        result.Matches[0].Outcome.Should().Be(CrossYearOutcome.Agree);     // q1 joins by qid
        result.Matches[1].Outcome.Should().Be(CrossYearOutcome.Neither);   // q2 has no qid counterpart
    }

    // ── Order preserved + matcher fields reflect whether a counterpart was found ──
    //
    // REWRITTEN by BLG-0077 (authorized, defensively). Its OLD intent — "the matcher fields are
    // always null, because the qid-join has no Hungarian candidate" — stayed mechanically green
    // here only because this fixture has an EMPTY previous list. It is now semantically stale: the
    // fields carry the qid-selected counterpart and its raw score whenever one exists. The NEW
    // intent keeps the order assertion untouched and pins the real rule: null exactly when no qid
    // counterpart was found, populated together when one was.

    [Fact]
    public void Matches_PreserveCurrentOrder_AndMatcherFieldsNullWhenNoCounterpart()
    {
        var cur1 = Cur("q1", rowNumber: 10);
        var cur2 = Cur("q2", rowNumber: 20);
        var cur3 = Cur("q3", rowNumber: 30);

        var result = Align(
            new[] { cur1, cur2, cur3 }, Array.Empty<GdV01Question>());

        result.Matches.Select(m => m.Current.XrefId).Should().Equal("q1", "q2", "q3");
        result.Matches.Should().AllSatisfy(m =>
        {
            m.MatcherCandidate.Should().BeNull();
            m.MatcherBaseScore.Should().BeNull();
        });
    }

    [Fact]
    public void MatcherFields_PopulatedTogether_WhenQidCounterpartExists()
    {
        var cur  = Cur("q1", originalText: "New Text");
        var prev = Prev("q1", originalText: "Old Text");

        var m = Align(new[] { cur }, new[] { prev }).Matches.Should().ContainSingle().Subject;

        m.MatcherCandidate.Should().Be(prev, "the qid selected this previous question to compare");
        m.MatcherBaseScore.Should().NotBeNull();
    }

    // The reported score must be the RAW TextSimilarity value — never inflated, never rounded up —
    // so a human can reproduce it from the two texts alone.
    [Fact]
    public void MatcherBaseScore_IsRawSimilarity_NotInflated()
    {
        var cur  = Cur("q1", originalText: "New Text");
        var prev = Prev("q1", originalText: "Old Text");

        var m = Align(new[] { cur }, new[] { prev }).Matches.Should().ContainSingle().Subject;

        // "new text" vs "old text" normalised: 8 chars, 3 substitutions → 1 - 3/8 = 0.6250 exactly.
        m.MatcherBaseScore.Should().BeApproximately(0.6250, 1e-9);
    }

    // A diverged pair still reports its score, so the operator can see how far short it fell.
    [Fact]
    public void MatcherBaseScore_PopulatedOnDivergedArm_Too()
    {
        var cur  = Cur("q1", originalText: "Number of staff employed at year end");
        var prev = Prev("q1", originalText: "Zzzz");

        var m = Align(new[] { cur }, new[] { prev }).Matches.Should().ContainSingle().Subject;

        m.Outcome.Should().Be(CrossYearOutcome.SameXrefIdTextDiverged);
        m.MatcherBaseScore.Should().NotBeNull();
        m.MatcherCandidate.Should().Be(prev);
    }
}
