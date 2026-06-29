using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.GeneralDataValidationV01;
using Xunit;

namespace ItrqTool.Integration.Tests.GdV01;

/// <summary>
/// Exact-set / halt tests for the GD-local, fail-loud section-header gate
/// (<c>structure.section-header-mismatch</c>, Fatal, <see cref="ValidationCheck.Structure"/>).
/// The gate runs PRE-align and short-circuits the pipeline: a declared section whose actual column-D
/// header diverges from its configured <c>ExpectedName</c> halts the run with ONLY the mismatch
/// finding(s). Also proves the HARD design constraint (declared-anchored, NOT discovery-anchored):
/// an UNDECLARED section-like row below the declared sections is neither parsed nor flagged (G2).
/// </summary>
public sealed class GdV01SectionHeaderMismatchTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-gdv01-header", Guid.NewGuid().ToString("N"));

    // Baseline config but with the G-ST section's ExpectedName overridden — to simulate a config
    // that declares the wrong header name (a config-typo, as opposed to a drifted workbook).
    private static GdV01Config ConfigWithGStExpectedName(string expectedName) => new()
    {
        QuestionNumberColumn = "C", TextColumn = "D", GuidanceColumn = "E",
        RequestedTypeColumn = "F", PreviousAnswerColumn = "G", AnswerColumn = "H",
        RequestedExplanationColumn = "I", PreviousExplanationColumn = "J",
        CurrentExplanationColumn = "K", MaterialChangeColumn = "L",
        ProvidedByColumn = "O", XrefIdColumn = "Q",
        SheetName = "General Data",
        Sections =
        [
            new GdSectionSpec(3,  4,  9,  "G-CO",        false),
            new GdSectionSpec(10, 11, 43, expectedName,  true),
        ],
        DeviationThreshold = 0.25,
    };

    [Fact]
    public void DriftedHeader_InCurrentOnly_HaltsWithSingleFatalMismatchFinding()
    {
        // Drift the current workbook's D10 header to "WRONG" (config ExpectedName = "G-ST").
        // Template + previous keep "G-ST", so exactly ONE mismatch (current) is expected, and the
        // run halts before the pipeline (no other findings).
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");

            GdV01BaselineFactory.WriteCurrent(currentPath);
            GdV01BaselineFactory.WriteTemplate(templatePath);
            GdV01BaselineFactory.WritePrevious(previousPath);

            using (var wb = new XLWorkbook(currentPath))
            {
                wb.Worksheets.First().Cell(10, "D").Value = "WRONG";
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeTrue("a drifted section header halts the run before the pipeline");
            GdV01Assert.Exactly(result.Findings,
                new GdV01ExpectedFinding(
                    ValidationCheck.Structure, FindingEvaluation.Fatal,
                    "D10", "expected 'G-ST'"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void ConfigExpectedNameTypo_MismatchesAllThreeWorkbooks_Halts()
    {
        // Config declares ExpectedName "TYPO" for the G-ST section; all three workbooks carry the
        // real "G-ST" header → one mismatch PER workbook (current, template, previous), all at D10.
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");

            GdV01BaselineFactory.WriteCurrent(currentPath);
            GdV01BaselineFactory.WriteTemplate(templatePath);
            GdV01BaselineFactory.WritePrevious(previousPath);

            var result = GdV01PipelineRunner.Run(ConfigWithGStExpectedName("TYPO"),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeTrue();
            result.Findings.Should().HaveCount(3, "one mismatch per workbook");
            result.Findings.Should().OnlyContain(f =>
                f.Check == ValidationCheck.Structure &&
                f.Evaluation == FindingEvaluation.Fatal &&
                f.CellAddresses == "D10" &&
                f.CheckResult.Contains("expected 'TYPO'", StringComparison.Ordinal));
            result.Findings.Select(f => f.CheckResult).Should().Contain(
                s => s.Contains("the current response", StringComparison.Ordinal));
            result.Findings.Select(f => f.CheckResult).Should().Contain(
                s => s.Contains("the empty template", StringComparison.Ordinal));
            result.Findings.Select(f => f.CheckResult).Should().Contain(
                s => s.Contains("the previous response", StringComparison.Ordinal));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void MatchingHeaders_CleanTrio_NoMismatchAndRunsToCompletion()
    {
        // Sanity: the clean trio's headers match the config ExpectedNames → no mismatch, not halted,
        // zero findings (the gate is silent; the pipeline runs and is itself clean).
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");

            GdV01BaselineFactory.WriteCurrent(currentPath);
            GdV01BaselineFactory.WriteTemplate(templatePath);
            GdV01BaselineFactory.WritePrevious(previousPath);

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse();
            result.Findings.Should().BeEmpty();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void UndeclaredSectionBelowDeclared_NotDiscoveredNorParsed_NoFindings()
    {
        // HARD CONSTRAINT + G2: add an UNDECLARED "General comments"-style header at D50 plus a data
        // row 51 to the current workbook. The config declares only G-CO (4–9) and G-ST (11–43), so:
        //   - the gate is declared-anchored → it does NOT discover/flag the undeclared D50 header;
        //   - the parser is declared-driven (no trailing-row capture) → rows 50/51 are not parsed,
        //     so no spurious questions / structure findings.
        // Net: zero findings, not halted.
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "current.xlsx");
            var templatePath = Path.Combine(dir, "template.xlsx");
            var previousPath = Path.Combine(dir, "previous.xlsx");

            GdV01BaselineFactory.WriteCurrent(currentPath);
            GdV01BaselineFactory.WriteTemplate(templatePath);
            GdV01BaselineFactory.WritePrevious(previousPath);

            using (var wb = new XLWorkbook(currentPath))
            {
                var ws = wb.Worksheets.First();
                ws.Cell(50, "D").Value = "General comments";          // undeclared section-like header
                ws.Cell(51, "C").Value = "99";
                ws.Cell(51, "D").Value = "Any free text";
                ws.Cell(51, "H").Value = "7";
                ws.Cell(51, "Q").Value = "G-XX-99";                   // would-be question if captured
                wb.Save();
            }

            var result = GdV01PipelineRunner.Run(GdV01BaselineFactory.Config(),
                currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse("the undeclared header is never discovered or flagged");
            result.Findings.Should().BeEmpty(
                "undeclared rows are neither parsed nor flagged; findings: [{0}]",
                string.Join("; ", result.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
