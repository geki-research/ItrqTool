using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.ControlLevelQuestionValidation;
using Xunit;

namespace ItrqTool.Tasks.Tests.ControlLevelQuestionValidation;

public sealed class ControlLevelQuestionValidationV01ConfigTests
{
    private static ControlLevelQuestionValidationV01Config ValidConfig() => new()
    {
        TextColumn = "D",
        GuidanceColumn = "E",
        PreviousAnswerColumn = "F",
        AnswerColumn = "H",
        StrengthsColumn = "I",
        WeaknessesColumn = "J",
        ProvidedByColumn = "M",
        XrefIdColumn = "N",
        SheetName = "Control Level Questions",
        AllowedAnswers = ["1", "2", "3", "4"],
        DeviationThreshold = 2
    };

    [Fact]
    public void Validate_AllValid_ReturnsNoErrors()
    {
        ValidConfig().Validate().Should().BeEmpty();
    }

    [Fact]
    public void Validate_BlankTextColumn_ReturnsError()
    {
        var config = new ControlLevelQuestionValidationV01Config
        {
            TextColumn = "",
            GuidanceColumn = "E", PreviousAnswerColumn = "F", AnswerColumn = "H",
            StrengthsColumn = "I", WeaknessesColumn = "J", ProvidedByColumn = "M", XrefIdColumn = "N",
            SheetName = "CLQ", AllowedAnswers = ["1"], DeviationThreshold = 1
        };
        var errors = config.Validate();
        errors.Should().ContainSingle(e => e.Contains("TextColumn"));
    }

