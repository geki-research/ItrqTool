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
        IReadOnlyList<GdSectionSpec>? sections = null,
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
            Sections = sections ?? [new GdSectionSpec(3, 4, 9, "G-CO", false)],
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
    public void Validate_EmptySections_ReturnsError()
    {
        var errors = MakeConfig(sections: []).Validate();
        errors.Should().ContainSingle()
            .Which.Should().Be("Sections must not be empty.");
    }

    [Fact]
    public void Validate_FirstDataRowNotAfterHeaderRow_ReturnsError()
    {
        // firstDataRow == headerRow violates the (ported LayoutParser) "first > header" invariant.
        var errors = MakeConfig(sections: [new GdSectionSpec(4, 4, 9, "G-CO", false)]).Validate();
        errors.Should().ContainSingle()
            .Which.Should().Contain("firstDataRow (4) must be greater than headerRow (4)");
    }

    [Fact]
    public void Validate_LastDataRowBeforeFirstDataRow_ReturnsError()
    {
        var errors = MakeConfig(sections: [new GdSectionSpec(3, 8, 4, "G-CO", false)]).Validate();
        errors.Should().ContainSingle()
            .Which.Should().Contain("lastDataRow (4) must not be less than firstDataRow (8)");
    }

    [Fact]
    public void Validate_BlankExpectedName_ReturnsError()
    {
        var errors = MakeConfig(sections: [new GdSectionSpec(3, 4, 9, "   ", false)]).Validate();
        errors.Should().ContainSingle()
            .Which.Should().Contain("expectedName must not be empty");
    }

    [Fact]
    public void Validate_DuplicateHeaderRows_ReturnsError()
    {
        var errors = MakeConfig(sections:
        [
            new GdSectionSpec(3, 4, 9, "G-CO", false),
            new GdSectionSpec(3, 10, 12, "G-ST", true),
        ]).Validate();
        errors.Should().Contain("Section header rows must be distinct.");
    }

    [Fact]
    public void Validate_MultipleValidSections_ReturnsNoErrors()
    {
        MakeConfig(sections:
        [
            new GdSectionSpec(3, 4, 9, "G-CO", false),
            new GdSectionSpec(10, 11, 43, "G-ST", true),
        ]).Validate().Should().BeEmpty();
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
