using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.ControlLevelQuestionValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Clq;
using Xunit;

namespace ItrqTool.Tasks.Tests.ControlLevelQuestionValidationV02;

public sealed class ControlLevelQuestionValidationV02ConfigTests
{
    private static ControlLevelQuestionValidationV02Config ValidConfig() => new()
    {
        TextColumn = "D",
        GuidanceColumn = "E",
        PreviousAnswerColumn = "F",
        AnswerColumn = "H",
        StrengthsColumn = "I",
        WeaknessesColumn = "J",
        ProvidedByColumn = "N",
        XrefIdColumn = "O",
        AnswerStabilityColumn = "K",
        SheetName = "Control Level Questions",
        AllowedAnswers = ["1", "2", "3", "4"],
        AllowedStabilityAnswers = ["Yes", "No"],
        DeviationThreshold = 2
    };

    // ── valid config ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_AllValid_ReturnsNoErrors()
    {
        ValidConfig().Validate().Should().BeEmpty();
    }

    // ── column letter blank ───────────────────────────────────────────────────

    [Fact]
    public void Validate_BlankTextColumn_ReturnsError()
    {
        var c = new ControlLevelQuestionValidationV02Config
        {
            TextColumn = "",
            GuidanceColumn = "E", PreviousAnswerColumn = "F", AnswerColumn = "H",
            StrengthsColumn = "I", WeaknessesColumn = "J", ProvidedByColumn = "N",
            XrefIdColumn = "O", AnswerStabilityColumn = "K",
            SheetName = "CLQ", AllowedAnswers = ["1"], AllowedStabilityAnswers = ["Yes"],
            DeviationThreshold = 1
        };
        c.Validate().Should().Contain(e => e.Contains("TextColumn"));
    }

    [Fact]
    public void Validate_BlankGuidanceColumn_ReturnsError()
    {
        var c = new ControlLevelQuestionValidationV02Config
        {
            TextColumn = "D", GuidanceColumn = "",
            PreviousAnswerColumn = "F", AnswerColumn = "H",
            StrengthsColumn = "I", WeaknessesColumn = "J", ProvidedByColumn = "N",
            XrefIdColumn = "O", AnswerStabilityColumn = "K",
            SheetName = "CLQ", AllowedAnswers = ["1"], AllowedStabilityAnswers = ["Yes"],
            DeviationThreshold = 1
        };
        c.Validate().Should().Contain(e => e.Contains("GuidanceColumn"));
    }

