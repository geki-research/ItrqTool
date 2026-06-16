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
        var cur = Q(rowNumber: 10, xrefId: "X-NEW");
        var aligned = Aligned(cur, withinYear: WithinYearJoin.AddedInResponse);

        var findings = Run(Result(aligned: new[] { aligned }));

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Error);
        f.CellAddresses.Should().Be("N10");
        f.CheckResult.Should().Contain("absent from the empty template");
    }

    [Fact]
    public void QuestionRowShifted_FiresWhenTemplateRowDiffers()
    {
        var tmpl = Q(rowNumber: 8,  xrefId: "X1");
        var cur  = Q(rowNumber: 12, xrefId: "X1");
        var aligned = Aligned(cur, withinYear: WithinYearJoin.JoinedByXrefId,
            templateMatch: tmpl, rowShifted: true);

        var findings = Run(Result(aligned: new[] { aligned }));

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.Structure);
        f.Evaluation.Should().Be(FindingEvaluation.Error);
        f.CellAddresses.Should().Be("N12");
        f.CheckResult.Should().Contain("moved from template row 8 to response row 12");
    }

    [Fact]
    public void ReferenceTextAltered_FiresAndListsDifferingFields()
    {
        // Text flag set by engine + guidance differs in payload → one compound finding.
        var tmpl = Q(rowNumber: 10, xrefId: "X1", guidance: "original guidance");
        var cur  = Q(rowNumber: 10, xrefId: "X1", guidance: "changed guidance");
        var aligned = Aligned(cur, withinYear: WithinYearJoin.JoinedByXrefId,
            templateMatch: tmpl, textMismatched: true);

        var findings = Run(Result(aligned: new[] { aligned }));

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.FrozenValue);
        f.Evaluation.Should().Be(FindingEvaluation.Warning);
        f.CheckResult.Should().Contain("question text").And.Contain("guidance");
        f.CellAddresses.Should().Contain("C10").And.Contain("E10");
    }

    [Fact]
    public void ReferenceTextAltered_DedupsColumnsWhenFieldsShareAColumn()
    {
        // text (TextColumn=C) and chapter (also TextColumn=C) both differ → C{row} appears once.
        var tmpl = Q(rowNumber: 10, xrefId: "X1", chapterName: "Chapter A");
        var cur  = Q(rowNumber: 10, xrefId: "X1", chapterName: "Chapter B");
        var aligned = Aligned(cur, withinYear: WithinYearJoin.JoinedByXrefId,
            templateMatch: tmpl, textMismatched: true);

        var findings = Run(Result(aligned: new[] { aligned }));

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.FrozenValue);
        f.CheckResult.Should().Contain("question text").And.Contain("chapter");
        // Both fields map to TextColumn (C) — the address must appear exactly once.
        f.CellAddresses.Split(',').Count(s => s.Trim() == "C10").Should().Be(1);
        f.CellAddresses.Should().NotContain("E10");
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
            dvType: "Whole", dvOperator: "Between", dvFormula: "1", dvFormula2: "5");
        var aligned = Aligned(cur, withinYear: WithinYearJoin.JoinedByXrefId, templateMatch: tmpl);

        var findings = Run(Result(aligned: new[] { aligned }));

        var f = OnlyFinding(findings);
        f.Check.Should().Be(ValidationCheck.FrozenConstraint);
        f.Evaluation.Should().Be(FindingEvaluation.Error);
        f.CellAddresses.Should().Be("H10");
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
                    numberFormatUnrecognized: true);

        var findings = Run(Result(aligned: new[] { Aligned(cur) }), config);

        var f = OnlyFinding(findings);
        f.CellAddresses.Should().Be("AA10");
        f.RequestedData.Should().BeNull();
        f.QuestionNumber.Should().Be("7.7");
        f.QuestionText.Should().Be("Mapped?");
        f.ProvidedBy.Should().Be("Unit B");
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
}
