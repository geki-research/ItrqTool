using System.Text.Json;
using FluentAssertions;
using Xunit;
using ItrqTool.Domain.Reporting;
using ItrqTool.Infrastructure.Reporting;

namespace ItrqTool.Infrastructure.Tests;

public sealed class HtmlCellRangeDiffReportWriterTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-cellrange-writer-tests", Guid.NewGuid().ToString("N"));

    private static HtmlCellRangeDiffReportWriter Writer() => new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static string ExtractEmbeddedJson(string html)
    {
        const string open  = "<script id=\"reportData\" type=\"application/json\">";
        const string close = "</script>";
        var start = html.IndexOf(open, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "embedded JSON script tag must be present");
        var contentStart = start + open.Length;
        var end = html.IndexOf(close, contentStart, StringComparison.Ordinal);
        end.Should().BeGreaterThan(-1);
        return html[contentStart..end].Trim();
    }

    // ── Basic changed/unchanged cells are embedded in JSON ─────────────────

    [Fact]
    public void WriteReport_ChangedAndUnchanged_EmbeddedJsonContainsBothCells()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report.html");
            var data = new HtmlDiffCellRangeReportData(
                Title: "Test Report",
                File1Path: @"C:\work\f1.xlsx",
                File2Path: @"C:\work\f2.xlsx",
                Sheet1Name: "Sheet1",
                Sheet2Name: "Sheet1",
                IncludeValidationFormatting: false,
                GeneratedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Changed: new List<HtmlDiffCellRangeChangedCell>
                {
                    new("A2", "A", 2, "old", "new", TextChanged: true,
                        "—", "—", DvChanged: false, "—", "—", CfChanged: false)
                },
                Unchanged: new List<HtmlDiffCellRangeUnchangedCell>
                {
                    new("A1", "A", 1, "same", "—", "—")
                });

            Writer().WriteReport(data, filePath);

            var html = File.ReadAllText(filePath);
            var json = ExtractEmbeddedJson(html);
            var doc  = JsonDocument.Parse(json);

            var changed   = doc.RootElement.GetProperty("changed");
            var unchanged = doc.RootElement.GetProperty("unchanged");

            changed.GetArrayLength().Should().Be(1);
            changed[0].GetProperty("address").GetString().Should().Be("A2");
            changed[0].GetProperty("file1Value").GetString().Should().Be("old");
            changed[0].GetProperty("file2Value").GetString().Should().Be("new");
            changed[0].GetProperty("textChanged").GetBoolean().Should().BeTrue();

            unchanged.GetArrayLength().Should().Be(1);
            unchanged[0].GetProperty("address").GetString().Should().Be("A1");
            unchanged[0].GetProperty("value").GetString().Should().Be("same");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── IncludeValidationFormatting = true: DV/CF data in JSON ────────────

    [Fact]
    public void WriteReport_IncludeValidationFormatting_DvCfDataEmbeddedInJson()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "report-vf.html");
            var data = new HtmlDiffCellRangeReportData(
                Title: "VF Report",
                File1Path: "f1.xlsx",
                File2Path: "f2.xlsx",
                Sheet1Name: "S",
                Sheet2Name: "S",
                IncludeValidationFormatting: true,
                GeneratedAt: DateTimeOffset.Now,
                Changed: new List<HtmlDiffCellRangeChangedCell>
                {
                    new("B5", "B", 5, null, null, TextChanged: false,
                        "List: Yes | No", "List: Yes | No | N/A", DvChanged: true,
                        "—", "—", CfChanged: false)
                },
                Unchanged: Array.Empty<HtmlDiffCellRangeUnchangedCell>());

            Writer().WriteReport(data, filePath);

            var html = File.ReadAllText(filePath);
            var json = ExtractEmbeddedJson(html);
            var doc  = JsonDocument.Parse(json);

            doc.RootElement.GetProperty("includeValidationFormatting").GetBoolean().Should().BeTrue();

            var cell = doc.RootElement.GetProperty("changed")[0];
            cell.GetProperty("dvChanged").GetBoolean().Should().BeTrue();
            cell.GetProperty("file1DvDisplay").GetString().Should().Contain("Yes");
            cell.GetProperty("file2DvDisplay").GetString().Should().Contain("N/A");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Empty lists produce valid HTML (no throw) ─────────────────────────

    [Fact]
    public void WriteReport_EmptyChangedAndUnchanged_WritesValidHtml()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "empty.html");
            var data = new HtmlDiffCellRangeReportData(
                Title: "Empty",
                File1Path: "a.xlsx", File2Path: "b.xlsx",
                Sheet1Name: "S", Sheet2Name: "S",
                IncludeValidationFormatting: false,
                GeneratedAt: DateTimeOffset.Now,
                Changed: Array.Empty<HtmlDiffCellRangeChangedCell>(),
                Unchanged: Array.Empty<HtmlDiffCellRangeUnchangedCell>());

            var act = () => Writer().WriteReport(data, filePath);
            act.Should().NotThrow();

            var html = File.ReadAllText(filePath);
            html.Should().Contain("<!DOCTYPE html>");

            var json = ExtractEmbeddedJson(html);
            var doc  = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("changed").GetArrayLength().Should().Be(0);
            doc.RootElement.GetProperty("unchanged").GetArrayLength().Should().Be(0);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
