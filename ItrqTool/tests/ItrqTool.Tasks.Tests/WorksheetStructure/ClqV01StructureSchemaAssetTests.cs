using FluentAssertions;
using ItrqTool.Tasks.WorksheetStructure;
using Xunit;

namespace ItrqTool.Tasks.Tests.WorksheetStructure;

public sealed class ClqV01StructureSchemaAssetTests
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
            .Load(new WorksheetSchemaRef("clq", "v01"), SolutionRoot());

        schema.SchemaFormatVersion.Should().Be(1);
        schema.Questionnaire.Should().Be("clq");
        schema.QuestionnaireVersion.Should().Be("v01");
        schema.SheetName.Should().Be("IT Risk Control Self-Assessment");
        schema.HeaderRow.Should().Be(2);

        schema.Columns.Select(c => c.Column)
            .Should().Equal("D", "E", "F", "G", "H", "I", "J", "M", "N");

        // I and J are the two-row header columns (row 3); all others default to headerRow 2.
        schema.Columns.Single(c => c.Column == "I").Row.Should().Be(3);
        schema.Columns.Single(c => c.Column == "J").Row.Should().Be(3);
        schema.Columns.Where(c => c.Column != "I" && c.Column != "J")
            .Should().OnlyContain(c => c.Row == null);

        // Column N's header is blank in the real template (derived from clq-v02 O).
        schema.Columns.Single(c => c.Column == "N").CanonicalHeader.Should().BeEmpty();

        // No CLQ-v01 column declares accepted variants.
        schema.Columns.Should().OnlyContain(c => c.AcceptedVariants == null);

        // Column M carries the "provided by" header (= clq-v02 column N).
        var m = schema.Columns.Single(c => c.Column == "M");
        HeaderNormalization.Normalize(m.CanonicalHeader).Should().Be("↓ ANSWER (TO BE) PROVIDED BY ↓");

        // Typo "Strenghts" is preserved.
        var i = schema.Columns.Single(c => c.Column == "I");
        HeaderNormalization.Normalize(i.CanonicalHeader).Should().Be("↓ STRENGHTS ↓");
    }
}
