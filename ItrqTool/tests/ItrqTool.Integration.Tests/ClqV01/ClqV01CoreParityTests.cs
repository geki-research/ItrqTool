using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Infrastructure;
using ItrqTool.Tasks;
using ItrqTool.Tasks.ControlLevelQuestionValidation;
using ItrqTool.Tasks.ControlLevelQuestionValidationV01Core;
using ItrqTool.Tasks.QuestionnaireValidation;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.Validation;

namespace ItrqTool.Integration.Tests.ClqV01;

/// <summary>
/// THE PROOF (m3). For each captured scenario, builds the IDENTICAL workbook trio the m2
/// golden harness built (reusing <see cref="ClqV01BaselineFactory"/> + <see cref="ClqV01TrialScenario"/>
/// + <see cref="ClqV01WorkbookWriter"/> via <see cref="ClqV01GoldenHarness.Scenarios"/>), then drives the
/// <b>v01-on-core</b> pipeline (<see cref="ValidationPipeline.Run{T}"/> over <see cref="ClqV01Profile"/>
/// + the temp core config), serializes the report with the CANONICAL TaskType, and asserts ordinal
/// string equality to the committed m2 golden.
///
/// The trio is built from the BESPOKE config (the factory's parameter type) and the core pipeline runs
/// over the temp core config — both carry the same column map (D/E/F/H/I/J/M/N) and sheet name, so the
/// workbooks are identical and the read data is identical.
///
/// Why CANONICAL, not the temp TaskType: the report's <c>taskType</c> field is part of the shipped
/// artifact (<c>ControlLevelQuestionValidation_v01</c> post-m4); the temp <c>…_v01_core</c> string exists
/// only to dodge the DI collision in this co-existence window. m4 switches this test to drive the live,
/// then-canonical task end-to-end.
///
/// If ANY byte-diff appears it is a planner call — the test fails loudly with the diff; it is NOT
/// normalized away.
/// </summary>
public sealed class ClqV01CoreParityTests
{
    private static string CoreConfigAssetPath() =>
        Path.Combine(
            Path.GetDirectoryName(ClqV01GoldenHarness.ConfigAssetPath())!,
            "clq-v01-core-validation-config.json");

    /// <summary>
    /// Builds the scenario trio (bespoke config), runs the v01-on-core pipeline over the temp core
    /// config, and returns the serialized report JSON exactly as the shipping artifact would carry it.
    /// </summary>
    private static async Task<string> DeriveCoreSerializedReportAsync(ClqV01GoldenScenario scenario)
    {
        // Build the identical trio the golden harness builds (bespoke config drives the factory).
        var bespokeConfigJson = await File.ReadAllTextAsync(ClqV01GoldenHarness.ConfigAssetPath());
        var bespokeConfig = ControlLevelQuestionValidationV01ConfigLoader.Load(bespokeConfigJson);
        var baseline = ClqV01BaselineFactory.Build(bespokeConfig);
        var (trio, dvOverrides) = scenario.Compose(baseline);

        // Load the temp v01-on-core config that drives the pipeline.
        var coreConfigJson = await File.ReadAllTextAsync(CoreConfigAssetPath());
        var coreConfig = ConfigLoader.Load<ClqV01Config>(coreConfigJson, c => c.Validate());

        var dir = Path.Combine(Path.GetTempPath(),
            "ItrqTool-clqv01-core-parity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");

            ClqV01WorkbookWriter.Write(currentPath,  coreConfig.SheetName, trio.Current, dvOverrides);
            ClqV01WorkbookWriter.Write(templatePath, coreConfig.SheetName, trio.Template);
            ClqV01WorkbookWriter.Write(previousPath, coreConfig.SheetName, trio.Previous);

            var reader = new ClosedXmlExcelStructureReader(
                NullLogger<ClosedXmlExcelStructureReader>.Instance);

            var messages = new List<TaskMessage>();
            var findings = ValidationPipeline.Run<ClqV01Question>(
                reader,
                currentPath,
                templatePath,
                previousPath,
                ClqV01Profile.Build(coreConfig),
                coreConfig.SeverityOverrides,
                messages,
                CancellationToken.None);

            var report = new ValidationReport(
                coreConfig.SheetName,
                ControlLevelQuestionValidationV01CoreTask.CanonicalTaskType,
                findings);
            return ValidationReportSerializer.Serialize(report);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public async Task BaselineZero_CoreMatchesGolden()
    {
        var scenario = ClqV01GoldenHarness.Scenarios.Single(s => s.Key == "baseline-zero");
        var derived = await DeriveCoreSerializedReportAsync(scenario);
        var golden  = ClqV01GoldenHarness.ReadGolden(scenario);

        derived.Should().Be(golden,
            "the v01-on-core pipeline output for 'baseline-zero' must reproduce the m2 golden byte-for-byte");
    }

    [Fact]
    public async Task Local11_CoreMatchesGolden()
    {
        var scenario = ClqV01GoldenHarness.Scenarios.Single(s => s.Key == "local-11");
        var derived = await DeriveCoreSerializedReportAsync(scenario);
        var golden  = ClqV01GoldenHarness.ReadGolden(scenario);

        derived.Should().Be(golden,
            "the v01-on-core pipeline output for 'local-11' must reproduce the m2 golden byte-for-byte");
    }

    [Fact]
    public async Task Full23_CoreMatchesGolden()
    {
        var scenario = ClqV01GoldenHarness.Scenarios.Single(s => s.Key == "full-23");
        var derived = await DeriveCoreSerializedReportAsync(scenario);
        var golden  = ClqV01GoldenHarness.ReadGolden(scenario);

        derived.Should().Be(golden,
            "the v01-on-core pipeline output for 'full-23' must reproduce the m2 golden byte-for-byte");
    }
}
