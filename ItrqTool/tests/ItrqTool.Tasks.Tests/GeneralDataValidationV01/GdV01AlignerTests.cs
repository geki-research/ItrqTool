using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.GeneralDataValidationV01;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.QuestionnaireValidation.Checks;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;
using Xunit;

namespace ItrqTool.Tasks.Tests.GeneralDataValidationV01;

// Coverage for GdV01Aligner.Align: cross-year qid-join fork, within-year join, malformed-key
// mapping, and reused-check integration (WithinYearStructureCheck + MalformedKeyCheck).
// All fixtures are in-memory; no file I/O, no ClosedXML.
public sealed class GdV01AlignerTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────

    // Minimal GdAnswer with empty explanation list (alignment never reads answers).
    private static GdAnswer EmptyAnswer(string? answerId = null) =>
        new(AnswerId:       answerId,
            AnchorRow:      1,
            PreviousAnswer: null,
            Answer:         null,
            MaterialChange: null,
            ProvidedBy:     null,
            Explanations:   Array.Empty<GdExplanationRow>());

    private static GdV01Question Q(
        string xrefId,
        int rowNumber = 10,
        string originalText = "Text",
        string sectionName = "Section",
        string? questionNumber = "1") =>
        new(RowNumber:      rowNumber,
            XrefId:         xrefId,
            OriginalText:   originalText,
            QuestionText:   originalText,   // GD: QuestionText == OriginalText
            SectionName:    sectionName,
            QuestionNumber: questionNumber,
            Answers:        new[] { EmptyAnswer() });

    private static GdMalformedXref Malformed(int row, string? xrefId, GdMalformedXrefReason reason) =>
        new(row, xrefId, reason);

    private static GdV01ParseResult ParseResult(
        IReadOnlyList<GdV01Question>? questions = null,
        IReadOnlyList<GdMalformedXref>? malformed = null) =>
        new(questions ?? Array.Empty<GdV01Question>(),
            malformed ?? Array.Empty<GdMalformedXref>(),
            Array.Empty<GdSectionHeaderMismatch>());

    private static FindingEmitter Emitter<T>(IExtensionCheck<T> check) where T : class, IAlignmentIdentity =>
        new(new Dictionary<string, FindingEvaluation>(StringComparer.Ordinal),
            new FindingCatalogue(check.Descriptors));

    // ── §3.1 Twin-pair → Agree, NOT XrefIdConflict ───────────────────────────────

    [Fact]
    public void TwinPair_SameCQnumAndText_DifferentQids_BothAgree()
    {
        // Two current questions sharing the same QuestionNumber ("C1") and identical OriginalText,
        // but DIFFERENT qids ("q1" and "q2"). Each has a matching previous entry with the same
        // text. The qid-join never sees a C-qnum/D-text "tie" — each qid resolves independently.
        var cur1 = Q("q1", rowNumber: 10, originalText: "Same Text", questionNumber: "C1");
        var cur2 = Q("q2", rowNumber: 11, originalText: "Same Text", questionNumber: "C1");
        var prev1 = Q("q1", rowNumber: 10, originalText: "Same Text");
        var prev2 = Q("q2", rowNumber: 11, originalText: "Same Text");

        var result = GdV01Aligner.Align(
            ParseResult(questions: new[] { cur1, cur2 }),
            ParseResult(),
            ParseResult(questions: new[] { prev1, prev2 }));

        result.Aligned.Should().HaveCount(2);

        var a1 = result.Aligned[0];
        a1.CrossYear.Should().Be(CrossYearOutcome.Agree);
        a1.PreviousMatch.Should().Be(prev1);
        a1.XrefIdCounterpart.Should().Be(prev1);

        var a2 = result.Aligned[1];
        a2.CrossYear.Should().Be(CrossYearOutcome.Agree);
        a2.PreviousMatch.Should().Be(prev2);
        a2.XrefIdCounterpart.Should().Be(prev2);
    }

    // ── §3.2 SameXrefIdTextDiverged ──────────────────────────────────────────────

    [Fact]
    public void QidPresentAndTextDiverged_SameXrefIdTextDiverged()
    {
        var cur  = Q("q1", rowNumber: 10, originalText: "New Text");
        var prev = Q("q1", rowNumber: 10, originalText: "Old Text");

        var result = GdV01Aligner.Align(
            ParseResult(questions: new[] { cur }),
            ParseResult(),
            ParseResult(questions: new[] { prev }));

        var aq = result.Aligned.Should().ContainSingle().Subject;
        aq.CrossYear.Should().Be(CrossYearOutcome.SameXrefIdTextDiverged);
        aq.PreviousMatch.Should().BeNull();
        aq.XrefIdCounterpart.Should().Be(prev);
    }

    // ── §3.3 Neither ─────────────────────────────────────────────────────────────

    [Fact]
    public void QidAbsentFromPrevious_Neither()
    {
        var cur = Q("q99", rowNumber: 5);

        var result = GdV01Aligner.Align(
            ParseResult(questions: new[] { cur }),
            ParseResult(),
            ParseResult());

        var aq = result.Aligned.Should().ContainSingle().Subject;
        aq.CrossYear.Should().Be(CrossYearOutcome.Neither);
        aq.PreviousMatch.Should().BeNull();
        aq.XrefIdCounterpart.Should().BeNull();
    }

    // ── §3.4 Within-year fields ───────────────────────────────────────────────────

    [Fact]
    public void JoinedByXrefId_RowShifted_WhenTemplateRowDiffers()
    {
        var cur  = Q("q1", rowNumber: 15);
        var tmpl = Q("q1", rowNumber: 10);   // different row → RowShifted

        var result = GdV01Aligner.Align(
            ParseResult(questions: new[] { cur }),
            ParseResult(questions: new[] { tmpl }),
            ParseResult());

        var aq = result.Aligned.Should().ContainSingle().Subject;
        aq.WithinYear.Should().Be(WithinYearJoin.JoinedByXrefId);
        aq.TemplateMatch.Should().Be(tmpl);
        aq.RowShifted.Should().BeTrue();
    }

    [Fact]
    public void JoinedByXrefId_TextMismatched_WhenOriginalTextDiffers()
    {
        var cur  = Q("q1", rowNumber: 10, originalText: "Response Text");
        var tmpl = Q("q1", rowNumber: 10, originalText: "Template Text");

        var result = GdV01Aligner.Align(
            ParseResult(questions: new[] { cur }),
            ParseResult(questions: new[] { tmpl }),
            ParseResult());

        var aq = result.Aligned.Should().ContainSingle().Subject;
        aq.WithinYear.Should().Be(WithinYearJoin.JoinedByXrefId);
        aq.TextMismatched.Should().BeTrue();
    }

    [Fact]
    public void AddedInResponse_WhenQidAbsentFromTemplate()
    {
        var cur = Q("q_new", rowNumber: 20);

        var result = GdV01Aligner.Align(
            ParseResult(questions: new[] { cur }),
            ParseResult(),
            ParseResult());

        var aq = result.Aligned.Should().ContainSingle().Subject;
        aq.WithinYear.Should().Be(WithinYearJoin.AddedInResponse);
        aq.TemplateMatch.Should().BeNull();
        aq.RowShifted.Should().BeFalse();
        aq.TextMismatched.Should().BeFalse();
    }

    [Fact]
    public void WithinYearRemoved_ContainsTemplateOnlyQid()
    {
        var cur  = Q("q1", rowNumber: 10);
        var tmpl = Q("q1", rowNumber: 10);
        var removedTmpl = Q("q2", rowNumber: 20);

        var result = GdV01Aligner.Align(
            ParseResult(questions: new[] { cur }),
            ParseResult(questions: new[] { tmpl, removedTmpl }),
            ParseResult());

        result.WithinYearRemoved.Should().ContainSingle()
            .Which.XrefId.Should().Be("q2");
    }

    // ── §3.5 SectionName regression ─────────────────────────────────────────────

    [Fact]
    public void SectionName_PreservedOnAlignedEntries()
    {
        var cur1 = Q("q1", rowNumber: 10, sectionName: "Alpha");
        var cur2 = Q("q2", rowNumber: 20, sectionName: "Beta");

        var result = GdV01Aligner.Align(
            ParseResult(questions: new[] { cur1, cur2 }),
            ParseResult(),
            ParseResult());

        result.Aligned.Select(a => a.Current.SectionName)
            .Should().Equal("Alpha", "Beta");
    }

    // ── §3.6 Order + matcher-null ────────────────────────────────────────────────

    [Fact]
    public void Aligned_PreservesCurrentOrder_AndMatcherFieldsAreNull()
    {
        var cur1 = Q("q1", rowNumber: 10);
        var cur2 = Q("q2", rowNumber: 20);
        var cur3 = Q("q3", rowNumber: 30);

        var result = GdV01Aligner.Align(
            ParseResult(questions: new[] { cur1, cur2, cur3 }),
            ParseResult(),
            ParseResult());

        result.Aligned.Should().HaveCount(3);
        result.Aligned.Select(a => a.Current.XrefId).Should().Equal("q1", "q2", "q3");
        result.Aligned.Should().AllSatisfy(a =>
        {
            a.MatcherCandidate.Should().BeNull();
            a.MatcherBaseScore.Should().BeNull();
        });
    }

    // ── §3.7 Malformed mapping ───────────────────────────────────────────────────

    [Fact]
    public void MalformedMapping_AllThreeWorkbooks_AllThreeReasons()
    {
        // Each workbook contributes Blank, Duplicate, Unparseable; 9 keys total.
        var curMalformed = new[]
        {
            Malformed(row: 5,  xrefId: null,    GdMalformedXrefReason.Blank),
            Malformed(row: 6,  xrefId: "dup1",  GdMalformedXrefReason.Duplicate),
            Malformed(row: 7,  xrefId: "bad::",  GdMalformedXrefReason.Unparseable),
        };
        var tmplMalformed = new[]
        {
            Malformed(row: 15, xrefId: null,    GdMalformedXrefReason.Blank),
            Malformed(row: 16, xrefId: "dup2",  GdMalformedXrefReason.Duplicate),
            Malformed(row: 17, xrefId: "x::y",  GdMalformedXrefReason.Unparseable),
        };
        var prevMalformed = new[]
        {
            Malformed(row: 25, xrefId: null,    GdMalformedXrefReason.Blank),
            Malformed(row: 26, xrefId: "dup3",  GdMalformedXrefReason.Duplicate),
            Malformed(row: 27, xrefId: ":::",   GdMalformedXrefReason.Unparseable),
        };

        var result = GdV01Aligner.Align(
            ParseResult(malformed: curMalformed),
            ParseResult(malformed: tmplMalformed),
            ParseResult(malformed: prevMalformed));

        result.MalformedKeys.Should().HaveCount(9);

        // Current (first 3)
        result.MalformedKeys[0].Should().BeEquivalentTo(
            new MalformedKey(ValidationWorkbook.CurrentResponse, 5,  null,    MalformedKeyReason.Blank));
        result.MalformedKeys[1].Should().BeEquivalentTo(
            new MalformedKey(ValidationWorkbook.CurrentResponse, 6,  "dup1",  MalformedKeyReason.Duplicate));
        result.MalformedKeys[2].Should().BeEquivalentTo(
            new MalformedKey(ValidationWorkbook.CurrentResponse, 7,  "bad::", MalformedKeyReason.Unparseable));

        // Template (next 3)
        result.MalformedKeys[3].Should().BeEquivalentTo(
            new MalformedKey(ValidationWorkbook.EmptyTemplate, 15, null,    MalformedKeyReason.Blank));
        result.MalformedKeys[4].Should().BeEquivalentTo(
            new MalformedKey(ValidationWorkbook.EmptyTemplate, 16, "dup2",  MalformedKeyReason.Duplicate));
        result.MalformedKeys[5].Should().BeEquivalentTo(
            new MalformedKey(ValidationWorkbook.EmptyTemplate, 17, "x::y",  MalformedKeyReason.Unparseable));

        // Previous (last 3)
        result.MalformedKeys[6].Should().BeEquivalentTo(
            new MalformedKey(ValidationWorkbook.PreviousResponse, 25, null,   MalformedKeyReason.Blank));
        result.MalformedKeys[7].Should().BeEquivalentTo(
            new MalformedKey(ValidationWorkbook.PreviousResponse, 26, "dup3", MalformedKeyReason.Duplicate));
        result.MalformedKeys[8].Should().BeEquivalentTo(
            new MalformedKey(ValidationWorkbook.PreviousResponse, 27, ":::",  MalformedKeyReason.Unparseable));
    }

    // ── §3.8 WithinYearStructureCheck integration ────────────────────────────────

    [Fact]
    public void WithinYearStructureCheck_OverAlignerResult_EmitsRemovedAddedAndRowShift()
    {
        // Template has q1 and q2; current has q2 (shifted) and q_new (added).
        // q2 is matched at a different row → row-shift in the within-year sense but the
        // check uses ORDER-based shift. Only one matched question → no row-shift flagged
        // (k < 2 guard). q1 is removed. q_new is added.
        var qCur_q2   = Q("q2",    rowNumber: 25);
        var qCur_qNew = Q("q_new", rowNumber: 30);
        var qTmpl_q1  = Q("q1",    rowNumber: 10);
        var qTmpl_q2  = Q("q2",    rowNumber: 20);  // q2 shifted from row 20 to 25

        var result = GdV01Aligner.Align(
            ParseResult(questions: new[] { qCur_q2, qCur_qNew }),
            ParseResult(questions: new[] { qTmpl_q1, qTmpl_q2 }),
            ParseResult());

        var check   = new WithinYearStructureCheck<GdV01Question>(q => null, "C");
        var emitter = Emitter(check);
        var findings = check.Run(result, emitter);

        findings.Should().HaveCount(2, "q1 removed + q_new added; only one matched question so no row-shift");
        findings.Should().ContainSingle(f => f.CheckResult.Contains("absent from the response") && f.CheckResult.Contains("q1"))
            .Which.CellAddresses.Should().Be("C10");
        findings.Should().ContainSingle(f => f.CheckResult.Contains("absent from the empty template") && f.CheckResult.Contains("q_new"))
            .Which.CellAddresses.Should().Be("C30");
    }

    [Fact]
    public void WithinYearStructureCheck_RowShift_FiredWhenTwoMatchedOutOfOrder()
    {
        // q2 appears before q1 in current but q1 precedes q2 in template → q2 is row-shifted.
        var qCur_q2 = Q("q2", rowNumber: 10);
        var qCur_q1 = Q("q1", rowNumber: 20);
        var qTmpl_q1 = Q("q1", rowNumber: 10);
        var qTmpl_q2 = Q("q2", rowNumber: 20);

        var result = GdV01Aligner.Align(
            ParseResult(questions: new[] { qCur_q2, qCur_q1 }),
            ParseResult(questions: new[] { qTmpl_q1, qTmpl_q2 }),
            ParseResult());

        var check    = new WithinYearStructureCheck<GdV01Question>(q => null, "C");
        var emitter  = Emitter(check);
        var findings = check.Run(result, emitter);

        findings.Should().ContainSingle()
            .Which.CheckResult.Should().Contain("identity key 'q2'");
    }

    // ── §3.9 MalformedKeyCheck integration (incl. Unparseable arm) ──────────────

    [Fact]
    public void MalformedKeyCheck_OverAlignerResult_EmitsFatalPerKey_UnparseableRendered()
    {
        // current carries Blank, Duplicate, and Unparseable keys.
        var curMalformed = new[]
        {
            Malformed(row: 5,  xrefId: null,     GdMalformedXrefReason.Blank),
            Malformed(row: 6,  xrefId: "dup",    GdMalformedXrefReason.Duplicate),
            Malformed(row: 7,  xrefId: "bad::x", GdMalformedXrefReason.Unparseable),
        };

        var result = GdV01Aligner.Align(
            ParseResult(malformed: curMalformed),
            ParseResult(),
            ParseResult());

        var check    = new MalformedKeyCheck<GdV01Question>("Q");
        var emitter  = Emitter(check);
        var findings = check.Run(result, emitter);

        findings.Should().HaveCount(3);
        findings.Should().AllSatisfy(f =>
        {
            f.Check.Should().Be(ValidationCheck.Structure);
            f.Evaluation.Should().Be(FindingEvaluation.Fatal);
        });

        var blank = findings.Single(f => f.CellAddresses == "Q5");
        blank.CheckResult.Should().Contain("blank");

        var dup = findings.Single(f => f.CellAddresses == "Q6");
        dup.CheckResult.Should().Contain("duplicated").And.Contain("dup");

        var unparseable = findings.Single(f => f.CellAddresses == "Q7");
        unparseable.CheckResult.Should().Contain("unparseable").And.Contain("bad::x");
    }
}
