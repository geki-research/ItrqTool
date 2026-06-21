using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Checks;

// E1 coverage for MalformedKeyCheck<T>: structure check for blank or duplicate XrefId keys.
// One descriptor (structure.xrefid-empty-or-duplicated, Fatal, Structure); iterates
// alignment.MalformedKeys with no workbook filter; one finding per entry. Sheet-agnostic:
// uses a minimal local IAlignmentIdentity record; no dependency on the Clq/Rlq harnesses.
public sealed class MalformedKeyCheckTests
{
    // ── Local test fixtures ──────────────────────────────────────────────────────

    private sealed record CheckTestQuestion(
        int RowNumber,
        string? XrefId = "X1",
        string OriginalText = "orig",
        string QuestionText = "What?",
        string SectionName = "Section",
        string? QuestionNumber = "1") : IAlignmentIdentity;

    private const string Column = "Q";

    private static MalformedKeyCheck<CheckTestQuestion> Primitive() => new(Column);

    // Only MalformedKeys is under test; Aligned and WithinYearRemoved are always empty.
    private static AlignmentResult<CheckTestQuestion> Result(params MalformedKey[] malformed) =>
        new(
            Array.Empty<AlignedQuestion<CheckTestQuestion>>(),
            Array.Empty<CheckTestQuestion>(),
            malformed);

    private static FindingEmitter Emitter(
        MalformedKeyCheck<CheckTestQuestion> primitive,
        IReadOnlyDictionary<string, FindingEvaluation>? overrides = null) =>
        new(
            overrides ?? new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(primitive.Descriptors));

    // ── Tests ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Descriptors_ExactlyOne_IdFatalStructure()
    {
        var primitive = Primitive();

        primitive.Descriptors.Should().ContainSingle();
        var d = primitive.Descriptors[0];
        d.Id.Should().Be("structure.xrefid-empty-or-duplicated");
        d.DefaultEvaluation.Should().Be(FindingEvaluation.Fatal);
        d.Check.Should().Be(ValidationCheck.Structure);
        d.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void BlankEntry_EmitsOneFinding_ContainsBlank()
    {
        var primitive = Primitive();
        var mk = new MalformedKey(ValidationWorkbook.CurrentResponse, 7, null, MalformedKeyReason.Blank);
        var findings = primitive.Run(Result(mk), Emitter(primitive));

        var f = findings.Should().ContainSingle().Subject;
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Fatal);
        f.CellAddresses.Should().Be("Q7");
        f.CheckResult.Should().Contain("blank");
        f.QuestionNumber.Should().BeNull();
        f.QuestionText.Should().BeNull();
    }

    [Fact]
    public void DuplicateEntry_EmitsOneFinding_ContainsDuplicatedAndValue()
    {
        var primitive = Primitive();
        var mk = new MalformedKey(ValidationWorkbook.CurrentResponse, 6, "x1", MalformedKeyReason.Duplicate);
        var findings = primitive.Run(Result(mk), Emitter(primitive));

        var f = findings.Should().ContainSingle().Subject;
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Fatal);
        f.CellAddresses.Should().Be("Q6");
        f.CheckResult.Should().Contain("duplicated").And.Contain("x1");
    }

    [Fact]
    public void DuplicateGroupTwoRows_TwoFindings_OnePerRow()
    {
        // ClassifyKeys emits one MalformedKey per duplicate row; this check emits one finding per entry.
        var primitive = Primitive();
        var mk6  = new MalformedKey(ValidationWorkbook.CurrentResponse, 6,  "x1", MalformedKeyReason.Duplicate);
        var mk13 = new MalformedKey(ValidationWorkbook.CurrentResponse, 13, "x1", MalformedKeyReason.Duplicate);
        var findings = primitive.Run(Result(mk6, mk13), Emitter(primitive));

        findings.Should().HaveCount(2);
        findings[0].CellAddresses.Should().Be("Q6");
        findings[1].CellAddresses.Should().Be("Q13");
        findings.Should().AllSatisfy(f =>
        {
            f.Check.Should().Be(ValidationCheck.Structure);
            f.Evaluation.Should().Be(FindingEvaluation.Fatal);
            f.CheckResult.Should().Contain("duplicated").And.Contain("x1");
        });
    }

    [Fact]
    public void EmptyMalformedKeys_NoFindings()
    {
        var primitive = Primitive();
        var findings = primitive.Run(Result(), Emitter(primitive));

        findings.Should().BeEmpty();
    }

    [Fact]
    public void EmptyTemplateEntry_IsEmitted()
    {
        // No workbook filter — EmptyTemplate entries must be surfaced, mirroring CLQ Phase 1.
        var primitive = Primitive();
        var mk = new MalformedKey(ValidationWorkbook.EmptyTemplate, 5, null, MalformedKeyReason.Blank);
        var findings = primitive.Run(Result(mk), Emitter(primitive));

        var f = findings.Should().ContainSingle().Subject;
        f.CellAddresses.Should().Be("Q5");
        f.CheckResult.Should().Contain("empty template");
    }

    [Fact]
    public void PreviousResponseEntry_IsEmitted()
    {
        // No workbook filter — PreviousResponse entries must be surfaced, mirroring CLQ Phase 1.
        var primitive = Primitive();
        var mk = new MalformedKey(ValidationWorkbook.PreviousResponse, 9, "dup", MalformedKeyReason.Duplicate);
        var findings = primitive.Run(Result(mk), Emitter(primitive));

        var f = findings.Should().ContainSingle().Subject;
        f.CellAddresses.Should().Be("Q9");
        f.CheckResult.Should().Contain("previous response").And.Contain("duplicated");
    }

    [Fact]
    public void OrderPreserved_FindingsFollowMalformedKeysOrder()
    {
        var primitive = Primitive();
        var mk1 = new MalformedKey(ValidationWorkbook.CurrentResponse,  6,  "x1", MalformedKeyReason.Duplicate);
        var mk2 = new MalformedKey(ValidationWorkbook.EmptyTemplate,    7,  null, MalformedKeyReason.Blank);
        var mk3 = new MalformedKey(ValidationWorkbook.PreviousResponse, 13, "x2", MalformedKeyReason.Duplicate);
        var findings = primitive.Run(Result(mk1, mk2, mk3), Emitter(primitive));

        findings.Should().HaveCount(3);
        findings[0].CellAddresses.Should().Be("Q6");
        findings[1].CellAddresses.Should().Be("Q7");
        findings[2].CellAddresses.Should().Be("Q13");
    }

    [Fact]
    public void SeverityOverride_Applies()
    {
        var primitive = Primitive();
        var overrides = new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal)
        {
            ["structure.xrefid-empty-or-duplicated"] = FindingEvaluation.Warning,
        };
        var mk = new MalformedKey(ValidationWorkbook.CurrentResponse, 5, null, MalformedKeyReason.Blank);
        var findings = primitive.Run(Result(mk), Emitter(primitive, overrides));

        findings.Should().ContainSingle().Which.Evaluation.Should().Be(FindingEvaluation.Warning);
    }

    [Fact]
    public void ColumnGuard_ThrowsOnNullOrWhitespace()
    {
        var act1 = () => new MalformedKeyCheck<CheckTestQuestion>("");
        var act2 = () => new MalformedKeyCheck<CheckTestQuestion>("   ");
        act1.Should().Throw<ArgumentException>();
        act2.Should().Throw<ArgumentException>();
    }
}
