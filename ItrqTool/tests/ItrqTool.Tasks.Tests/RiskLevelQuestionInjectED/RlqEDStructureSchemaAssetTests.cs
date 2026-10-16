using FluentAssertions;
using ItrqTool.Tasks.WorksheetStructure;
using Xunit;

namespace ItrqTool.Tasks.Tests.RiskLevelQuestionInjectED;

/// <summary>
/// Shape and content tests for the shipped ED structure schema assets
/// (<c>schemas/rlq-ed-v01.structure.json</c>, <c>schemas/rlq-ed-v02.structure.json</c>), loaded
/// through the production <see cref="WorksheetStructureSchemaLoader"/> from the solution root, as the
/// standard RLQ schema asset tests do. Unlike those, the ED tests pin every canonical header exactly
/// (raw ordinal), and cross-check the shared headers against the committed standard RLQ schemas.
/// </summary>
public sealed class RlqEDStructureSchemaAssetTests
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

    private static WorksheetStructureSchema Load(string questionnaire, string version)
        => new WorksheetStructureSchemaLoader().Load(new WorksheetSchemaRef(questionnaire, version), SolutionRoot());

    private static string HeaderAt(WorksheetStructureSchema schema, string column)
        => schema.Columns.Single(c => c.Column == column).CanonicalHeader;

    private const string V01PeriodHeader =
        "Questions to the IT Risk Level\r\nUnless specified otherwise, the question shall be answered in relation to the reference year \r\n(1 January 2025 to 31  December 2025)";

    private const string V02PeriodHeader =
        "Questions to the IT Risk Level\r\nUnless specified otherwise, the question shall be answered in relation to the reference year \r\n(1 July 2025 to 30 June 2026)";

    // Design literals (2026-10-15A ED layout), in column order.
    private static readonly (string Column, string Header)[] V01Expected =
    [
        ("C", ""),
        ("D", V01PeriodHeader),
        ("E", "Erste Group Guideline for Answer Selection"),
        ("F", "Requested Type of Information"),
        ("G", "Last Year's Answer"),
        ("H", "Last Year's Answer\r\nfor Holding"),
        ("I", "Last Year's Answer\r\nfor EBOe"),
        ("J", "↓ Answer ↓"),
        ("K", "↓ of which Holding ↓"),
        ("L", "↓ of which EBOe ↓"),
        ("M", "Requested Explanations"),
        ("N", "Last Year's Explanation"),
        ("O", "↓ Explanations ↓"),
        ("P", "Has there been a material change of the definition of this item in comparison to last year's reporting? \n↓"),
        ("S", "↓ Answer (to be) \nprovided by ↓"),
        ("U", "Erste Group Internal ID"),
    ];

    private static readonly (string Column, string Header)[] V02Expected =
    [
        ("C", ""),
        ("D", V02PeriodHeader),
        ("E", "Erste Group Guideline for Answer Selection"),
        ("F", "Requested Type of Information"),
        ("G", "Last Year's Answer"),
        ("H", "Last Year's Answer\r\nfor Holding"),
        ("I", "Last Year's Answer\r\nfor EBOe"),
        ("J", "↓ Answer ↓"),
        ("K", "↓ of which Holding ↓"),
        ("L", "↓ of which EBOe ↓"),
        ("M", "Requested Explanations"),
        ("N", "Last Year's Explanation"),
        ("O", "↓ Explanations ↓"),
        ("P", "↓ Has there been a material change of the definition of this item in comparison to last year's reporting? ↓"),
        ("Q", "↓ Explanation of the material change of definition ↓"),
        ("T", "↓ Answer (to be) \nprovided by ↓"),
        ("V", "Erste Group Internal ID"),
    ];

    // ── S1 + S2. identity, sheet, header row, exact column sequence, no overrides ──

    [Fact]
    public void V01Schema_LoadsWithExpectedIdentityAndColumns()
    {
        var schema = Load("rlq-ed", "v01");

        schema.SchemaFormatVersion.Should().Be(1);
        schema.Questionnaire.Should().Be("rlq-ed");
        schema.QuestionnaireVersion.Should().Be("v01");
        schema.SheetName.Should().Be("IT Risk Level Questions");
        schema.HeaderRow.Should().Be(2);

        schema.Columns.Select(c => c.Column).Should().Equal(
            "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "S", "U");
        schema.Columns.Should().OnlyContain(c => c.Row == null && c.AcceptedVariants == null);
    }

    [Fact]
    public void V02Schema_LoadsWithExpectedIdentityAndColumns()
    {
        var schema = Load("rlq-ed", "v02");

        schema.SchemaFormatVersion.Should().Be(1);
        schema.Questionnaire.Should().Be("rlq-ed");
        schema.QuestionnaireVersion.Should().Be("v02");
        schema.SheetName.Should().Be("IT Risk Level Questions");
        schema.HeaderRow.Should().Be(2);

        schema.Columns.Select(c => c.Column).Should().Equal(
            "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "T", "V");
        schema.Columns.Should().OnlyContain(c => c.Row == null && c.AcceptedVariants == null);
    }

    // ── S3. every canonical header equals the design literal (raw, ordinal) ───

    [Fact]
    public void V01Schema_EveryHeaderEqualsDesignLiteral()
    {
        Load("rlq-ed", "v01").Columns.Select(c => (c.Column, c.CanonicalHeader))
            .Should().Equal(V01Expected);
    }

    [Fact]
    public void V02Schema_EveryHeaderEqualsDesignLiteral()
    {
        Load("rlq-ed", "v02").Columns.Select(c => (c.Column, c.CanonicalHeader))
            .Should().Equal(V02Expected);
    }

    // ── S4. shared headers equal the committed standard RLQ schema at the remapped letter ──

    [Fact]
    public void V01Schema_SharedHeadersEqualStandardAtRemappedLetters()
    {
        var ed = Load("rlq-ed", "v01");
        var standard = Load("rlq", "v01");

        // ED letter ← standard letter (all 12 standard v01 columns).
        var remap = new (string Ed, string Standard)[]
        {
            ("C", "C"), ("D", "D"), ("E", "E"), ("F", "F"), ("G", "G"), ("J", "H"),
            ("M", "I"), ("N", "J"), ("O", "K"), ("P", "L"), ("S", "O"), ("U", "Q"),
        };

        remap.Select(r => r.Standard).Should().BeEquivalentTo(standard.Columns.Select(c => c.Column),
            "the v01 remap covers every standard v01 column");
        foreach (var (edLetter, standardLetter) in remap)
            string.Equals(HeaderAt(ed, edLetter), HeaderAt(standard, standardLetter), StringComparison.Ordinal)
                .Should().BeTrue($"ED v01 {edLetter} must equal standard v01 {standardLetter}");
    }

    [Fact]
    public void V02Schema_SharedHeadersEqualStandardAtRemappedLetters()
    {
        var ed = Load("rlq-ed", "v02");
        var standard = Load("rlq", "v02");

        // ED letter ← standard letter. D (period) and P (↓ wording) differ by design and are excluded.
        var remap = new (string Ed, string Standard)[]
        {
            ("C", "C"), ("E", "E"), ("F", "F"), ("G", "G"), ("J", "H"), ("M", "I"),
            ("N", "J"), ("O", "K"), ("Q", "M"), ("T", "P"), ("V", "R"),
        };

        foreach (var (edLetter, standardLetter) in remap)
            string.Equals(HeaderAt(ed, edLetter), HeaderAt(standard, standardLetter), StringComparison.Ordinal)
                .Should().BeTrue($"ED v02 {edLetter} must equal standard v02 {standardLetter}");

        HeaderAt(ed, "D").Should().NotBe(HeaderAt(standard, "D"), "the ED v02 reporting period differs by design");
        HeaderAt(ed, "P").Should().NotBe(HeaderAt(standard, "L"), "the ED v02 material-change wording differs by design");
    }

    // ── S5. mutual exclusion (hazard H1): each schema rejects the other's workbook ──

    [Theory]
    [InlineData("v01")]
    [InlineData("v02")]
    public void EdAndStandardSchemas_DisagreeOnASharedLetter(string version)
    {
        var ed = Load("rlq-ed", version);
        var standard = Load("rlq", version);

        var conflicting = ed.Columns
            .Join(standard.Columns, e => e.Column, s => s.Column, (e, s) => (e.Column, Ed: e.CanonicalHeader, Std: s.CanonicalHeader))
            .Where(x => HeaderNormalization.Normalize(x.Ed) != HeaderNormalization.Normalize(x.Std))
            .Select(x => x.Column)
            .ToList();

        conflicting.Should().Contain("H",
            "letter H is declared by both schemas with different headers, so neither accepts the other's workbook");
        HeaderNormalization.Normalize(HeaderAt(ed, "H")).Should().Be("LAST YEAR'S ANSWER FOR HOLDING");
        HeaderNormalization.Normalize(HeaderAt(standard, "H")).Should().Be("↓ ANSWER ↓");
    }
}
