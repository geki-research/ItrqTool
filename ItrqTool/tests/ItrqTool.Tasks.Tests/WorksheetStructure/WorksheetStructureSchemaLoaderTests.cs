using FluentAssertions;
using ItrqTool.Tasks.WorksheetStructure;
using Xunit;

namespace ItrqTool.Tasks.Tests.WorksheetStructure;

public sealed class WorksheetStructureSchemaLoaderTests : IDisposable
{
    private readonly string _baseDir;
    private readonly string _schemasDir;
    private readonly WorksheetStructureSchemaLoader _loader = new();

    public WorksheetStructureSchemaLoaderTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), "itrq-schema-loader-" + Guid.NewGuid().ToString("N"));
        _schemasDir = Path.Combine(_baseDir, "schemas");
        Directory.CreateDirectory(_schemasDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_baseDir, recursive: true); } catch { /* best effort */ }
    }

    private void WriteSchema(string fileName, string json) =>
        File.WriteAllText(Path.Combine(_schemasDir, fileName), json);

    [Fact]
    public void ValidJson_RoundTripsToPopulatedRecord()
    {
        WriteSchema("syn-v01.structure.json", """
            {
              "schemaFormatVersion": 1,
              "questionnaire": "syn",
              "questionnaireVersion": "v01",
              "sheetName": "Synthetic",
              "headerRow": 2,
              "columns": [
                { "column": "C", "canonicalHeader": "" },
                { "column": "H", "canonicalHeader": "Answer", "acceptedVariants": ["Response"] }
              ]
            }
            """);

        var schema = _loader.Load(new WorksheetSchemaRef("syn", "v01"), _baseDir);

        schema.SchemaFormatVersion.Should().Be(1);
        schema.Questionnaire.Should().Be("syn");
        schema.QuestionnaireVersion.Should().Be("v01");
        schema.SheetName.Should().Be("Synthetic");
        schema.HeaderRow.Should().Be(2);
        schema.Columns.Should().HaveCount(2);
        schema.Columns[0].Column.Should().Be("C");
        schema.Columns[0].CanonicalHeader.Should().BeEmpty();
        schema.Columns[0].Row.Should().BeNull();
        schema.Columns[0].AcceptedVariants.Should().BeNull();
        schema.Columns[1].AcceptedVariants.Should().Equal("Response");
    }

    [Fact]
    public void MissingFile_ThrowsWorksheetSchemaLoadException()
    {
        var act = () => _loader.Load(new WorksheetSchemaRef("nope", "v99"), _baseDir);
        act.Should().Throw<WorksheetSchemaLoadException>();
    }

    [Fact]
    public void MalformedJson_ThrowsWorksheetSchemaLoadException()
    {
        WriteSchema("syn-v01.structure.json", "{ not valid json ");
        var act = () => _loader.Load(new WorksheetSchemaRef("syn", "v01"), _baseDir);
        act.Should().Throw<WorksheetSchemaLoadException>();
    }

    [Fact]
    public void UnknownProperty_ThrowsWorksheetSchemaLoadException()
    {
        WriteSchema("syn-v01.structure.json", """
            {
              "schemaFormatVersion": 1,
              "questionnaire": "syn",
              "questionnaireVersion": "v01",
              "sheetName": "Synthetic",
              "headerRow": 2,
              "columns": [],
              "unexpectedExtraKey": true
            }
            """);
        var act = () => _loader.Load(new WorksheetSchemaRef("syn", "v01"), _baseDir);
        act.Should().Throw<WorksheetSchemaLoadException>();
    }
}
