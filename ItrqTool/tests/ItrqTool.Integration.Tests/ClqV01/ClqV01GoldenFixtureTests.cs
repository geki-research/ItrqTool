using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Infrastructure;
using ItrqTool.Tasks;
using ItrqTool.Tasks.ControlLevelQuestionValidation;

namespace ItrqTool.Integration.Tests.ClqV01;

/// <summary>
/// Identifies one captured golden scenario: its stable key, the embedded golden
/// resource file name, and how to build the perturbed trio + DV overrides that
/// drive the frozen bespoke v01 task.
/// </summary>
public sealed record ClqV01GoldenScenario(
    string Key,
    string FileName,
    Func<ClqV01BaselineTrio, (ClqV01BaselineTrio Trio, IReadOnlyDictionary<int, string> DvOverrides)> Compose);

/// <summary>
/// Shared harness for the CLQ_v01 golden fixtures. Drives the REAL
/// <see cref="ControlLevelQuestionValidationV01Task"/> over a temp working directory
/// exactly as the shipping task runs, then returns the serialized report JSON it wrote.
/// This guarantees the captured golden is byte-identical to production task output.
///
/// Used by both the one-off capture step (m2) and the assert tests below, which pin
/// the frozen stack and serve as the durable byte-for-byte parity anchor for the
/// v01-on-core re-implementation (m3) — surviving the bespoke stack's deletion (m4).
/// </summary>
public static class ClqV01GoldenHarness
{
    public const string GoldenResourcePrefix = "ItrqTool.Integration.Tests.ClqV01.golden.";

    public static readonly IReadOnlyList<ClqV01GoldenScenario> Scenarios =
    [
        // baseline-zero — unperturbed 193-q trio → 0 findings.
        new("baseline-zero", "clq-v01-baseline-zero.json",
            baseline => (baseline, new Dictionary<int, string>())),

        // local-11 — ten local perturbations → 11 findings.
        new("local-11", "clq-v01-local-11.json",
            baseline =>
            {
                var s = ClqV01TrialScenario.Build(baseline);
                return (s.Trio, s.CurrentAnswerDvOverrides);
            }),

        // full-23 — local + structural perturbations → 23 findings.
        new("full-23", "clq-v01-full-23.json",
            baseline =>
            {
                var structural = ClqV01TrialScenario.BuildStructuralPerturbations(baseline);
                var s = ClqV01TrialScenario.Build(baseline, structural);
                return (s.Trio, s.CurrentAnswerDvOverrides);
            }),
    ];

    private static DirectoryInfo FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException(
            "Solution root (.slnx) not found above test output directory.");
    }

    public static string ConfigAssetPath() =>
        Path.Combine(FindSolutionRoot().FullName, "configs", "clq-v01-validation-config.json");

    /// <summary>
    /// Path of the tracked golden source file (used by the one-off capture step).
    /// </summary>
    public static string GoldenSourcePath(ClqV01GoldenScenario scenario) =>
        Path.Combine(FindSolutionRoot().FullName,
            "tests", "ItrqTool.Integration.Tests", "ClqV01", "golden", scenario.FileName);

    /// <summary>
    /// Builds the scenario trio, runs the real frozen v01 task over a temp working
    /// directory, and returns the exact JSON the task wrote to its <c>report</c> output.
    /// </summary>
    public static async Task<string> DeriveSerializedReportAsync(ClqV01GoldenScenario scenario)
    {
        var configPath = ConfigAssetPath();
        var configJson = await File.ReadAllTextAsync(configPath);
        var config = ControlLevelQuestionValidationV01ConfigLoader.Load(configJson);

        var baseline = ClqV01BaselineFactory.Build(config);
        var (trio, dvOverrides) = scenario.Compose(baseline);

        var dir = Path.Combine(Path.GetTempPath(),
            "ItrqTool-clqv01-golden", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var reportPath   = Path.Combine(dir, "report.json");

            ClqV01WorkbookWriter.Write(currentPath,  config.SheetName, trio.Current, dvOverrides);
            ClqV01WorkbookWriter.Write(templatePath, config.SheetName, trio.Template);
            ClqV01WorkbookWriter.Write(previousPath, config.SheetName, trio.Previous);

            var task = new ControlLevelQuestionValidationV01Task(
                new ClosedXmlExcelStructureReader(
                    NullLogger<ClosedXmlExcelStructureReader>.Instance),
                NullLogger<ControlLevelQuestionValidationV01Task>.Instance);

            var ctx = new TaskExecutionContext(
                TaskId: "validate",
                InputPaths: new Dictionary<string, string>
                {
                    ["currentResponse"]  = currentPath,
                    ["emptyTemplate"]    = templatePath,
                    ["previousResponse"] = previousPath
                },
                OutputPaths: new Dictionary<string, string> { ["report"] = reportPath },
                Logger: NullLogger.Instance,
                WorkingDirectory: dir)
            {
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["configurationFullFilename"] = configPath
                }
            };

            var result = await task.ExecuteAsync(ctx, CancellationToken.None);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    "v01 task failed while deriving golden; messages: " +
                    string.Join("; ", result.Messages.Select(m => m.Text)));

            return await File.ReadAllTextAsync(reportPath);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    public static string ReadGolden(ClqV01GoldenScenario scenario)
    {
        var resourceName = GoldenResourcePrefix + scenario.FileName;
        using var stream = typeof(ClqV01GoldenHarness).Assembly
            .GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded golden resource '{resourceName}' not found. " +
                $"Ensure 'ClqV01\\golden\\{scenario.FileName}' is marked EmbeddedResource in the .csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// Pins the FROZEN bespoke CLQ_v01 stack: re-derives each scenario's serialized report
/// from the same harness and asserts it equals the committed golden by ordinal string
/// equality. Proves the capture is deterministic/reproducible and is the durable parity
/// anchor that the v01-on-core re-implementation reproduces byte-for-byte.
/// </summary>
public sealed class ClqV01GoldenFixtureTests
{
    [Fact]
    public async Task BaselineZero_MatchesCommittedGolden()
    {
        var scenario = ClqV01GoldenHarness.Scenarios.Single(s => s.Key == "baseline-zero");
        var derived = await ClqV01GoldenHarness.DeriveSerializedReportAsync(scenario);
        var golden  = ClqV01GoldenHarness.ReadGolden(scenario);

        derived.Should().Be(golden,
            "the frozen v01 task output for 'baseline-zero' must match the committed golden byte-for-byte");
    }

    [Fact]
    public async Task Local11_MatchesCommittedGolden()
    {
        var scenario = ClqV01GoldenHarness.Scenarios.Single(s => s.Key == "local-11");
        var derived = await ClqV01GoldenHarness.DeriveSerializedReportAsync(scenario);
        var golden  = ClqV01GoldenHarness.ReadGolden(scenario);

        derived.Should().Be(golden,
            "the frozen v01 task output for 'local-11' must match the committed golden byte-for-byte");
    }

    [Fact]
    public async Task Full23_MatchesCommittedGolden()
    {
        var scenario = ClqV01GoldenHarness.Scenarios.Single(s => s.Key == "full-23");
        var derived = await ClqV01GoldenHarness.DeriveSerializedReportAsync(scenario);
        var golden  = ClqV01GoldenHarness.ReadGolden(scenario);

        derived.Should().Be(golden,
            "the frozen v01 task output for 'full-23' must match the committed golden byte-for-byte");
    }
}