    [Fact]
    public void Validate_BlankPreviousAnswerColumn_ReturnsError()
    {
        var c = new ControlLevelQuestionValidationV02Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "",
            AnswerColumn = "H", StrengthsColumn = "I", WeaknessesColumn = "J",
            ProvidedByColumn = "N", XrefIdColumn = "O", AnswerStabilityColumn = "K",
            SheetName = "CLQ", AllowedAnswers = ["1"], AllowedStabilityAnswers = ["Yes"],
            DeviationThreshold = 1
        };
        c.Validate().Should().Contain(e => e.Contains("PreviousAnswerColumn"));
    }

    [Fact]
    public void Validate_BlankAnswerColumn_ReturnsError()
    {
        var c = new ControlLevelQuestionValidationV02Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F",
            AnswerColumn = "",
            StrengthsColumn = "I", WeaknessesColumn = "J", ProvidedByColumn = "N",
            XrefIdColumn = "O", AnswerStabilityColumn = "K",
            SheetName = "CLQ", AllowedAnswers = ["1"], AllowedStabilityAnswers = ["Yes"],
            DeviationThreshold = 1
        };
        c.Validate().Should().Contain(e => e.Contains("AnswerColumn"));
    }

    [Fact]
    public void Validate_BlankStrengthsColumn_ReturnsError()
    {
        var c = new ControlLevelQuestionValidationV02Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F",
            AnswerColumn = "H", StrengthsColumn = "",
            WeaknessesColumn = "J", ProvidedByColumn = "N",
            XrefIdColumn = "O", AnswerStabilityColumn = "K",
            SheetName = "CLQ", AllowedAnswers = ["1"], AllowedStabilityAnswers = ["Yes"],
            DeviationThreshold = 1
        };
        c.Validate().Should().Contain(e => e.Contains("StrengthsColumn"));
    }

    [Fact]
    public void Validate_BlankWeaknessesColumn_ReturnsError()
    {
        var c = new ControlLevelQuestionValidationV02Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F",
            AnswerColumn = "H", StrengthsColumn = "I", WeaknessesColumn = "",
            ProvidedByColumn = "N", XrefIdColumn = "O", AnswerStabilityColumn = "K",
            SheetName = "CLQ", AllowedAnswers = ["1"], AllowedStabilityAnswers = ["Yes"],
            DeviationThreshold = 1
        };
        c.Validate().Should().Contain(e => e.Contains("WeaknessesColumn"));
    }

    [Fact]
    public void Validate_BlankProvidedByColumn_ReturnsError()
    {
        var c = new ControlLevelQuestionValidationV02Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F",
            AnswerColumn = "H", StrengthsColumn = "I", WeaknessesColumn = "J",
            ProvidedByColumn = "",
            XrefIdColumn = "O", AnswerStabilityColumn = "K",
            SheetName = "CLQ", AllowedAnswers = ["1"], AllowedStabilityAnswers = ["Yes"],
            DeviationThreshold = 1
        };
        c.Validate().Should().Contain(e => e.Contains("ProvidedByColumn"));
    }

    [Fact]
    public void Validate_BlankXrefIdColumn_ReturnsError()
    {
        var c = new ControlLevelQuestionValidationV02Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F",
            AnswerColumn = "H", StrengthsColumn = "I", WeaknessesColumn = "J",
            ProvidedByColumn = "N", XrefIdColumn = "",
            AnswerStabilityColumn = "K",
            SheetName = "CLQ", AllowedAnswers = ["1"], AllowedStabilityAnswers = ["Yes"],
            DeviationThreshold = 1
        };
        c.Validate().Should().Contain(e => e.Contains("XrefIdColumn"));
    }

    [Fact]
    public void Validate_BlankAnswerStabilityColumn_ReturnsError()
    {
        var c = new ControlLevelQuestionValidationV02Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F",
            AnswerColumn = "H", StrengthsColumn = "I", WeaknessesColumn = "J",
            ProvidedByColumn = "N", XrefIdColumn = "O",
            AnswerStabilityColumn = "",
            SheetName = "CLQ", AllowedAnswers = ["1"], AllowedStabilityAnswers = ["Yes"],
            DeviationThreshold = 1
        };
        c.Validate().Should().Contain(e => e.Contains("AnswerStabilityColumn"));
    }

    // ── column letter non-alphabetic ──────────────────────────────────────────

    [Fact]
    public void Validate_NonAlphabeticTextColumn_ReturnsError()
    {
        var c = new ControlLevelQuestionValidationV02Config
        {
            TextColumn = "D2",   // invalid — contains digit
            GuidanceColumn = "E", PreviousAnswerColumn = "F", AnswerColumn = "H",
            StrengthsColumn = "I", WeaknessesColumn = "J", ProvidedByColumn = "N",
            XrefIdColumn = "O", AnswerStabilityColumn = "K",
            SheetName = "CLQ", AllowedAnswers = ["1"], AllowedStabilityAnswers = ["Yes"],
            DeviationThreshold = 1
        };
        c.Validate().Should().Contain(e => e.Contains("TextColumn"));
    }

    [Fact]
    public void Validate_NonAlphabeticAnswerStabilityColumn_ReturnsError()
    {
        var c = new ControlLevelQuestionValidationV02Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F",
            AnswerColumn = "H", StrengthsColumn = "I", WeaknessesColumn = "J",
            ProvidedByColumn = "N", XrefIdColumn = "O",
            AnswerStabilityColumn = "K1",  // invalid
            SheetName = "CLQ", AllowedAnswers = ["1"], AllowedStabilityAnswers = ["Yes"],
            DeviationThreshold = 1
        };
        c.Validate().Should().Contain(e => e.Contains("AnswerStabilityColumn"));
    }

    // ── structural fields ─────────────────────────────────────────────────────

    [Fact]
    public void Validate_BlankSheetName_ReturnsError()
    {
        var c = new ControlLevelQuestionValidationV02Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F",
            AnswerColumn = "H", StrengthsColumn = "I", WeaknessesColumn = "J",
            ProvidedByColumn = "N", XrefIdColumn = "O", AnswerStabilityColumn = "K",
            SheetName = "",
            AllowedAnswers = ["1"], AllowedStabilityAnswers = ["Yes"],
            DeviationThreshold = 1
        };
        c.Validate().Should().Contain(e => e.Contains("SheetName"));
    }

    // ── allowed-set fields ────────────────────────────────────────────────────

    [Fact]
    public void Validate_EmptyAllowedAnswers_ReturnsError()
    {
        var c = new ControlLevelQuestionValidationV02Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F",
            AnswerColumn = "H", StrengthsColumn = "I", WeaknessesColumn = "J",
            ProvidedByColumn = "N", XrefIdColumn = "O", AnswerStabilityColumn = "K",
            SheetName = "CLQ", AllowedAnswers = [],
            AllowedStabilityAnswers = ["Yes"], DeviationThreshold = 1
        };
        c.Validate().Should().Contain(e => e.Contains("AllowedAnswers"));
    }

    [Fact]
    public void Validate_EmptyAllowedStabilityAnswers_ReturnsError()
    {
        var c = new ControlLevelQuestionValidationV02Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F",
            AnswerColumn = "H", StrengthsColumn = "I", WeaknessesColumn = "J",
            ProvidedByColumn = "N", XrefIdColumn = "O", AnswerStabilityColumn = "K",
            SheetName = "CLQ", AllowedAnswers = ["1"],
            AllowedStabilityAnswers = [], DeviationThreshold = 1
        };
        c.Validate().Should().Contain(e => e.Contains("AllowedStabilityAnswers"));
    }

    // ── deviation threshold ───────────────────────────────────────────────────

    [Fact]
    public void Validate_DeviationThresholdZero_ReturnsError()
    {
        var c = new ControlLevelQuestionValidationV02Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F",
            AnswerColumn = "H", StrengthsColumn = "I", WeaknessesColumn = "J",
            ProvidedByColumn = "N", XrefIdColumn = "O", AnswerStabilityColumn = "K",
            SheetName = "CLQ", AllowedAnswers = ["1"], AllowedStabilityAnswers = ["Yes"],
            DeviationThreshold = 0
        };
        c.Validate().Should().Contain(e => e.Contains("DeviationThreshold"));
    }

    [Fact]
    public void Validate_DeviationThresholdNegative_ReturnsError()
    {
        var c = new ControlLevelQuestionValidationV02Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F",
            AnswerColumn = "H", StrengthsColumn = "I", WeaknessesColumn = "J",
            ProvidedByColumn = "N", XrefIdColumn = "O", AnswerStabilityColumn = "K",
            SheetName = "CLQ", AllowedAnswers = ["1"], AllowedStabilityAnswers = ["Yes"],
            DeviationThreshold = -3
        };
        c.Validate().Should().Contain(e => e.Contains("DeviationThreshold"));
    }

    // ── multi-error collection ────────────────────────────────────────────────

    [Fact]
    public void Validate_MultipleErrors_AllCollected()
    {
        var c = new ControlLevelQuestionValidationV02Config
        {
            TextColumn = "",         // error
            GuidanceColumn = "E", PreviousAnswerColumn = "F",
            AnswerColumn = "",       // error
            StrengthsColumn = "I", WeaknessesColumn = "J",
            ProvidedByColumn = "N", XrefIdColumn = "O",
            AnswerStabilityColumn = "",  // error
            SheetName = "",          // error
            AllowedAnswers = ["1"], AllowedStabilityAnswers = [],  // error
            DeviationThreshold = 0   // error
        };
        var errors = c.Validate();
        errors.Should().HaveCountGreaterThanOrEqualTo(6);
        errors.Should().Contain(e => e.Contains("TextColumn"));
        errors.Should().Contain(e => e.Contains("AnswerColumn"));
        errors.Should().Contain(e => e.Contains("AnswerStabilityColumn"));
        errors.Should().Contain(e => e.Contains("SheetName"));
        errors.Should().Contain(e => e.Contains("AllowedStabilityAnswers"));
        errors.Should().Contain(e => e.Contains("DeviationThreshold"));
    }

    // ── SeverityOverrides ─────────────────────────────────────────────────────

    [Fact]
    public void Validate_SeverityOverrides_DefaultsToEmpty()
    {
        ValidConfig().SeverityOverrides.Should().BeEmpty();
    }

    [Fact]
    public void Validate_SeverityOverrides_CanBeSet_DoesNotAffectValidate()
    {
        var c = new ControlLevelQuestionValidationV02Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F",
            AnswerColumn = "H", StrengthsColumn = "I", WeaknessesColumn = "J",
            ProvidedByColumn = "N", XrefIdColumn = "O", AnswerStabilityColumn = "K",
            SheetName = "CLQ", AllowedAnswers = ["1"], AllowedStabilityAnswers = ["Yes"],
            DeviationThreshold = 1,
            SeverityOverrides = new Dictionary<string, FindingEvaluation>
            {
                ["some-finding-id"] = FindingEvaluation.Warning
            }
        };
        c.Validate().Should().BeEmpty();
        c.SeverityOverrides.Should().ContainKey("some-finding-id")
            .WhoseValue.Should().Be(FindingEvaluation.Warning);
    }

    // ── IClqBaselineConfig conformance ────────────────────────────────────────

    [Fact]
    public void Config_IsAssignableToIClqBaselineConfig()
    {
        ValidConfig().Should().BeAssignableTo<IClqBaselineConfig>();
    }

    [Fact]
    public void Config_IClqBaselineConfigMembers_ReadBackCorrectly()
    {
        IClqBaselineConfig cfg = ValidConfig();

        cfg.TextColumn.Should().Be("D");
        cfg.GuidanceColumn.Should().Be("E");
        cfg.PreviousAnswerColumn.Should().Be("F");
        cfg.AnswerColumn.Should().Be("H");
        cfg.StrengthsColumn.Should().Be("I");
        cfg.WeaknessesColumn.Should().Be("J");
        cfg.XrefIdColumn.Should().Be("O");
        cfg.AllowedAnswers.Should().Equal("1", "2", "3", "4");
        cfg.DeviationThreshold.Should().Be(2);
    }
}
