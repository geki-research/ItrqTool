using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Infrastructure;
using ItrqTool.Tasks;
using DiffTextSimilarity = ItrqTool.Tasks.ControlLevelQuestionDiff.TextSimilarity;
using ItrqTool.Tasks.ControlLevelQuestionValidationV01;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.Validation;

namespace ItrqTool.Integration.Tests.ClqV01;

/// <summary>
/// Proves that the baseline trio produced by <see cref="ClqV01BaselineFactory"/> yields
/// zero findings when run through <see cref="ControlLevelQuestionValidationV01Task"/> with
/// the production config. This is the correctness anchor for the generator; if it fails,
/// the baseline logic has a consistency bug.
/// </summary>
public sealed class ClqV01BaselineTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-clqv01-baseline", Guid.NewGuid().ToString("N"));

    private static string FindConfigAssetPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Solution root (.slnx) not found above test output directory.");
        return Path.Combine(dir.FullName, "configs", "clq-v01-validation-config.json");
    }

    [Fact]
    public async Task BaselineTrioWithProductionConfig_ZeroFindings()
    {
        var configPath = FindConfigAssetPath();
        var configJson = await File.ReadAllTextAsync(configPath);
        var config = ConfigLoader.Load<ClqV01Config>(configJson, c => c.Validate());

        var trio = ClqV01BaselineFactory.Build(config);

        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var reportPath   = Path.Combine(dir, "report.json");

            ClqV01WorkbookWriter.Write(currentPath,  config.SheetName, trio.Current);
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

            result.Succeeded.Should().BeTrue(
                "baseline task should succeed; errors: {0}",
                string.Join("; ", result.Messages.Select(m => m.Text)));

            var report = ValidationReportSerializer.Deserialize(
                await File.ReadAllTextAsync(reportPath));

            report.Findings.Should().BeEmpty(
                "baseline trio is fully consistent; no findings expected; got: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    /// <summary>
    /// Guard: when one current H cell carries a narrower DV list than the template,
    /// exactly one <see cref="ValidationCheck.FrozenConstraint"/> finding fires at
    /// the answer cell of that row — no other rows are affected.
    /// </summary>
    [Fact]
    public async Task SingleRowDvChange_ExactlyOneConstraintFinding()
    {
        var configPath = FindConfigAssetPath();
        var configJson = await File.ReadAllTextAsync(configPath);
        var config = ConfigLoader.Load<ClqV01Config>(configJson, c => c.Validate());

        var trio = ClqV01BaselineFactory.Build(config);
        int targetRow = trio.Current.Questions[0].RowNumber;

        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");
            var reportPath   = Path.Combine(dir, "report.json");

            // Current has a narrowed DV list (1,2,3) on the first question row;
            // template keeps the standard list (1,2,3,4).
            ClqV01WorkbookWriter.Write(currentPath, config.SheetName, trio.Current,
                answerDvOverrides: new Dictionary<int, string> { [targetRow] = "\"1,2,3\"" });
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

            result.Succeeded.Should().BeTrue(
                "task should succeed; errors: {0}",
                string.Join("; ", result.Messages.Select(m => m.Text)));

            var report = ValidationReportSerializer.Deserialize(
                await File.ReadAllTextAsync(reportPath));

            report.Findings.Should().HaveCount(1,
                "only the DV-change row should fire; got: {0}",
                string.Join("; ", report.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}")));

            var finding = report.Findings[0];
            finding.Check.Should().Be(ValidationCheck.FrozenConstraint);
            finding.CellAddresses.Should().Be($"{config.AnswerColumn}{targetRow}");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    /// <summary>
    /// Guard: the 193 real audit question texts are all pairwise distinct (no exact duplicates)
    /// and the global maximum pairwise <see cref="DiffTextSimilarity.Score"/> is below 0.85.
    /// The measured max on the production question set is 0.737, giving comfortable margin.
    /// This ensures the cross-year matcher's self-match (1.0) is strictly dominant over any
    /// off-diagonal similarity, preventing cascade assignments.
    /// </summary>
    [Fact]
    public async Task BaselineFactory_QuestionTexts_GloballyDistinctAndMaxSimilarityBelow085()
    {
        var configPath = FindConfigAssetPath();
        var configJson = await File.ReadAllTextAsync(configPath);
        var config = ConfigLoader.Load<ClqV01Config>(configJson, c => c.Validate());

        var trio = ClqV01BaselineFactory.Build(config);
        var questions = trio.Current.Questions;
        var texts = questions.Select(q => q.OriginalText!).ToList();

        // 1. Global uniqueness — no exact duplicates.
        texts.Distinct(StringComparer.Ordinal).Should().HaveCount(texts.Count,
            "all {0} baseline question texts must be globally unique (no exact duplicates)",
            texts.Count);

        // 2. Global max pairwise similarity < 0.85 — diagonal strictly dominant for all
        //    cross-year section comparisons (self-match = 1.0; nearest distractor ≤ 0.737).
        double maxScore = 0.0;
        string? worstPair = null;
        for (int i = 0; i < texts.Count; i++)
        {
            for (int j = i + 1; j < texts.Count; j++)
            {
                double score = DiffTextSimilarity.Score(texts[i], texts[j]);
                if (score > maxScore)
                {
                    maxScore = score;
                    worstPair =
                        $"rows {questions[i].RowNumber}/{questions[j].RowNumber}: " +
                        $"score={score:F3}";
                }
            }
        }
        maxScore.Should().BeLessThan(0.85,
            "global max pairwise TextSimilarity.Score must be < 0.85 " +
            "(measured max on real questions: 0.737); worst pair: {0}", worstPair);
    }
}
