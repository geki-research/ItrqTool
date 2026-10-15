using FluentAssertions;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.RiskLevelQuestionInjectED;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using ItrqTool.Tasks.RiskLevelQuestionValidationV02;
using Xunit;

namespace ItrqTool.Tasks.Tests.RiskLevelQuestionInjectED;

public sealed class RlqEDInjectConfigTests
{
    // ── builders ──────────────────────────────────────────────────────────────

    private static RlqEDInjectConfig ValidConfig() => new()
    {
        CurrentConfigFilename = "rlq-ed-v02-validation-config.json",
        PreviousConfigFilename = "rlq-ed-v01-validation-config.json",
        SourceHoldingAnswerColumn = "K",
        SourceEBOeAnswerColumn = "L",
        TargetHoldingAnswerColumn = "H",
        TargetEBOeAnswerColumn = "I",
    };

    // ValidConfig() with exactly one property replaced (by name).
    private static RlqEDInjectConfig With(string property, string value)
    {
        var v = ValidConfig();
        return new RlqEDInjectConfig
        {
            CurrentConfigFilename = property == nameof(RlqEDInjectConfig.CurrentConfigFilename) ? value : v.CurrentConfigFilename,
            PreviousConfigFilename = property == nameof(RlqEDInjectConfig.PreviousConfigFilename) ? value : v.PreviousConfigFilename,
            SourceHoldingAnswerColumn = property == nameof(RlqEDInjectConfig.SourceHoldingAnswerColumn) ? value : v.SourceHoldingAnswerColumn,
            SourceEBOeAnswerColumn = property == nameof(RlqEDInjectConfig.SourceEBOeAnswerColumn) ? value : v.SourceEBOeAnswerColumn,
            TargetHoldingAnswerColumn = property == nameof(RlqEDInjectConfig.TargetHoldingAnswerColumn) ? value : v.TargetHoldingAnswerColumn,
            TargetEBOeAnswerColumn = property == nameof(RlqEDInjectConfig.TargetEBOeAnswerColumn) ? value : v.TargetEBOeAnswerColumn,
        };
    }

    // The ED v01 letters (standard RlqV01Config shape).
    private static RlqV01Config EdV01() => new()
    {
        QuestionNumberColumn = "C",
        TextColumn = "D",
        GuidanceColumn = "E",
        RequestedTypeColumn = "F",
        PreviousAnswerColumn = "G",
        AnswerColumn = "J",
        RequestedExplanationColumn = "M",
        PreviousExplanationColumn = "N",
        CurrentExplanationColumn = "O",
        MaterialChangeColumn = "P",
        ProvidedByColumn = "S",
        XrefIdColumn = "U",
    };

    // The ED v02 letters (standard RlqV02Config shape).
    private static RlqV02Config EdV02() => new()
    {
        QuestionNumberColumn = "C",
        TextColumn = "D",
        GuidanceColumn = "E",
        RequestedTypeColumn = "F",
        PreviousAnswerColumn = "G",
        AnswerColumn = "J",
        RequestedExplanationColumn = "M",
        PreviousExplanationColumn = "N",
        CurrentExplanationColumn = "O",
        MaterialChangeColumn = "P",
        HowExplanationColumn = "Q",
        ProvidedByColumn = "T",
        XrefIdColumn = "V",
    };

    // ── K1. valid config ──────────────────────────────────────────────────────

    [Fact]
    public void Validate_AllValid_ReturnsNoErrors()
    {
        ValidConfig().Validate().Should().BeEmpty();
    }

    // ── K2. each key missing / empty → the mirrored message ───────────────────

    [Theory]
    [InlineData(nameof(RlqEDInjectConfig.CurrentConfigFilename))]
    [InlineData(nameof(RlqEDInjectConfig.PreviousConfigFilename))]
    [InlineData(nameof(RlqEDInjectConfig.SourceHoldingAnswerColumn))]
    [InlineData(nameof(RlqEDInjectConfig.SourceEBOeAnswerColumn))]
    [InlineData(nameof(RlqEDInjectConfig.TargetHoldingAnswerColumn))]
    [InlineData(nameof(RlqEDInjectConfig.TargetEBOeAnswerColumn))]
    public void Validate_EmptyKey_ReturnsExactlyTheMirroredMessage(string property)
    {
        With(property, "").Validate().Should().Equal($"{property} must not be empty.");
    }

    // ── K3. unknown JSON key rejected through ConfigLoader ────────────────────

