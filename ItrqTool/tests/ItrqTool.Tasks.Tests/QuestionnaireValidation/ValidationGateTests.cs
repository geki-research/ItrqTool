using FluentAssertions;
using ItrqTool.Domain;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Config;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using ItrqTool.Tasks.QuestionnaireValidation.Parsing;
using Xunit;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation;

// Generic coverage for the opt-in identity-integrity gate in ValidationPipeline.RunFromParsedGated<T>.
// Self-contained: a tiny IAlignmentIdentity record, a spy extension (records whether its Run ran), and
// a stub gate-check (records whether its Run ran and emits one finding per malformed key). The gate is
// proven sheet-agnostic: it reads only alignment.MalformedKeys + the profile flag/slot, never RLQ/CLQ types.
public sealed class ValidationGateTests
{
    // ── Local IAlignmentIdentity record ──────────────────────────────────────────

    private sealed record GateTestQuestion(
        int RowNumber,
        string? XrefId,
        string OriginalText,
        string QuestionText,
        string SectionName,
        string? QuestionNumber) : IAlignmentIdentity;

    // ── Spy extension: records whether Run was called; emits nothing ──────────────

    private sealed class SpyExtension : IExtensionCheck<GateTestQuestion>
    {
        public bool RunCalled { get; private set; }

        public IReadOnlyList<FindingDescriptor> Descriptors { get; } =
            [new("test.spy.ext", FindingEvaluation.Warning, ValidationCheck.Structure, "spy")];

        public IReadOnlyList<ValidationFinding> Run(
            AlignmentResult<GateTestQuestion> alignment, FindingEmitter emitter)
        {
            RunCalled = true;
            return Array.Empty<ValidationFinding>();
        }
    }

    // ── Stub gate-check: records whether Run was called; one finding per malformed key ──

    private sealed class StubGateCheck : IExtensionCheck<GateTestQuestion>
    {
        public const string GateId = "test.gate.malformed";
        public bool RunCalled { get; private set; }

        public IReadOnlyList<FindingDescriptor> Descriptors { get; } =
            [new(GateId, FindingEvaluation.Fatal, ValidationCheck.Structure, "gate")];

        public IReadOnlyList<ValidationFinding> Run(
            AlignmentResult<GateTestQuestion> alignment, FindingEmitter emitter)
        {
            RunCalled = true;
            var findings = new List<ValidationFinding>();
            foreach (var mk in alignment.MalformedKeys)
                findings.Add(emitter.Emit(GateId, $"Q{mk.RowNumber}",
                    questionNumber: null, questionText: null, requestedData: null, providedBy: null,
                    $"malformed key at row {mk.RowNumber}"));
            return findings;
        }
    }

    // ── Layout (never read on the RunFromParsed* path) + helpers ──────────────────

    private static readonly QuestionnaireLayout Layout =
        LayoutParser.Parse(["1"], ["2:3-99"], "C", "C", "C");

    private static GateTestQuestion Clean(string xref = "X1") =>
        new(3, xref, "Q", "Q", "Section", null);

    private static GateTestQuestion BlankKey() =>
        new(3, null, "Q", "Q", "Section", null);

