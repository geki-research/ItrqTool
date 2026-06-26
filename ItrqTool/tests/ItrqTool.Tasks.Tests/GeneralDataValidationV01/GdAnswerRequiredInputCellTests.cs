using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using Xunit;

namespace ItrqTool.Tasks.Tests.GeneralDataValidationV01;

// Tests for GdAnswerRequiredInputCell and the MaterialChangeSections Warnings() rule.
// Assertions: Check + CellAddress (+ CheckResult substring), never id or total count (lesson 112).
public sealed class GdAnswerRequiredInputCellTests
{
    // ── Builders ────────────────────────────────────────────────────────────────

    private static GdAnswer Answer(string? value, int anchorRow, string? provided = null) =>
        new(AnswerId: "A-01", AnchorRow: anchorRow,
            PreviousAnswer: null, Answer: value, MaterialChange: value,
            ProvidedBy: provided, Explanations: []);

    private static GdV01Question Question(
        IReadOnlyList<GdAnswer> answers, string section = "G-ST", int row = 10) =>
        new(RowNumber: row, XrefId: "Q1", OriginalText: "orig", QuestionText: "What?",
            SectionName: section, QuestionNumber: "1", Answers: answers);

    private static AlignedQuestion<GdV01Question> Aq(
        GdV01Question cur, WithinYearJoin wy = WithinYearJoin.JoinedByXrefId) =>
        new(Current: cur, WithinYear: wy, TemplateMatch: null,
            RowShifted: false, TextMismatched: false,
            CrossYear: CrossYearOutcome.Neither, PreviousMatch: null,
            XrefIdCounterpart: null, MatcherCandidate: null, MatcherBaseScore: null);

    private static AlignmentResult<GdV01Question> Result(params AlignedQuestion<GdV01Question>[] rows) =>
        new(rows.ToList(), Array.Empty<GdV01Question>(), Array.Empty<MalformedKey>());

    private static GdAnswerRequiredInputCell HCheck() =>
        new(a => a.Answer, a => a.ProvidedBy, role: "answer", column: "H",
            sectionGate: GdPerAnswerEmit.AllSections);

    private static GdAnswerRequiredInputCell LCheck(IReadOnlySet<string>? sections = null) =>
        new(a => a.MaterialChange, a => a.ProvidedBy, role: "material-change", column: "L",
            sectionGate: sections is not null
                ? GdPerAnswerEmit.SectionsIn(sections)
                : GdPerAnswerEmit.AllSections);

    private static FindingEmitter Emitter(GdAnswerRequiredInputCell check) =>
        new(new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(check.Descriptors));

    // ── H (ungated) tests ───────────────────────────────────────────────────────

    [Fact]
    public void H_ThreeAnswers_OneBlank_SingleFindingAtBlankRow()
    {
        var cur = Question(
        [
            Answer("Yes", 10, "Unit-A"),
            Answer(null,  20, "Unit-B"),   // blank → finding
            Answer("No",  30, "Unit-C"),
        ]);
        var check = HCheck();
        var findings = check.Run(Result(Aq(cur)), Emitter(check));

        var f = findings.Should().ContainSingle().Subject;
        f.Check.Should().Be(ValidationCheck.MissingResponse);
        f.CellAddresses.Should().Be("H20");
        f.CheckResult.Should().Contain("H20");
        f.ProvidedBy.Should().Be("Unit-B");
    }

    [Fact]
    public void H_AllNonBlank_NoFindings()
    {
        var cur = Question([Answer("Yes", 10), Answer("No", 20)]);
        var check = HCheck();
        var findings = check.Run(Result(Aq(cur)), Emitter(check));
        findings.Should().BeEmpty();
    }

    [Fact]
    public void H_WhitespaceOnlyValue_TreatedAsBlank()
    {
        var cur = Question([Answer("   ", 10)]);
        var check = HCheck();
        var findings = check.Run(Result(Aq(cur)), Emitter(check));

        findings.Should().ContainSingle().Which.CellAddresses.Should().Be("H10");
    }

