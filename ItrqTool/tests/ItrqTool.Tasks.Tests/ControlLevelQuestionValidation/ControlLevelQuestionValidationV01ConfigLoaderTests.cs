using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.ControlLevelQuestionValidation;
using Xunit;

namespace ItrqTool.Tasks.Tests.ControlLevelQuestionValidation;

public sealed class ControlLevelQuestionValidationV01ConfigLoaderTests
{
    private const string ValidJson = """
    {
      "textColumn":"C","guidanceColumn":"E","previousAnswerColumn":"F","answerColumn":"H",
      "strengthsColumn":"I","weaknessesColumn":"J","providedByColumn":"M","xrefIdColumn":"N",
      "sheetName":"CLQ","chapterRows":[1],"sectionRows":["2:3-5"],
      "allowedAnswers":["1","2","3","4","N/A"],"deviationThreshold":2,
      "severityOverrides":{"input-cell.answer-missing":"Warning"}
    }
    """;

    [Fact]
    public void Load_ValidConfig_Succeeds()
    {
        var config = ControlLevelQuestionValidationV01ConfigLoader.Load(ValidJson);

        config.SheetName.Should().Be("CLQ");
        config.AnswerColumn.Should().Be("H");
        config.DeviationThreshold.Should().Be(2);
        config.AllowedAnswers.Should().Equal("1", "2", "3", "4", "N/A");
        config.ParsedSections.Should().ContainSingle();
        config.SeverityOverrides["input-cell.answer-missing"].Should().Be(FindingEvaluation.Warning);
    }

    [Fact]
    public void Load_UnknownOverrideKey_FailsListingValidIds()
    {
        var json = """
        {
          "textColumn":"C","guidanceColumn":"E","previousAnswerColumn":"F","answerColumn":"H",
          "strengthsColumn":"I","weaknessesColumn":"J","providedByColumn":"M","xrefIdColumn":"N",
          "sheetName":"CLQ","allowedAnswers":["1"],"deviationThreshold":1,
          "severityOverrides":{"not.a.real.finding":"Error"}
        }
        """;

        var act = () => ControlLevelQuestionValidationV01ConfigLoader.Load(json);

        act.Should().Throw<ClqConfigException>()
            .Which.Message.Should().Contain("not.a.real.finding")
            .And.Contain("structure.xrefid-empty-or-duplicated"); // valid ids listed
    }

    [Fact]
    public void Load_BadOverrideValue_Fails()
    {
        var json = """
        {
          "textColumn":"C","guidanceColumn":"E","previousAnswerColumn":"F","answerColumn":"H",
          "strengthsColumn":"I","weaknessesColumn":"J","providedByColumn":"M","xrefIdColumn":"N",
          "sheetName":"CLQ","allowedAnswers":["1"],"deviationThreshold":1,
          "severityOverrides":{"input-cell.answer-missing":"Banana"}
        }
        """;

        var act = () => ControlLevelQuestionValidationV01ConfigLoader.Load(json);

        act.Should().Throw<ClqConfigException>()
            .Which.Message.Should().Contain("could not be parsed");
    }

    [Fact]
    public void Load_BlankColumnLetter_FailsNamingProperty()
    {
        var json = """
        {
          "textColumn":"","guidanceColumn":"E","previousAnswerColumn":"F","answerColumn":"H",
          "strengthsColumn":"I","weaknessesColumn":"J","providedByColumn":"M","xrefIdColumn":"N",
          "sheetName":"CLQ","allowedAnswers":["1"],"deviationThreshold":1
        }
        """;

        var act = () => ControlLevelQuestionValidationV01ConfigLoader.Load(json);

        act.Should().Throw<ClqConfigException>()
            .Which.Message.Should().Contain("TextColumn");
    }

    [Fact]
    public void Load_EmptyAllowedAnswers_Fails()
    {
        var json = """
        {
          "textColumn":"C","guidanceColumn":"E","previousAnswerColumn":"F","answerColumn":"H",
          "strengthsColumn":"I","weaknessesColumn":"J","providedByColumn":"M","xrefIdColumn":"N",
          "sheetName":"CLQ","allowedAnswers":[],"deviationThreshold":1
        }
        """;

        var act = () => ControlLevelQuestionValidationV01ConfigLoader.Load(json);

        act.Should().Throw<ClqConfigException>()
            .Which.Message.Should().Contain("AllowedAnswers");
    }

    [Fact]
    public void Load_NonPositiveDeviationThreshold_Fails()
    {
        var json = """
        {
          "textColumn":"C","guidanceColumn":"E","previousAnswerColumn":"F","answerColumn":"H",
          "strengthsColumn":"I","weaknessesColumn":"J","providedByColumn":"M","xrefIdColumn":"N",
          "sheetName":"CLQ","allowedAnswers":["1"],"deviationThreshold":0
        }
        """;

        var act = () => ControlLevelQuestionValidationV01ConfigLoader.Load(json);

        act.Should().Throw<ClqConfigException>()
            .Which.Message.Should().Contain("DeviationThreshold");
    }

    [Fact]
    public void Load_BadSectionRows_Fails()
    {
        var json = """
        {
          "textColumn":"C","guidanceColumn":"E","previousAnswerColumn":"F","answerColumn":"H",
          "strengthsColumn":"I","weaknessesColumn":"J","providedByColumn":"M","xrefIdColumn":"N",
          "sheetName":"CLQ","sectionRows":["badformat"],"allowedAnswers":["1"],"deviationThreshold":1
        }
        """;

        var act = () => ControlLevelQuestionValidationV01ConfigLoader.Load(json);

        act.Should().Throw<ClqConfigException>()
            .Which.Message.Should().Contain("badformat");
    }

    [Fact]
    public void Load_LiteralNull_Fails()
    {
        var act = () => ControlLevelQuestionValidationV01ConfigLoader.Load("null");

        act.Should().Throw<ClqConfigException>()
            .Which.Message.Should().Contain("null");
    }

    [Fact]
    public void Load_UnknownJsonProperty_Fails()
    {
        var json = """
        {
          "textColumn":"C","guidanceColumn":"E","previousAnswerColumn":"F","answerColumn":"H",
          "strengthsColumn":"I","weaknessesColumn":"J","providedByColumn":"M","xrefIdColumn":"N",
          "sheetName":"CLQ","allowedAnswers":["1"],"deviationThreshold":1,
          "bogusUnknownField":42
        }
        """;

        var act = () => ControlLevelQuestionValidationV01ConfigLoader.Load(json);

        act.Should().Throw<ClqConfigException>()
            .Which.Message.Should().Contain("could not be parsed");
    }
}
