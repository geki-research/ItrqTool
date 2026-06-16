using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Clq;
using Xunit;
using static ItrqTool.Tasks.Tests.QuestionnaireValidation.Clq.ClqBaselineTestHarness;

namespace ItrqTool.Tasks.Tests.QuestionnaireValidation.Clq;

// D1a coverage: the two pre-loop structure sweeps (malformed-key, within-year removed),
// the malformed-key per-row guard, and the standalone NumberFormatUnrecognized check —
// plus the shape/conformance assertions (configured columns, null RequestedData,
// metadata pass-through, severity-override flow). Within-year structure (D1b),
// input-validity (D1c), and the cross-year switch (D2) are NOT exercised here.
public sealed class ClqBaselineChecksTests
{
    // ── Phase 1: malformed-key sweep ─────────────────────────────────────────────

    [Fact]
    public void XrefIdEmptyOrDuplicated_FiresForMalformedKey()
    {
        var malformed = new[] { new MalformedKey(ValidationWorkbook.CurrentResponse, 10, null, MalformedKeyReason.Blank) };
        var findings = Run(Result(malformed: malformed));

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Fatal);
        f.CellAddresses.Should().Be("N10");
        f.CheckResult.Should().Contain("current response").And.Contain("blank");
    }

    [Fact]
    public void XrefIdEmptyOrDuplicated_FiresForDuplicatedKey()
    {
        var malformed = new[] { new MalformedKey(ValidationWorkbook.CurrentResponse, 10, "DUP", MalformedKeyReason.Duplicate) };
        var findings = Run(Result(malformed: malformed));

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Fatal);
        f.CellAddresses.Should().Be("N10");
        // Ported v01 wording: "... is duplicated ('{XrefId}'); ...".
        f.CheckResult.Should().Contain("duplicated").And.Contain("DUP");
    }

    [Fact]
    public void MalformedKey_FiresOncePerWorkbookEntry()
    {
        var malformed = new[]
        {
            new MalformedKey(ValidationWorkbook.CurrentResponse, 10, null, MalformedKeyReason.Blank),
            new MalformedKey(ValidationWorkbook.EmptyTemplate, 11, "DUP", MalformedKeyReason.Duplicate),
            new MalformedKey(ValidationWorkbook.PreviousResponse, 12, null, MalformedKeyReason.Blank)
        };
        var findings = Run(Result(malformed: malformed));

        findings.Should().HaveCount(3);
        findings.Should().OnlyContain(f => f.Evaluation == FindingEvaluation.Fatal);
        findings.Should().Contain(f => f.CheckResult.Contains("current response"));
        findings.Should().Contain(f => f.CheckResult.Contains("empty template"));
        findings.Should().Contain(f => f.CheckResult.Contains("previous response"));
    }

    [Fact]
    public void MalformedKey_Guard_SkipsNumberFormatCheck()
    {
        // The current row is malformed: it appears both as a MalformedKey AND as an
        // AlignedQuestion with WithinYear == NotEvaluatedMalformedKey. The guard must
        // suppress the per-row NumberFormatUnrecognized check even though its flag is set.
        var cur = Q(rowNumber: 10, xrefId: null, numberFormatUnrecognized: true);
        var aligned = Aligned(cur, withinYear: WithinYearJoin.NotEvaluatedMalformedKey,
            crossYear: CrossYearOutcome.NotEvaluatedMalformedKey);
        var malformed = new[] { new MalformedKey(ValidationWorkbook.CurrentResponse, 10, null, MalformedKeyReason.Blank) };

        var findings = Run(Result(aligned: new[] { aligned }, malformed: malformed));

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Fatal);
        f.CellAddresses.Should().Be("N10");
        // NumberFormatUnrecognized would land at C10 (TextColumn) — suppressed by the guard.
        findings.Should().NotContain(x => x.CellAddresses == "C10");
    }

    // ── Phase 2: within-year removed sweep ───────────────────────────────────────

    [Fact]
    public void QuestionRemoved_FiresForWithinYearRemoved_UsesTemplateRow()
    {
        var removed = Q(rowNumber: 42, xrefId: "T9", questionNumber: "3.3", questionText: "Removed?");
        var findings = Run(Result(removed: new[] { removed }));

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Error);
        f.CellAddresses.Should().Be("N42");
        f.QuestionNumber.Should().Be("3.3");
        f.QuestionText.Should().Be("Removed?");
    }

    [Fact]
    public void OutputMapping_QuestionRemoved_UsesTemplateMetadata_NotCurrent()
    {
        var config = ClqBaselineTestHarness.Config(xrefIdColumn: "ZZ");
        var removed = Q(rowNumber: 88, xrefId: "T1", questionNumber: "9.9", questionText: "Gone", providedBy: "ignored");

        var findings = Run(Result(removed: new[] { removed }), config);

        var f = OnlyFinding(findings);
        f.CellAddresses.Should().Be("ZZ88");
        f.QuestionNumber.Should().Be("9.9");
        f.QuestionText.Should().Be("Gone");
        f.ProvidedBy.Should().BeNull();  // template-side finding: no responder
        f.RequestedData.Should().BeNull();
    }

    // ── Phase 3a: within-year row structure (D1b) ────────────────────────────────

    [Fact]
    public void QuestionAdded_FiresForAddedInResponse()
    {
        var cur = Q(rowNumber: 10, xrefId: "X-NEW", answer: "1", strengths: "s");
        var aligned = Aligned(cur, withinYear: WithinYearJoin.AddedInResponse);

        var findings = Run(Result(aligned: new[] { aligned }));

        // Default-Neither row now also emits NoPreviousBaseline (D2) alongside the within-year finding.
        findings.Should().HaveCount(2);
        findings.Should().ContainSingle(x =>
            x.Check == ValidationCheck.Structure && x.Evaluation == FindingEvaluation.Error
            && x.CellAddresses == "N10" && x.CheckResult.Contains("absent from the empty template"));
        findings.Should().ContainSingle(x =>
            x.Check == ValidationCheck.Structure && x.Evaluation == FindingEvaluation.Information
            && x.CellAddresses == "N10");   // NoPreviousBaseline companion
    }

    [Fact]
    public void QuestionRowShifted_FiresWhenTemplateRowDiffers()
    {
        var tmpl = Q(rowNumber: 8,  xrefId: "X1");
        var cur  = Q(rowNumber: 12, xrefId: "X1", answer: "1", strengths: "s");
        var aligned = Aligned(cur, withinYear: WithinYearJoin.JoinedByXrefId,
            templateMatch: tmpl, rowShifted: true);

        var findings = Run(Result(aligned: new[] { aligned }));

        // Default-Neither row now also emits NoPreviousBaseline (D2) at the response row.
        findings.Should().HaveCount(2);
        findings.Should().ContainSingle(x =>
            x.Check == ValidationCheck.Structure && x.Evaluation == FindingEvaluation.Error
            && x.CellAddresses == "N12" && x.CheckResult.Contains("moved from template row 8 to response row 12"));
        findings.Should().ContainSingle(x =>
            x.Check == ValidationCheck.Structure && x.Evaluation == FindingEvaluation.Information
            && x.CellAddresses == "N12");   // NoPreviousBaseline companion
    }

    [Fact]
    public void ReferenceTextAltered_FiresAndListsDifferingFields()
    {
        // Text flag set by engine + guidance differs in payload → one compound finding.
        var tmpl = Q(rowNumber: 10, xrefId: "X1", guidance: "original guidance");
        var cur  = Q(rowNumber: 10, xrefId: "X1", guidance: "changed guidance", answer: "1", strengths: "s");
        var aligned = Aligned(cur, withinYear: WithinYearJoin.JoinedByXrefId,
            templateMatch: tmpl, textMismatched: true);

        var findings = Run(Result(aligned: new[] { aligned }));

        // Default-Neither row now also emits NoPreviousBaseline (D2) at N10.
        findings.Should().HaveCount(2);
        var f = findings.Should().ContainSingle(x => x.Check == ValidationCheck.FrozenValue).Subject;
        f.Evaluation.Should().Be(FindingEvaluation.Warning);
        f.CheckResult.Should().Contain("question text").And.Contain("guidance");
        f.CellAddresses.Should().Contain("C10").And.Contain("E10");
        findings.Should().ContainSingle(x =>
            x.Check == ValidationCheck.Structure && x.Evaluation == FindingEvaluation.Information
            && x.CellAddresses == "N10");   // NoPreviousBaseline companion
    }

    [Fact]
    public void ReferenceTextAltered_DedupsColumnsWhenFieldsShareAColumn()
    {
        // text (TextColumn=C) and chapter (also TextColumn=C) both differ → C{row} appears once.
        var tmpl = Q(rowNumber: 10, xrefId: "X1", chapterName: "Chapter A");
        var cur  = Q(rowNumber: 10, xrefId: "X1", chapterName: "Chapter B", answer: "1", strengths: "s");
        var aligned = Aligned(cur, withinYear: WithinYearJoin.JoinedByXrefId,
            templateMatch: tmpl, textMismatched: true);

        var findings = Run(Result(aligned: new[] { aligned }));

        // Default-Neither row now also emits NoPreviousBaseline (D2) at N10.
        findings.Should().HaveCount(2);
        var f = findings.Should().ContainSingle(x => x.Check == ValidationCheck.FrozenValue).Subject;
        f.CheckResult.Should().Contain("question text").And.Contain("chapter");
        // Both fields map to TextColumn (C) — the address must appear exactly once.
        f.CellAddresses.Split(',').Count(s => s.Trim() == "C10").Should().Be(1);
        f.CellAddresses.Should().NotContain("E10");
        findings.Should().ContainSingle(x =>
            x.Check == ValidationCheck.Structure && x.Evaluation == FindingEvaluation.Information
            && x.CellAddresses == "N10");   // NoPreviousBaseline companion
    }

    [Fact]
    public void ReferenceTextAltered_DoesNotFire_WhenJoinedAndAllFieldsMatch()
    {
        // JoinedByXrefId with templateMatch = current (all five fields identical) → no finding.
        var q = Q(rowNumber: 10, xrefId: "X1", guidance: "same", chapterName: "Ch");
        var aligned = Aligned(q); // withinYear = JoinedByXrefId, templateMatch = q by default

        var findings = Run(Result(aligned: new[] { aligned }));

        findings.Should().NotContain(f => f.Check == ValidationCheck.FrozenValue);
    }

    [Fact]
    public void AnswerValidationRuleChanged_FiresWhenAnswerDvDiffers()
    {
        var tmpl = Q(rowNumber: 10, xrefId: "X1",
            dvType: "Whole", dvOperator: "Between", dvFormula: "1", dvFormula2: "4");
        var cur = Q(rowNumber: 10, xrefId: "X1",
            dvType: "Whole", dvOperator: "Between", dvFormula: "1", dvFormula2: "5",
            answer: "1", strengths: "s");
        var aligned = Aligned(cur, withinYear: WithinYearJoin.JoinedByXrefId, templateMatch: tmpl);

        var findings = Run(Result(aligned: new[] { aligned }));

        // Default-Neither row now also emits NoPreviousBaseline (D2) at N10.
        findings.Should().HaveCount(2);
        findings.Should().ContainSingle(x =>
            x.Check == ValidationCheck.FrozenConstraint && x.Evaluation == FindingEvaluation.Error
            && x.CellAddresses == "H10");
        findings.Should().ContainSingle(x =>
            x.Check == ValidationCheck.Structure && x.Evaluation == FindingEvaluation.Information
            && x.CellAddresses == "N10");   // NoPreviousBaseline companion
    }

    // ── Phase 3b: number-format ───────────────────────────────────────────────────

    [Fact]
    public void NumberFormatUnrecognized_FiresWhenFlagSet()
    {
        var cur = Q(rowNumber: 10, numberFormatUnrecognized: true);
        var findings = Run(Result(aligned: new[] { Aligned(cur) }));

        findings.Should().Contain(f =>
            f.Check == ValidationCheck.Structure &&
            f.Evaluation == FindingEvaluation.Warning &&
            f.CellAddresses == "C10");
    }

    // ── shape / conformance ──────────────────────────────────────────────────────

    [Fact]
    public void OutputMapping_UsesConfiguredColumns_AndNullRequestedData()
    {
        var config = ClqBaselineTestHarness.Config(textColumn: "AA");
        var cur = Q(rowNumber: 10, questionNumber: "7.7", questionText: "Mapped?", providedBy: "Unit B",
                    numberFormatUnrecognized: true, answer: "1", strengths: "s");

        var findings = Run(Result(aligned: new[] { Aligned(cur) }), config);

        // textColumn=AA, xrefIdColumn=N (default). Default-Neither row now also emits NoPreviousBaseline at N10.
        findings.Should().HaveCount(2);
        var f = findings.Should().ContainSingle(x => x.CellAddresses == "AA10").Subject;
        f.RequestedData.Should().BeNull();
        f.QuestionNumber.Should().Be("7.7");
        f.QuestionText.Should().Be("Mapped?");
        f.ProvidedBy.Should().Be("Unit B");
        findings.Should().ContainSingle(x =>
            x.Check == ValidationCheck.Structure && x.Evaluation == FindingEvaluation.Information
            && x.CellAddresses == "N10");   // NoPreviousBaseline companion
    }

    [Fact]
    public void SeverityOverride_ChangesEvaluation_UnoverriddenKeepsDefault()
    {
        // Override the number-format finding (default Warning) to Error; leave the
        // malformed-key finding (default Fatal) un-overridden. Both are D1a findings.
        var overrides = new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal)
        {
            [ClqBaselineFindings.Descriptor(ClqBaselineFinding.NumberFormatUnrecognized).Id] = FindingEvaluation.Error
        };
        var cur = Q(rowNumber: 10, numberFormatUnrecognized: true);
        var malformed = new[] { new MalformedKey(ValidationWorkbook.CurrentResponse, 20, null, MalformedKeyReason.Blank) };

        var findings = Run(Result(aligned: new[] { Aligned(cur) }, malformed: malformed), overrides: overrides);

        // Overridden: number-format Warning → Error.
        findings.Should().ContainSingle(f => f.CellAddresses == "C10")
            .Which.Evaluation.Should().Be(FindingEvaluation.Error);
        // Un-overridden: malformed-key keeps its default Fatal.
        findings.Should().ContainSingle(f => f.CellAddresses == "N20")
            .Which.Evaluation.Should().Be(FindingEvaluation.Fatal);
    }

    // ── Phase 3c: input validity (D1c) ──────────────────────────────────────────

    [Fact]
    public void AnswerMissing_FiresWhenAnswerBlank()
    {
        var cur = Q(rowNumber: 10, answer: null);
        var findings = Run(Result(aligned: new[] { Aligned(cur) }));

        // Default-Neither row now also emits NoPreviousBaseline (D2) at N10.
        findings.Should().HaveCount(2);
        findings.Should().ContainSingle(x =>
            x.Check == ValidationCheck.MissingResponse && x.Evaluation == FindingEvaluation.Error
            && x.CellAddresses == "H10" && x.CheckResult.Contains("empty"));
        findings.Should().ContainSingle(x =>
            x.Check == ValidationCheck.Structure && x.Evaluation == FindingEvaluation.Information
            && x.CellAddresses == "N10");   // NoPreviousBaseline companion
    }

    [Fact]
    public void AnswerNotInAllowedSet_FiresWhenAnswerOutsideAllowed()
    {
        var cur = Q(rowNumber: 10, answer: "X");
        var findings = Run(Result(aligned: new[] { Aligned(cur) }));

        // Default-Neither row now also emits NoPreviousBaseline (D2) at N10.
        findings.Should().HaveCount(2);
        findings.Should().ContainSingle(x =>
            x.Check == ValidationCheck.MissingResponse && x.Evaluation == FindingEvaluation.Fatal
            && x.CellAddresses == "H10" && x.CheckResult.Contains("X"));
        findings.Should().ContainSingle(x =>
            x.Check == ValidationCheck.Structure && x.Evaluation == FindingEvaluation.Information
            && x.CellAddresses == "N10");   // NoPreviousBaseline companion
    }

    // answer "2" requires both I (strengths) and J (weaknesses) — all four quadrants exercised.
    [Theory]
    [InlineData("s", "w")]    // both present → no I/J findings
    [InlineData(null, "w")]   // strengths blank → StrengthsMissing at I10 only
    [InlineData("s", null)]   // weaknesses blank → WeaknessesMissing at J10 only
    [InlineData(null, null)]  // both blank → both findings
    public void IjMatrix_UsableAnswer2_EmitsCorrectIjFindings(string? strengths, string? weaknesses)
    {
        var cur = Q(rowNumber: 10, answer: "2", strengths: strengths, weaknesses: weaknesses);
        var findings = Run(Result(aligned: new[] { Aligned(cur) }));

        if (strengths is null)
            findings.Should().Contain(f =>
                f.Check == ValidationCheck.MissingResponse &&
                f.Evaluation == FindingEvaluation.Error &&
                f.CellAddresses == "I10");
        else
            findings.Should().NotContain(f => f.CellAddresses == "I10");

        if (weaknesses is null)
            findings.Should().Contain(f =>
                f.Check == ValidationCheck.MissingResponse &&
                f.Evaluation == FindingEvaluation.Error &&
                f.CellAddresses == "J10");
        else
            findings.Should().NotContain(f => f.CellAddresses == "J10");
    }

    [Fact]
    public void MalformedGuard_SuppressesInputValidity()
    {
        // A NotEvaluatedMalformedKey row with a blank answer: the per-row guard skips all
        // per-row checks — only the XrefIdEmptyOrDuplicated finding fires (Phase 1 sweep),
        // no AnswerMissing.
        var cur = Q(rowNumber: 10, xrefId: null, answer: null);
        var aligned = Aligned(cur, withinYear: WithinYearJoin.NotEvaluatedMalformedKey,
            crossYear: CrossYearOutcome.NotEvaluatedMalformedKey);
        var malformed = new[] { new MalformedKey(ValidationWorkbook.CurrentResponse, 10, null, MalformedKeyReason.Blank) };

        var findings = Run(Result(aligned: new[] { aligned }, malformed: malformed));

        var f = OnlyFinding(findings);
        f.Evaluation.Should().Be(FindingEvaluation.Fatal);   // XrefIdEmptyOrDuplicated
        findings.Should().NotContain(x => x.CellAddresses == "H10"); // no AnswerMissing
    }

    [Fact]
    public void AnswerNotInAllowedSet_SuppressesIj()
    {
        // answer ∉ AllowedAnswers + blank strengths: AnswerNotInAllowedSet fires; I/J skipped
        // because answerUsable remains false.
        var cur = Q(rowNumber: 10, answer: "X", strengths: null);
        var findings = Run(Result(aligned: new[] { Aligned(cur) }));

        // Default-Neither row now also emits NoPreviousBaseline (D2); assert the exact expanded set.
        findings.Should().HaveCount(2);
        findings.Should().ContainSingle(x =>
            x.Check == ValidationCheck.MissingResponse && x.Evaluation == FindingEvaluation.Fatal
            && x.CellAddresses == "H10");                       // AnswerNotInAllowedSet, intent unchanged
        findings.Should().ContainSingle(x =>
            x.Check == ValidationCheck.Structure && x.Evaluation == FindingEvaluation.Information
            && x.CellAddresses == "N10");                       // NoPreviousBaseline companion
        findings.Should().NotContain(x => x.CellAddresses == "I10");  // I/J suppression, preserved
    }

    [Fact]
    public void AnswerMissing_SuppressesIj()
    {
        // blank answer + blank strengths: AnswerMissing fires; I/J skipped because
        // answerUsable remains false.
        var cur = Q(rowNumber: 10, answer: null, strengths: null);
        var findings = Run(Result(aligned: new[] { Aligned(cur) }));

        // Default-Neither row now also emits NoPreviousBaseline (D2); assert the exact expanded set.
        findings.Should().HaveCount(2);
        findings.Should().ContainSingle(x =>
            x.Check == ValidationCheck.MissingResponse && x.Evaluation == FindingEvaluation.Error
            && x.CellAddresses == "H10");                       // AnswerMissing, intent unchanged
        findings.Should().ContainSingle(x =>
            x.Check == ValidationCheck.Structure && x.Evaluation == FindingEvaluation.Information
            && x.CellAddresses == "N10");                       // NoPreviousBaseline companion
        findings.Should().NotContain(x => x.CellAddresses == "I10");  // I/J suppression, preserved
    }

    // ── Phase 3d: cross-year switch (D2) ────────────────────────────────────────

    [Fact]
    public void PreviousAnswerAltered_FiresWhenInjectedFDiffersFromPriorActual()
    {
        // Agree row: injected previous answer (col F) "3" disagrees with the prior year's
        // actual answer "2" → F-integrity fires. Δ(1→1) below threshold, so no deviation.
        var prev = Q(answer: "2");
        var cur  = Q(rowNumber: 10, previousAnswer: "3", answer: "1", strengths: "s");
        var findings = Run(Result(aligned: new[]
            { Aligned(cur, crossYear: CrossYearOutcome.Agree, previousMatch: prev) }));

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.FrozenValue);
        f.Evaluation.Should().Be(FindingEvaluation.Warning);
        f.CellAddresses.Should().Be("F10");
        f.CheckResult.Should().Contain("'3'").And.Contain("'2'");
    }

    [Fact]
    public void FIntegrity_WhenInjectedEqualsPriorActual_NoFinding()
    {
        // Injected previous answer "2" == prior actual "2" → no F-integrity. Δ(1→1) below
        // threshold → no deviation. No findings at all.
        var prev = Q(answer: "2");
        var cur  = Q(rowNumber: 10, previousAnswer: "2", answer: "1", strengths: "s");
        var findings = Run(Result(aligned: new[]
            { Aligned(cur, crossYear: CrossYearOutcome.Agree, previousMatch: prev) }));

        findings.Should().NotContain(x => x.Check == ValidationCheck.FrozenValue);
        findings.Should().NotContain(x => x.CellAddresses == "F10");
    }

    [Fact]
    public void XrefIdConflict_FiresAndSurfacesBothCandidates()
    {
        // Key points to previous row 7; text best matches previous row 3 → conflict surfaces both.
        var counter   = Q(rowNumber: 7);
        var candidate = Q(rowNumber: 3);
        var cur = Clean(row: 10);
        var findings = Run(Result(aligned: new[]
            { Aligned(cur, crossYear: CrossYearOutcome.XrefIdConflict,
                      xrefIdCounterpart: counter, matcherCandidate: candidate) }));

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Error);
        f.CellAddresses.Should().Be("N10");
        f.CheckResult.Should().Contain("row 7").And.Contain("row 3");
    }

    [Fact]
    public void NewXrefIdResemblesPrevious_FiresAndSurfacesTwin()
    {
        var twin = Q(rowNumber: 5);
        var cur = Clean(row: 10);
        var findings = Run(Result(aligned: new[]
            { Aligned(cur, crossYear: CrossYearOutcome.NewXrefIdWithLookalike, matcherCandidate: twin) }));

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Warning);
        f.CellAddresses.Should().Be("N10");
        f.CheckResult.Should().Contain("textual twin").And.Contain("row 5");
    }

    [Fact]
    public void SameXrefIdTextDiverged_FiresAndSurfacesCounterpart()
    {
        var counter = Q(rowNumber: 8);
        var cur = Clean(row: 10);
        var findings = Run(Result(aligned: new[]
            { Aligned(cur, crossYear: CrossYearOutcome.SameXrefIdTextDiverged, xrefIdCounterpart: counter) }));

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Warning);
        f.CellAddresses.Should().Be("N10");
        f.CheckResult.Should().Contain("diverged").And.Contain("row 8");
    }

    [Fact]
    public void NoPreviousBaseline_FiresForNeither()
    {
        var cur = Clean(row: 10);
        var findings = Run(Result(aligned: new[]
            { Aligned(cur, crossYear: CrossYearOutcome.Neither) }));

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Information);
        f.CellAddresses.Should().Be("N10");
    }

    [Fact]
    public void AnswerDeviation_FiresWhenDeltaMeetsThreshold()
    {
        // Δ(1→4) = 3 ≥ threshold 2 → deviation. F intact, I/J satisfied → single finding.
        var prev = Q(answer: "1");
        var cur  = Q(rowNumber: 10, previousAnswer: "1", answer: "4", strengths: "s", weaknesses: "w");
        var findings = Run(Result(aligned: new[]
            { Aligned(cur, crossYear: CrossYearOutcome.Agree, previousMatch: prev) }));

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.Deviation);
        f.Evaluation.Should().Be(FindingEvaluation.Warning);
        f.CellAddresses.Should().Be("H10");
        f.CheckResult.Should().Contain("1 → 4");
    }

    [Fact]
    public void Deviation_AtThreshold_Fires_BelowThreshold_DoesNot()
    {
        // At threshold: Δ(1→3) = 2 ≥ 2 → deviation. Below: Δ(1→2) = 1 < 2 → none.
        var prev = Q(answer: "1");

        var atCur = Q(rowNumber: 10, previousAnswer: "1", answer: "3", strengths: "s", weaknesses: "w");
        var atFindings = Run(Result(aligned: new[]
            { Aligned(atCur, crossYear: CrossYearOutcome.Agree, previousMatch: prev) }));
        atFindings.Should().Contain(x => x.Check == ValidationCheck.Deviation && x.CellAddresses == "H10");

        var belowCur = Q(rowNumber: 10, previousAnswer: "1", answer: "2", strengths: "s", weaknesses: "w");
        var belowFindings = Run(Result(aligned: new[]
            { Aligned(belowCur, crossYear: CrossYearOutcome.Agree, previousMatch: prev) }));
        belowFindings.Should().NotContain(x => x.Check == ValidationCheck.Deviation);
    }

    [Fact]
    public void PreviousAnswerUnusable_FiresWhenPreviousAnswerEmpty()
    {
        // Agree with an empty previous answer: deviation can't be evaluated → Unusable.
        // F intact (both blank), current answer usable.
        var prev = Q(answer: null);
        var cur  = Q(rowNumber: 10, previousAnswer: null, answer: "1", strengths: "s");
        var findings = Run(Result(aligned: new[]
            { Aligned(cur, crossYear: CrossYearOutcome.Agree, previousMatch: prev) }));

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.Deviation);
        f.Evaluation.Should().Be(FindingEvaluation.Information);
        f.CellAddresses.Should().Be("H10");
    }

    [Fact]
    public void AnswerNotInAllowedSet_SkipsIjAndDeviation_ButFIntegrityStillRuns()
    {
        // answer "X" ∉ AllowedAnswers → AnswerNotInAllowedSet, answerUsable false (no I/J,
        // no deviation). F-integrity still runs on the Agree row: "3" vs prior "2".
        var prev = Q(answer: "2");
        var cur  = Q(rowNumber: 10, previousAnswer: "3", answer: "X", strengths: null);
        var findings = Run(Result(aligned: new[]
            { Aligned(cur, crossYear: CrossYearOutcome.Agree, previousMatch: prev) }));

        findings.Should().Contain(x =>
            x.Check == ValidationCheck.MissingResponse &&
            x.Evaluation == FindingEvaluation.Fatal &&
            x.CellAddresses == "H10");                       // AnswerNotInAllowedSet
        findings.Should().Contain(x =>
            x.Check == ValidationCheck.FrozenValue &&
            x.CellAddresses == "F10");                       // PreviousAnswerAltered
        findings.Should().NotContain(x => x.CellAddresses == "I10");      // no strengths I/J
        findings.Should().NotContain(x => x.CellAddresses == "J10");
        findings.Should().NotContain(x => x.Check == ValidationCheck.Deviation);
    }

    [Fact]
    public void AnswerMissing_DoesNotSuppressFIntegrity_OnAgreeRow()
    {
        // Blank answer → AnswerMissing, answerUsable false (no deviation). F-integrity still
        // runs on the Agree row: "3" vs prior "2".
        var prev = Q(answer: "2");
        var cur  = Q(rowNumber: 10, previousAnswer: "3", answer: null, strengths: null);
        var findings = Run(Result(aligned: new[]
            { Aligned(cur, crossYear: CrossYearOutcome.Agree, previousMatch: prev) }));

        findings.Should().Contain(x =>
            x.Check == ValidationCheck.MissingResponse &&
            x.Evaluation == FindingEvaluation.Error &&
            x.CellAddresses == "H10");                       // AnswerMissing
        findings.Should().Contain(x =>
            x.Check == ValidationCheck.FrozenValue &&
            x.CellAddresses == "F10");                       // PreviousAnswerAltered
        findings.Should().NotContain(x => x.Check == ValidationCheck.Deviation);
    }

    [Fact]
    public void Deviation_CurrentNotNumeric_NoDeviationNorUnusable()
    {
        // Current answer "N/A" is usable (∈ AllowedAnswers) but not numeric → curNumeric false,
        // so neither AnswerDeviation nor PreviousAnswerUnusable fires. F intact.
        var prev = Q(answer: "2");
        var cur  = Q(rowNumber: 10, previousAnswer: "2", answer: "N/A");
        var findings = Run(Result(aligned: new[]
            { Aligned(cur, crossYear: CrossYearOutcome.Agree, previousMatch: prev) }));

        findings.Should().NotContain(x => x.Check == ValidationCheck.Deviation);
    }
}
