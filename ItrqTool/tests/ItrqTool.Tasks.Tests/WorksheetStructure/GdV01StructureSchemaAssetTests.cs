using FluentAssertions;
using ItrqTool.Tasks.WorksheetStructure;
using Xunit;

namespace ItrqTool.Tasks.Tests.WorksheetStructure;

public sealed class GdV01StructureSchemaAssetTests
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
            .Load(new WorksheetSchemaRef("gd", "v01"), SolutionRoot());

        schema.SchemaFormatVersion.Should().Be(1);
        schema.Questionnaire.Should().Be("gd");
        schema.QuestionnaireVersion.Should().Be("v01");
        schema.SheetName.Should().Be("General Data");
        schema.HeaderRow.Should().Be(2);

        schema.Columns.Select(c => c.Column)
            .Should().Equal("C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "O", "Q");

        // GD-v01 is a single-row header; no column overrides the schema headerRow.
        schema.Columns.Should().OnlyContain(c => c.Row == null);

        // Column C's header is blank in the real template.
        schema.Columns.Single(c => c.Column == "C").CanonicalHeader.Should().BeEmpty();

        // No GD-v01 column declares accepted variants.
        schema.Columns.Should().OnlyContain(c => c.AcceptedVariants == null);

        // H is plural "Answers" (distinct from CLQ/RLQ "Answer").
        var h = schema.Columns.Single(c => c.Column == "H");
        HeaderNormalization.Normalize(h.CanonicalHeader).Should().Be("↓ ANSWERS ↓");

        var q = schema.Columns.Single(c => c.Column == "Q");
        HeaderNormalization.Normalize(q.CanonicalHeader).Should().Be("ERSTE GROUP INTERNAL ID");
    }
}
