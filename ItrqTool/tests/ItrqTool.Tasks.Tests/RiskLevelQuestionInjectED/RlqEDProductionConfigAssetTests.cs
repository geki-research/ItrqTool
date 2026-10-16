using FluentAssertions;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.RiskLevelQuestionInjectED;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using ItrqTool.Tasks.RiskLevelQuestionValidationV02;
using ItrqTool.Tasks.WorksheetStructure;
using Xunit;

namespace ItrqTool.Tasks.Tests.RiskLevelQuestionInjectED;

/// <summary>
/// Production-asset tests for the ED inject config and the two ED column-map configs, loaded from the
/// solution root through <see cref="ConfigLoader"/> exactly as RlqV01ProductionConfigAssetTests does,
/// plus their coherence with the ED structure schemas.
/// </summary>
public sealed class RlqEDProductionConfigAssetTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Solution root (.slnx) not found above test output directory.");
        return dir.FullName;
    }

    private static string ConfigPath(string fileName) => Path.Combine(SolutionRoot(), "configs", fileName);

    private static RlqEDInjectConfig InjectConfig()
        => ConfigLoader.Load<RlqEDInjectConfig>(File.ReadAllText(ConfigPath("rlq-ed-inject-config.json")), c => c.Validate());

    private static RlqV01Config V01Config()
        => ConfigLoader.Load<RlqV01Config>(File.ReadAllText(ConfigPath("rlq-ed-v01-columns-config.json")), c => c.Validate());

    private static RlqV02Config V02Config()
        => ConfigLoader.Load<RlqV02Config>(File.ReadAllText(ConfigPath("rlq-ed-v02-columns-config.json")), c => c.Validate());

    private static WorksheetStructureSchema Schema(string version)
        => new WorksheetStructureSchemaLoader().Load(new WorksheetSchemaRef("rlq-ed", version), SolutionRoot());

    private static string HeaderAt(WorksheetStructureSchema schema, string column)
        => schema.Columns.Single(c => c.Column == column).CanonicalHeader;

    // ── C1. inject config ─────────────────────────────────────────────────────

    [Fact]
    public void InjectConfig_LoadsCleanWithAllEightValuesPinned()
    {
        var config = InjectConfig();

        config.Validate().Should().BeEmpty();
        config.CurrentConfigFilename.Should().Be("rlq-ed-v02-columns-config.json");
        config.PreviousConfigFilename.Should().Be("rlq-ed-v01-columns-config.json");
        config.SourceHoldingAnswerColumn.Should().Be("K");
        config.SourceEBOeAnswerColumn.Should().Be("L");
        config.TargetHoldingAnswerColumn.Should().Be("H");
        config.TargetEBOeAnswerColumn.Should().Be("I");
        config.TargetHoldingDvColumn.Should().Be("K");
        config.TargetEBOeDvColumn.Should().Be("L");
    }

    // ── C2. column-map configs ────────────────────────────────────────────────

    [Fact]
    public void V01ColumnConfig_LoadsCleanWithAllValuesPinned()
    {
        var config = V01Config();

        config.Validate().Should().BeEmpty();
        config.QuestionNumberColumn.Should().Be("C");
        config.TextColumn.Should().Be("D");
        config.GuidanceColumn.Should().Be("E");
        config.RequestedTypeColumn.Should().Be("F");
        config.PreviousAnswerColumn.Should().Be("G");
        config.AnswerColumn.Should().Be("J");
        config.RequestedExplanationColumn.Should().Be("M");
        config.PreviousExplanationColumn.Should().Be("N");
        config.CurrentExplanationColumn.Should().Be("O");
        config.MaterialChangeColumn.Should().Be("P");
        config.ProvidedByColumn.Should().Be("S");
        config.XrefIdColumn.Should().Be("U");
        config.SheetName.Should().Be("IT Risk Level Questions");
        config.SectionRows.Should().Equal("3:4-21", "22:23-42", "43:44-58", "59:60-71");
        config.DeviationThreshold.Should().Be(0.25);
        config.SeverityOverrides.Should().BeEmpty();

        LayoutParser.Parse([], config.SectionRows, config.TextColumn, config.TextColumn, config.TextColumn)
            .Sections.Should().HaveCount(4);
    }

    [Fact]
    public void V02ColumnConfig_LoadsCleanWithAllValuesPinned()
    {
        var config = V02Config();

        config.Validate().Should().BeEmpty();
        config.QuestionNumberColumn.Should().Be("C");
        config.TextColumn.Should().Be("D");
        config.GuidanceColumn.Should().Be("E");
        config.RequestedTypeColumn.Should().Be("F");
        config.PreviousAnswerColumn.Should().Be("G");
        config.AnswerColumn.Should().Be("J");
        config.RequestedExplanationColumn.Should().Be("M");
        config.PreviousExplanationColumn.Should().Be("N");
        config.CurrentExplanationColumn.Should().Be("O");
        config.MaterialChangeColumn.Should().Be("P");
        config.HowExplanationColumn.Should().Be("Q");
        config.ProvidedByColumn.Should().Be("T");
        config.XrefIdColumn.Should().Be("V");
        config.SheetName.Should().Be("IT Risk Level Questions");
        config.SectionRows.Should().Equal("3:4-21", "22:23-42", "43:44-58", "59:60-71");
        config.DeviationThreshold.Should().Be(0.25);
        config.MaterialChangeExplanationTriggers.Should().Equal("Yes");
        config.SeverityOverrides.Should().BeEmpty();

        LayoutParser.Parse([], config.SectionRows, config.TextColumn, config.TextColumn, config.TextColumn)
            .Sections.Should().HaveCount(4);
    }

    // ── C3. no ED letter collides with the real column maps ──────────────────

    [Fact]
    public void InjectConfig_CollidesWithNothingInTheRealColumnMaps()
    {
        InjectConfig().CollisionsWith(V01Config(), V02Config()).Should().BeEmpty();
    }

    // ── C4. config ↔ schema coherence ─────────────────────────────────────────

    [Fact]
    public void EveryConfiguredLetter_IsDeclaredInTheMatchingSchema_AndReferencedFilesExist()
    {
        var inject = InjectConfig();
        var v01 = V01Config();
        var v02 = V02Config();
        var v01Declared = Schema("v01").Columns.Select(c => c.Column).ToHashSet();
        var v02Declared = Schema("v02").Columns.Select(c => c.Column).ToHashSet();

        new[]
        {
            v01.QuestionNumberColumn, v01.TextColumn, v01.GuidanceColumn, v01.RequestedTypeColumn,
            v01.PreviousAnswerColumn, v01.AnswerColumn, v01.RequestedExplanationColumn,
            v01.PreviousExplanationColumn, v01.CurrentExplanationColumn, v01.MaterialChangeColumn,
            v01.ProvidedByColumn, v01.XrefIdColumn,
            inject.SourceHoldingAnswerColumn, inject.SourceEBOeAnswerColumn,
        }.Should().OnlyContain(letter => v01Declared.Contains(letter));

        new[]
        {
            v02.QuestionNumberColumn, v02.TextColumn, v02.GuidanceColumn, v02.RequestedTypeColumn,
            v02.PreviousAnswerColumn, v02.AnswerColumn, v02.RequestedExplanationColumn,
            v02.PreviousExplanationColumn, v02.CurrentExplanationColumn, v02.MaterialChangeColumn,
            v02.HowExplanationColumn, v02.ProvidedByColumn, v02.XrefIdColumn,
            inject.TargetHoldingAnswerColumn, inject.TargetEBOeAnswerColumn,
            inject.TargetHoldingDvColumn, inject.TargetEBOeDvColumn,
        }.Should().OnlyContain(letter => v02Declared.Contains(letter));

        File.Exists(ConfigPath(inject.CurrentConfigFilename)).Should().BeTrue();
        File.Exists(ConfigPath(inject.PreviousConfigFilename)).Should().BeTrue();
    }

    // ── C5. role ↔ header coherence for the inject-relevant columns ──────────

    [Fact]
    public void InjectRelevantColumns_CarryTheExpectedHeaders()
    {
        var inject = InjectConfig();
        var v01 = V01Config();
        var v02 = V02Config();
        var s01 = Schema("v01");
        var s02 = Schema("v02");

        HeaderAt(s01, v01.AnswerColumn).Should().Contain("Answer");
        HeaderAt(s01, inject.SourceHoldingAnswerColumn).Should().Contain("Holding");
        HeaderAt(s01, inject.SourceEBOeAnswerColumn).Should().Contain("EBOe");

        HeaderAt(s02, v02.PreviousAnswerColumn).Should().Be("Last Year's Answer");
        HeaderAt(s02, inject.TargetHoldingAnswerColumn).Should().Contain("Holding").And.Contain("Last Year");
        HeaderAt(s02, inject.TargetEBOeAnswerColumn).Should().Contain("EBOe").And.Contain("Last Year");
        HeaderAt(s02, v02.ProvidedByColumn).Should().Contain("provided by");
        HeaderAt(s02, v02.XrefIdColumn).Should().Be("Erste Group Internal ID");
    }
}
