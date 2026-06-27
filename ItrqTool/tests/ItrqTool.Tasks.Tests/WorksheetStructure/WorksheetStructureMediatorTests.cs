using FluentAssertions;
using ItrqTool.Domain;
using ItrqTool.Tasks.WorksheetStructure;
using NSubstitute;
using Xunit;

namespace ItrqTool.Tasks.Tests.WorksheetStructure;

public sealed class WorksheetStructureMediatorTests : IDisposable
{
    private readonly string _baseDir;
    private readonly string _schemasDir;
    private readonly IExcelStructureReader _reader = Substitute.For<IExcelStructureReader>();

    public WorksheetStructureMediatorTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), "itrq-mediator-" + Guid.NewGuid().ToString("N"));
        _schemasDir = Path.Combine(_baseDir, "schemas");
        Directory.CreateDirectory(_schemasDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_baseDir, recursive: true); } catch { /* best effort */ }
    }

    private WorksheetStructureMediator NewMediator() =>
        new(_reader, new WorksheetStructureSchemaLoader(),
            [new SchemaVerificationStrategyV1()], _baseDir);

    private void WriteSchema(string fileName, string json) =>
        File.WriteAllText(Path.Combine(_schemasDir, fileName), json);

    private const string SynSchema = """
        {
          "schemaFormatVersion": 1,
          "questionnaire": "syn",
          "questionnaireVersion": "v01",
          "sheetName": "Synthetic",
          "headerRow": 2,
          "columns": [
            { "column": "C", "canonicalHeader": "" },
            { "column": "H", "canonicalHeader": "↓ Answer ↓" }
          ]
        }
        """;

    private static ExcelCellStructure Cell(string text) => new(text, null, null, null);

    [Fact]
    public void MatchingWorkbook_ReturnsMatch()
    {
        WriteSchema("syn-v01.structure.json", SynSchema);
        _reader.ReadCells("book.xlsx", "Synthetic", Arg.Any<IReadOnlyList<string>>())
            .Returns(new Dictionary<string, ExcelCellStructure>
            {
                ["C2"] = Cell(""),
                ["H2"] = Cell("↓ Answer ↓"),
            });

        var result = NewMediator().Verify("book.xlsx", new WorksheetSchemaRef("syn", "v01"));

        result.Outcome.Should().Be(WorksheetStructureOutcome.Match);
    }

    [Fact]
    public void WrongHeader_ReturnsMismatchWithOneFatalFinding()
    {
        WriteSchema("syn-v01.structure.json", SynSchema);
        _reader.ReadCells("book.xlsx", "Synthetic", Arg.Any<IReadOnlyList<string>>())
            .Returns(new Dictionary<string, ExcelCellStructure>
            {
                ["C2"] = Cell(""),
                ["H2"] = Cell("Response"),
            });

        var result = NewMediator().Verify("book.xlsx", new WorksheetSchemaRef("syn", "v01"));

        result.Outcome.Should().Be(WorksheetStructureOutcome.Mismatch);
        result.Findings.Should().ContainSingle();
        result.Findings[0].Evaluation.Should().Be(Domain.Validation.FindingEvaluation.Fatal);
    }

    [Fact]
    public void MissingSchemaFile_ReturnsAssetError_AndDoesNotReadWorkbook()
    {
        // no schema written
        var result = NewMediator().Verify("book.xlsx", new WorksheetSchemaRef("syn", "v01"));

        result.Outcome.Should().Be(WorksheetStructureOutcome.AssetError);
        result.AssetErrorReason.Should().NotBeNullOrEmpty();
        _reader.DidNotReceive().ReadCells(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>());
    }

    [Fact]
    public void UnsupportedSchemaFormatVersion_ReturnsAssetError_AndDoesNotReadWorkbook()
    {
        WriteSchema("syn-v01.structure.json", """
            {
              "schemaFormatVersion": 999,
              "questionnaire": "syn",
              "questionnaireVersion": "v01",
              "sheetName": "Synthetic",
              "headerRow": 2,
              "columns": [ { "column": "H", "canonicalHeader": "Answer" } ]
            }
            """);

        var result = NewMediator().Verify("book.xlsx", new WorksheetSchemaRef("syn", "v01"));

        result.Outcome.Should().Be(WorksheetStructureOutcome.AssetError);
        result.AssetErrorReason.Should().Contain("999");
        _reader.DidNotReceive().ReadCells(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>());
    }
}
