using FluentAssertions;
using Xunit;
using ItrqTool.Tasks.CellRangeInject;

namespace ItrqTool.Tasks.Tests;

public sealed class CellMappingParserTests
{
    // ── Happy: single-cell pair ───────────────────────────────────────────────

    [Fact]
    public void Parse_SingleCellPair_ReturnsOnePair()
    {
        var r = CellMappingParser.Parse("B2->G2");

        r.Errors.Should().BeEmpty();
        r.Pairs.Should().BeEquivalentTo(new[] { new CellPair(2, "B", 2, "G") });
    }

    // ── Happy: range pair shift (3×1) ─────────────────────────────────────────

    [Fact]
    public void Parse_RangePairShift_ReturnsThreePairsRowMajor()
    {
        var r = CellMappingParser.Parse("B2:B4->G2:G4");

        r.Errors.Should().BeEmpty();
        r.Pairs.Should().BeEquivalentTo(new[]
        {
            new CellPair(2, "B", 2, "G"),
            new CellPair(3, "B", 3, "G"),
            new CellPair(4, "B", 4, "G"),
        });
    }

    // ── Happy: multi-column range (2×2) ──────────────────────────────────────

    [Fact]
    public void Parse_MultiColumnRange_ReturnsFourPairsRowMajor()
    {
        var r = CellMappingParser.Parse("B2:C3->E5:F6");

        r.Errors.Should().BeEmpty();
        r.Pairs.Should().BeEquivalentTo(new[]
        {
            new CellPair(2, "B", 5, "E"),
            new CellPair(2, "C", 5, "F"),
            new CellPair(3, "B", 6, "E"),
            new CellPair(3, "C", 6, "F"),
        });
    }

    // ── Happy: multi-pair list with leading/trailing whitespace and space around "->" ──

    [Fact]
    public void Parse_MultiPairListWithWhitespace_TrimsAndExpandsAll()
    {
        var r = CellMappingParser.Parse("  B2->G2 ;  D2:D3 -> I2:I3 ");

        r.Errors.Should().BeEmpty();
        r.Pairs.Should().BeEquivalentTo(new[]
        {
            new CellPair(2, "B", 2, "G"),
            new CellPair(2, "D", 2, "I"),
            new CellPair(3, "D", 3, "I"),
        });
    }

    // ── Happy: same-address (degenerate 1×1) ─────────────────────────────────

    [Fact]
    public void Parse_SameAddress_ReturnsIdentityPair()
    {
        var r = CellMappingParser.Parse("B2->B2");

        r.Errors.Should().BeEmpty();
        r.Pairs.Should().BeEquivalentTo(new[] { new CellPair(2, "B", 2, "B") });
    }

    // ── Happy: corner-order normalization (min/max normalises D5:B2 ≡ B2:D5) ─

    [Fact]
    public void Parse_ReversedCornerOrder_NormalisesToSameExpansionAsOrdered()
    {
        var reversed = CellMappingParser.Parse("D5:B2->E5:C2");
        var ordered  = CellMappingParser.Parse("B2:D5->C2:E5");

        reversed.Errors.Should().BeEmpty();
        ordered.Errors.Should().BeEmpty();
        reversed.Pairs.Should().BeEquivalentTo(ordered.Pairs);
    }

    // ── Failure: empty / whitespace-only / all-blank-semicolons ──────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" ; ; ")]
    public void Parse_EmptyOrBlankInput_ReturnsNoTokensError(string input)
    {
        var r = CellMappingParser.Parse(input);

        r.Pairs.Should().BeEmpty();
        r.Errors.Should().ContainSingle()
            .Which.Should().Be("Parameter 'mappings' contains no valid mapping tokens.");
    }

    // ── Failure: malformed tokens — collect-all, order-preserving ────────────

    [Fact]
    public void Parse_MalformedTokens_ReturnsOneErrorWithAllBadTokens()
    {
        var r = CellMappingParser.Parse("B2G2; ZZ; B2->");

        r.Pairs.Should().BeEmpty();
        r.Errors.Should().ContainSingle()
            .Which.Should().Be("Malformed mapping token(s): B2G2, ZZ, B2->.");
    }

    // ── Failure: dimension mismatch — one error per offending pair ───────────

    [Fact]
    public void Parse_DimensionMismatches_CollectsOneErrorPerPair()
    {
        // 3×1 vs 4×1 and 1×2 vs 1×1
        var r = CellMappingParser.Parse("B2:B4->G2:G5;B2:C2->E2");

        r.Pairs.Should().BeEmpty();
        r.Errors.Should().HaveCount(2);
        r.Errors.Should().Contain("Mapping 'B2:B4->G2:G5': source is 3x1 but target is 4x1.");
        r.Errors.Should().Contain("Mapping 'B2:C2->E2': source is 1x2 but target is 1x1.");
    }
}
