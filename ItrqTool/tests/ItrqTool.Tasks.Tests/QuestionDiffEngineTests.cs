using FluentAssertions;
using Xunit;
using ItrqTool.Tasks.TemplateDiff;

namespace ItrqTool.Tasks.Tests;

public sealed class QuestionDiffEngineTests
{
    private static AuditQuestion Q(string text, string? dv = null, string? cf = null, int row = 1, string? qn = null, string? dvf = null)
        => new("Chapter", "Section", AuditQuestion.StripPrefix(text), text, qn, row, dv, dvf, cf);

    [Fact]
    public void Diff_AllIdentical_NoChanges()
    {
        var questions = new[] { Q("What is risk?"), Q("Describe controls.") };
        var result = QuestionDiffEngine.Diff(questions, questions);

        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
        result.Changed.Should().BeEmpty();
        result.ValidationChanges.Should().BeEmpty();
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
    public void Diff_SameTextDvTypeChanged_AppearsInValidationChanges_NotChanged()
    {
        var old = new[] { Q("What is risk?", dv: "List") };
        var newQ = new[] { Q("What is risk?", dv: "WholeNumber") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.ValidationChanges.Should().HaveCount(1);
        result.ValidationChanges[0].OldDvType.Should().Be("List");
        result.ValidationChanges[0].NewDvType.Should().Be("WholeNumber");
        result.Changed.Should().BeEmpty();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_TextAndDvTypeChanged_AppearsInBothChangedAndValidationChanges()
    {
        var old = new[] { Q("What is the risk level?", dv: "List") };
        var newQ = new[] { Q("What is the risk rating?", dv: "WholeNumber") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.ValidationChanges.Should().HaveCount(1);
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
    public void Diff_IdenticalTextSameNumberNoValidationChange_AppearsInUnchanged()
    {
        var old = new[] { Q("What is risk?", qn: "1.1") };
        var newQ = new[] { Q("What is risk?", qn: "1.1") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Unchanged.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
        result.ValidationChanges.Should().BeEmpty();
    }

    [Fact]
    public void Diff_IdenticalTextSameNumberDvTypeDiffers_NotInUnchanged_InValidationChanges()
    {
        var old = new[] { Q("What is risk?", dv: "List",        qn: "1.1") };
        var newQ = new[] { Q("What is risk?", dv: "WholeNumber", qn: "1.1") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Unchanged.Should().BeEmpty();
        result.Changed.Should().BeEmpty();
        result.ValidationChanges.Should().HaveCount(1);
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
        result.ValidationChanges.Should().BeEmpty();
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

    // ── IsDvChanged / List comparison ────────────────────────────────────────

    [Fact]
    public void Diff_ListDvSameInlineFormula_NoValidationChange_InUnchanged()
    {
        var old = new[] { Q("What is risk?", dv: "List", dvf: "\"Yes,No,N/A\"") };
        var newQ = new[] { Q("What is risk?", dv: "List", dvf: "\"Yes,No,N/A\"") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.ValidationChanges.Should().BeEmpty();
        result.Unchanged.Should().HaveCount(1);
    }

    [Fact]
    public void Diff_ListDvDifferentInlineItems_ValidationChangeEmitted()
    {
        var old = new[] { Q("What is risk?", dv: "List", dvf: "\"Yes,No\"") };
        var newQ = new[] { Q("What is risk?", dv: "List", dvf: "\"Yes,No,N/A\"") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.ValidationChanges.Should().HaveCount(1);
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_ListDvSameItemsDifferentOrder_NoValidationChange()
    {
        var old = new[] { Q("What is risk?", dv: "List", dvf: "\"Yes,No\"") };
        var newQ = new[] { Q("What is risk?", dv: "List", dvf: "\"No,Yes\"") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.ValidationChanges.Should().BeEmpty();
        result.Unchanged.Should().HaveCount(1);
    }

    [Fact]
    public void Diff_ListDvOneInlineOneRangeRef_ValidationChangeEmitted()
    {
        var old = new[] { Q("What is risk?", dv: "List", dvf: "\"Yes,No\"") };
        var newQ = new[] { Q("What is risk?", dv: "List", dvf: "Sheet1!$A$1:$A$2") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.ValidationChanges.Should().HaveCount(1);
    }

    [Fact]
    public void Diff_ListDvSameRangeRef_NoValidationChange()
    {
        var old = new[] { Q("What is risk?", dv: "List", dvf: "Sheet1!$A$1:$A$5") };
        var newQ = new[] { Q("What is risk?", dv: "List", dvf: "Sheet1!$A$1:$A$5") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.ValidationChanges.Should().BeEmpty();
        result.Unchanged.Should().HaveCount(1);
    }

    [Fact]
    public void Diff_NullDvNullCf_IdenticalTextSameNumber_Unchanged()
    {
        var old = new[] { Q("What is risk?", qn: "1.1") };
        var newQ = new[] { Q("What is risk?", qn: "1.1") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.Unchanged.Should().HaveCount(1);
        result.ValidationChanges.Should().BeEmpty();
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_SameDvTypeNonList_NoValidationChange()
    {
        var old = new[] { Q("What is risk?", dv: "WholeNumber") };
        var newQ = new[] { Q("What is risk?", dv: "WholeNumber") };

        var result = QuestionDiffEngine.Diff(old, newQ);

        result.ValidationChanges.Should().BeEmpty();
        result.Unchanged.Should().HaveCount(1);
    }

    // ── Prefix extraction helpers ─────────────────────────────────────────────

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
}
