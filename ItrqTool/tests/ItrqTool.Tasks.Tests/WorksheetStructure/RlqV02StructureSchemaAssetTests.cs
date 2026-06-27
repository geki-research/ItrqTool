using FluentAssertions;
using ItrqTool.Tasks.WorksheetStructure;
using Xunit;

namespace ItrqTool.Tasks.Tests.WorksheetStructure;

public sealed class RlqV02StructureSchemaAssetTests
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
            .Load(new WorksheetSchemaRef("rlq", "v02"), SolutionRoot());

        schema.SchemaFormatVersion.Should().Be(1);
        schema.Questionnaire.Should().Be("rlq");
        schema.QuestionnaireVersion.Should().Be("v02");
        schema.SheetName.Should().Be("IT Risk Level Questions");
        schema.HeaderRow.Should().Be(2);

        schema.Columns.Select(c => c.Column)
            .Should().Equal("C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "P", "R");

        // RLQ-v02 is a single-row header; no column overrides the schema headerRow.
        schema.Columns.Should().OnlyContain(c => c.Row == null);

        // Column C's header is blank (same as RLQ-v01).
        schema.Columns.Single(c => c.Column == "C").CanonicalHeader.Should().BeEmpty();

        // No RLQ-v02 column declares accepted variants.
        schema.Columns.Should().OnlyContain(c => c.AcceptedVariants == null);

        // M is the new v02 column (supplied string).
        var m = schema.Columns.Single(c => c.Column == "M");
        HeaderNormalization.Normalize(m.CanonicalHeader)
            .Should().Be("↓ EXPLANATION OF THE MATERIAL CHANGE OF DEFINITION ↓");

        // P derives from RLQ-v01 O; R derives from RLQ-v01 Q.
        var p = schema.Columns.Single(c => c.Column == "P");
        HeaderNormalization.Normalize(p.CanonicalHeader).Should().Be("↓ ANSWER (TO BE) PROVIDED BY ↓");

        var r = schema.Columns.Single(c => c.Column == "R");
        HeaderNormalization.Normalize(r.CanonicalHeader).Should().Be("ERSTE GROUP INTERNAL ID");
    }
}
