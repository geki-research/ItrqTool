using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Clq;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Clq;

// ── Shared test infrastructure for the CLQ baseline checks ───────────────────
//
// Re-typed port of v01's original validation-checks test fixtures, targeting the
// version-neutral surface (ClqBaselineChecks.Run / ClqBaselineRoleMap / IClqBaselineConfig
// / FindingEmitter). D1b/D1c reuse this harness; their checks INSERT into the same
// Run<T> the D1a tests already exercise here.
//
// TestBaselineQuestion is a TEST fixture only — it is NOT v02's question type. It
// carries the identity fields (IAlignmentIdentity) plus the full baseline payload the
// role map selects, so the harness already supports the D1b/D1c selectors.

public sealed record TestBaselineQuestion(
    int RowNumber,
    string? XrefId,
    string? QuestionNumber,
    string QuestionText,
    string OriginalText,
    string ChapterName,
    string SectionName,
    string? Guidance,
    string? PreviousAnswer,
    string? Answer,
    string? Strengths,
    string? Weaknesses,
    string? ProvidedBy,
    string? DvType,
    string? DvFormula,
    string? DvOperator,
    string? DvFormula2,
    bool NumberFormatUnrecognized) : IAlignmentIdentity;

public static class ClqBaselineTestHarness
{
    // The default role map: every selector wired to the matching TestBaselineQuestion field.
    public static readonly ClqBaselineRoleMap<TestBaselineQuestion> DefaultRoles = new(
        Guidance: q => q.Guidance,
        ChapterName: q => q.ChapterName,
        Answer: q => q.Answer,
        Strengths: q => q.Strengths,
        Weaknesses: q => q.Weaknesses,
        PreviousAnswer: q => q.PreviousAnswer,
        ProvidedBy: q => q.ProvidedBy,
        NumberFormatUnrecognized: q => q.NumberFormatUnrecognized,
        AnswerDvType: q => q.DvType,
        AnswerDvOperator: q => q.DvOperator,
        AnswerDvFormula: q => q.DvFormula,
        AnswerDvFormula2: q => q.DvFormula2);

    // ── builders ─────────────────────────────────────────────────────────────────

    public static TestBaselineQuestion Q(
        int rowNumber = 10,
        string? xrefId = "X1",
        string? questionNumber = "1.1",
        string questionText = "What is risk?",
        string? originalText = null,
        string chapterName = "Chapter",
        string sectionName = "Section",
        string? guidance = null,
        string? previousAnswer = null,
        string? answer = null,
        string? strengths = null,
        string? weaknesses = null,
        string? providedBy = null,
        string? dvType = null, string? dvFormula = null, string? dvOperator = null, string? dvFormula2 = null,
        bool numberFormatUnrecognized = false)
        => new(
            RowNumber: rowNumber,
            XrefId: xrefId,
            QuestionNumber: questionNumber,
            QuestionText: questionText,
            OriginalText: originalText ?? questionText,
            ChapterName: chapterName,
            SectionName: sectionName,
            Guidance: guidance,
            PreviousAnswer: previousAnswer,
            Answer: answer,
            Strengths: strengths,
            Weaknesses: weaknesses,
            ProvidedBy: providedBy,
            DvType: dvType,
            DvFormula: dvFormula,
            DvOperator: dvOperator,
            DvFormula2: dvFormula2,
            NumberFormatUnrecognized: numberFormatUnrecognized);

    public static AlignedQuestion<TestBaselineQuestion> Aligned(
        TestBaselineQuestion current,
        WithinYearJoin withinYear = WithinYearJoin.JoinedByXrefId,
        TestBaselineQuestion? templateMatch = null,
        bool rowShifted = false,
        bool textMismatched = false,
        CrossYearOutcome crossYear = CrossYearOutcome.Neither,
        TestBaselineQuestion? previousMatch = null,
        TestBaselineQuestion? xrefIdCounterpart = null,
        TestBaselineQuestion? matcherCandidate = null,
        double? matcherBaseScore = null)
    {
        // JoinedByXrefId requires a non-null template; default to an identical copy so the
        // within-year compares produce no structural noise unless a test asks for it.
        if (withinYear == WithinYearJoin.JoinedByXrefId && templateMatch is null)
            templateMatch = current;
        return new(current, withinYear, templateMatch, rowShifted, textMismatched,
                   crossYear, previousMatch, xrefIdCounterpart, matcherCandidate, matcherBaseScore);
    }

    public static AlignmentResult<TestBaselineQuestion> Result(
        IEnumerable<AlignedQuestion<TestBaselineQuestion>>? aligned = null,
        IEnumerable<TestBaselineQuestion>? removed = null,
        IEnumerable<MalformedKey>? malformed = null)
        => new((aligned ?? []).ToList(), (removed ?? []).ToList(), (malformed ?? []).ToList());

    public static IClqBaselineConfig Config(
        int deviationThreshold = 2,
        IReadOnlyList<string>? allowedAnswers = null,
        string textColumn = "C", string guidanceColumn = "E", string previousAnswerColumn = "F",
        string answerColumn = "H", string strengthsColumn = "I", string weaknessesColumn = "J",
        string xrefIdColumn = "N")
        => new TestConfig
        {
            TextColumn = textColumn,
            GuidanceColumn = guidanceColumn,
            PreviousAnswerColumn = previousAnswerColumn,
            AnswerColumn = answerColumn,
            StrengthsColumn = strengthsColumn,
            WeaknessesColumn = weaknessesColumn,
            XrefIdColumn = xrefIdColumn,
            AllowedAnswers = allowedAnswers ?? new[] { "1", "2", "3", "4", "N/A" },
            DeviationThreshold = deviationThreshold
        };

    // A "clean" answered current question: answer "1" with strengths present, so no
    // input-validity findings fire and within-year/cross-year tests stay isolated.
    public static TestBaselineQuestion Clean(
        int row = 10, string? xref = "X1", string answer = "1", string? strengths = "s")
        => Q(rowNumber: row, xrefId: xref, answer: answer, strengths: strengths);

    public static ValidationFinding OnlyFinding(IReadOnlyList<ValidationFinding> findings)
        => findings.Should().ContainSingle().Subject;

    // ── emitter + run convenience ──────────────────────────────────────────────────

    // Builds a FindingEmitter from C1's baseline catalogue (ClqBaselineFindings → FindingCatalogue)
    // plus an optional SeverityOverrides dictionary.
    public static FindingEmitter Emitter(IReadOnlyDictionary<string, FindingEvaluation>? overrides = null)
        => new(
            overrides ?? new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(ClqBaselineFindings.All));

    // Runs the baseline checks with the default role map and a freshly-built emitter.
    public static IReadOnlyList<ValidationFinding> Run(
        AlignmentResult<TestBaselineQuestion> alignment,
        IClqBaselineConfig? config = null,
        IReadOnlyDictionary<string, FindingEvaluation>? overrides = null)
        => ClqBaselineChecks.Run(alignment, DefaultRoles, config ?? Config(), Emitter(overrides));

    private sealed class TestConfig : IClqBaselineConfig
    {
        public string TextColumn { get; init; } = "C";
        public string GuidanceColumn { get; init; } = "E";
        public string XrefIdColumn { get; init; } = "N";
        public string AnswerColumn { get; init; } = "H";
        public string StrengthsColumn { get; init; } = "I";
        public string WeaknessesColumn { get; init; } = "J";
        public string PreviousAnswerColumn { get; init; } = "F";
        public IReadOnlyList<string> AllowedAnswers { get; init; } = new[] { "1", "2", "3", "4", "N/A" };
        public int DeviationThreshold { get; init; } = 2;
    }
}
