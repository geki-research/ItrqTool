using FluentAssertions;
using ItrqTool.Tasks.WorksheetStructure;
using Xunit;

namespace ItrqTool.Tasks.Tests.WorksheetStructure;

/// <summary>
/// Shape test for the real shipped schema asset, mirroring RlqV01ProductionConfigAssetTests:
/// walk up from the test output dir to the solution root (.slnx) and load schemas/rlq-v01.structure.json
/// through the production loader. Asserts the structural shape (format version, identity, sheet, header row,
/// exact column set, blank-C) — NOT exact multi-line header bytes (newline representation is ClosedXML-dependent).
/// </summary>
public sealed class RlqV01StructureSchemaAssetTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Solution root (.slnx) not found above test output directory.");
        return dir.FullName;
    }

    [Fact]
    public void ShippedSchema_HasExpectedShape()
    {
        var schema = new WorksheetStructureSchemaLoader()
            .Load(new WorksheetSchemaRef("rlq", "v01"), SolutionRoot());

        schema.SchemaFormatVersion.Should().Be(1);
        schema.Questionnaire.Should().Be("rlq");
        schema.QuestionnaireVersion.Should().Be("v01");
        schema.SheetName.Should().Be("IT Risk Level Questions");
        schema.HeaderRow.Should().Be(2);

        schema.Columns.Select(c => c.Column)
            .Should().Equal("C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "O", "Q");

        // Column C's header is blank in the real template.
        schema.Columns.Single(c => c.Column == "C").CanonicalHeader.Should().BeEmpty();

        // No RLQ-v01 column declares accepted variants or a per-column row override.
        schema.Columns.Should().OnlyContain(c => c.AcceptedVariants == null && c.Row == null);

        // One stable value check via the normalizer (avoids asserting newline bytes).
        var q = schema.Columns.Single(c => c.Column == "Q");
        HeaderNormalization.Normalize(q.CanonicalHeader).Should().Be("ERSTE GROUP INTERNAL ID");
    }
}
