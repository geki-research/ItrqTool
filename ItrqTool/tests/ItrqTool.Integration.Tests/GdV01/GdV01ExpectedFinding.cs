using FluentAssertions;
using ItrqTool.Domain.Validation;

namespace ItrqTool.Integration.Tests.GdV01;

public sealed record GdV01ExpectedFinding(
    ValidationCheck Check,
    FindingEvaluation Evaluation,
    string CellAddresses,
    string CheckResultSubstring);

public static class GdV01Assert
{
    private static string Dump(IEnumerable<ValidationFinding> findings) =>
        string.Join("; ", findings.Select(f =>
            $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}"));

    /// <summary>
    /// Exact-set assertion: count == expected, each expected is present, no unexpected findings.
    /// Mirrors the RLQ input-presence perturbation test idiom.
    /// </summary>
    public static void Exactly(IReadOnlyList<ValidationFinding> actual, params GdV01ExpectedFinding[] expected)
    {
        actual.Should().HaveCount(expected.Length,
            "expected {0} finding(s); actual: [{1}]", expected.Length, Dump(actual));

        foreach (var ex in expected)
            actual.Should().Contain(f =>
                f.Check == ex.Check &&
                f.Evaluation == ex.Evaluation &&
                f.CellAddresses == ex.CellAddresses &&
                f.CheckResult.Contains(ex.CheckResultSubstring, StringComparison.Ordinal),
                because: $"expected [{ex.Evaluation}] {ex.Check} @ {ex.CellAddresses} containing '{ex.CheckResultSubstring}'");

        var unexpected = actual
            .Where(f => !expected.Any(ex =>
                ex.Check == f.Check && ex.Evaluation == f.Evaluation &&
                ex.CellAddresses == f.CellAddresses &&
                f.CheckResult.Contains(ex.CheckResultSubstring, StringComparison.Ordinal)))
            .ToList();
        unexpected.Should().BeEmpty("no unexpected findings allowed; found: [{0}]", Dump(unexpected));
    }

    /// <summary>Returns the single finding matching <paramref name="check"/>.</summary>
    public static ValidationFinding Single(IReadOnlyList<ValidationFinding> actual, ValidationCheck check)
        => actual.Should().ContainSingle(f => f.Check == check,
                "exactly one {0} finding expected; actual: [{1}]", check, Dump(actual))
            .Subject;
}
