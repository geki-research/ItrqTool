using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.ControlLevelQuestionValidation;
using Xunit;

namespace ItrqTool.Tasks.Tests.ControlLevelQuestionValidation;

public sealed class ClqValidationChecksTests
{
    // ── builders ─────────────────────────────────────────────────────────────────

    private static InternalClqQuestion Q(
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
        => new(rowNumber, xrefId, questionNumber, questionText, originalText ?? questionText,
               chapterName, sectionName, guidance, previousAnswer, answer, strengths, weaknesses,
               providedBy, dvType, dvFormula, dvOperator, dvFormula2, numberFormatUnrecognized);

    private static AlignedQuestion Aligned(
        InternalClqQuestion current,
        WithinYearJoin withinYear = WithinYearJoin.JoinedByXrefId,
        InternalClqQuestion? templateMatch = null,
        bool rowShifted = false,
        bool textMismatched = false,
        CrossYearOutcome crossYear = CrossYearOutcome.Neither,
        InternalClqQuestion? previousMatch = null,
        InternalClqQuestion? xrefIdCounterpart = null,
        InternalClqQuestion? matcherCandidate = null,
        double? matcherBaseScore = null)
    {
        // JoinedByXrefId requires a non-null template; default to an identical copy so the
        // within-year compares produce no structural noise unless a test asks for it.
        if (withinYear == WithinYearJoin.JoinedByXrefId && templateMatch is null)
            templateMatch = current;
        return new(current, withinYear, templateMatch, rowShifted, textMismatched,
                   crossYear, previousMatch, xrefIdCounterpart, matcherCandidate, matcherBaseScore);
    }

    private static ClqAlignmentResult Result(
        IEnumerable<AlignedQuestion>? aligned = null,
        IEnumerable<InternalClqQuestion>? removed = null,
        IEnumerable<MalformedKey>? malformed = null)
        => new((aligned ?? []).ToList(), (removed ?? []).ToList(), (malformed ?? []).ToList());

    private static ControlLevelQuestionValidationV01Config Config(
        IReadOnlyDictionary<string, FindingEvaluation>? overrides = null,
        int deviationThreshold = 2,
        IReadOnlyList<string>? allowedAnswers = null,
        string textColumn = "C", string guidanceColumn = "E", string previousAnswerColumn = "F",
        string answerColumn = "H", string strengthsColumn = "I", string weaknessesColumn = "J",
        string providedByColumn = "M", string xrefIdColumn = "N")
        => new()
        {
            TextColumn = textColumn,
            GuidanceColumn = guidanceColumn,
            PreviousAnswerColumn = previousAnswerColumn,
            AnswerColumn = answerColumn,
            StrengthsColumn = strengthsColumn,
            WeaknessesColumn = weaknessesColumn,
            ProvidedByColumn = providedByColumn,
            XrefIdColumn = xrefIdColumn,
            SheetName = "CLQ",
            AllowedAnswers = allowedAnswers ?? new[] { "1", "2", "3", "4", "N/A" },
            DeviationThreshold = deviationThreshold,
            SeverityOverrides = overrides ?? new Dictionary<string, FindingEvaluation>()
        };

    // A "clean" answered current question: answer "1" with strengths present, so no
    // input-validity findings fire and within-year/cross-year tests stay isolated.
    private static InternalClqQuestion Clean(
        int row = 10, string? xref = "X1", string answer = "1", string? strengths = "s")
        => Q(rowNumber: row, xrefId: xref, answer: answer, strengths: strengths);

    private static ValidationFinding OnlyFinding(IReadOnlyList<ValidationFinding> findings)
        => findings.Should().ContainSingle().Subject;

    // ── 18 finding-ids, one focused check each ───────────────────────────────────

    [Fact]
    public void XrefIdEmptyOrDuplicated_FiresForMalformedKey()
    {
        var malformed = new[] { new MalformedKey(ClqWorkbook.CurrentResponse, 10, null, MalformedKeyReason.Blank) };
        var findings = ClqValidationChecks.Build(Result(malformed: malformed), Config());

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Fatal);
        f.CellAddresses.Should().Be("N10");
        f.CheckResult.Should().Contain("current response").And.Contain("blank");
    }

