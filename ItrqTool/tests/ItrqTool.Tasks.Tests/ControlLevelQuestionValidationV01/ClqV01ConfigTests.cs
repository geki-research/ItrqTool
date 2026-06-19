using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.ControlLevelQuestionValidationV01;
using ItrqTool.Tasks.QuestionnaireValidation.Clq;
using Xunit;

namespace ItrqTool.Tasks.Tests.ControlLevelQuestionValidationV01;

/// <summary>
/// Negative-coverage for <see cref="ClqV01Config.Validate"/>. Mirrors
/// ControlLevelQuestionValidationV02ConfigTests minus the answer-stability rules
/// (v01 has no column K / AllowedStabilityAnswers). v01 column map: provided-by M, xref-id N.
/// </summary>
public sealed class ClqV01ConfigTests
{
    private static ClqV01Config ValidConfig() => new()
    {
        TextColumn = "D",
        GuidanceColumn = "E",
        PreviousAnswerColumn = "F",
        PreviousExplanationColumn = "G",
        AnswerColumn = "H",
        StrengthsColumn = "I",
        WeaknessesColumn = "J",
        ProvidedByColumn = "M",
        XrefIdColumn = "N",
        SheetName = "Control Level Questions",
        AllowedAnswers = ["1", "2", "3", "4"],
        DeviationThreshold = 2
    };

    // A config identical to ValidConfig() except for the overrides applied via the action.
    // ClqV01Config is a sealed class (not a record), so each case builds a fresh instance.
    private static ClqV01Config ConfigWith(
        string text = "D", string guidance = "E", string prevAnswer = "F",
        string prevExplanation = "G", string answer = "H",
        string strengths = "I", string weaknesses = "J", string providedBy = "M", string xrefId = "N",
        string sheetName = "Control Level Questions",
        IReadOnlyList<string>? allowedAnswers = null, int deviationThreshold = 2) => new()
    {
        TextColumn = text,
        GuidanceColumn = guidance,
        PreviousAnswerColumn = prevAnswer,
        PreviousExplanationColumn = prevExplanation,
        AnswerColumn = answer,
        StrengthsColumn = strengths,
        WeaknessesColumn = weaknesses,
        ProvidedByColumn = providedBy,
        XrefIdColumn = xrefId,
        SheetName = sheetName,
        AllowedAnswers = allowedAnswers ?? ["1", "2", "3", "4"],
        DeviationThreshold = deviationThreshold
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
        ConfigWith(text: "").Validate().Should().Contain(e => e.Contains("TextColumn"));
    }

    [Fact]
    public void Validate_BlankGuidanceColumn_ReturnsError()
    {
        ConfigWith(guidance: "").Validate().Should().Contain(e => e.Contains("GuidanceColumn"));
    }

    [Fact]
    public void Validate_BlankPreviousAnswerColumn_ReturnsError()
    {
        ConfigWith(prevAnswer: "").Validate().Should().Contain(e => e.Contains("PreviousAnswerColumn"));
    }

    [Fact]
    public void Validate_BlankAnswerColumn_ReturnsError()
    {
        ConfigWith(answer: "").Validate().Should().Contain(e => e.Contains("AnswerColumn"));
    }

    [Fact]
    public void Validate_BlankStrengthsColumn_ReturnsError()
    {
        ConfigWith(strengths: "").Validate().Should().Contain(e => e.Contains("StrengthsColumn"));
    }

    [Fact]
    public void Validate_BlankWeaknessesColumn_ReturnsError()
    {
        ConfigWith(weaknesses: "").Validate().Should().Contain(e => e.Contains("WeaknessesColumn"));
    }

    [Fact]
    public void Validate_BlankProvidedByColumn_ReturnsError()
    {
        ConfigWith(providedBy: "").Validate().Should().Contain(e => e.Contains("ProvidedByColumn"));
    }

    [Fact]
    public void Validate_BlankXrefIdColumn_ReturnsError()
    {
        ConfigWith(xrefId: "").Validate().Should().Contain(e => e.Contains("XrefIdColumn"));
    }

    // ── column letter non-alphabetic ──────────────────────────────────────────

    [Fact]
    public void Validate_NonAlphabeticTextColumn_ReturnsError()
    {
        ConfigWith(text: "D2").Validate().Should().Contain(e => e.Contains("TextColumn"));
    }

    [Fact]
    public void Validate_NonAlphabeticXrefIdColumn_ReturnsError()
    {
        ConfigWith(xrefId: "N1").Validate().Should().Contain(e => e.Contains("XrefIdColumn"));
    }

    // ── structural fields ─────────────────────────────────────────────────────

    [Fact]
    public void Validate_BlankSheetName_ReturnsError()
    {
        ConfigWith(sheetName: "").Validate().Should().Contain(e => e.Contains("SheetName"));
    }

    // ── allowed-set field ─────────────────────────────────────────────────────

    [Fact]
    public void Validate_EmptyAllowedAnswers_ReturnsError()
    {
        ConfigWith(allowedAnswers: []).Validate().Should().Contain(e => e.Contains("AllowedAnswers"));
    }

    // ── deviation threshold ───────────────────────────────────────────────────

    [Fact]
    public void Validate_DeviationThresholdZero_ReturnsError()
    {
        ConfigWith(deviationThreshold: 0).Validate().Should().Contain(e => e.Contains("DeviationThreshold"));
    }

    [Fact]
    public void Validate_DeviationThresholdNegative_ReturnsError()
    {
        ConfigWith(deviationThreshold: -3).Validate().Should().Contain(e => e.Contains("DeviationThreshold"));
    }

    // ── multi-error collection ────────────────────────────────────────────────

    [Fact]
    public void Validate_MultipleErrors_AllCollected()
    {
        var c = ConfigWith(
            text: "",                // error
            answer: "",              // error
            sheetName: "",           // error
            allowedAnswers: [],      // error
            deviationThreshold: 0);  // error
        var errors = c.Validate();
        errors.Should().HaveCountGreaterThanOrEqualTo(5);
        errors.Should().Contain(e => e.Contains("TextColumn"));
        errors.Should().Contain(e => e.Contains("AnswerColumn"));
        errors.Should().Contain(e => e.Contains("SheetName"));
        errors.Should().Contain(e => e.Contains("AllowedAnswers"));
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
        var c = new ClqV01Config
        {
            TextColumn = "D", GuidanceColumn = "E", PreviousAnswerColumn = "F",
            PreviousExplanationColumn = "G", AnswerColumn = "H",
            StrengthsColumn = "I", WeaknessesColumn = "J", ProvidedByColumn = "M", XrefIdColumn = "N",
            SheetName = "CLQ", AllowedAnswers = ["1"], DeviationThreshold = 1,
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
        cfg.XrefIdColumn.Should().Be("N");
        cfg.AllowedAnswers.Should().Equal("1", "2", "3", "4");
        cfg.DeviationThreshold.Should().Be(2);
    }
}
