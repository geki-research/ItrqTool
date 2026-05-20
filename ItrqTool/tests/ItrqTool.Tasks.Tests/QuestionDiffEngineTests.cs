using FluentAssertions;
using Xunit;
using ItrqTool.Tasks.TemplateDiff;

namespace ItrqTool.Tasks.Tests;

public sealed class QuestionDiffEngineTests
{
    private static AuditQuestion Q(string text, string? dv = null, string? cf = null, int row = 1, string? qn = null, string? dvf = null)
        => new("Chapter", "Section", AuditQuestion.StripPrefix(text), text, qn, row, dv, dvf, cf);

    // Helper that accepts an explicit section name (for bonus tests).
    private static AuditQuestion Qs(string text, string section, string? qn = null)
        => new("Chapter", section, AuditQuestion.StripPrefix(text), text, qn, 1, null, null, null);

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

    [Fact]
    public void Diff_PrefixDoesNotAffectMatching()
    {
        // Q() helper leaves QuestionNumber=null, so numbers are equal (null==null)
        // → text-identical match lands in Unchanged, not Changed
        var old = new[] { Q("1.1) What is risk?") };
        var newQ = new[] { Q("2.1) What is risk?") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
        result.Changed.Should().BeEmpty();
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
        result.Changed[0].NumberChanged.Should().BeFalse();
        result.Changed[0].DvChanged.Should().BeFalse();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_ListDv_SameList_CfDiffers_IsUnchanged()
    {
        // CF on dropdown cells is presentational noise — ignored when DvType is List
        var old = new[] { Q("What is risk?", dv: "List", dvf: "\"Yes,No\"", cf: "Equal") };
        var newQ = new[] { Q("What is risk?", dv: "List", dvf: "\"Yes,No\"", cf: "NotEqual") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Unchanged.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
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
        // Similarity matrix (approximate values using real Levenshtein scoring):
        //
        //                 oldQ0               oldQ1
        //   newQ0:  [  ~0.65 (moderate),   ~0.0 (none)  ]
        //   newQ1:  [  ~0.85 (strong),     ~0.0 (none)  ]
        //
        // Greedy (processes in order) assigns newQ0 → oldQ0 first (0.65 ≥ 0.5),
        // leaving newQ1 with only oldQ1 (score ≈ 0 < 0.5) → Added(newQ1).
        //
        // Optimal (Hungarian): give oldQ0 to newQ1 (0.85 > 0.65 profit),
        // newQ0 → oldQ1 (score ≈ 0 < threshold) → Added(newQ0).
        //
        // Total profit: greedy = 0.65; optimal = 0.85. Hungarian wins.

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

    // ── Prefix extraction helpers ─────────────────────────────────────────────

    [Fact]
    public void ExtractNumber_WithPrefix_ReturnsNumber()
    {
        AuditQuestion.ExtractNumber("3.5) Some text").Should().Be("3.5");
        AuditQuestion.ExtractNumber("1.1) What is risk?").Should().Be("1.1");
        AuditQuestion.ExtractNumber("10.3) Multi-digit").Should().Be("10.3");
    }

    [Fact]
    public void ExtractNumber_NoPrefix_ReturnsNull()
    {
        AuditQuestion.ExtractNumber("Some text").Should().BeNull();
        AuditQuestion.ExtractNumber("  No prefix here  ").Should().BeNull();
    }

    [Fact]
    public void StripPrefix_RemovesNumberPrefix()
    {
        AuditQuestion.StripPrefix("1.2) What is risk?").Should().Be("What is risk?");
        AuditQuestion.StripPrefix("10.3) Multi-digit prefix").Should().Be("Multi-digit prefix");
    }

    [Fact]
    public void StripPrefix_NoPrefix_ReturnsTrimmedText()
    {
        AuditQuestion.StripPrefix("  No prefix here  ").Should().Be("No prefix here");
        AuditQuestion.StripPrefix("What is risk?").Should().Be("What is risk?");
    }

    // ── Contextual match bonuses ──────────────────────────────────────────────

    [Fact]
    public void Diff_SectionBonus_ResolvesTieInFavourOfSameSection()
    {
        // 2×2: all four pairs have equal base text similarity (same texts used).
        // Without bonus the matrix is a four-way tie; Hungarian may assign either way.
        // With +0.10 on same-section pairs, Option A (X→X, Y→Y) scores 0.85+0.85=1.70
        // vs Option B (X→Y, Y→X) scores 0.75+0.75=1.50 — Hungarian picks Option A.
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
    public void Diff_BothBonuses_Stack_CappedAt1()
    {
        // "...control?" vs "...process?" → base = 33/40 = 0.825.
        // Same section + same number → +0.10 + +0.10 = +0.20.
        // Adjusted = min(1.0, 0.825 + 0.20) = 1.0 — capped, not 1.025.
        // textChanged = score < 1.0 = false → Unchanged despite texts differing.
        var old  = new[] { Qs("What is the risk level for this control?", "SectionA", "1.1") };
        var newQ = new[] { Qs("What is the risk level for this process?",  "SectionA", "1.1") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
        result.Unchanged.Should().HaveCount(1,
            because: "both bonuses applied; adjusted score capped at 1.0 so textChanged=false");
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_EmptySectionName_NoSectionBonus()
    {
        // Both questions have empty SectionName: the section bonus must NOT fire.
        // Only the number bonus applies (+0.10); adjusted = 0.825 + 0.10 = 0.925 < 1.0
        // → textChanged=true → Changed.
        // With a named section both bonuses fire (+0.20); score caps at 1.0 → Unchanged.
        var oldEmpty = new[]
        {
            new AuditQuestion("Ch", "",
                "What is the risk level for this control?",
                "What is the risk level for this control?",
                "1.1", 1, null, null, null)
        };
        var newEmpty = new[]
        {
            new AuditQuestion("Ch", "",
                "What is the risk level for this process?",
                "What is the risk level for this process?",
                "1.1", 1, null, null, null)
        };
        var oldNamed = new[] { Qs("What is the risk level for this control?", "SectionA", "1.1") };
        var newNamed = new[] { Qs("What is the risk level for this process?",  "SectionA", "1.1") };

        var resultEmpty = QuestionDiffEngine.Diff(oldEmpty, newEmpty);
        var resultNamed = QuestionDiffEngine.Diff(oldNamed, newNamed);

        resultEmpty.Changed.Should().HaveCount(1,
            because: "no section bonus on empty names; score stays below 1.0");
        resultNamed.Unchanged.Should().HaveCount(1,
            because: "both bonuses fire on named section; score caps at 1.0");
    }

    [Fact]
    public void Diff_NullQuestionNumber_NoNumberBonus()
    {
        // Both questions have null QuestionNumber: the number bonus must NOT fire.
        // Only the section bonus applies (+0.10); adjusted = 0.825 + 0.10 = 0.925 < 1.0
        // → textChanged=true → Changed.
        // With a real shared number both bonuses fire (+0.20); score caps at 1.0 → Unchanged.
        var oldNull = new[] { Qs("What is the risk level for this control?", "SectionA") };
        var newNull = new[] { Qs("What is the risk level for this process?",  "SectionA") };
        var oldNum  = new[] { Qs("What is the risk level for this control?", "SectionA", "1.1") };
        var newNum  = new[] { Qs("What is the risk level for this process?",  "SectionA", "1.1") };

        var resultNull = QuestionDiffEngine.Diff(oldNull, newNull);
        var resultNum  = QuestionDiffEngine.Diff(oldNum,  newNum);

        resultNull.Changed.Should().HaveCount(1,
            because: "no number bonus on null numbers; score stays below 1.0");
        resultNum.Unchanged.Should().HaveCount(1,
            because: "both bonuses fire with matching numbers; score caps at 1.0");
    }
}