    private static IReadOnlyDictionary<string, FindingEvaluation> NoOverrides
        => new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal);

    private static ValidationPipelineProfile<GateTestQuestion> Profile(
        SpyExtension spy,
        StubGateCheck? gate,
        bool haltOnMalformedKeys,
        out bool[] baselineRanBox)
    {
        var ran = new bool[1];
        baselineRanBox = ran;
        return new ValidationPipelineProfile<GateTestQuestion>(
            SheetName: "Sheet1",
            Layout: Layout,
            RecordFactory: _ => throw new InvalidOperationException("RecordFactory not used on RunFromParsed path"),
            DvRoles: [],
            BaselineDescriptors: [],
            RunBaseline: (_, __) => { ran[0] = true; return Array.Empty<ValidationFinding>(); },
            Extensions: [spy],
            HaltOnMalformedKeys: haltOnMalformedKeys,
            IdentityGateCheck: gate);
    }

    // ── Test 1 — flag ON + malformed keys → halt: only gate findings, chain not run ──

    [Fact]
    public void GateOn_MalformedKeys_HaltsWithOnlyGateFindings()
    {
        var spy = new SpyExtension();
        var gate = new StubGateCheck();
        var profile = Profile(spy, gate, haltOnMalformedKeys: true, out var baselineRan);

        var malformed = new[] { BlankKey() };
        var clean = new[] { Clean() };

        var result = ValidationPipeline.RunFromParsedGated(
            malformed, clean, clean, profile, NoOverrides,
            new List<TaskMessage>(), CancellationToken.None);

        result.Halted.Should().BeTrue();
        result.Findings.Should().HaveCount(1);
        result.Findings[0].Check.Should().Be(ValidationCheck.Structure);
        result.Findings[0].Evaluation.Should().Be(FindingEvaluation.Fatal);
        gate.RunCalled.Should().BeTrue("the gate check is the halt emitter");
        spy.RunCalled.Should().BeFalse("extensions must not run on a halt");
        baselineRan[0].Should().BeFalse("the baseline must not run on a halt");
    }

    // ── Test 2 — flag ON + clean keys → full chain, gate not run ──────────────────

    [Fact]
    public void GateOn_CleanKeys_RunsFullChain_GateNotRun()
    {
        var spy = new SpyExtension();
        var gate = new StubGateCheck();
        var profile = Profile(spy, gate, haltOnMalformedKeys: true, out var baselineRan);

        var clean = new[] { Clean() };

        var result = ValidationPipeline.RunFromParsedGated(
            clean, clean, clean, profile, NoOverrides,
            new List<TaskMessage>(), CancellationToken.None);

        result.Halted.Should().BeFalse();
        spy.RunCalled.Should().BeTrue("the extension chain runs when keys are clean");
        baselineRan[0].Should().BeTrue("the baseline runs when keys are clean");
        gate.RunCalled.Should().BeFalse("the gate emitter must not run when keys are clean");
    }

    // ── Test 3 — flag OFF + malformed keys → gate inert, full chain runs (CLQ-shaped) ──

    [Fact]
    public void GateOff_MalformedKeys_GateInert_FullChainRuns()
    {
        // CLQ-shaped: gate slot null and flag off. Even with a malformed key present the gate is inert
        // and the chain runs normally — this locks CLQ behaviour-preservation.
        var spy = new SpyExtension();
        var profile = Profile(spy, gate: null, haltOnMalformedKeys: false, out var baselineRan);

        var malformed = new[] { BlankKey() };
        var clean = new[] { Clean() };

        var result = ValidationPipeline.RunFromParsedGated(
            malformed, clean, clean, profile, NoOverrides,
            new List<TaskMessage>(), CancellationToken.None);

        result.Halted.Should().BeFalse();
        spy.RunCalled.Should().BeTrue("the full chain runs when the gate is off");
        baselineRan[0].Should().BeTrue("the baseline runs when the gate is off");
    }

    // ── Test 4 — catalogue includes gate-check descriptors ────────────────────────

    [Fact]
    public void GateCheckDescriptors_AreInCatalogue_OverrideKeyAccepted_AndEmitDoesNotThrow()
    {
        // A severityOverrides key equal to the gate-check's descriptor id must be ACCEPTED (no
        // ConfigException) — proving the gate-check descriptors joined the catalogue — and the gate's
        // Emit must resolve that registered id without throwing.
        var spy = new SpyExtension();
        var gate = new StubGateCheck();
        var profile = Profile(spy, gate, haltOnMalformedKeys: true, out _);

        var overrides = new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal)
        {
            [StubGateCheck.GateId] = FindingEvaluation.Error,
        };

        var malformed = new[] { BlankKey() };
        var clean = new[] { Clean() };

        var act = () => ValidationPipeline.RunFromParsedGated(
            malformed, clean, clean, profile, overrides,
            new List<TaskMessage>(), CancellationToken.None);

        var result = act.Should().NotThrow().Subject;
        result.Halted.Should().BeTrue();
        result.Findings.Should().ContainSingle()
            .Which.Evaluation.Should().Be(FindingEvaluation.Error, "the override applies to the gate finding");
    }

    // ── Test 5 — non-gated wrapper returns the gated result's Findings ────────────

    [Fact]
    public void RunFromParsed_Wrapper_ReturnsSameFindings_AsGated()
    {
        var spyA = new SpyExtension();
        var profileA = Profile(spyA, gate: null, haltOnMalformedKeys: false, out _);
        var spyB = new SpyExtension();
        var profileB = Profile(spyB, gate: null, haltOnMalformedKeys: false, out _);

        var clean = new[] { Clean() };

        var viaWrapper = ValidationPipeline.RunFromParsed(
            clean, clean, clean, profileA, NoOverrides,
            new List<TaskMessage>(), CancellationToken.None);
        var viaGated = ValidationPipeline.RunFromParsedGated(
            clean, clean, clean, profileB, NoOverrides,
            new List<TaskMessage>(), CancellationToken.None);

        viaWrapper.Should().Equal(viaGated.Findings);
    }
}