    [Fact]
    public void H_MalformedKeyRow_NoFindings()
    {
        var cur = Question([Answer(null, 10)]);
        var check = HCheck();
        var findings = check.Run(
            Result(Aq(cur, WithinYearJoin.NotEvaluatedMalformedKey)), Emitter(check));
        findings.Should().BeEmpty();
    }

    // ── L (section-gated) tests ─────────────────────────────────────────────────

    [Fact]
    public void L_AnswerInGateSection_Blank_Fires()
    {
        var gate = new HashSet<string>(StringComparer.Ordinal) { "G-ST" };
        var cur  = Question([Answer(null, 10)], section: "G-ST");
        var check = LCheck(gate);
        var findings = check.Run(Result(Aq(cur)), Emitter(check));

        findings.Should().ContainSingle().Which.Check.Should().Be(ValidationCheck.MissingResponse);
        findings[0].CellAddresses.Should().Be("L10");
    }

    [Fact]
    public void L_AnswerOutsideGateSection_DoesNotFire()
    {
        var gate = new HashSet<string>(StringComparer.Ordinal) { "G-ST" };
        var cur  = Question([Answer(null, 10)], section: "G-CO");  // not in gate
        var check = LCheck(gate);
        var findings = check.Run(Result(Aq(cur)), Emitter(check));
        findings.Should().BeEmpty();
    }

    [Fact]
    public void L_SameShapeInGateVsOutOfGate_OnlyInGateFires()
    {
        var gate = new HashSet<string>(StringComparer.Ordinal) { "G-ST" };
        var inGate  = Question([Answer(null, 10)], section: "G-ST");
        var outGate = Question([Answer(null, 20)], section: "G-CO");
        var check = LCheck(gate);

        var findings = check.Run(Result(Aq(inGate), Aq(outGate)), Emitter(check));

        // Only the in-gate answer fires.
        findings.Should().ContainSingle().Which.CellAddresses.Should().Be("L10");
    }

    // ── GdV01Config.Warnings (MaterialChangeSections) ───────────────────────────

    [Fact]
    public void Config_EmptyMaterialChangeSections_ReturnsOneWarning()
    {
        var config = new GdV01Config
        {
            QuestionNumberColumn = "C", TextColumn = "D", GuidanceColumn = "E",
            RequestedTypeColumn = "F", PreviousAnswerColumn = "G", AnswerColumn = "H",
            RequestedExplanationColumn = "I", PreviousExplanationColumn = "J",
            CurrentExplanationColumn = "K", MaterialChangeColumn = "L",
            ProvidedByColumn = "O", XrefIdColumn = "Q",
            SheetName = "General Data", SectionRows = ["3:4-9"],
            DeviationThreshold = 0.25,
            // MaterialChangeSections defaults to [] — should produce a warning
        };

        var warnings = config.Warnings();
        warnings.Should().ContainSingle()
            .Which.Should().Contain("MaterialChangeSections");
    }

    [Fact]
    public void Config_PopulatedMaterialChangeSections_NoWarnings()
    {
        var config = new GdV01Config
        {
            QuestionNumberColumn = "C", TextColumn = "D", GuidanceColumn = "E",
            RequestedTypeColumn = "F", PreviousAnswerColumn = "G", AnswerColumn = "H",
            RequestedExplanationColumn = "I", PreviousExplanationColumn = "J",
            CurrentExplanationColumn = "K", MaterialChangeColumn = "L",
            ProvidedByColumn = "O", XrefIdColumn = "Q",
            SheetName = "General Data", SectionRows = ["3:4-9"],
            DeviationThreshold = 0.25,
            MaterialChangeSections = ["G-ST", "G-FI"],
        };

        config.Warnings().Should().BeEmpty();
    }
}
