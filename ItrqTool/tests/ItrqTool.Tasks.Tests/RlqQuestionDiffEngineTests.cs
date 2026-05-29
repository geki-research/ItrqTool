using FluentAssertions;
using Xunit;
using ItrqTool.Tasks.RiskLevelQuestionDiff;

namespace ItrqTool.Tasks.Tests;

public sealed class RlqQuestionDiffEngineTests
{
    private static RiskLevelQuestion Q(string text, string? dv = null, string? cf = null,
                                        int row = 1, string? qn = null, string? dvf = null,
                                        string? explanation = null,
                                        string? dvOp = null, string? dvf2 = null,
                                        string? cfType = null, string? cfVal = null, string? cfVal2 = null)
        => new("Section", text, explanation ?? "", qn, row, dv, dvf, cf, dvOp, dvf2, cfType, cfVal, cfVal2);

    // Helper that accepts an explicit section name (for bonus tests).
    private static RiskLevelQuestion Qs(string text, string section, string? qn = null,
                                         string? explanation = null)
        => new(section, text, explanation ?? "", qn, 1, null, null, null);

    [Fact]
    public void Diff_AllIdentical_NoChanges()
    {
        var questions = new[] { Q("What is risk?"), Q("Describe controls.") };
        var result = QuestionDiffEngine.Diff(questions, questions);

        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_OneAdded_AppearsInAdded()
    {
        var old = new[] { Q("What is risk?") };
        var newQ = new[] { Q("What is risk?"), Q("New question.") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Added.Should().HaveCount(1);
        result.Added[0].Question.QuestionText.Should().Be("New question.");
        result.Removed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_OneRemoved_AppearsInRemoved()
    {
        var old = new[] { Q("What is risk?"), Q("Old question.") };
        var newQ = new[] { Q("What is risk?") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Removed.Should().HaveCount(1);
        result.Removed[0].Question.QuestionText.Should().Be("Old question.");
        result.Added.Should().BeEmpty();
    }

    [Fact]
    public void Diff_QuestionRewordedSignificantly_OldRemovedNewAdded()
    {
        var old = new[] { Q("aaa bbb ccc") };
        var newQ = new[] { Q("zzz yyy xxx") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Removed.Should().HaveCount(1);
        result.Added.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_QuestionRewordedSlightly_AppearsInChanged()
    {
        var old = new[] { Q("What is the risk level for this control?") };
        var newQ = new[] { Q("What is the risk level for this process?") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].SimilarityScore.Should().BeGreaterThanOrEqualTo(0.5).And.BeLessThan(1.0);
        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_SameTextDvTypeChanged_IsChangedWithDvChangedTrue()
    {
        var old = new[] { Q("What is risk?", dv: "List") };
        var newQ = new[] { Q("What is risk?", dv: "WholeNumber") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].DvChanged.Should().BeTrue();
        result.Changed[0].TextChanged.Should().BeFalse();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_TextAndDvTypeChanged_ChangedWithTextAndDvChangedTrue()
    {
        var old = new[] { Q("What is the risk level?", dv: "List") };
        var newQ = new[] { Q("What is the risk rating?", dv: "WholeNumber") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].TextChanged.Should().BeTrue();
        result.Changed[0].DvChanged.Should().BeTrue();
    }

    // ── Number-change tests ───────────────────────────────────────────────────

    [Fact]
    public void Diff_IdenticalTextDifferentNumbers_AppearsInChangedNotUnchanged()
    {
        var old = new[] { Q("What is risk?", qn: "1.1") };
        var newQ = new[] { Q("What is risk?", qn: "1.2") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].OldQuestion.QuestionNumber.Should().Be("1.1");
        result.Changed[0].NewQuestion.QuestionNumber.Should().Be("1.2");
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_NumberChangedOnly_ChangedWithNumberChangedTrueAllOtherFlagsFalse()
    {
        var old = new[] { Q("What is risk?", qn: "1.1") };
        var newQ = new[] { Q("What is risk?", qn: "1.2") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].NumberChanged.Should().BeTrue();
        result.Changed[0].TextChanged.Should().BeFalse();
        result.Changed[0].ExplanationChanged.Should().BeFalse();
        result.Changed[0].DvChanged.Should().BeFalse();
        result.Changed[0].CfChanged.Should().BeFalse();
    }

    [Fact]
    public void Diff_IdenticalTextSameNumberNoDvCfChange_AppearsInUnchanged()
    {
        var old = new[] { Q("What is risk?", qn: "1.1") };
        var newQ = new[] { Q("What is risk?", qn: "1.1") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Unchanged.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_IdenticalTextSameNumberDvTypeDiffers_IsChangedWithDvChangedTrue()
    {
        var old = new[] { Q("What is risk?", dv: "List",        qn: "1.1") };
        var newQ = new[] { Q("What is risk?", dv: "WholeNumber", qn: "1.1") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].DvChanged.Should().BeTrue();
        result.Unchanged.Should().BeEmpty();
    }

    // ── Unchanged category ────────────────────────────────────────────────────

    [Fact]
    public void Diff_AllIdentical_AllLandInUnchanged()
    {
        var q1 = Q("What is risk?", dv: "List", qn: "1.1");
        var q2 = Q("Describe controls.", qn: "1.2");
        var result = QuestionDiffEngine.Diff([q1, q2], [q1, q2]);

        result.Unchanged.Should().HaveCount(2);
        result.Changed.Should().BeEmpty();
    }

    // ── CF-change rules ───────────────────────────────────────────────────────

    [Fact]
    public void Diff_NonListDv_CfDiffers_ChangedWithCfChangedTrueAllOtherFlagsFalse()
    {
        var old = new[] { Q("What is risk?", dv: "WholeNumber", cf: "Equal") };
        var newQ = new[] { Q("What is risk?", dv: "WholeNumber", cf: "NotEqual") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].CfChanged.Should().BeTrue();
        result.Changed[0].TextChanged.Should().BeFalse();
        result.Changed[0].ExplanationChanged.Should().BeFalse();
        result.Changed[0].NumberChanged.Should().BeFalse();
        result.Changed[0].DvChanged.Should().BeFalse();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_ListDv_SameList_CfDiffers_IsChangedWithCfChangedTrue()
    {
        // Detect-everything: the former List-CF mute is gone. A CF operator change on a
        // List/dropdown cell is now surfaced like any other CF change.
        var old = new[] { Q("What is risk?", dv: "List", dvf: "\"Yes,No\"", cf: "Equal") };
        var newQ = new[] { Q("What is risk?", dv: "List", dvf: "\"Yes,No\"", cf: "NotEqual") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].CfChanged.Should().BeTrue();
        result.Changed[0].DvChanged.Should().BeFalse();
        result.Unchanged.Should().BeEmpty();
    }

    // ── IsDvChanged / List comparison ─────────────────────────────────────────

    [Fact]
    public void Diff_ListDvSameInlineFormula_IsUnchanged()
    {
        var old = new[] { Q("What is risk?", dv: "List", dvf: "\"Yes,No,N/A\"") };
        var newQ = new[] { Q("What is risk?", dv: "List", dvf: "\"Yes,No,N/A\"") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Unchanged.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_ListDvDifferentInlineItems_ChangedWithDvChangedTrue()
    {
        var old = new[] { Q("What is risk?", dv: "List", dvf: "\"Yes,No\"") };
        var newQ = new[] { Q("What is risk?", dv: "List", dvf: "\"Yes,No,N/A\"") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].DvChanged.Should().BeTrue();
        result.Changed[0].CfChanged.Should().BeFalse();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_ListDvSameItemsDifferentOrder_IsUnchanged()
    {
        var old = new[] { Q("What is risk?", dv: "List", dvf: "\"Yes,No\"") };
        var newQ = new[] { Q("What is risk?", dv: "List", dvf: "\"No,Yes\"") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Unchanged.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_ListDvOneInlineOneRangeRef_ChangedWithDvChangedTrue()
    {
        var old = new[] { Q("What is risk?", dv: "List", dvf: "\"Yes,No\"") };
        var newQ = new[] { Q("What is risk?", dv: "List", dvf: "Sheet1!$A$1:$A$2") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].DvChanged.Should().BeTrue();
    }

    [Fact]
    public void Diff_ListDvSameRangeRef_IsUnchanged()
    {
        var old = new[] { Q("What is risk?", dv: "List", dvf: "Sheet1!$A$1:$A$5") };
        var newQ = new[] { Q("What is risk?", dv: "List", dvf: "Sheet1!$A$1:$A$5") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Unchanged.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_NullDvNullCf_IdenticalTextSameNumber_IsUnchanged()
    {
        var old = new[] { Q("What is risk?", qn: "1.1") };
        var newQ = new[] { Q("What is risk?", qn: "1.1") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Unchanged.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_SameDvTypeNonList_IsUnchanged()
    {
        var old = new[] { Q("What is risk?", dv: "WholeNumber") };
        var newQ = new[] { Q("What is risk?", dv: "WholeNumber") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Unchanged.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
    }

    // ── Detect-everything: full DV/CF detection (B.2b) ────────────────────────

    [Fact]
    public void Diff_DvOperatorOnlyChanged_DvChangedTrue()
    {
        var old  = new[] { Q("What is risk?", dv: "Decimal", dvf: "0", dvOp: "GreaterThan") };
        var newQ = new[] { Q("What is risk?", dv: "Decimal", dvf: "0", dvOp: "LessThan") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].DvChanged.Should().BeTrue();
        result.Changed[0].CfChanged.Should().BeFalse();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_DvSecondValueOnlyChanged_DvChangedTrue()
    {
        var old  = new[] { Q("What is risk?", dv: "WholeNumber", dvf: "0", dvOp: "Between", dvf2: "100") };
        var newQ = new[] { Q("What is risk?", dv: "WholeNumber", dvf: "0", dvOp: "Between", dvf2: "200") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].DvChanged.Should().BeTrue();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_CfTypeOnlyChanged_CfChangedTrue()
    {
        var old  = new[] { Q("What is risk?", dv: "WholeNumber", cfType: "CellIs", cf: "GreaterThan", cfVal: "5") };
        var newQ = new[] { Q("What is risk?", dv: "WholeNumber", cfType: "Expression", cf: "GreaterThan", cfVal: "5") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].CfChanged.Should().BeTrue();
        result.Changed[0].DvChanged.Should().BeFalse();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_CfValueOnlyChanged_CfChangedTrue()
    {
        var old  = new[] { Q("What is risk?", dv: "WholeNumber", cfType: "CellIs", cf: "GreaterThan", cfVal: "5") };
        var newQ = new[] { Q("What is risk?", dv: "WholeNumber", cfType: "CellIs", cf: "GreaterThan", cfVal: "10") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].CfChanged.Should().BeTrue();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_CfValue2OnlyChanged_CfChangedTrue()
    {
        var old  = new[] { Q("What is risk?", dv: "WholeNumber", cfType: "CellIs", cf: "Between", cfVal: "1", cfVal2: "10") };
        var newQ = new[] { Q("What is risk?", dv: "WholeNumber", cfType: "CellIs", cf: "Between", cfVal: "1", cfVal2: "20") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].CfChanged.Should().BeTrue();
        result.Unchanged.Should().BeEmpty();
    }

    // ── SecondBestSimilarity ──────────────────────────────────────────────────

    [Fact]
    public void Diff_OneOldQuestion_SecondBestSimilarityIsNull()
    {
        // Only one old question → no second candidate exists
        var old  = new[] { Q("What is risk?") };
        var newQ = new[] { Q("What is risk?") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Unchanged.Should().HaveCount(1);
        result.Unchanged[0].SecondBestSimilarity.Should().BeNull();
    }

    [Fact]
    public void Diff_TwoOldQuestions_SecondBestSimilarityIsPopulated()
    {
        // Two old questions: one very similar, one very different.
        // The matched pair must record the score of the non-chosen old question as SecondBestSimilarity.
        var oldClose = Q("What is risk?");
        var oldFar   = Q("zyxwv utsrq mnopq");    // nothing in common

        var newQ = new[] { Q("What is risk?") };  // identical to oldClose

        var result = QuestionDiffEngine.Diff([oldClose, oldFar], newQ);

        result.Unchanged.Should().HaveCount(1, because: "identical text match → Unchanged");
        result.Unchanged[0].SecondBestSimilarity.Should().NotBeNull(
            because: "two old questions exist, so a second-best score must be recorded");
        result.Unchanged[0].SecondBestSimilarity.Should().BeLessThan(1.0,
            because: "second-best candidate is a dissimilar question");
    }

    // ── Optimal vs greedy assignment ──────────────────────────────────────────

    [Fact]
    public void Diff_OptimalAssignmentDiffersFromGreedy_CorrectPairingProduced()
    {
        var oldQ0 = Q("abcde fghij");           // base text
        var oldQ1 = Q("zyxwv utsrq");           // completely different

        // newQ1 appends " p" (2 chars): dist=2, max=13 → score ≈ 0.85 vs oldQ0
        var newQ0 = Q("abcde fghij klmno");     // dist=6, max=17 → score ≈ 0.65 vs oldQ0
        var newQ1 = Q("abcde fghij p");

        var result = QuestionDiffEngine.Diff([oldQ0, oldQ1], [newQ0, newQ1]);

        // Optimal: newQ1 is the one matched to oldQ0 (not newQ0 as greedy would choose)
        result.Changed.Should().HaveCount(1);
        result.Changed[0].NewQuestion.QuestionText.Should().Be("abcde fghij p",
            because: "optimal assigns the closer new question (newQ1) to oldQ0");
        result.Changed[0].OldQuestion.QuestionText.Should().Be("abcde fghij");

        result.Added.Should().HaveCount(1);
        result.Added[0].Question.QuestionText.Should().Be("abcde fghij klmno",
            because: "newQ0 has no acceptable match under the optimal assignment");

        result.Removed.Should().HaveCount(1);
        result.Removed[0].Question.QuestionText.Should().Be("zyxwv utsrq");
    }

    // ── Contextual match bonuses ──────────────────────────────────────────────

    [Fact]
    public void Diff_SectionBonus_ResolvesTieInFavourOfSameSection()
    {
        var oldQ0 = Qs("alpha beta gamma", "SectionX");
        var oldQ1 = Qs("alpha beta gamma", "SectionY");
        var newQ0 = Qs("alpha beta delta", "SectionX");
        var newQ1 = Qs("alpha beta delta", "SectionY");

        var result = QuestionDiffEngine.Diff([oldQ0, oldQ1], [newQ0, newQ1]);

        result.Changed.Should().HaveCount(2);
        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
        result.Changed.Should().Contain(c =>
            c.NewQuestion.SectionName == "SectionX" && c.OldQuestion.SectionName == "SectionX");
        result.Changed.Should().Contain(c =>
            c.NewQuestion.SectionName == "SectionY" && c.OldQuestion.SectionName == "SectionY");
    }

    [Fact]
    public void Diff_BothBonuses_Stack_CappedAt1_MatchedWithBaseScore()
    {
        var old  = new[] { Qs("What is the risk level for this control?", "SectionA", "1.1") };
        var newQ = new[] { Qs("What is the risk level for this process?",  "SectionA", "1.1") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
        result.Changed.Should().HaveCount(1,
            because: "base score is 0.825 < 1.0 so textChanged=true → Changed");
        result.Changed[0].TextChanged.Should().BeTrue();
        result.Changed[0].SimilarityScore.Should().BeApproximately(0.825, 0.005,
            because: "SimilarityScore is the base text similarity, not the bonus-adjusted score");
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_EmptySectionName_NoSectionBonus()
    {
        var oldEmpty = new[]
        {
            new RiskLevelQuestion("",
                "What is the risk level for this control?",
                "", "1.1", 1, null, null, null)
        };
        var newEmpty = new[]
        {
            new RiskLevelQuestion("",
                "What is the risk level for this process?",
                "", "1.1", 1, null, null, null)
        };
        var oldNamed = new[] { Qs("What is the risk level for this control?", "SectionA", "1.1") };
        var newNamed = new[] { Qs("What is the risk level for this process?",  "SectionA", "1.1") };

        var resultEmpty = QuestionDiffEngine.Diff(oldEmpty, newEmpty);
        var resultNamed = QuestionDiffEngine.Diff(oldNamed, newNamed);

        resultEmpty.Changed.Should().HaveCount(1,
            because: "no section bonus on empty names; only number bonus → adjusted 0.925 < 1.0");
        resultNamed.Changed.Should().HaveCount(1,
            because: "base 0.825 < 1.0 → textChanged=true → Changed regardless of bonus count");
    }

    [Fact]
    public void Diff_NullQuestionNumber_NoNumberBonus()
    {
        var oldNull = new[] { Qs("What is the risk level for this control?", "SectionA") };
        var newNull = new[] { Qs("What is the risk level for this process?",  "SectionA") };
        var oldNum  = new[] { Qs("What is the risk level for this control?", "SectionA", "1.1") };
        var newNum  = new[] { Qs("What is the risk level for this process?",  "SectionA", "1.1") };

        var resultNull = QuestionDiffEngine.Diff(oldNull, newNull);
        var resultNum  = QuestionDiffEngine.Diff(oldNum,  newNum);

        resultNull.Changed.Should().HaveCount(1,
            because: "no number bonus on null numbers; only section bonus → adjusted 0.925 < 1.0");
        resultNum.Changed.Should().HaveCount(1,
            because: "base 0.825 < 1.0 → textChanged=true → Changed regardless of bonus count");
    }

    // ── Base vs adjusted separation regression tests ──────────────────────────

    [Fact]
    public void Diff_TextChanged_BothBonuses_ReportedScoreIsBaseNotAdjusted()
    {
        var old  = new[] { Qs("What is the risk level for this control?", "SectionA", "1.1") };
        var newQ = new[] { Qs("What is the risk level for this process?",  "SectionA", "1.1") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].TextChanged.Should().BeTrue();
        result.Changed[0].SimilarityScore.Should().BeApproximately(0.825, 0.005);
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_IdenticalText_BothBonuses_IsUnchanged()
    {
        var old  = new[] { Qs("What is risk?", "SectionA", "1.1") };
        var newQ = new[] { Qs("What is risk?", "SectionA", "1.1") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Unchanged.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_BonusPushesSubThresholdPairAboveThreshold_MatchedWithBaseScore()
    {
        var old  = new[] { Qs("abcdefghij", "SectionA", "1.1") };
        var newQ = new[] { Qs("123456ghij", "SectionA", "1.1") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1,
            because: "adjusted score 0.60 ≥ 0.50 so the pair is matched despite base < threshold");
        result.Changed[0].TextChanged.Should().BeTrue();
        result.Changed[0].SimilarityScore.Should().BeApproximately(0.40, 0.005,
            because: "SimilarityScore is the base score, not the bonus-adjusted score");
        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_NoBonusSubThresholdPair_NotMatched_AddedAndRemoved()
    {
        var old  = new[] { Qs("abcdefghij", "SectionX") };
        var newQ = new[] { Qs("123456ghij", "SectionY") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Removed.Should().HaveCount(1);
        result.Added.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_SecondBestSimilarity_IsBaseNotAdjusted()
    {
        var newQ  = new[] { Qs("abcde", "SectionA", "1.1") };
        var oldQ0 = Q("abcde", qn: "1.1");
        var oldQ1 = new RiskLevelQuestion("SectionA", "fghij", "", "1.1", 2, null, null, null);

        var result = QuestionDiffEngine.Diff([oldQ0, oldQ1], newQ);

        result.Unchanged.Should().HaveCount(1, because: "newQ matches oldQ0 identically");
        result.Unchanged[0].SecondBestSimilarity.Should().Be(0.0,
            because: "SecondBestSimilarity is the base score (0.0), not the adjusted score (0.20)");
    }

    // ── ExplanationChanged flag ───────────────────────────────────────────────

    [Fact]
    public void Diff_ExplanationUnchanged_ExplanationChangedFalse()
    {
        var old = new[] { Q("What is risk?", explanation: "Please explain.") };
        var newQ = new[] { Q("What is risk?", explanation: "Please explain.") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Unchanged.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_ExplanationChanged_ExplanationChangedTrue()
    {
        var old = new[] { Q("What is risk?", explanation: "Old explanation.") };
        var newQ = new[] { Q("What is risk?", explanation: "New explanation text.") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].ExplanationChanged.Should().BeTrue();
        result.Changed[0].TextChanged.Should().BeFalse();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_TextAndExplanationBothChanged_BothFlagsTrue()
    {
        var old = new[] { Q("What is the risk level?", explanation: "Old explanation.") };
        var newQ = new[] { Q("What is the risk rating?", explanation: "New explanation text.") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].TextChanged.Should().BeTrue();
        result.Changed[0].ExplanationChanged.Should().BeTrue();
    }

    [Fact]
    public void Diff_ExplanationChangedOnly_DoesNotAffectMatchingDecision()
    {
        // Two questions with identical text but different explanations must still match
        // (explanation is not part of the text-similarity matrix for matching).
        var old = new[] { Q("What is risk?", explanation: "Old explanation.") };
        var newQ = new[] { Q("What is risk?", explanation: "Completely different explanation text here.") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1,
            because: "explanation changed → ExplanationChanged=true → Changed");
        result.Changed[0].ExplanationChanged.Should().BeTrue();
        result.Changed[0].TextChanged.Should().BeFalse();
        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
    }
}
