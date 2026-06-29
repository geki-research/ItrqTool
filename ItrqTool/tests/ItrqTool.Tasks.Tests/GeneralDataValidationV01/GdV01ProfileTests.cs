using FluentAssertions;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.QuestionnaireValidation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using Xunit;

namespace ItrqTool.Tasks.Tests.GeneralDataValidationV01;

// Wiring tests for GdV01Profile.Build.
// Verifies:
//   1. Structural shape — extension count, flags, empty baseline/DvRoles.
//   2. Run-smoke (end-to-end) — three distinct findings (missing-input, constraint, deviation).
//   3. L section gate — only in-gate sections fire material-change required-input.
//   4. Gate-halt — malformed key halts with only gate findings; Halted == true.
// Assertions: Check + CellAddress (+ CheckResult substring) for findings (lesson 112).
public sealed class GdV01ProfileTests
{
    // ── Config builder ───────────────────────────────────────────────────────────

    // materialChangeSections = the section names whose column L is a required input. The two declared
    // sections (G-CO row 3, G-ST row 10) get MaterialChangeRequired = membership in that set, so the
    // profile derives the same L-gate set the old flat MaterialChangeSections produced.
    private static GdV01Config MakeConfig(IReadOnlyList<string>? materialChangeSections = null)
    {
        var lset = (materialChangeSections ?? ["G-ST"]).ToHashSet(StringComparer.Ordinal);
        return new()
        {
            QuestionNumberColumn = "C", TextColumn = "D", GuidanceColumn = "E",
            RequestedTypeColumn = "F", PreviousAnswerColumn = "G", AnswerColumn = "H",
            RequestedExplanationColumn = "I", PreviousExplanationColumn = "J",
            CurrentExplanationColumn = "K", MaterialChangeColumn = "L",
            ProvidedByColumn = "O", XrefIdColumn = "Q",
            SheetName = "General Data",
            Sections =
            [
                new GdSectionSpec(3,  4,  9,  "G-CO", lset.Contains("G-CO")),
                new GdSectionSpec(10, 11, 43, "G-ST", lset.Contains("G-ST")),
            ],
            DeviationThreshold = 0.25,
        };
    }

    // ── Answer / Question builders ────────────────────────────────────────────────

    private static GdAnswer Ans(
        string? answerId, int anchorRow,
        string? answer = null, string? materialChange = null, string? providedBy = null,
        string? dvType = null, string? dvOp = null, string? dvFormula = null, string? dvFormula2 = null) =>
        new(AnswerId: answerId, AnchorRow: anchorRow,
            PreviousAnswer: null, Answer: answer, MaterialChange: materialChange,
            ProvidedBy: providedBy, Explanations: [],
            AnswerDvType: dvType, AnswerDvOperator: dvOp, AnswerDvFormula: dvFormula, AnswerDvFormula2: dvFormula2);

    private static GdV01Question Q(
        string xrefId, int row, IReadOnlyList<GdAnswer> answers,
        string section = "G-CO", string origText = "orig", string qText = "What?") =>
        new(RowNumber: row, XrefId: xrefId, OriginalText: origText, QuestionText: qText,
            SectionName: section, QuestionNumber: "1", Answers: answers);

    private static GdV01ParseResult PR(params GdV01Question[] questions) =>
        new(questions.ToList(), [], []);

    private static GdV01ParseResult Empty() => new([], [], []);

    // ── Alignment via GdV01Aligner ────────────────────────────────────────────────

    private static AlignmentResult<GdV01Question> Align(
        GdV01ParseResult current,
        GdV01ParseResult? template = null,
        GdV01ParseResult? previous = null) =>
        GdV01Aligner.Align(current, template ?? Empty(), previous ?? Empty());

    // ── RunFromAlignedGated helper ────────────────────────────────────────────────

    private static ValidationRunResult Run(
        AlignmentResult<GdV01Question> alignment, GdV01Config config) =>
        ValidationPipeline.RunFromAlignedGated(
            alignment,
            GdV01Profile.Build(config),
            new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new List<TaskMessage>(),
            CancellationToken.None);

