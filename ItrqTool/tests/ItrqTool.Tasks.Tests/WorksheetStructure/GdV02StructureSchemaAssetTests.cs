using FluentAssertions;
using ItrqTool.Tasks.WorksheetStructure;
using Xunit;

namespace ItrqTool.Tasks.Tests.WorksheetStructure;

public sealed class GdV02StructureSchemaAssetTests
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
            .Load(new WorksheetSchemaRef("gd", "v02"), SolutionRoot());

        schema.SchemaFormatVersion.Should().Be(1);
        schema.Questionnaire.Should().Be("gd");
        schema.QuestionnaireVersion.Should().Be("v02");
        schema.SheetName.Should().Be("General Data");
        schema.HeaderRow.Should().Be(2);

        schema.Columns.Select(c => c.Column)
            .Should().Equal("C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "P", "R");

        schema.Columns.Should().OnlyContain(c => c.Row == null);

        // C header is blank in the template.
        schema.Columns.Single(c => c.Column == "C").CanonicalHeader.Should().BeEmpty();

        schema.Columns.Should().OnlyContain(c => c.AcceptedVariants == null);

        // H is plural "Answers" (distinct from CLQ/RLQ "Answer").
        var h = schema.Columns.Single(c => c.Column == "H");
        HeaderNormalization.Normalize(h.CanonicalHeader).Should().Be("↓ ANSWERS ↓");

        // M carries the how-explanation header (lifted byte-verbatim from rlq-v02).
        var m = schema.Columns.Single(c => c.Column == "M");
        m.CanonicalHeader.Should().NotBeNullOrWhiteSpace();
        HeaderNormalization.Normalize(m.CanonicalHeader)
            .Should().Be("↓ EXPLANATION OF THE MATERIAL CHANGE OF DEFINITION ↓");

        var r = schema.Columns.Single(c => c.Column == "R");
        HeaderNormalization.Normalize(r.CanonicalHeader).Should().Be("ERSTE GROUP INTERNAL ID");
    }
}