    [Fact]
    public void QuestionRemoved_FiresForWithinYearRemoved_UsesTemplateRow()
    {
        var removed = Q(rowNumber: 42, xrefId: "T9", questionNumber: "3.3", questionText: "Removed?");
        var findings = ClqValidationChecks.Build(Result(removed: new[] { removed }), Config());

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Error);
        f.CellAddresses.Should().Be("N42");
        f.QuestionNumber.Should().Be("3.3");
        f.QuestionText.Should().Be("Removed?");
    }

    [Fact]
    public void QuestionAdded_FiresForAddedInResponse()
    {
        var cur = Clean();
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[] { Aligned(cur, WithinYearJoin.AddedInResponse) }), Config());

        findings.Should().Contain(f =>
            f.Check == ValidationCheck.Structure &&
            f.Evaluation == FindingEvaluation.Error &&
            f.CellAddresses == "N10" &&
            f.CheckResult.Contains("absent from the empty template"));
    }

    [Fact]
    public void QuestionRowShifted_FiresWhenTemplateRowDiffers()
    {
        var cur = Clean(row: 12);
        var tmpl = Q(rowNumber: 8, xrefId: "X1", answer: "1", strengths: "s");
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[] { Aligned(cur, templateMatch: tmpl, rowShifted: true) }), Config());

        findings.Should().Contain(f =>
            f.CheckResult.Contains("moved from template row 8 to response row 12") &&
            f.Evaluation == FindingEvaluation.Error &&
            f.CellAddresses == "N12");
    }

    [Fact]
    public void NumberFormatUnrecognized_FiresWhenFlagSet()
    {
        var cur = Q(rowNumber: 10, answer: "1", strengths: "s", numberFormatUnrecognized: true);
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[] { Aligned(cur) }), Config());

        findings.Should().Contain(f =>
            f.Check == ValidationCheck.Structure &&
            f.Evaluation == FindingEvaluation.Warning &&
            f.CellAddresses == "C10");
    }

    [Fact]
    public void ReferenceTextAltered_FiresAndListsDifferingFields()
    {
        var cur = Q(rowNumber: 10, answer: "1", strengths: "s", guidance: "new guidance");
        var tmpl = Q(rowNumber: 10, answer: "1", strengths: "s", guidance: "old guidance");
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[] { Aligned(cur, templateMatch: tmpl, textMismatched: true) }), Config());

        var f = findings.Should().ContainSingle(x => x.Check == ValidationCheck.FrozenValue).Subject;
        f.Evaluation.Should().Be(FindingEvaluation.Warning);
        f.CheckResult.Should().Contain("question text").And.Contain("guidance");
        f.CellAddresses.Should().Contain("C10").And.Contain("E10");
    }

    [Fact]
    public void PreviousAnswerAltered_FiresWhenInjectedFDiffersFromPriorActual()
    {
        var cur = Q(rowNumber: 10, answer: "2", strengths: "s", weaknesses: "w", previousAnswer: "9");
        var prev = Q(rowNumber: 5, answer: "1");
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[]
            {
                Aligned(cur, templateMatch: cur, crossYear: CrossYearOutcome.Agree, previousMatch: prev)
            }), Config());

        findings.Should().Contain(f =>
            f.Check == ValidationCheck.FrozenValue &&
            f.Evaluation == FindingEvaluation.Warning &&
            f.CellAddresses == "F10" &&
            f.CheckResult.Contains("'9'") && f.CheckResult.Contains("'1'"));
    }

    [Fact]
    public void AnswerValidationRuleChanged_FiresWhenAnswerDvDiffers()
    {
        var cur = Q(rowNumber: 10, answer: "1", strengths: "s", dvType: "Decimal", dvOperator: "LessThan", dvFormula: "0");
        var tmpl = Q(rowNumber: 10, answer: "1", strengths: "s", dvType: "Decimal", dvOperator: "GreaterThan", dvFormula: "0");
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[] { Aligned(cur, templateMatch: tmpl) }), Config());

        findings.Should().Contain(f =>
            f.Check == ValidationCheck.FrozenConstraint &&
            f.Evaluation == FindingEvaluation.Error &&
            f.CellAddresses == "H10");
    }

    [Fact]
    public void AnswerMissing_FiresWhenAnswerBlank()
    {
        var cur = Q(rowNumber: 10, answer: "  ");
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[] { Aligned(cur) }), Config());

        findings.Should().Contain(f =>
            f.Check == ValidationCheck.MissingResponse &&
            f.Evaluation == FindingEvaluation.Error &&
            f.CellAddresses == "H10" &&
            f.CheckResult.Contains("empty"));
    }

    [Fact]
    public void AnswerNotInAllowedSet_FiresWhenAnswerOutsideAllowed()
    {
        var cur = Q(rowNumber: 10, answer: "9");
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[] { Aligned(cur) }), Config());

        findings.Should().Contain(f =>
            f.Check == ValidationCheck.MissingResponse &&
            f.Evaluation == FindingEvaluation.Fatal &&
            f.CellAddresses == "H10");
    }

    [Fact]
    public void StrengthsMissing_FiresWhenRequiredButEmpty()
    {
        var cur = Q(rowNumber: 10, answer: "1", strengths: null);
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[] { Aligned(cur) }), Config());

        findings.Should().Contain(f =>
            f.Check == ValidationCheck.MissingResponse &&
            f.Evaluation == FindingEvaluation.Error &&
            f.CellAddresses == "I10");
    }

    [Fact]
    public void WeaknessesMissing_FiresWhenRequiredButEmpty()
    {
        var cur = Q(rowNumber: 10, answer: "4", weaknesses: null);
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[] { Aligned(cur) }), Config());

        findings.Should().Contain(f =>
            f.Check == ValidationCheck.MissingResponse &&
            f.Evaluation == FindingEvaluation.Error &&
            f.CellAddresses == "J10");
    }

    [Fact]
    public void XrefIdConflict_FiresAndSurfacesBothCandidates()
    {
        var cur = Clean();
        var matcher = Q(rowNumber: 3, xrefId: "P-text", questionNumber: "2.1", questionText: "Matcher pick");
        var counterpart = Q(rowNumber: 7, xrefId: "X1", questionNumber: "5.5", questionText: "Key pick");
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[]
            {
                Aligned(cur, crossYear: CrossYearOutcome.XrefIdConflict,
                    xrefIdCounterpart: counterpart, matcherCandidate: matcher)
            }), Config());

        var f = findings.Should().ContainSingle(x => x.CheckResult.Contains("disagree")).Subject;
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Error);
        f.CheckResult.Should().Contain("row 7").And.Contain("row 3");
    }

    [Fact]
    public void NewXrefIdResemblesPrevious_FiresAndSurfacesTwin()
    {
        var cur = Clean();
        var twin = Q(rowNumber: 4, xrefId: "OLD", questionText: "Looks alike");
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[]
            {
                Aligned(cur, crossYear: CrossYearOutcome.NewXrefIdWithLookalike, matcherCandidate: twin)
            }), Config());

        var f = findings.Should().ContainSingle(x => x.CheckResult.Contains("textual twin")).Subject;
        f.Evaluation.Should().Be(FindingEvaluation.Warning);
        f.CheckResult.Should().Contain("row 4");
    }

    [Fact]
    public void SameXrefIdTextDiverged_FiresAndSurfacesCounterpart()
    {
        var cur = Clean();
        var counterpart = Q(rowNumber: 6, xrefId: "X1", questionText: "Old wording");
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[]
            {
                Aligned(cur, crossYear: CrossYearOutcome.SameXrefIdTextDiverged, xrefIdCounterpart: counterpart)
            }), Config());

        var f = findings.Should().ContainSingle(x => x.CheckResult.Contains("diverged")).Subject;
        f.Evaluation.Should().Be(FindingEvaluation.Warning);
        f.CheckResult.Should().Contain("row 6");
    }

    [Fact]
    public void NoPreviousBaseline_FiresForNeither()
    {
        var cur = Clean();
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[] { Aligned(cur, crossYear: CrossYearOutcome.Neither) }), Config());

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Information);
    }

    [Fact]
    public void AnswerDeviation_FiresWhenDeltaMeetsThreshold()
    {
        var cur = Q(rowNumber: 10, answer: "4", weaknesses: "w", previousAnswer: "1");
        var prev = Q(rowNumber: 5, answer: "1");
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[]
            {
                Aligned(cur, templateMatch: cur, crossYear: CrossYearOutcome.Agree, previousMatch: prev)
            }), Config(deviationThreshold: 3));

        var f = findings.Should().ContainSingle(x => x.Check == ValidationCheck.Deviation).Subject;
        f.Evaluation.Should().Be(FindingEvaluation.Warning);
        f.CellAddresses.Should().Be("H10");
        f.CheckResult.Should().Contain("1 → 4");
    }

    [Fact]
    public void PreviousAnswerUnusable_FiresWhenPreviousAnswerEmpty()
    {
        var cur = Q(rowNumber: 10, answer: "1", strengths: "s", previousAnswer: null);
        var prev = Q(rowNumber: 5, answer: null);
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[]
            {
                Aligned(cur, templateMatch: cur, crossYear: CrossYearOutcome.Agree, previousMatch: prev)
            }), Config());

        var f = findings.Should().ContainSingle(x => x.Check == ValidationCheck.Deviation).Subject;
        f.Evaluation.Should().Be(FindingEvaluation.Information);
        f.CellAddresses.Should().Be("H10");
    }

    // ── I/J matrix ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1", true, false)]   // strengths required only
    [InlineData("2", true, true)]    // both required
    [InlineData("3", true, true)]    // both required
    [InlineData("4", false, true)]   // weaknesses required only
    [InlineData("N/A", false, false)] // neither required
    public void IjMatrix_RequiredCellsMissing_Fire(string answer, bool expectStrengths, bool expectWeaknesses)
    {
        var cur = Q(rowNumber: 10, answer: answer, strengths: null, weaknesses: null);
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[] { Aligned(cur, crossYear: CrossYearOutcome.Neither) }), Config());

        findings.Any(f => f.CellAddresses == "I10").Should().Be(expectStrengths);
        findings.Any(f => f.CellAddresses == "J10").Should().Be(expectWeaknesses);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("3")]
    [InlineData("4")]
    public void IjMatrix_RequiredCellsSatisfied_DoNotFire(string answer)
    {
        var cur = Q(rowNumber: 10, answer: answer, strengths: "provided", weaknesses: "provided");
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[] { Aligned(cur, crossYear: CrossYearOutcome.Neither) }), Config());

        findings.Should().NotContain(f => f.CellAddresses == "I10" || f.CellAddresses == "J10");
    }

    // ── Deviation boundary ───────────────────────────────────────────────────────

    [Fact]
    public void Deviation_AtThreshold_Fires_BelowThreshold_DoesNot()
    {
        // Δ = |4 - 1| = 3.
        var cur = Q(rowNumber: 10, answer: "4", weaknesses: "w", previousAnswer: "1");
        var prev = Q(rowNumber: 5, answer: "1");
        AlignedQuestion A() => Aligned(cur, templateMatch: cur, crossYear: CrossYearOutcome.Agree, previousMatch: prev);

        // threshold 3 → |Δ| == threshold → fires
        ClqValidationChecks.Build(Result(aligned: new[] { A() }), Config(deviationThreshold: 3))
            .Should().Contain(f => f.Check == ValidationCheck.Deviation && f.CheckResult.Contains("1 → 4"));

        // threshold 4 → |Δ| == threshold - 1 → does not fire
        ClqValidationChecks.Build(Result(aligned: new[] { A() }), Config(deviationThreshold: 4))
            .Should().NotContain(f => f.Check == ValidationCheck.Deviation);
    }

    // ── F-integrity ──────────────────────────────────────────────────────────────

    [Fact]
    public void FIntegrity_WhenInjectedEqualsPriorActual_NoFinding()
    {
        var cur = Q(rowNumber: 10, answer: "1", strengths: "s", previousAnswer: "1");
        var prev = Q(rowNumber: 5, answer: "1");
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[]
            {
                Aligned(cur, templateMatch: cur, crossYear: CrossYearOutcome.Agree, previousMatch: prev)
            }), Config());

        // PreviousAnswerAltered would land at F10 (FrozenValue); assert it is absent.
        findings.Should().NotContain(f =>
            f.Check == ValidationCheck.FrozenValue && f.CellAddresses == "F10");
    }

    // ── AnswerNotInAllowedSet skips I/J + deviation, but NOT F-integrity ─────────

    [Fact]
    public void AnswerNotInAllowedSet_SkipsIjAndDeviation_ButFIntegrityStillRuns()
    {
        // Agree row whose current answer is Fatal-invalid AND whose injected F is altered.
        // F-integrity is a frozen-value check on col F, independent of the current answer's
        // usability, so PreviousAnswerAltered STILL fires; the I/J matrix and the deviation
        // sub-block stay gated on a usable answer, so neither fires.
        var cur = Q(rowNumber: 10, answer: "9", strengths: null, weaknesses: null, previousAnswer: "9");
        var prev = Q(rowNumber: 5, answer: "1");
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[]
            {
                Aligned(cur, templateMatch: cur, crossYear: CrossYearOutcome.Agree, previousMatch: prev)
            }), Config());

        // AnswerNotInAllowedSet (Fatal) AND PreviousAnswerAltered (F10) both emitted.
        findings.Should().Contain(f =>
            f.Check == ValidationCheck.MissingResponse && f.Evaluation == FindingEvaluation.Fatal &&
            f.CellAddresses == "H10");
        findings.Should().Contain(f =>
            f.Check == ValidationCheck.FrozenValue && f.CellAddresses == "F10");
        // Deviation stays gated on a usable answer → not evaluated.
        findings.Should().NotContain(f => f.Check == ValidationCheck.Deviation);
        // I/J matrix skipped (answer not usable).
        findings.Should().NotContain(f => f.CellAddresses == "I10" || f.CellAddresses == "J10");
    }

    [Fact]
    public void AnswerMissing_DoesNotSuppressFIntegrity_OnAgreeRow()
    {
        // Agree row with an EMPTY current answer (col H) but an altered injected F.
        // F-integrity must still fire; deviation must not (no usable current answer).
        var cur = Q(rowNumber: 10, answer: "  ", previousAnswer: "9");
        var prev = Q(rowNumber: 5, answer: "1");
        var findings = ClqValidationChecks.Build(
            Result(aligned: new[]
            {
                Aligned(cur, templateMatch: cur, crossYear: CrossYearOutcome.Agree, previousMatch: prev)
            }), Config());

        // BOTH AnswerMissing AND PreviousAnswerAltered emitted.
        findings.Should().Contain(f =>
            f.Check == ValidationCheck.MissingResponse && f.Evaluation == FindingEvaluation.Error &&
            f.CellAddresses == "H10");
        findings.Should().Contain(f =>
            f.Check == ValidationCheck.FrozenValue && f.CellAddresses == "F10");
        // Deviation stays gated on a usable answer → not evaluated.
        findings.Should().NotContain(f => f.Check == ValidationCheck.Deviation);
    }

    // ── Malformed key across the three workbooks ─────────────────────────────────

    [Fact]
    public void MalformedKey_FiresOncePerWorkbookEntry()
    {
        var malformed = new[]
        {
            new MalformedKey(ClqWorkbook.CurrentResponse, 10, null, MalformedKeyReason.Blank),
            new MalformedKey(ClqWorkbook.EmptyTemplate, 11, "DUP", MalformedKeyReason.Duplicate),
            new MalformedKey(ClqWorkbook.PreviousResponse, 12, null, MalformedKeyReason.Blank)
        };
        var findings = ClqValidationChecks.Build(Result(malformed: malformed), Config());

        findings.Should().HaveCount(3);
        findings.Should().OnlyContain(f => f.Evaluation == FindingEvaluation.Fatal);
        findings.Should().Contain(f => f.CheckResult.Contains("current response"));
        findings.Should().Contain(f => f.CheckResult.Contains("empty template"));
        findings.Should().Contain(f => f.CheckResult.Contains("previous response"));
    }

    [Fact]
    public void MalformedCurrentRow_EmitsOnce_AndSkipsDependentChecks()
    {
        // The current row is malformed: it appears both as a MalformedKey AND as an
        // AlignedQuestion with WithinYear == NotEvaluatedMalformedKey. Expect exactly one finding.
        var cur = Q(rowNumber: 10, xrefId: null, answer: "9"); // would-be AnswerNotInAllowedSet if not skipped
        var aligned = Aligned(cur, withinYear: WithinYearJoin.NotEvaluatedMalformedKey,
            crossYear: CrossYearOutcome.NotEvaluatedMalformedKey);
        var malformed = new[] { new MalformedKey(ClqWorkbook.CurrentResponse, 10, null, MalformedKeyReason.Blank) };

        var findings = ClqValidationChecks.Build(
            Result(aligned: new[] { aligned }, malformed: malformed), Config());

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Fatal);
    }

    // ── Severity overrides ───────────────────────────────────────────────────────

    [Fact]
    public void SeverityOverride_ChangesEvaluation_UnoverriddenKeepsDefault()
    {
        var overrides = new Dictionary<string, FindingEvaluation>
        {
            [ClqFindings.Id(ClqFinding.ReferenceTextAltered)] = FindingEvaluation.Error
        };
        var cur = Q(rowNumber: 10, answer: "1", strengths: "s", guidance: "new");
        var tmpl = Q(rowNumber: 10, answer: "1", strengths: "s", guidance: "old");

        var overridden = ClqValidationChecks.Build(
            Result(aligned: new[] { Aligned(cur, templateMatch: tmpl) }), Config(overrides: overrides));
        overridden.Should().ContainSingle(f => f.Check == ValidationCheck.FrozenValue)
            .Which.Evaluation.Should().Be(FindingEvaluation.Error);

        // Un-overridden default is Warning.
        var defaulted = ClqValidationChecks.Build(
            Result(aligned: new[] { Aligned(cur, templateMatch: tmpl) }), Config());
        defaulted.Should().ContainSingle(f => f.Check == ValidationCheck.FrozenValue)
            .Which.Evaluation.Should().Be(FindingEvaluation.Warning);
    }

    // ── Output mapping: configured columns, RequestedData null, source of metadata ─

    [Fact]
    public void OutputMapping_UsesConfiguredColumns_AndNullRequestedData()
    {
        var config = Config(answerColumn: "ZZ", textColumn: "AA", strengthsColumn: "QQ");
        var cur = Q(rowNumber: 10, answer: null, questionNumber: "7.7", questionText: "Mapped?", providedBy: "Unit B");

        var findings = ClqValidationChecks.Build(
            Result(aligned: new[] { Aligned(cur, crossYear: CrossYearOutcome.Neither) }), config);

        var missing = findings.Should().ContainSingle(f => f.CheckResult.Contains("empty")).Subject;
        missing.CellAddresses.Should().Be("ZZ10");
        missing.RequestedData.Should().BeNull();
        missing.QuestionNumber.Should().Be("7.7");
        missing.QuestionText.Should().Be("Mapped?");
        missing.ProvidedBy.Should().Be("Unit B");
    }

    [Fact]
    public void OutputMapping_QuestionRemoved_UsesTemplateMetadata_NotCurrent()
    {
        var config = Config(xrefIdColumn: "ZZ");
        var removed = Q(rowNumber: 88, xrefId: "T1", questionNumber: "9.9", questionText: "Gone", providedBy: "ignored");

        var findings = ClqValidationChecks.Build(Result(removed: new[] { removed }), config);

        var f = OnlyFinding(findings);
        f.CellAddresses.Should().Be("ZZ88");
        f.QuestionNumber.Should().Be("9.9");
        f.QuestionText.Should().Be("Gone");
        f.ProvidedBy.Should().BeNull(); // template-side finding: no responder
        f.RequestedData.Should().BeNull();
    }
}