    // ─────────────────────────────────────────────────────────────────────────────
    // Test 1 — Structural shape
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_StructuralShape()
    {
        var config = MakeConfig();
        var profile = GdV01Profile.Build(config);

        // Cardinality guard — proof that all 10 extension instances are wired.
        profile.Extensions.Should().HaveCount(10);
        profile.HaltOnMalformedKeys.Should().BeTrue();
        profile.IdentityGateCheck.Should().NotBeNull();
        // BaselineDescriptors carries ONLY the section-header gate descriptor (registered so the
        // catalogue knows the id / SeverityOverrides can target it); RunBaseline is still a no-op stub.
        profile.BaselineDescriptors.Should().ContainSingle()
            .Which.Id.Should().Be(GdSectionHeaderGate.MismatchId);
        profile.DvRoles.Should().BeEmpty();

        // RunBaseline must be the identity stub (always returns empty).
        var alignment = Align(Empty());
        var allDescriptors = profile.BaselineDescriptors
            .Concat(profile.Extensions.SelectMany(e => e.Descriptors))
            .Concat(profile.IdentityGateCheck?.Descriptors ?? []);
        var emitter = new FindingEmitter(
            new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(allDescriptors));
        profile.RunBaseline(alignment, emitter).Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Test 2 — Run-smoke: three distinct findings (wiring end-to-end)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RunSmoke_ThreeDistinctFindingsFire()
    {
        // G-ST in MaterialChangeSections; all questions in G-CO so L gate never fires.
        var config = MakeConfig(["G-ST"]);

        // Q1 — blank H answer → input-cell.answer.missing at H10.
        var curQ1 = Q("Q1", 10, [Ans("A-01", 10, answer: null, providedBy: "Unit-A",
                                     dvType: "WholeNumber")]);
        var tplQ1 = Q("Q1", 11, [Ans("A-01", 11, dvType: "WholeNumber")]);

        // Q2 — DV operator changed (between→greaterThan) → constraint.answer-dv.validation-rule-changed at H20.
        //   Current DV: WholeNumber/greaterThan/>0. Template DV: WholeNumber/between/1–10.
        //   Value "200" conforms to greaterThan 0 → no conformance finding; only constraint fires.
        var curQ2 = Q("Q2", 20,
            [Ans("A-01", 20, answer: "200", dvType: "WholeNumber", dvOp: "greaterThan", dvFormula: "0")],
            origText: "How?", qText: "How?");
        var tplQ2 = Q("Q2", 21,
            [Ans("A-01", 21, dvType: "WholeNumber", dvOp: "between", dvFormula: "1", dvFormula2: "10")],
            origText: "How?", qText: "How?");

        // Q3 — numeric deviation 100→200 (100 % > 25 % threshold), CrossYear=Agree → cross-year.answer-deviation at H30.
        var curQ3 = Q("Q3", 30,
            [Ans("A-01", 30, answer: "200", dvType: "WholeNumber")],
            origText: "Rate?", qText: "Rate?");
        var tplQ3 = Q("Q3", 31,
            [Ans("A-01", 31, dvType: "WholeNumber")],
            origText: "Rate?", qText: "Rate?");
        var prevQ3 = Q("Q3", 32,
            [Ans("A-01", 32, answer: "100", dvType: "WholeNumber")],
            origText: "Rate?", qText: "Rate?");

        var alignment = Align(
            PR(curQ1, curQ2, curQ3),
            PR(tplQ1, tplQ2, tplQ3),
            PR(prevQ3));

        var result = Run(alignment, config);

        result.Halted.Should().BeFalse();

        result.Findings.Should().Contain(f =>
            f.Check == ValidationCheck.MissingResponse && f.CellAddresses == "H10",
            "blank H answer at row 10 must fire input-cell.answer.missing");

        result.Findings.Should().Contain(f =>
            f.Check == ValidationCheck.FrozenConstraint && f.CellAddresses == "H20",
            "changed DV operator at row 20 must fire constraint.answer-dv.validation-rule-changed");

        result.Findings.Should().Contain(f =>
            f.Check == ValidationCheck.Deviation && f.CellAddresses == "H30",
            "numeric deviation at row 30 must fire cross-year.answer-deviation");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Test 3 — L section gate: out-of-gate vs in-gate behaviour
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LGate_OutOfGateSection_DoesNotFire()
    {
        var config = MakeConfig(["G-ST"]);  // G-CO is NOT in gate

        var cur = Q("Q1", 10, [Ans("A-01", 10, answer: "Yes", materialChange: null)],
            section: "G-CO");
        var tpl = Q("Q1", 11, [Ans("A-01", 11)]);

        var result = Run(Align(PR(cur), PR(tpl), Empty()), config);

        result.Findings.Should().NotContain(f =>
            f.Check == ValidationCheck.MissingResponse && f.CellAddresses == "L10",
            "out-of-gate section G-CO must not fire input-cell.material-change.missing");
    }

    [Fact]
    public void LGate_InGateSection_Fires()
    {
        var config = MakeConfig(["G-ST"]);  // G-ST IS in gate

        var cur = Q("Q1", 10, [Ans("A-01", 10, answer: "Yes", materialChange: null, providedBy: "Unit-A")],
            section: "G-ST", origText: "Q1", qText: "Q1");
        var tpl = Q("Q1", 11, [Ans("A-01", 11)],
            origText: "Q1", qText: "Q1");

        var result = Run(Align(PR(cur), PR(tpl), Empty()), config);

        result.Findings.Should().Contain(f =>
            f.Check == ValidationCheck.MissingResponse && f.CellAddresses == "L10",
            "in-gate section G-ST with blank L must fire input-cell.material-change.missing");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Test 4 — Gate-halt: malformed key halts the pipeline
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GateHalt_MalformedKey_HaltsWithGateFindingsOnly()
    {
        var config = MakeConfig();

        // A parse result with no questions but one malformed (blank XrefId) entry.
        var malformedParse = new GdV01ParseResult(
            [],
            [new GdMalformedXref(5, null, GdMalformedXrefReason.Blank)],
            []);

        var alignment = Align(malformedParse, Empty(), Empty());

        alignment.MalformedKeys.Should().HaveCount(1, "one malformed entry must be in the alignment");

        var result = Run(alignment, config);

        result.Halted.Should().BeTrue();
        result.Findings.Should().NotBeEmpty("gate must emit at least one finding for the malformed key");
        result.Findings.Should().OnlyContain(f => f.Check == ValidationCheck.Structure,
            "gate-halted run must emit only gate findings (structure.xrefid-empty-or-duplicated)");
    }
}
