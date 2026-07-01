using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Infrastructure;

namespace ItrqTool.Infrastructure.Tests;

public sealed class ClosedXmlExcelStructureReaderResolveDefinedNameValuesTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-infra-resolvedefinedname-tests", Guid.NewGuid().ToString("N"));

    private static ClosedXmlExcelStructureReader Reader() =>
        new(NullLogger<ClosedXmlExcelStructureReader>.Instance);

    // Builds a workbook with:
    //   - "Lists" sheet: A1="Yes", A2="No"
    //   - workbook-scoped name "MyAllowed" → Lists!A1:A2
    //   - worksheet-scoped name "WsScoped" on "Lists" → Lists!A1:A2
    //   - "Questions" sheet (the DV-cell sheet, no named ranges of its own)
    private static string BuildWorkbook(string dir)
    {
        var path = Path.Combine(dir, "wb.xlsx");
        using var wb = new XLWorkbook();
        var listsWs = wb.Worksheets.Add("Lists");
        listsWs.Cell("A1").Value = "Yes";
        listsWs.Cell("A2").Value = "No";
        wb.DefinedNames.Add("MyAllowed", listsWs.Range("A1:A2"));
        listsWs.DefinedNames.Add("WsScoped", listsWs.Range("A1:A2"));
        wb.Worksheets.Add("Questions");
        wb.SaveAs(path);
        return path;
    }

    // 1. Workbook-scoped name resolved when called with the DV-cell sheet (not the backing sheet).
    [Fact]
    public void ResolveDefinedNameValues_WorkbookScopedName_ReturnsValues()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var path = BuildWorkbook(dir);

            var result = Reader().ResolveDefinedNameValues(path, "Questions", "MyAllowed");

            result.Should().NotBeNull();
            result!.Should().Equal("Yes", "No");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // 2. Absent name → null (no exception; NotCheckable downstream).
    [Fact]
    public void ResolveDefinedNameValues_AbsentName_ReturnsNull()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var path = BuildWorkbook(dir);

            var result = Reader().ResolveDefinedNameValues(path, "Questions", "DoesNotExist");

            result.Should().BeNull();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // 3. Worksheet-scoped name on the DV-cell sheet → resolves (ws scope takes precedence).
    //    And a ws-scoped name read from a DIFFERENT sheet → null (not visible cross-sheet).
    [Fact]
    public void ResolveDefinedNameValues_WorksheetScopedName_ResolvesFromOwnSheet_NullFromOtherSheet()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var path = BuildWorkbook(dir);

            // "WsScoped" is defined on "Lists"; resolved when sheetName="Lists"
            var fromOwnSheet = Reader().ResolveDefinedNameValues(path, "Lists", "WsScoped");
            fromOwnSheet.Should().NotBeNull();
            fromOwnSheet!.Should().Equal("Yes", "No");

            // "WsScoped" is NOT visible from "Questions" (workbook scope has no such name either)
            var fromOtherSheet = Reader().ResolveDefinedNameValues(path, "Questions", "WsScoped");
            fromOtherSheet.Should().BeNull();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
