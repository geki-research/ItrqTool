using FluentAssertions;
using ItrqTool.Tasks.GeneralDataValidationV01;
using Xunit;

namespace ItrqTool.Tasks.Tests.GeneralDataValidationV01;

public sealed class GdV01ConfigTests
{
    private static GdV01Config MakeConfig(
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
        string sheetName = "General Data",
        IReadOnlyList<string>? sectionRows = null,
        double deviationThreshold = 0.25)
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
            SectionRows = sectionRows ?? ["3:4-9"],
            DeviationThreshold = deviationThreshold,
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
    public void Validate_NonLetterColumn_ReturnsError()
    {
        var errors = MakeConfig(answer: "H1").Validate();
        errors.Should().ContainSingle()
            .Which.Should().Be("AnswerColumn must be a valid column letter (e.g. 'A', 'AA').");
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

    [Fact]
    public void Validate_NegativeDeviationThreshold_ReturnsError()
    {
        var errors = MakeConfig(deviationThreshold: -0.1).Validate();
        errors.Should().ContainSingle()
            .Which.Should().Be("DeviationThreshold must be >= 0.");
    }

    [Fact]
    public void Validate_ZeroDeviationThreshold_IsAllowed()
    {
        MakeConfig(deviationThreshold: 0).Validate().Should().BeEmpty();
    }
}
