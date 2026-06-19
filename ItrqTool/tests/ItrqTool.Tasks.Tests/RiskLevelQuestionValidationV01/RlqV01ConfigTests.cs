using FluentAssertions;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using Xunit;

namespace ItrqTool.Tasks.Tests.RiskLevelQuestionValidationV01;

public sealed class RlqV01ConfigTests
{
    private static RlqV01Config MakeConfig(
        string questionNumber = "C",
        string text = "D",
        string guidance = "E",
        string requestedType = "F",
        string previousAnswer = "G",
        string answer = "H",
        string requestedExplanation = "I",
        string previousExplanation = "J",
        string currentExplanation = "K",
        string materialChange = "L",
        string providedBy = "O",
        string xrefId = "Q",
        string sheetName = "IT Risk Level Questions",
        IReadOnlyList<string>? sectionRows = null)
        => new()
        {
            QuestionNumberColumn = questionNumber,
            TextColumn = text,
            GuidanceColumn = guidance,
            RequestedTypeColumn = requestedType,
            PreviousAnswerColumn = previousAnswer,
            AnswerColumn = answer,
            RequestedExplanationColumn = requestedExplanation,
            PreviousExplanationColumn = previousExplanation,
            CurrentExplanationColumn = currentExplanation,
            MaterialChangeColumn = materialChange,
            ProvidedByColumn = providedBy,
            XrefIdColumn = xrefId,
            SheetName = sheetName,
            SectionRows = sectionRows ?? ["2:3-20"],
        };

    [Fact]
    public void Validate_ValidConfig_ReturnsNoErrors()
    {
        MakeConfig().Validate().Should().BeEmpty();
    }

    [Fact]
    public void Validate_BlankColumnLetter_ReturnsError()
    {
        var errors = MakeConfig(text: "").Validate();
        errors.Should().ContainSingle()
            .Which.Should().Be("TextColumn must not be empty.");
    }

    [Fact]
    public void Validate_BlankSheetName_ReturnsError()
    {
        var errors = MakeConfig(sheetName: "").Validate();
        errors.Should().ContainSingle()
            .Which.Should().Be("SheetName must not be empty.");
    }

    [Fact]
    public void Validate_DuplicateColumnLetters_ReturnsError()
    {
        // Guidance reuses the text column letter D.
        var errors = MakeConfig(guidance: "D").Validate();
        errors.Should().Contain("Column letters must be distinct.");
    }

    [Fact]
    public void Validate_EmptySectionRows_ReturnsError()
    {
        var errors = MakeConfig(sectionRows: []).Validate();
        errors.Should().ContainSingle()
            .Which.Should().Be("SectionRows must not be empty.");
    }
}
