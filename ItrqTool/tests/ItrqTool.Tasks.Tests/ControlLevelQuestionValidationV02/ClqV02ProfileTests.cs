using FluentAssertions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Tasks.ControlLevelQuestionValidationV02;
using ItrqTool.Tasks.QuestionnaireValidation.Clq;

namespace ItrqTool.Tasks.Tests.ControlLevelQuestionValidationV02;

public sealed class ClqV02ProfileTests
{
    private static ControlLevelQuestionValidationV02Config MakeConfig() =>
        new()
        {
            SheetName            = "CLQ",
            TextColumn           = "D",
            GuidanceColumn       = "E",
            PreviousAnswerColumn = "F",
            AnswerColumn         = "H",
            StrengthsColumn      = "I",
            WeaknessesColumn     = "J",
            AnswerStabilityColumn = "K",
            ProvidedByColumn     = "N",
            XrefIdColumn         = "O",
            ChapterRows          = ["1"],
            SectionRows          = ["2:3-4"],
            AllowedAnswers       = ["1", "2", "3", "4", "N/A"],
            AllowedStabilityAnswers = ["Yes", "No"],
            DeviationThreshold   = 2,
        };

    private static ExcelRowStructure MakeRow(int rowNumber, Dictionary<string, string?> values)
    {
        var cells = values
            .Where(kv => kv.Value is not null)
            .ToDictionary(
                kv => kv.Key,
                kv => new ExcelCellStructure(kv.Value, null, null, null));
        return new ExcelRowStructure(rowNumber, cells);
    }

    // ── RecordFactory column mapping ──────────────────────────────────────────────

    [Fact]
    public void RecordFactory_MapsAllColumns_Correctly()
    {
        var config = MakeConfig();
        var profile = ClqV02Profile.Build(config);

        var row = MakeRow(3, new()
        {
            ["D"] = "1.1) What is risk?",
            ["E"] = "Guidance text",
            ["F"] = "PrevAns",
            ["H"] = "2",
            ["I"] = "Strengths text",
            ["J"] = "Weaknesses text",
            ["K"] = "Yes",
            ["N"] = "OrgUnit A",
            ["O"] = "XREF-001",
        });

        var ctx = new ItrqTool.Tasks.QuestionnaireValidation.Parsing.QuestionRowContext(
            Row: row, RowNumber: 3, ChapterName: "Chapter 1", SectionName: "Section 1");

        var q = profile.RecordFactory(ctx);

        q.RowNumber.Should().Be(3);
        q.XrefId.Should().Be("XREF-001");          // O
        q.QuestionNumber.Should().Be("1.1");        // extracted from D prefix
        q.QuestionText.Should().Be("What is risk?"); // stripped from D
        q.OriginalText.Should().Be("1.1) What is risk?");
        q.ChapterName.Should().Be("Chapter 1");
        q.SectionName.Should().Be("Section 1");
        q.Guidance.Should().Be("Guidance text");   // E
        q.PreviousAnswer.Should().Be("PrevAns");   // F
        q.Answer.Should().Be("2");                 // H
        q.Strengths.Should().Be("Strengths text"); // I
        q.Weaknesses.Should().Be("Weaknesses text"); // J
        q.AnswerStability.Should().Be("Yes");      // K
        q.ProvidedBy.Should().Be("OrgUnit A");     // N
        q.NumberFormatUnrecognized.Should().BeFalse();
    }

    [Fact]
    public void RecordFactory_DvFields_AllNullFromFactory()
    {
        var config = MakeConfig();
        var profile = ClqV02Profile.Build(config);

        var row = MakeRow(3, new() { ["D"] = "1.1) Q", ["H"] = "1", ["K"] = "Yes", ["O"] = "X1" });
        var ctx = new ItrqTool.Tasks.QuestionnaireValidation.Parsing.QuestionRowContext(
            Row: row, RowNumber: 3, ChapterName: "Ch", SectionName: "Sec");

        var q = profile.RecordFactory(ctx);

        q.AnswerDvType.Should().BeNull();
        q.AnswerDvFormula.Should().BeNull();
        q.AnswerDvOperator.Should().BeNull();
        q.AnswerDvFormula2.Should().BeNull();
        q.AnswerStabilityDvType.Should().BeNull();
        q.AnswerStabilityDvFormula.Should().BeNull();
        q.AnswerStabilityDvOperator.Should().BeNull();
        q.AnswerStabilityDvFormula2.Should().BeNull();
    }

    // ── DvRoles: 2 entries, correct columns, correct field patching ───────────────

