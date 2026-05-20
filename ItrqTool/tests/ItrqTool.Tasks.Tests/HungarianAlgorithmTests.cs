using FluentAssertions;
using Xunit;
using ItrqTool.Tasks.ControlLevelQuestionDiff;

namespace ItrqTool.Tasks.Tests;

public sealed class HungarianAlgorithmTests
{
    private static double TotalProfit(double[,] matrix, int[] assignment)
    {
        double total = 0;
        for (int i = 0; i < assignment.Length; i++)
            if (assignment[i] >= 0)
                total += matrix[i, assignment[i]];
        return total;
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Solve_EmptyMatrix_0Rows_ReturnsEmpty()
    {
        var result = HungarianAlgorithm.SolveMaximumAssignment(new double[0, 3]);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Solve_EmptyMatrix_0Cols_ReturnsEmpty()
    {
        var result = HungarianAlgorithm.SolveMaximumAssignment(new double[3, 0]);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Solve_1x1_ReturnsSingleAssignment()
    {
        var matrix = new double[1, 1] { { 0.5 } };
        var result = HungarianAlgorithm.SolveMaximumAssignment(matrix);
        result.Should().BeEquivalentTo(new[] { 0 });
    }

    [Fact]
    public void Solve_AllZero_ReturnsValidAssignment()
    {
        var matrix = new double[2, 2] { { 0, 0 }, { 0, 0 } };
        var result = HungarianAlgorithm.SolveMaximumAssignment(matrix);
        result.Should().HaveCount(2);
        result.Should().OnlyContain(j => j == 0 || j == 1);
        result[0].Should().NotBe(result[1], because: "each column assigned at most once");
    }

    // ── Clear optimal ─────────────────────────────────────────────────────────

    [Fact]
    public void Solve_2x2_ClearOptimal_MatchesDiagonal()
    {
        // row 0 strongly prefers col 0, row 1 strongly prefers col 1
        var matrix = new double[2, 2] { { 0.9, 0.1 }, { 0.1, 0.9 } };
        var result = HungarianAlgorithm.SolveMaximumAssignment(matrix);
        result[0].Should().Be(0);
        result[1].Should().Be(1);
    }

    // ── Greedy-fails case ────────────────────────────────────────────────────

    [Fact]
    public void Solve_2x2_GreedyWouldFail_ReturnsOptimalAssignment()
    {
        // Greedy picks row0→col0 (0.9), row1→col1 (0.1) → total 1.0
        // Optimal: row0→col1 (0.8), row1→col0 (0.7) → total 1.5
        var matrix = new double[2, 2] { { 0.9, 0.8 }, { 0.7, 0.1 } };
        var result = HungarianAlgorithm.SolveMaximumAssignment(matrix);
        result[0].Should().Be(1, because: "row 0 is optimally matched to col 1");
        result[1].Should().Be(0, because: "row 1 is optimally matched to col 0");
        TotalProfit(matrix, result).Should().BeApproximately(1.5, 1e-9);
    }

    // ── Non-square: more rows than cols ──────────────────────────────────────

    [Fact]
    public void Solve_3x2_MoreRowsThanCols_OneRowUnassigned()
    {
        var matrix = new double[3, 2]
        {
            { 0.9, 0.1 },
            { 0.2, 0.8 },
            { 0.3, 0.3 }
        };
        var result = HungarianAlgorithm.SolveMaximumAssignment(matrix);
        result.Should().HaveCount(3);

        // Exactly two real assignments; one row gets -1
        var assigned = result.Where(j => j >= 0).ToList();
        assigned.Should().HaveCount(2);
        assigned.Distinct().Should().HaveCount(2, because: "each column is assigned at most once");

        // Total profit must be optimal: row0→col0 (0.9) + row1→col1 (0.8) = 1.7
        TotalProfit(matrix, result).Should().BeApproximately(1.7, 1e-9);
    }

    // ── Non-square: more cols than rows ──────────────────────────────────────

    [Fact]
    public void Solve_2x3_MoreColsThanRows_AllRowsAssigned()
    {
        var matrix = new double[2, 3]
        {
            { 0.1, 0.9, 0.5 },
            { 0.8, 0.2, 0.4 }
        };
        var result = HungarianAlgorithm.SolveMaximumAssignment(matrix);
        result.Should().HaveCount(2);
        result.Should().OnlyContain(j => j >= 0, because: "all rows must be assigned when cols > rows");
        result[0].Should().NotBe(result[1], because: "each column assigned at most once");

        // Optimal: row0→col1 (0.9), row1→col0 (0.8) → total 1.7
        TotalProfit(matrix, result).Should().BeApproximately(1.7, 1e-9);
    }

    // ── 3×3 asymmetric ───────────────────────────────────────────────────────

    [Fact]
    public void Solve_3x3_AsymmetricMatrix_ReturnsGlobalOptimum()
    {
        var matrix = new double[3, 3]
        {
            { 0.1, 0.2, 0.9 },
            { 0.9, 0.1, 0.2 },
            { 0.2, 0.9, 0.1 }
        };
        var result = HungarianAlgorithm.SolveMaximumAssignment(matrix);
        result.Should().HaveCount(3);
        result.Distinct().Should().HaveCount(3, because: "bijection: every column used exactly once");

        // Each row picks its max column → total = 2.7
        TotalProfit(matrix, result).Should().BeApproximately(2.7, 1e-9);
        result[0].Should().Be(2);
        result[1].Should().Be(0);
        result[2].Should().Be(1);
    }
}
