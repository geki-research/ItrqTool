using FluentAssertions;
using ItrqTool.Tasks.ControlLevelQuestionValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using Xunit;

namespace ItrqTool.Tasks.Tests.ControlLevelQuestionValidationV02;

public sealed class ClqV02QuestionTests
{
    [Fact]
    public void ClqV02Question_IsAssignableToIAlignmentIdentity()
    {
        var q = MakeQuestion();
        q.Should().BeAssignableTo<IAlignmentIdentity>();
    }

    [Fact]
    public void ClqV02Question_IdentityMembers_RoundTrip()
    {
        var q = MakeQuestion();

        ((IAlignmentIdentity)q).RowNumber.Should().Be(42);
        ((IAlignmentIdentity)q).XrefId.Should().Be("XREF-001");
        ((IAlignmentIdentity)q).OriginalText.Should().Be("1.1) Original text");
        ((IAlignmentIdentity)q).QuestionText.Should().Be("Original text");
        ((IAlignmentIdentity)q).SectionName.Should().Be("Section A");
        ((IAlignmentIdentity)q).QuestionNumber.Should().Be("1.1");
    }

    [Fact]
    public void ClqV02Question_AnswerStability_RoundTrip()
    {
        var q = MakeQuestion();
        q.AnswerStability.Should().Be("Yes");
    }

    [Fact]
    public void ClqV02Question_V02DvFields_RoundTrip()
    {
        var q = MakeQuestion();
        q.AnswerStabilityDvType.Should().Be("List");
        q.AnswerStabilityDvFormula.Should().Be("Yes,No");
        q.AnswerStabilityDvOperator.Should().BeNull();
        q.AnswerStabilityDvFormula2.Should().BeNull();
    }

    [Fact]
    public void ClqV02Question_WithExpression_UpdatesField()
    {
        var q = MakeQuestion();
        var updated = q with { AnswerStability = "No", AnswerStabilityDvType = "List2" };
        updated.AnswerStability.Should().Be("No");
        updated.AnswerStabilityDvType.Should().Be("List2");
        updated.RowNumber.Should().Be(q.RowNumber);
    }

    private static ClqV02Question MakeQuestion() => new(
        RowNumber: 42,
        XrefId: "XREF-001",
        QuestionNumber: "1.1",
        QuestionText: "Original text",
        OriginalText: "1.1) Original text",
        ChapterName: "Chapter 1",
        SectionName: "Section A",
        Guidance: "Some guidance",
        PreviousAnswer: "3",
        Answer: "2",
        Strengths: "Good",
        Weaknesses: null,
        ProvidedBy: "Unit A",
        AnswerDvType: "List",
        AnswerDvFormula: "1,2,3,4",
        AnswerDvOperator: null,
        AnswerDvFormula2: null,
        NumberFormatUnrecognized: false,
        AnswerStability: "Yes",
        AnswerStabilityDvType: "List",
        AnswerStabilityDvFormula: "Yes,No",
        AnswerStabilityDvOperator: null,
        AnswerStabilityDvFormula2: null
    );
}