    [Fact]
    public void ConfigLoader_UnknownKey_IsRejected()
    {
        const string validBody =
            "\"CurrentConfigFilename\": \"rlq-ed-v02-validation-config.json\", " +
            "\"PreviousConfigFilename\": \"rlq-ed-v01-validation-config.json\", " +
            "\"SourceHoldingAnswerColumn\": \"K\", \"SourceEBOeAnswerColumn\": \"L\", " +
            "\"TargetHoldingAnswerColumn\": \"H\", \"TargetEBOeAnswerColumn\": \"I\"";

        var path = Path.GetTempFileName();
        try
        {
            // Control: the same document without the unknown key loads clean.
            File.WriteAllText(path, "{ " + validBody + " }");
            ConfigLoader.Load<RlqEDInjectConfig>(File.ReadAllText(path), c => c.Validate())
                .TargetEBOeAnswerColumn.Should().Be("I");

            File.WriteAllText(path, "{ " + validBody + ", \"Bogus\": \"x\" }");
            var act = () => ConfigLoader.Load<RlqEDInjectConfig>(File.ReadAllText(path), c => c.Validate());

            act.Should().Throw<ConfigException>()
                .WithMessage("Configuration could not be parsed:*Bogus*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── K4. distinctness (same comparer as RlqV02Config: case-insensitive) ────

    [Fact]
    public void Validate_SourceHoldingEqualsSourceEBOe_IsRejected()
    {
        var c = new RlqEDInjectConfig
        {
            CurrentConfigFilename = "a.json",
            PreviousConfigFilename = "b.json",
            SourceHoldingAnswerColumn = "K",
            SourceEBOeAnswerColumn = "k",
            TargetHoldingAnswerColumn = "H",
            TargetEBOeAnswerColumn = "I",
        };

        c.Validate().Should().Equal("SourceHoldingAnswerColumn and SourceEBOeAnswerColumn must be distinct.");
    }

    [Fact]
    public void Validate_TargetHoldingEqualsTargetEBOe_IsRejected()
    {
        var c = new RlqEDInjectConfig
        {
            CurrentConfigFilename = "a.json",
            PreviousConfigFilename = "b.json",
            SourceHoldingAnswerColumn = "K",
            SourceEBOeAnswerColumn = "L",
            TargetHoldingAnswerColumn = "H",
            TargetEBOeAnswerColumn = "h",
        };

        c.Validate().Should().Equal("TargetHoldingAnswerColumn and TargetEBOeAnswerColumn must be distinct.");
    }

    // ── K5. CollisionsWith — the real ED letters collide with nothing ─────────

    [Fact]
    public void CollisionsWith_RealEdLetters_IsEmpty()
    {
        ValidConfig().CollisionsWith(EdV01(), EdV02()).Should().BeEmpty();
    }

    // ── K6. CollisionsWith — exact collision text ─────────────────────────────

    [Fact]
    public void CollisionsWith_SourceLetterEqualsV01Letter_ReportsExactText()
    {
        With(nameof(RlqEDInjectConfig.SourceHoldingAnswerColumn), "G")
            .CollisionsWith(EdV01(), EdV02())
            .Should().Equal(
                "Column letter collision: RlqEDInjectConfig.SourceHoldingAnswerColumn 'G' equals RlqV01Config.PreviousAnswerColumn.");
    }

    [Fact]
    public void CollisionsWith_TargetLetterEqualsV02Letter_ReportsExactText()
    {
        With(nameof(RlqEDInjectConfig.TargetEBOeAnswerColumn), "J")
            .CollisionsWith(EdV01(), EdV02())
            .Should().Equal(
                "Column letter collision: RlqEDInjectConfig.TargetEBOeAnswerColumn 'J' equals RlqV02Config.AnswerColumn.");
    }

    [Fact]
    public void CollisionsWith_TargetLetterEqualsV02HowExplanation_ReportsExactText_CaseInsensitive()
    {
        With(nameof(RlqEDInjectConfig.TargetHoldingAnswerColumn), "q")
            .CollisionsWith(EdV01(), EdV02())
            .Should().Equal(
                "Column letter collision: RlqEDInjectConfig.TargetHoldingAnswerColumn 'q' equals RlqV02Config.HowExplanationColumn.");
    }

    // Several collisions come back in ED property-declaration order; sources are checked against
    // v01 only and targets against v02 only (V is a v02-only letter, S a v01-only letter).
    [Fact]
    public void CollisionsWith_SeveralCollisions_InDeclarationOrder_SameSideOnly()
    {
        var c = new RlqEDInjectConfig
        {
            CurrentConfigFilename = "a.json",
            PreviousConfigFilename = "b.json",
            SourceHoldingAnswerColumn = "U",   // v01 XrefIdColumn
            SourceEBOeAnswerColumn = "c",      // v01 QuestionNumberColumn
            TargetHoldingAnswerColumn = "T",   // v02 ProvidedByColumn
            TargetEBOeAnswerColumn = "S",      // v01-only letter → no collision on the v02 side
        };

        c.CollisionsWith(EdV01(), EdV02()).Should().Equal(
            "Column letter collision: RlqEDInjectConfig.SourceHoldingAnswerColumn 'U' equals RlqV01Config.XrefIdColumn.",
            "Column letter collision: RlqEDInjectConfig.SourceEBOeAnswerColumn 'c' equals RlqV01Config.QuestionNumberColumn.",
            "Column letter collision: RlqEDInjectConfig.TargetHoldingAnswerColumn 'T' equals RlqV02Config.ProvidedByColumn.");

        With(nameof(RlqEDInjectConfig.SourceHoldingAnswerColumn), "V")
            .CollisionsWith(EdV01(), EdV02())
            .Should().BeEmpty("a source letter is checked against the v01 map only");
    }

    // ── K7. per-letter syntax rule (mirrors RlqV02Config.ValidateColumnLetter) ─

    [Theory]
    [InlineData(nameof(RlqEDInjectConfig.SourceHoldingAnswerColumn))]
    [InlineData(nameof(RlqEDInjectConfig.SourceEBOeAnswerColumn))]
    [InlineData(nameof(RlqEDInjectConfig.TargetHoldingAnswerColumn))]
    [InlineData(nameof(RlqEDInjectConfig.TargetEBOeAnswerColumn))]
    public void Validate_NonLetterColumn_ReturnsSyntaxMessage(string property)
    {
        With(property, "1A").Validate().Should().Equal(
            $"{property} must be a valid column letter (e.g. 'A', 'AA').");
    }
}
