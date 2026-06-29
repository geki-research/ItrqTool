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

    // ── Agree — qid present + OriginalText equal ──────────────────────────────────

    [Fact]
    public void QidPresentAndTextEqual_Agree_PreviousSet()
    {
        var cur  = Cur("q1", originalText: "Same Text");
        var prev = Prev("q1", originalText: "Same Text");

        var result = GdInjectAligner.Align(new[] { cur }, new[] { prev });

        var m = result.Matches.Should().ContainSingle().Subject;
        m.Outcome.Should().Be(CrossYearOutcome.Agree);
        m.Previous.Should().Be(prev);
        m.XrefIdCounterpart.Should().Be(prev);
        result.MalformedKeys.Should().BeEmpty();
    }

    // ── SameXrefIdTextDiverged — qid present + text differs ───────────────────────

    [Fact]
    public void QidPresentAndTextDiverged_SameXrefIdTextDiverged_PreviousNull()
    {
        var cur  = Cur("q1", originalText: "New Text");
        var prev = Prev("q1", originalText: "Old Text");

        var result = GdInjectAligner.Align(new[] { cur }, new[] { prev });

        var m = result.Matches.Should().ContainSingle().Subject;
        m.Outcome.Should().Be(CrossYearOutcome.SameXrefIdTextDiverged);
        m.Previous.Should().BeNull();
        m.XrefIdCounterpart.Should().Be(prev);
    }

    // ── Neither — qid absent from previous ────────────────────────────────────────

    [Fact]
    public void QidAbsentFromPrevious_Neither_PreviousNull()
    {
        var cur = Cur("q99");

        var result = GdInjectAligner.Align(new[] { cur }, Array.Empty<GdV01Question>());

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

        var result = GdInjectAligner.Align(new[] { cur }, Array.Empty<GdV01Question>());

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

        var result = GdInjectAligner.Align(new[] { cur1, cur2 }, Array.Empty<GdV01Question>());

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

        var result = GdInjectAligner.Align(new[] { cur1, cur2 }, new[] { prev });

        result.Matches.Should().NotContain(m =>
            m.Outcome == CrossYearOutcome.XrefIdConflict ||
            m.Outcome == CrossYearOutcome.NewXrefIdWithLookalike);

        result.Matches[0].Outcome.Should().Be(CrossYearOutcome.Agree);     // q1 joins by qid
        result.Matches[1].Outcome.Should().Be(CrossYearOutcome.Neither);   // q2 has no qid counterpart
    }

    // ── Order preserved + matcher fields null ─────────────────────────────────────

    [Fact]
    public void Matches_PreserveCurrentOrder_AndMatcherFieldsAreNull()
    {
        var cur1 = Cur("q1", rowNumber: 10);
        var cur2 = Cur("q2", rowNumber: 20);
        var cur3 = Cur("q3", rowNumber: 30);

        var result = GdInjectAligner.Align(
            new[] { cur1, cur2, cur3 }, Array.Empty<GdV01Question>());

        result.Matches.Select(m => m.Current.XrefId).Should().Equal("q1", "q2", "q3");
        result.Matches.Should().AllSatisfy(m =>
        {
            m.MatcherCandidate.Should().BeNull();
            m.MatcherBaseScore.Should().BeNull();
        });
    }
}
