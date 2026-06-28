using System.IO;
using ClosedXML.Excel;
using ItrqTool.Tasks.WorksheetStructure;

namespace ItrqTool.Integration.Tests.WorksheetStructure;

/// <summary>
/// Stamps a worksheet's header cells with the canonical values from the shipped structure schema,
/// guaranteeing that the gate will return Match for that worksheet. Loads the schema from the
/// committed source tree (slnx-walk), so the stamp is always byte-identical to the schema asset —
/// immune to typo drift (e.g. the "Strenghts" template typo or the ↓ glyph).
/// </summary>
public static class StructureHeaderStamper
{
    public static void Stamp(IXLWorksheet ws, string questionnaire, string version)
    {
        var schema = new WorksheetStructureSchemaLoader()
            .Load(new WorksheetSchemaRef(questionnaire, version), SchemasBaseDir());
        foreach (var col in schema.Columns)
        {
            if (string.IsNullOrEmpty(col.CanonicalHeader)) continue;
            ws.Cell($"{col.Column}{col.Row ?? schema.HeaderRow}").Value = col.CanonicalHeader;
        }
    }

    /// <summary>
    /// Resolves the directory that contains the <c>schemas/</c> folder by walking up from the
    /// test output directory until a <c>*.slnx</c> file is found — the same convention used by
    /// the Tasks.Tests schema asset tests (<see cref="WorksheetStructureSchemaLoader"/> docs).
    /// </summary>
    private static string SchemasBaseDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException(
                "Solution root (.slnx) not found above test output directory.");
    }
}