    [Fact]
    public void DvRoles_HasTwoEntries_AnswerColumnThenStabilityColumn()
    {
        var config = MakeConfig();
        var profile = ClqV02Profile.Build(config);

        profile.DvRoles.Should().HaveCount(2);
        profile.DvRoles[0].Column.Should().Be("H"); // AnswerColumn
        profile.DvRoles[1].Column.Should().Be("K"); // AnswerStabilityColumn
    }

    [Fact]
    public void DvRole_Answer_PatchesAnswerDvFields_LeavesStabilityUntouched()
    {
        var config = MakeConfig();
        var profile = ClqV02Profile.Build(config);

        var q = new ClqV02Question(
            RowNumber: 3, XrefId: "X", QuestionNumber: "1.1", QuestionText: "Q",
            OriginalText: "1.1) Q", ChapterName: "Ch", SectionName: "Sec",
            Guidance: null, PreviousAnswer: null, Answer: "1", Strengths: null, Weaknesses: null,
            ProvidedBy: null,
            AnswerDvType: null, AnswerDvFormula: null, AnswerDvOperator: null, AnswerDvFormula2: null,
            NumberFormatUnrecognized: false,
            AnswerStability: "Yes",
            AnswerStabilityDvType: null, AnswerStabilityDvFormula: null,
            AnswerStabilityDvOperator: null, AnswerStabilityDvFormula2: null);

        var cell = new ExcelCellStructure(
            TextValue: null,
            DataValidationType: "List",
            DataValidationFormula: "\"1,2,3,4,N/A\"",
            ConditionalFormattingOperator: null,
            DataValidationOperator: null,
            DataValidationFormula2: null);

        var patched = profile.DvRoles[0].ApplyDv(q, cell);

        patched.AnswerDvType.Should().Be("List");
        patched.AnswerDvFormula.Should().Be("\"1,2,3,4,N/A\"");
        patched.AnswerDvOperator.Should().BeNull();
        patched.AnswerDvFormula2.Should().BeNull();
        patched.AnswerStabilityDvType.Should().BeNull();
        patched.AnswerStabilityDvFormula.Should().BeNull();
    }

    [Fact]
    public void DvRole_Stability_PatchesStabilityDvFields_LeavesAnswerUntouched()
    {
        var config = MakeConfig();
        var profile = ClqV02Profile.Build(config);

        var q = new ClqV02Question(
            RowNumber: 3, XrefId: "X", QuestionNumber: "1.1", QuestionText: "Q",
            OriginalText: "1.1) Q", ChapterName: "Ch", SectionName: "Sec",
            Guidance: null, PreviousAnswer: null, Answer: "1", Strengths: null, Weaknesses: null,
            ProvidedBy: null,
            AnswerDvType: "List", AnswerDvFormula: "\"1,2,3\"", AnswerDvOperator: null, AnswerDvFormula2: null,
            NumberFormatUnrecognized: false,
            AnswerStability: "Yes",
            AnswerStabilityDvType: null, AnswerStabilityDvFormula: null,
            AnswerStabilityDvOperator: null, AnswerStabilityDvFormula2: null);

        var cell = new ExcelCellStructure(
            TextValue: null,
            DataValidationType: "List",
            DataValidationFormula: "\"Yes,No\"",
            ConditionalFormattingOperator: null,
            DataValidationOperator: null,
            DataValidationFormula2: null);

        var patched = profile.DvRoles[1].ApplyDv(q, cell);

        patched.AnswerStabilityDvType.Should().Be("List");
        patched.AnswerStabilityDvFormula.Should().Be("\"Yes,No\"");
        patched.AnswerStabilityDvOperator.Should().BeNull();
        patched.AnswerStabilityDvFormula2.Should().BeNull();
        // Answer DV is untouched
        patched.AnswerDvType.Should().Be("List");
        patched.AnswerDvFormula.Should().Be("\"1,2,3\"");
    }

    // ── Descriptors: baseline + 3 extension ids ───────────────────────────────────

    [Fact]
    public void BaselineDescriptors_EqualsClqBaselineFindingsAll()
    {
        var config = MakeConfig();
        var profile = ClqV02Profile.Build(config);

        profile.BaselineDescriptors.Should().BeEquivalentTo(ClqBaselineFindings.All,
            opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void Extensions_ExposeExactlyThreeIds()
    {
        var config = MakeConfig();
        var profile = ClqV02Profile.Build(config);

        var extensionIds = profile.Extensions
            .SelectMany(e => e.Descriptors)
            .Select(d => d.Id)
            .ToList();

        extensionIds.Should().BeEquivalentTo(
        [
            "input-cell.answer-stability.missing",
            "input-cell.answer-stability.not-in-allowed-set",
            "constraint.answer-stability.validation-rule-changed",
        ], opts => opts.WithStrictOrdering());
    }
}
