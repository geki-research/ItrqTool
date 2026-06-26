using System.IO;
using FluentAssertions;
using ItrqTool.Domain;
using ItrqTool.Infrastructure;
using ItrqTool.Tasks.GeneralDataValidationV01;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ItrqTool.Integration.Tests.GdV01;

/// <summary>
/// Foundational GD C3a tests: clean-parse sanity and non-vacuity on the clean trio.
/// These prove that <see cref="GdV01WorkbookWriter"/> produces a parseable GD workbook
/// and that the full direct pipeline returns zero findings on a clean baseline.
/// </summary>
public sealed class GdV01NonVacuityTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-gdv01-c3a", Guid.NewGuid().ToString("N"));

    private static IExcelStructureReader Reader() =>
        new ClosedXmlExcelStructureReader(NullLogger<ClosedXmlExcelStructureReader>.Instance);

    /// <summary>
    /// Proves that <see cref="GdV01WorkbookWriter.Write"/> produces a workbook that parses
    /// cleanly: the expected question / answer counts are correct and zero malformed XrefIds
    /// are detected.
    /// </summary>
    [Fact]
    public void GdV01WorkbookWriter_CleanWorkbook_ParsesSanely()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "gd_current.xlsx");
            GdV01WorkbookWriter.Write(path);

            var config   = GdV01WorkbookWriter.Config();
            var profile  = GdV01Profile.Build(config);
            var messages = new List<TaskMessage>();

            var rows   = Reader().ReadRows(path, config.SheetName);
            var result = GdV01QuestionParser.Parse(rows, profile.Layout, config, messages);

            // Exactly 2 qid-level question records: Q1 (bare) and Q2 (two answers).
            result.Questions.Should().HaveCount(2,
                "Q1 (bare-qid) + Q2 (qid with two answers A-01 / A-02); got: {0}",
                string.Join(" | ", result.Questions.Select(q =>
                    $"row {q.RowNumber} xref '{q.XrefId}' answers {q.Answers.Count}")));

            // Q1: bare-qid → one implicit answer (AnswerId = null).
            var q1 = result.Questions[0];
            q1.XrefId.Should().Be("Q1");
            q1.RowNumber.Should().Be(4);
            q1.SectionName.Should().Be("G-CO");
            q1.Answers.Should().ContainSingle("Q1 is a bare-qid with one implicit answer");
            q1.Answers[0].AnswerId.Should().BeNull("bare-qid collapses to one answer with null AnswerId");
            q1.Answers[0].AnchorRow.Should().Be(4);
            q1.Answers[0].Answer.Should().Be("1");
            q1.Answers[0].Explanations.Should().ContainSingle(
                "Q1 has one explanation row (I/K on row 4)");
            q1.Answers[0].Explanations[0].Requested.Should().Be("req1");
            q1.Answers[0].Explanations[0].Current.Should().Be("cur1");
            q1.Answers[0].Explanations[0].RowNumber.Should().Be(4);

            // Q2: two answers (A-01 at row 11, A-02 at row 12).
            var q2 = result.Questions[1];
            q2.XrefId.Should().Be("Q2");
            q2.RowNumber.Should().Be(11, "qid anchor = first row = row 11 (Q2:A-01)");
            q2.SectionName.Should().Be("G-ST");
            q2.Answers.Should().HaveCount(2, "Q2:A-01 and Q2:A-02");

            var a01 = q2.Answers[0];
            a01.AnswerId.Should().Be("A-01");
            a01.AnchorRow.Should().Be(11);
            a01.Answer.Should().Be("2");
            a01.Explanations.Should().ContainSingle("A-01 has one explanation row (row 11)");
            a01.Explanations[0].Requested.Should().Be("req2a");
            a01.Explanations[0].Current.Should().Be("cur2a");

            var a02 = q2.Answers[1];
            a02.AnswerId.Should().Be("A-02");
            a02.AnchorRow.Should().Be(12);
            a02.Answer.Should().Be("3");
            a02.Explanations.Should().ContainSingle("A-02 has one explanation row (row 12, I blank)");
            a02.Explanations[0].Requested.Should().BeNull("I is blank on row 12");

            // Zero malformed XrefIds: the workbook was built with valid bare-qid and qid:aid xrefs.
            result.Malformed.Should().BeEmpty(
                "GdV01WorkbookWriter writes only valid XrefIds — no blank, duplicate, or unparseable Q cells");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Proves that the full GD_v01 direct pipeline produces zero findings on the clean trio
    /// (current + template + previous all QID-aligned, DV-conforming, text-matching).
    /// This is the first end-to-end exercise of <c>GdDvPatcher</c>'s inline-DV path on a
    /// real workbook written with ClosedXML.
    /// </summary>
    [Fact]
    public void GdV01Pipeline_CleanTrio_ZeroFindings()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var currentPath  = Path.Combine(dir, "gd_current.xlsx");
            var templatePath = Path.Combine(dir, "gd_template.xlsx");
            var previousPath = Path.Combine(dir, "gd_previous.xlsx");

            GdV01BaselineFactory.WriteCurrent(currentPath);
            GdV01BaselineFactory.WriteTemplate(templatePath);
            GdV01BaselineFactory.WritePrevious(previousPath);

            var config = GdV01BaselineFactory.Config();
            var result = GdV01PipelineRunner.Run(config, currentPath, templatePath, previousPath);

            result.Halted.Should().BeFalse(
                "the clean trio has no malformed XrefIds — the identity gate does not fire");

            result.Findings.Should().BeEmpty(
                "every extension must be silent on a clean baseline; unexpected findings: [{0}]",
                string.Join("; ", result.Findings.Select(f =>
                    $"[{f.Evaluation}] {f.Check} @ {f.CellAddresses}: {f.CheckResult}")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
