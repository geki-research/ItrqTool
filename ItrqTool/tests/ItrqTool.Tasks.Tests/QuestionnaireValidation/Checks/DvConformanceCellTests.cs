using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Checks;

// Coverage for the DvConformanceCell<T> wrapper: role-templated descriptor, the JoinedByXrefId
// gate (template required), present-gate (blank skipped — finding 1's territory), emit-only-on-
// NotConformant, anchor address + InputConformance check, AddedInResponse/malformed skips, the
// unresolved-List NotCheckable skip, and severity-override flow. Mirrors FrozenConstraintCellTests'
// self-contained local-record + tiny-AlignmentResult-builder style (no Clq-harness dependency).
public sealed class DvConformanceCellTests
{
    // value lives on the CURRENT question; the DV fields + resolved list live on the TEMPLATE match.
    private sealed record DvTestQuestion(
        int RowNumber,
        string? Value,
        string? DvType,
        string? DvOp,
        string? DvFormula,
        string? DvFormula2,
        IReadOnlyList<string>? ListValues = null,
        string? ProvidedBy = null,
        string? XrefId = "X1",
        string OriginalText = "orig",
        string QuestionText = "What?",
        string SectionName = "Section",
        string? QuestionNumber = "1") : IAlignmentIdentity;

    private const string Role = "answer";
    private const string Column = "H";

    private static DvConformanceCell<DvTestQuestion> Primitive(
        FindingEvaluation def = FindingEvaluation.Error) =>
        new(q => q.Value,
            q => q.DvType, q => q.DvOp, q => q.DvFormula, q => q.DvFormula2,
            q => q.ListValues, q => q.ProvidedBy, Role, Column, def);

    private static AlignedQuestion<DvTestQuestion> Aq(
        DvTestQuestion cur,
        WithinYearJoin withinYear = WithinYearJoin.JoinedByXrefId,
        DvTestQuestion? tmpl = null) =>
        new(Current: cur, WithinYear: withinYear, TemplateMatch: tmpl,
            RowShifted: false, TextMismatched: false,
            CrossYear: CrossYearOutcome.Neither, PreviousMatch: null,
            XrefIdCounterpart: null, MatcherCandidate: null, MatcherBaseScore: null);

    private static AlignmentResult<DvTestQuestion> Result(params AlignedQuestion<DvTestQuestion>[] rows) =>
        new(rows.ToList(), Array.Empty<DvTestQuestion>(), Array.Empty<MalformedKey>());

    private static FindingEmitter Emitter(
        DvConformanceCell<DvTestQuestion> primitive,
        IReadOnlyDictionary<string, FindingEvaluation>? overrides = null) =>
        new(overrides ?? new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(primitive.Descriptors));

    private static DvTestQuestion Tmpl(int row, string? type, string? op, string? f1,
        string? f2 = null, IReadOnlyList<string>? list = null) =>
        new(row, Value: null, type, op, f1, f2, list);

    private static DvTestQuestion Cur(int row, string? value, string? providedBy = null) =>
        new(row, value, DvType: null, DvOp: null, DvFormula: null, DvFormula2: null, ProvidedBy: providedBy);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Descriptor_IsRoleTemplatedNotConformant()
    {
        var d = Primitive().Descriptors.Should().ContainSingle().Subject;
        d.Id.Should().Be("input-cell.answer.not-conformant");
        d.Check.Should().Be(ValidationCheck.InputConformance);
        d.DefaultEvaluation.Should().Be(FindingEvaluation.Error);
        d.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void NotConformant_EmitsAtAnchorAddress_WithInputConformanceCheck()
    {
        var tmpl = Tmpl(6, "WholeNumber", "EqualOrGreaterThan", "0");
        var cur = Cur(6, "ans1", providedBy: "Unit-B"); // text under WholeNumber → not conformant
        var p = Primitive();

        var f = p.Run(Result(Aq(cur, tmpl: tmpl)), Emitter(p)).Should().ContainSingle().Subject;

        f.Check.Should().Be(ValidationCheck.InputConformance);
        f.Evaluation.Should().Be(FindingEvaluation.Error);
        f.CellAddresses.Should().Be("H6");
        f.ProvidedBy.Should().Be("Unit-B");
        f.CheckResult.Should().Contain("H6");
    }

    [Fact]
    public void ConformantValue_NoFinding()
    {
        var tmpl = Tmpl(6, "WholeNumber", "EqualOrGreaterThan", "0");
        var cur = Cur(6, "3");
        var p = Primitive();
        p.Run(Result(Aq(cur, tmpl: tmpl)), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void BlankCurrentValue_NoFinding_PresentGate()
    {
        var tmpl = Tmpl(6, "WholeNumber", "EqualOrGreaterThan", "0");
        var cur = Cur(6, "   "); // whitespace-only → present-gate skips (finding 1's territory)
        var p = Primitive();
        p.Run(Result(Aq(cur, tmpl: tmpl)), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void AddedInResponse_NoTemplate_NoFinding()
    {
        var cur = Cur(8, "ans1");
        var p = Primitive();
        p.Run(Result(Aq(cur, WithinYearJoin.AddedInResponse, tmpl: null)), Emitter(p))
            .Should().BeEmpty();
    }

    [Fact]
    public void MalformedRow_NoFinding()
    {
        var cur = Cur(9, "ans1");
        var p = Primitive();
        p.Run(Result(Aq(cur, WithinYearJoin.NotEvaluatedMalformedKey, tmpl: null)), Emitter(p))
            .Should().BeEmpty();
    }

    [Fact]
    public void ListUnresolved_NotCheckable_NoFinding()
    {
        var tmpl = Tmpl(7, "List", null, "\"Yes,No\"", list: null); // source not yet resolved
        var cur = Cur(7, "Maybe");
        var p = Primitive();
        p.Run(Result(Aq(cur, tmpl: tmpl)), Emitter(p)).Should().BeEmpty();
    }

    [Fact]
    public void ListResolved_NonMember_Emits()
    {
        var tmpl = Tmpl(7, "List", null, "\"Yes,No\"", list: new[] { "Yes", "No" });
        var cur = Cur(7, "Maybe");
        var p = Primitive();
        p.Run(Result(Aq(cur, tmpl: tmpl)), Emitter(p))
            .Should().ContainSingle().Which.CellAddresses.Should().Be("H7");
    }

    [Fact]
    public void SeverityOverride_Applies()
    {
        var tmpl = Tmpl(6, "WholeNumber", "EqualOrGreaterThan", "0");
        var cur = Cur(6, "ans1");
        var p = Primitive();
        var overrides = new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal)
        {
            ["input-cell.answer.not-conformant"] = FindingEvaluation.Warning,
        };
        p.Run(Result(Aq(cur, tmpl: tmpl)), Emitter(p, overrides))
            .Should().ContainSingle().Which.Evaluation.Should().Be(FindingEvaluation.Warning);
    }
}