    [Fact]
    public void Validate_BlankAnswerColumn_ReturnsError()
    {
        var config = new ControlLevelQuestionValidationV01Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F",
            AnswerColumn = "",
            StrengthsColumn = "I", WeaknessesColumn = "J", ProvidedByColumn = "M", XrefIdColumn = "N",
            SheetName = "CLQ", AllowedAnswers = ["1"], DeviationThreshold = 1
        };
        var errors = config.Validate();
        errors.Should().ContainSingle(e => e.Contains("AnswerColumn"));
    }

    [Fact]
    public void Validate_BlankSheetName_ReturnsError()
    {
        var config = new ControlLevelQuestionValidationV01Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F", AnswerColumn = "H",
            StrengthsColumn = "I", WeaknessesColumn = "J", ProvidedByColumn = "M", XrefIdColumn = "N",
            SheetName = "",
            AllowedAnswers = ["1"], DeviationThreshold = 1
        };
        var errors = config.Validate();
        errors.Should().ContainSingle(e => e.Contains("SheetName"));
    }

    [Fact]
    public void Validate_EmptyAllowedAnswers_ReturnsError()
    {
        var config = new ControlLevelQuestionValidationV01Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F", AnswerColumn = "H",
            StrengthsColumn = "I", WeaknessesColumn = "J", ProvidedByColumn = "M", XrefIdColumn = "N",
            SheetName = "CLQ", AllowedAnswers = [],
            DeviationThreshold = 1
        };
        var errors = config.Validate();
        errors.Should().ContainSingle(e => e.Contains("AllowedAnswers"));
    }

    [Fact]
    public void Validate_DeviationThresholdZero_ReturnsError()
    {
        var config = new ControlLevelQuestionValidationV01Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F", AnswerColumn = "H",
            StrengthsColumn = "I", WeaknessesColumn = "J", ProvidedByColumn = "M", XrefIdColumn = "N",
            SheetName = "CLQ", AllowedAnswers = ["1"], DeviationThreshold = 0
        };
        var errors = config.Validate();
        errors.Should().ContainSingle(e => e.Contains("DeviationThreshold"));
    }

    [Fact]
    public void Validate_DeviationThresholdNegative_ReturnsError()
    {
        var config = new ControlLevelQuestionValidationV01Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F", AnswerColumn = "H",
            StrengthsColumn = "I", WeaknessesColumn = "J", ProvidedByColumn = "M", XrefIdColumn = "N",
            SheetName = "CLQ", AllowedAnswers = ["1"], DeviationThreshold = -5
        };
        var errors = config.Validate();
        errors.Should().ContainSingle(e => e.Contains("DeviationThreshold"));
    }

    [Fact]
    public void Validate_InvalidColumnToken_ReturnsError()
    {
        var config = new ControlLevelQuestionValidationV01Config
        {
            TextColumn = "D", GuidanceColumn = "D2",  // invalid — contains digit
            PreviousAnswerColumn = "F", AnswerColumn = "H",
            StrengthsColumn = "I", WeaknessesColumn = "J", ProvidedByColumn = "M", XrefIdColumn = "N",
            SheetName = "CLQ", AllowedAnswers = ["1"], DeviationThreshold = 1
        };
        var errors = config.Validate();
        errors.Should().ContainSingle(e => e.Contains("GuidanceColumn"));
    }

    [Fact]
    public void Validate_MultipleBlankColumns_AllErrorsCollected()
    {
        var config = new ControlLevelQuestionValidationV01Config
        {
            TextColumn = "",    // error
            GuidanceColumn = "E",
            PreviousAnswerColumn = "F",
            AnswerColumn = "",  // error
            StrengthsColumn = "I", WeaknessesColumn = "J", ProvidedByColumn = "M", XrefIdColumn = "N",
            SheetName = "",     // error
            AllowedAnswers = ["1"],
            DeviationThreshold = 0  // error
        };
        var errors = config.Validate();
        errors.Should().HaveCountGreaterThanOrEqualTo(4);
        errors.Should().Contain(e => e.Contains("TextColumn"));
        errors.Should().Contain(e => e.Contains("AnswerColumn"));
        errors.Should().Contain(e => e.Contains("SheetName"));
        errors.Should().Contain(e => e.Contains("DeviationThreshold"));
    }

    [Fact]
    public void ParsedSections_WellFormed_ReturnsCorrectDefinitions()
    {
        var config = new ControlLevelQuestionValidationV01Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F", AnswerColumn = "H",
            StrengthsColumn = "I", WeaknessesColumn = "J", ProvidedByColumn = "M", XrefIdColumn = "N",
            SheetName = "CLQ", AllowedAnswers = ["1"], DeviationThreshold = 1,
            SectionRows = ["5:6-10", "12:13-20"]
        };
        var sections = config.ParsedSections;
        sections.Should().HaveCount(2);
        sections[0].Should().Be(new SectionDefinition(5, 6, 10));
        sections[1].Should().Be(new SectionDefinition(12, 13, 20));
    }

    [Fact]
    public void ParsedSections_MalformedEntry_NoColon_ThrowsFormatException()
    {
        var config = new ControlLevelQuestionValidationV01Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F", AnswerColumn = "H",
            StrengthsColumn = "I", WeaknessesColumn = "J", ProvidedByColumn = "M", XrefIdColumn = "N",
            SheetName = "CLQ", AllowedAnswers = ["1"], DeviationThreshold = 1,
            SectionRows = ["badentry"]
        };
        var act = () => config.ParsedSections;
        act.Should().Throw<FormatException>().WithMessage("*<sectionRow>:<first>-<last>*");
    }

    [Fact]
    public void ParsedSections_MalformedEntry_FirstNotGreaterThanSection_ThrowsFormatException()
    {
        var config = new ControlLevelQuestionValidationV01Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F", AnswerColumn = "H",
            StrengthsColumn = "I", WeaknessesColumn = "J", ProvidedByColumn = "M", XrefIdColumn = "N",
            SheetName = "CLQ", AllowedAnswers = ["1"], DeviationThreshold = 1,
            SectionRows = ["5:5-10"]
        };
        var act = () => config.ParsedSections;
        act.Should().Throw<FormatException>().WithMessage("*firstQuestionRow*");
    }

    [Fact]
    public void ParsedSections_MalformedEntry_LastLessThanFirst_ThrowsFormatException()
    {
        var config = new ControlLevelQuestionValidationV01Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F", AnswerColumn = "H",
            StrengthsColumn = "I", WeaknessesColumn = "J", ProvidedByColumn = "M", XrefIdColumn = "N",
            SheetName = "CLQ", AllowedAnswers = ["1"], DeviationThreshold = 1,
            SectionRows = ["5:6-5"]
        };
        var act = () => config.ParsedSections;
        act.Should().Throw<FormatException>().WithMessage("*lastQuestionRow*");
    }

    [Fact]
    public void Validate_DoesNotWalkParsedSections_MalformedEntryDoesNotCauseError()
    {
        var config = new ControlLevelQuestionValidationV01Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F", AnswerColumn = "H",
            StrengthsColumn = "I", WeaknessesColumn = "J", ProvidedByColumn = "M", XrefIdColumn = "N",
            SheetName = "CLQ", AllowedAnswers = ["1"], DeviationThreshold = 1,
            SectionRows = ["badentry"]
        };
        // Validate() must NOT pre-walk ParsedSections; malformed entry must not cause an error here
        var act = () => config.Validate();
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_SeverityOverrides_OptionalDefaultsToEmpty()
    {
        ValidConfig().SeverityOverrides.Should().BeEmpty();
    }

    [Fact]
    public void Validate_SeverityOverrides_CanBeSet()
    {
        var config = new ControlLevelQuestionValidationV01Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F", AnswerColumn = "H",
            StrengthsColumn = "I", WeaknessesColumn = "J", ProvidedByColumn = "M", XrefIdColumn = "N",
            SheetName = "CLQ", AllowedAnswers = ["1"], DeviationThreshold = 1,
            SeverityOverrides = new Dictionary<string, FindingEvaluation>
            {
                ["check-id-1"] = FindingEvaluation.Warning
            }
        };
        config.Validate().Should().BeEmpty();
        config.SeverityOverrides.Should().ContainKey("check-id-1")
            .WhoseValue.Should().Be(FindingEvaluation.Warning);
    }
}
