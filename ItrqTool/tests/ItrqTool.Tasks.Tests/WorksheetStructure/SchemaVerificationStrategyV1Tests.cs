using FluentAssertions;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.WorksheetStructure;
using Xunit;

namespace ItrqTool.Tasks.Tests.WorksheetStructure;

public sealed class SchemaVerificationStrategyV1Tests
{
    private static readonly SchemaVerificationStrategyV1 Strategy = new();

    // A small synthetic schema: a blank-header column (C), two plain columns (D, H), and a
    // column carrying an accepted-variant allow-list (X). Header row 2; no per-column overrides.
    private static WorksheetStructureSchema Schema() => new(
        SchemaFormatVersion: 1,
        Questionnaire: "syn",
        QuestionnaireVersion: "v01",
        SheetName: "Synthetic",
        HeaderRow: 2,
        Columns:
        [
            new("C", null, "", null),
            new("D", null, "Question Text\r\nsecond  line", null),
            new("H", null, "↓ Answer ↓", null),
            new("X", null, "Color", ["Colour"]),
        ]);

    private static Dictionary<string, ExcelCellStructure> MatchingCells() => new()
    {
        ["C2"] = Cell(""),
        ["D2"] = Cell("Question Text\r\nsecond  line"),
        ["H2"] = Cell("↓ Answer ↓"),
        ["X2"] = Cell("Color"),
    };

    private static ExcelCellStructure Cell(string text) => new(text, null, null, null);

    [Fact]
    public void AllHeadersMatch_IncludingBlankColumn_ReturnsMatch()
    {
        var result = Strategy.Verify(Schema(), MatchingCells(), "syn.xlsx");

        result.Outcome.Should().Be(WorksheetStructureOutcome.Match);
        result.Findings.Should().BeEmpty();
        result.AssetErrorReason.Should().BeNull();
    }

    [Fact]
    public void OneHeaderDiffers_ReturnsSingleFatalStructureFinding()
    {
        var cells = MatchingCells();
        cells["H2"] = Cell("Response");

        var result = Strategy.Verify(Schema(), cells, "syn.xlsx");

        result.Outcome.Should().Be(WorksheetStructureOutcome.Mismatch);
        result.Findings.Should().ContainSingle();
        var f = result.Findings[0];
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Fatal);
        f.CellAddresses.Should().Contain("H2");
        f.CheckResult.Should().StartWith("structure.unexpected-worksheet-structure");
    }

    [Fact]
    public void TwoHeadersDiffer_StillSingleFinding_NamingBothAddresses()
    {
        var cells = MatchingCells();
        cells["H2"] = Cell("Response");
        cells["D2"] = Cell("Different");

        var result = Strategy.Verify(Schema(), cells, "syn.xlsx");

        result.Outcome.Should().Be(WorksheetStructureOutcome.Mismatch);
        result.Findings.Should().ContainSingle();
        result.Findings[0].CellAddresses.Should().Contain("D2").And.Contain("H2");
    }

    [Fact]
    public void HeaderDiffersOnlyByWhitespaceAndCase_ReturnsMatch()
    {
        var cells = MatchingCells();
        // case-folded, \r\n -> \n, collapsed double-space, leading/trailing space
        cells["D2"] = Cell("  question text\nsecond line  ");

        var result = Strategy.Verify(Schema(), cells, "syn.xlsx");

        result.Outcome.Should().Be(WorksheetStructureOutcome.Match);
    }

    [Fact]
    public void AcceptedVariant_Matches_ButUnlistedValue_Mismatches()
    {
        var cells = MatchingCells();
        cells["X2"] = Cell("Colour"); // a listed variant
        Strategy.Verify(Schema(), cells, "syn.xlsx").Outcome
            .Should().Be(WorksheetStructureOutcome.Match);

        cells["X2"] = Cell("Hue"); // neither canonical nor a variant
        Strategy.Verify(Schema(), cells, "syn.xlsx").Outcome
            .Should().Be(WorksheetStructureOutcome.Mismatch);
    }

    [Fact]
    public void BlankCanonical_BlankActualMatches_NonBlankActualMismatches()
    {
        var cells = MatchingCells();
        cells["C2"] = Cell("   "); // whitespace-only normalizes to blank → still match
        Strategy.Verify(Schema(), cells, "syn.xlsx").Outcome
            .Should().Be(WorksheetStructureOutcome.Match);

        cells["C2"] = Cell("No.");
        var result = Strategy.Verify(Schema(), cells, "syn.xlsx");
        result.Outcome.Should().Be(WorksheetStructureOutcome.Mismatch);
        result.Findings[0].CellAddresses.Should().Contain("C2");
    }
}
