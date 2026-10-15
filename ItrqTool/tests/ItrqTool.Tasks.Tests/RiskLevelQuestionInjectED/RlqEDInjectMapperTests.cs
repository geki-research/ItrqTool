using FluentAssertions;
using ItrqTool.Domain;
using ItrqTool.Tasks.QuestionnaireValidation.Alignment;
using ItrqTool.Tasks.RiskLevelQuestionInject;
using ItrqTool.Tasks.RiskLevelQuestionInjectED;
using ItrqTool.Tasks.RiskLevelQuestionValidationV01;
using ItrqTool.Tasks.RiskLevelQuestionValidationV02;
using ItrqTool.Tasks.Shared;
using Xunit;

namespace ItrqTool.Tasks.Tests.RiskLevelQuestionInjectED;

/// <summary>
/// Pure unit tests for <see cref="RlqEDInjectMapper"/> — no Excel, no mocks. Builders are cloned
/// from the standard <c>RlqInjectMapperTests</c>. Where a message must be "the standard text",
/// the expected text is taken from the standard <see cref="RlqInjectMapper"/> run on the same
/// single-arm input, so the ED prefix is the only permitted difference.
/// </summary>
public sealed class RlqEDInjectMapperTests
{
    // ── builders ──────────────────────────────────────────────────────────────

    // The ED v02 letters (standard RlqV02Config shape).
    private static RlqV02Config EdV02() => new()
    {
        QuestionNumberColumn = "C",
        TextColumn = "D",
        GuidanceColumn = "E",
        RequestedTypeColumn = "F",
        PreviousAnswerColumn = "G",
        AnswerColumn = "J",
        RequestedExplanationColumn = "M",
        PreviousExplanationColumn = "N",
        CurrentExplanationColumn = "O",
        MaterialChangeColumn = "P",
        HowExplanationColumn = "Q",
        ProvidedByColumn = "T",
        XrefIdColumn = "V",
    };

    private static RlqEDInjectConfig EdConfig(string holding = "H", string eboe = "I") => new()
    {
        CurrentConfigFilename = "rlq-ed-v02-validation-config.json",
        PreviousConfigFilename = "rlq-ed-v01-validation-config.json",
        SourceHoldingAnswerColumn = "K",
        SourceEBOeAnswerColumn = "L",
        TargetHoldingAnswerColumn = holding,
        TargetEBOeAnswerColumn = eboe,
    };

    private static RlqV01Question Prev(
        int row = 100,
        string? answer = "3",
        string? providedBy = null,
        IReadOnlyList<RlqExplanationRow>? expl = null) => new(
        RowNumber: row,
        XrefId: "RLQ-1.1",
        OriginalText: "How is risk assessed?",
        QuestionText: "How is risk assessed?",
        SectionName: "Risk",
        QuestionNumber: "1.1",
        Guidance: null,
        RequestedType: null,
        PreviousAnswer: null,
        Answer: answer,
        AnswerDvType: null,
        AnswerDvFormula: null,
        AnswerDvOperator: null,
        AnswerDvFormula2: null,
        MaterialChange: null,
        MaterialChangeDvType: null,
        MaterialChangeDvFormula: null,
        MaterialChangeDvOperator: null,
        MaterialChangeDvFormula2: null,
        ProvidedBy: providedBy,
        ExplanationRows: expl ?? []);

    private static RlqV02Question Cur(
        int row = 10,
        string? xref = "RLQ-1.1",
        IReadOnlyList<RlqExplanationRow>? expl = null) => new(
        RowNumber: row,
        XrefId: xref,
        OriginalText: "How is risk assessed?",
        QuestionText: "How is risk assessed?",
        SectionName: "Risk",
        QuestionNumber: "1.1",
        Guidance: null,
        RequestedType: null,
        PreviousAnswer: null,
        Answer: null,
        AnswerDvType: null,
        AnswerDvFormula: null,
        AnswerDvOperator: null,
        AnswerDvFormula2: null,
        MaterialChange: null,
        MaterialChangeDvType: null,
        MaterialChangeDvFormula: null,
        MaterialChangeDvOperator: null,
        MaterialChangeDvFormula2: null,
        ProvidedBy: null,
        ExplanationRows: expl ?? [],
        HowExplanation: null);

    private static RlqExplanationRow Expl(string? current, int row)
        => new(Requested: null, Previous: null, Current: current, RowNumber: row);

    private static CrossFormatMatch<RlqV02Question, RlqV01Question> Agree(
        RlqV02Question c, RlqV01Question p)
        => new(c, CrossYearOutcome.Agree, p, p, p, 0.95);

    private static CrossFormatMatch<RlqV02Question, RlqV01Question> Ambiguous(
        RlqV02Question c, CrossYearOutcome outcome)
        => new(c, outcome, null, null, null, null);

    private static CrossFormatMatch<RlqV02Question, RlqV01Question> AgreeWithTexts(
        string currentText, string previousText, double? score, int currentRow = 10)
    {
        var c = Cur(row: currentRow) with { OriginalText = currentText, QuestionText = currentText };
        var p = Prev() with { OriginalText = previousText, QuestionText = previousText };
        return new(c, CrossYearOutcome.Agree, p, p, p, score);
    }

    private static TargetDvInfo TgtDv(string? type)
        => new(type, Operator: null, Formula: null, Formula2: null, ListValues: null);

    private static Dictionary<int, (string? DvType, object? Native, string? TextValue)> Src(
        string? dvType, object? native, string? text, int row = 100)
        => new() { [row] = (dvType, native, text) };

    private static Dictionary<int, TargetDvInfo> Tgt(TargetDvInfo dv, int row = 10)
        => new() { [row] = dv };

    private static Dictionary<int, (string? DvType, object? Native, string? TextValue)> NoSrc() => new();
    private static Dictionary<int, TargetDvInfo> NoTgt() => new();

    private sealed record ArmMaps(
        Dictionary<RlqEDAnswerArm, IReadOnlyDictionary<int, (string? DvType, object? Native, string? TextValue)>> Source,
        Dictionary<RlqEDAnswerArm, IReadOnlyDictionary<int, TargetDvInfo>> Target);

    // All three arm keys present; the given arm carries the given lookups, the others are empty.
    private static ArmMaps OnlyArm(
        RlqEDAnswerArm arm,
        IReadOnlyDictionary<int, (string? DvType, object? Native, string? TextValue)> src,
        IReadOnlyDictionary<int, TargetDvInfo> tgt)
    {
        var maps = Empty();
        maps.Source[arm] = src;
        maps.Target[arm] = tgt;
        return maps;
    }

    private static ArmMaps Empty() => new(
        new()
        {
            [RlqEDAnswerArm.Answer] = NoSrc(),
            [RlqEDAnswerArm.Holding] = NoSrc(),
            [RlqEDAnswerArm.EBOe] = NoSrc(),
        },
        new()
        {
            [RlqEDAnswerArm.Answer] = NoTgt(),
            [RlqEDAnswerArm.Holding] = NoTgt(),
            [RlqEDAnswerArm.EBOe] = NoTgt(),
        });

    // Every arm writes: answer 3, Holding 1, EBOe 2, all into WholeNumber targets.
    private static ArmMaps AllArmsWriting(int sourceRow = 100, int targetRow = 10) => new(
        new()
        {
            [RlqEDAnswerArm.Answer] = Src("WholeNumber", 3.0, "3", sourceRow),
            [RlqEDAnswerArm.Holding] = Src("WholeNumber", 1.0, "1", sourceRow),
            [RlqEDAnswerArm.EBOe] = Src("WholeNumber", 2.0, "2", sourceRow),
        },
        new()
        {
            [RlqEDAnswerArm.Answer] = Tgt(TgtDv("WholeNumber"), targetRow),
            [RlqEDAnswerArm.Holding] = Tgt(TgtDv("WholeNumber"), targetRow),
            [RlqEDAnswerArm.EBOe] = Tgt(TgtDv("WholeNumber"), targetRow),
        });

    private static (IReadOnlyList<CellWriteEntry> cells, IReadOnlyList<TaskMessage> messages) MapOne(
        CrossFormatMatch<RlqV02Question, RlqV01Question> match,
        ArmMaps? arms = null,
        RlqEDInjectConfig? edConfig = null)
    {
        var maps = arms ?? Empty();
        var result = new CrossFormatAlignmentResult<RlqV02Question, RlqV01Question>([match], []);
        return RlqEDInjectMapper.Map(result, EdV02(), edConfig ?? EdConfig(), maps.Source, maps.Target);
    }

    // The standard mapper on the same single-arm lookups — the source of "the standard text".
    private static (IReadOnlyList<CellWriteEntry> cells, IReadOnlyList<TaskMessage> messages) Standard(
        CrossFormatMatch<RlqV02Question, RlqV01Question> match,
        IReadOnlyDictionary<int, (string? DvType, object? Native, string? TextValue)> src,
        IReadOnlyDictionary<int, TargetDvInfo> tgt)
    {
        var result = new CrossFormatAlignmentResult<RlqV02Question, RlqV01Question>([match], []);
        return RlqInjectMapper.Map(result, EdV02(), src, tgt);
    }

    private static CellWriteEntry? Cell(IReadOnlyList<CellWriteEntry> cells, int row, string column)
        => cells.SingleOrDefault(e => e.Row == row && e.Column == column);

    private static IEnumerable<CellWriteEntry> ArmCells(IReadOnlyList<CellWriteEntry> cells)
        => cells.Where(e => e.Column is "G" or "H" or "I");

    // ── M1. equal type → native at the arm's column, no message ───────────────
    // The text fallback: Answer keeps the standard p.Answer; Holding / EBOe use the source cell's
    // own TextValue (RlqV01Question has no Holding / EBOe text).

    [Theory]
    [InlineData(RlqEDAnswerArm.Answer, "G", "total")]
    [InlineData(RlqEDAnswerArm.Holding, "H", "3")]
    [InlineData(RlqEDAnswerArm.EBOe, "I", "3")]
    public void Arm_EqualType_WritesNativeAtArmColumn_NoMessage(RlqEDAnswerArm arm, string column, string expectedText)
    {
        var arms = OnlyArm(arm, Src("WholeNumber", 3.0, "3"), Tgt(TgtDv("WholeNumber")));

        var (cells, messages) = MapOne(Agree(Cur(), Prev(answer: "total")), arms);

        var cell = Cell(cells, 10, column);
        cell.Should().NotBeNull();
        cell!.TypedValue.Should().Be(3.0);
        cell.TypedValue.Should().BeOfType<double>();
        cell.Value.Should().Be(expectedText);
        ArmCells(cells).Should().ContainSingle("only the populated arm writes");
        messages.Should().BeEmpty();
    }

    // ── M2. widen (WholeNumber → Decimal) → native + one labelled Info ────────

    [Theory]
    [InlineData(RlqEDAnswerArm.Answer, "G", "answer")]
    [InlineData(RlqEDAnswerArm.Holding, "H", "answer (Holding)")]
    [InlineData(RlqEDAnswerArm.EBOe, "I", "answer (EBOe)")]
    public void Arm_WholeToDecimal_WritesNative_OneLabelledInfo(RlqEDAnswerArm arm, string column, string label)
    {
        var src = Src("WholeNumber", 3.0, "3");
        var tgt = Tgt(TgtDv("Decimal"));

        var (cells, messages) = MapOne(Agree(Cur(), Prev()), OnlyArm(arm, src, tgt));

        Cell(cells, 10, column)!.TypedValue.Should().Be(3.0);
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Info);
        messages[0].Text.Should().Be(
            $"[{label}] Row 10 (xref RLQ-1.1): answer type widened (WholeNumber → Decimal) — value written as-is.");
        messages[0].Text.Should().Be(
            $"[{label}] " + Standard(Agree(Cur(), Prev()), src, tgt).messages.Single().Text);
    }

    // ── M3. decimal with a fraction → WholeNumber → skip + labelled Error ─────

    [Theory]
    [InlineData(RlqEDAnswerArm.Answer, "G", "answer")]
    [InlineData(RlqEDAnswerArm.Holding, "H", "answer (Holding)")]
    [InlineData(RlqEDAnswerArm.EBOe, "I", "answer (EBOe)")]
    public void Arm_DecimalFractionToWhole_SkipsWithLabelledError(RlqEDAnswerArm arm, string column, string label)
    {
        var src = Src("Decimal", 3.5, "3.5");
        var tgt = Tgt(TgtDv("WholeNumber"));

        var (cells, messages) = MapOne(Agree(Cur(), Prev()), OnlyArm(arm, src, tgt));

        Cell(cells, 10, column).Should().BeNull();
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Error);
        messages[0].Text.Should().StartWith($"[{label}] Row 10 (xref RLQ-1.1): value '3.5' does not conform");
        messages[0].Text.Should().Be(
            $"[{label}] " + Standard(Agree(Cur(), Prev()), src, tgt).messages.Single().Text);
    }

    // ── M4. numeric source into a Date/Time target → pre-gate skip + labelled Error ──

    [Theory]
    [InlineData(RlqEDAnswerArm.Answer, "G", "answer")]
    [InlineData(RlqEDAnswerArm.Holding, "H", "answer (Holding)")]
    [InlineData(RlqEDAnswerArm.EBOe, "I", "answer (EBOe)")]
    public void Arm_NumericIntoDateTarget_SkipsWithLabelledError(RlqEDAnswerArm arm, string column, string label)
    {
        var arms = OnlyArm(arm, Src("WholeNumber", 5.0, "5"), Tgt(TgtDv("Date")));

        var (cells, messages) = MapOne(Agree(Cur(), Prev()), arms);

        Cell(cells, 10, column).Should().BeNull();
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Error);
        messages[0].Text.Should().Be(
            $"[{label}] Row 10 (xref RLQ-1.1): numeric value not injected into a date/time cell");
    }

    // ── M5. List target, non-member source → skip + labelled Error ────────────

    [Theory]
    [InlineData(RlqEDAnswerArm.Answer, "G", "answer")]
    [InlineData(RlqEDAnswerArm.Holding, "H", "answer (Holding)")]
    [InlineData(RlqEDAnswerArm.EBOe, "I", "answer (EBOe)")]
    public void Arm_ListTargetNonMember_SkipsWithLabelledError(RlqEDAnswerArm arm, string column, string label)
    {
        var src = Src(null, "Maybe", "Maybe");
        var tgt = Tgt(new TargetDvInfo("List", Operator: null, Formula: null, Formula2: null, ListValues: ["Yes", "No"]));

        var (cells, messages) = MapOne(Agree(Cur(), Prev(answer: "Maybe")), OnlyArm(arm, src, tgt));

        Cell(cells, 10, column).Should().BeNull();
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Error);
        messages[0].Text.Should().Contain("does not conform");
        messages[0].Text.Should().Be(
            $"[{label}] " + Standard(Agree(Cur(), Prev(answer: "Maybe")), src, tgt).messages.Single().Text);
    }

    // ── M6. unresolvable List → skip + labelled Warning ───────────────────────

    [Theory]
    [InlineData(RlqEDAnswerArm.Answer, "G", "answer")]
    [InlineData(RlqEDAnswerArm.Holding, "H", "answer (Holding)")]
    [InlineData(RlqEDAnswerArm.EBOe, "I", "answer (EBOe)")]
    public void Arm_UnresolvableList_SkipsWithLabelledWarning(RlqEDAnswerArm arm, string column, string label)
    {
        var arms = OnlyArm(arm, Src(null, "Yes", "Yes"), Tgt(TgtDv("List")));

        var (cells, messages) = MapOne(Agree(Cur(), Prev(answer: "Yes")), arms);

        Cell(cells, 10, column).Should().BeNull();
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Warning);
        messages[0].Text.Should().Be(
            $"[{label}] Row 10 (xref RLQ-1.1): target data-validation vocabulary could not be resolved");
    }

    // ── M7. NotCheckable: value-typed target injects; otherwise skip + Warning ─

    [Theory]
    [InlineData(RlqEDAnswerArm.Answer, "G")]
    [InlineData(RlqEDAnswerArm.Holding, "H")]
    [InlineData(RlqEDAnswerArm.EBOe, "I")]
    public void Arm_NotCheckable_ValueTypedTarget_Injects(RlqEDAnswerArm arm, string column)
    {
        // Unparseable bound ⇒ the evaluator answers NotCheckable; WholeNumber is value-typed ⇒ Inject.
        var tgt = Tgt(new TargetDvInfo("WholeNumber", Operator: "Between", Formula: "=$A$1", Formula2: "10", ListValues: null));
        var arms = OnlyArm(arm, Src("WholeNumber", 5.0, "5"), tgt);

        var (cells, messages) = MapOne(Agree(Cur(), Prev(answer: "5")), arms);

        Cell(cells, 10, column)!.TypedValue.Should().Be(5.0);
        messages.Should().BeEmpty();
    }

    [Theory]
    [InlineData(RlqEDAnswerArm.Answer, "G", "answer")]
    [InlineData(RlqEDAnswerArm.Holding, "H", "answer (Holding)")]
    [InlineData(RlqEDAnswerArm.EBOe, "I", "answer (EBOe)")]
    public void Arm_NotCheckable_NonValueTypedTarget_SkipsWithLabelledWarning(RlqEDAnswerArm arm, string column, string label)
    {
        var arms = OnlyArm(arm, Src(null, "x", "x"), Tgt(TgtDv("Custom")));

        var (cells, messages) = MapOne(Agree(Cur(), Prev(answer: "x")), arms);

        Cell(cells, 10, column).Should().BeNull();
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Warning);
        messages[0].Text.Should().Be(
            $"[{label}] Row 10 (xref RLQ-1.1): target data-validation rule could not be evaluated");
    }

    // ── M8. comma-rendered decimal, native in bound → injects (LSN-0201) ──────

    [Theory]
    [InlineData(RlqEDAnswerArm.Answer, "G")]
    [InlineData(RlqEDAnswerArm.Holding, "H")]
    [InlineData(RlqEDAnswerArm.EBOe, "I")]
    public void Arm_CommaDecimalSource_NativeInBound_Injects(RlqEDAnswerArm arm, string column)
    {
        var tgt = Tgt(new TargetDvInfo("Decimal", Operator: "Between", Formula: "0", Formula2: "100", ListValues: null));
        var arms = OnlyArm(arm, Src("Decimal", 9.1, "9,1"), tgt);

        var (cells, messages) = MapOne(Agree(Cur(), Prev(answer: "9,1")), arms);

        var cell = Cell(cells, 10, column);
        cell.Should().NotBeNull();
        cell!.TypedValue.Should().Be(9.1);
        messages.Should().BeEmpty();
    }

    // ── M9. blank source → that arm's cell omitted, no message ────────────────

    [Theory]
    [InlineData(RlqEDAnswerArm.Answer)]
    [InlineData(RlqEDAnswerArm.Holding)]
    [InlineData(RlqEDAnswerArm.EBOe)]
    public void Arm_BlankSource_OmitsCell_NoMessage(RlqEDAnswerArm arm)
    {
        foreach (var blank in new object?[] { null, "   " })
        {
            var arms = OnlyArm(arm, Src("WholeNumber", blank, blank as string), Tgt(TgtDv("WholeNumber")));

            var (cells, messages) = MapOne(Agree(Cur(), Prev(providedBy: null)), arms);

            cells.Should().BeEmpty();
            messages.Should().BeEmpty();
        }
    }

    // ── M10. independence: a Holding skip blocks nothing else ─────────────────

    [Fact]
    public void HoldingSkip_StillWritesAnswerEBOeExplanationsAndProvidedBy()
    {
        var arms = AllArmsWriting();
        arms.Source[RlqEDAnswerArm.Holding] = Src("Decimal", 3.5, "3.5");   // fraction into WholeNumber → Error

        var c = Cur(row: 10, expl: [Expl(null, 11), Expl(null, 12)]);
        var p = Prev(row: 100, providedBy: "Bob", expl: [Expl("e1", 101), Expl("e2", 102)]);

        var (cells, messages) = MapOne(Agree(c, p), arms);

        cells.Select(e => (e.Row, e.Column)).Should().BeEquivalentTo(new[]
        {
            (10, "G"), (10, "I"), (11, "N"), (12, "N"), (10, "T"),
        });
        Cell(cells, 10, "G")!.TypedValue.Should().Be(3.0);
        Cell(cells, 10, "I")!.TypedValue.Should().Be(2.0);
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Error);
        messages[0].Text.Should().StartWith("[answer (Holding)] Row 10 (xref RLQ-1.1): ");
    }

    // ── M11. missing inner row entries behave as the standard ─────────────────
    // Standard (RlqInjectMapper.cs:97-98): TryGetValue, so a missing SOURCE row reads as
    // (null, null, null) → blank → no cell, no message; a missing TARGET row passes a null DV
    // rule to the guard → Conformant → native written, no message.

    [Fact]
    public void MissingInnerRowEntries_BehaveAsStandard()
    {
        var srcOtherRow = Src("WholeNumber", 1.0, "1", row: 999);   // nothing for v01 row 100
        var tgtPresent = Tgt(TgtDv("WholeNumber"));
        var srcPresent = Src("WholeNumber", 2.0, "2");
        var tgtOtherRow = Tgt(TgtDv("Date"), row: 999);              // nothing for v02 row 10

        var arms = Empty();
        arms.Source[RlqEDAnswerArm.Holding] = srcOtherRow;
        arms.Target[RlqEDAnswerArm.Holding] = tgtPresent;
        arms.Source[RlqEDAnswerArm.EBOe] = srcPresent;
        arms.Target[RlqEDAnswerArm.EBOe] = tgtOtherRow;

        var (cells, messages) = MapOne(Agree(Cur(), Prev()), arms);

        Cell(cells, 10, "H").Should().BeNull();
        Cell(cells, 10, "I")!.TypedValue.Should().Be(2.0);
        messages.Should().BeEmpty();

        var stdMissingSource = Standard(Agree(Cur(), Prev()), srcOtherRow, tgtPresent);
        stdMissingSource.cells.Should().BeEmpty();
        stdMissingSource.messages.Should().BeEmpty();

        var stdMissingTarget = Standard(Agree(Cur(), Prev()), srcPresent, tgtOtherRow);
        stdMissingTarget.cells.Should().ContainSingle().Which.TypedValue.Should().Be(2.0);
        stdMissingTarget.messages.Should().BeEmpty();
    }

    // ── M12. missing arm key → ArgumentException naming the arm ───────────────

    [Theory]
    [InlineData(true, RlqEDAnswerArm.Answer)]
    [InlineData(true, RlqEDAnswerArm.Holding)]
    [InlineData(true, RlqEDAnswerArm.EBOe)]
    [InlineData(false, RlqEDAnswerArm.Answer)]
    [InlineData(false, RlqEDAnswerArm.Holding)]
    [InlineData(false, RlqEDAnswerArm.EBOe)]
    public void MissingArmKey_Throws(bool sourceSide, RlqEDAnswerArm arm)
    {
        var arms = AllArmsWriting();
        if (sourceSide) arms.Source.Remove(arm);
        else arms.Target.Remove(arm);

        var act = () => MapOne(Agree(Cur(), Prev()), arms);

        act.Should().Throw<ArgumentException>()
            .WithMessage($"*'{arm}' arm*")
            .And.ParamName.Should().Be(sourceSide ? "sourceByArm" : "targetByArm");
    }

    // ── M13. explanations (standard K→J logic into N) ─────────────────────────

    [Fact]
    public void Explanations_PositionAligned_WritesPerRow()
    {
        var c = Cur(row: 10, expl: [Expl(null, 11), Expl(null, 12), Expl(null, 13)]);
        var p = Prev(row: 100, expl: [Expl("s1", 101), Expl("s2", 102), Expl("s3", 103)]);

        var (cells, messages) = MapOne(Agree(c, p));

        Cell(cells, 11, "N")!.Value.Should().Be("s1");
        Cell(cells, 12, "N")!.Value.Should().Be("s2");
        Cell(cells, 13, "N")!.Value.Should().Be("s3");
        messages.Should().BeEmpty();
    }

    [Fact]
    public void Explanations_RowCountMismatch_WritesOverlapPlusOneUnchangedWarning()
    {
        var c = Cur(row: 10, expl: [Expl(null, 11), Expl(null, 12)]);
        var p = Prev(row: 100, expl: [Expl("s1", 101), Expl("s2", 102), Expl("s3", 103)]);

        var (cells, messages) = MapOne(Agree(c, p));

        Cell(cells, 11, "N")!.Value.Should().Be("s1");
        Cell(cells, 12, "N")!.Value.Should().Be("s2");
        cells.Should().NotContain(e => e.Column == "N" && e.Row == 13);
        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Warning);
        messages[0].Text.Should().Be(
            "Row 10 (xref RLQ-1.1): explanation row count mismatch (source 3, target 2) — overlap written.");
        messages[0].Text.Should().Be(Standard(Agree(c, p), NoSrc(), NoTgt()).messages.Single().Text);
    }

    [Fact]
    public void Explanations_BlankSourceRow_OmitsThatRow()
    {
        var c = Cur(row: 10, expl: [Expl(null, 11), Expl(null, 12), Expl(null, 13)]);
        var p = Prev(row: 100, expl: [Expl("s1", 101), Expl("   ", 102), Expl("s3", 103)]);

        var (cells, messages) = MapOne(Agree(c, p));

        Cell(cells, 11, "N")!.Value.Should().Be("s1");
        Cell(cells, 12, "N").Should().BeNull();
        Cell(cells, 13, "N")!.Value.Should().Be("s3");
        messages.Should().BeEmpty();
    }

    // ── M14. provided-by as text at T ─────────────────────────────────────────

    [Fact]
    public void ProvidedBy_WritesTextAtT()
    {
        var (cells, _) = MapOne(Agree(Cur(), Prev(providedBy: "Carol")));
        var t = Cell(cells, 10, "T");
        t.Should().NotBeNull();
        t!.Value.Should().Be("Carol");
        t.TypedValue.Should().BeNull();

        var (blankCells, _) = MapOne(Agree(Cur(), Prev(providedBy: "   ")));
        Cell(blankCells, 10, "T").Should().BeNull();
    }

    // ── M15. drift — standard text, unchanged; every arm still written ────────

    [Fact]
    public void Agree_IdenticalText_EmitsNoWarning()
    {
        var (_, messages) = MapOne(Agree(Cur(), Prev()), AllArmsWriting());

        messages.Should().BeEmpty();
    }

    [Fact]
    public void Agree_ChangedText_ExactUnchangedStandardMessage()
    {
        var match = AgreeWithTexts("How is risk assessed in 2025?", "How is risk assessed in 2024?",
                                   score: 0.9655, currentRow: 42);

        var (_, messages) = MapOne(match);

        messages.Should().ContainSingle().Which.Text.Should().Be(
            "Row 42 (xref RLQ-1.1): question text changed since the previous year " +
            "(similarity 0.9655) — treated as the same question; previous values carried forward. " +
            "Previous (row 100): \"How is risk assessed in 2024?\". " +
            "Current: \"How is risk assessed in 2025?\".");
        messages[0].Severity.Should().Be(MessageSeverity.Warning);
        messages[0].Text.Should().Be(Standard(match, NoSrc(), NoTgt()).messages.Single().Text);
    }

    [Fact]
    public void Agree_CaseOnlyDrift_StillWarns()
    {
        var (_, messages) = MapOne(AgreeWithTexts("HOW IS RISK ASSESSED?", "how is risk assessed?", score: 1.0));

        messages.Should().ContainSingle().Which.Severity.Should().Be(MessageSeverity.Warning);
    }

    [Fact]
    public void Agree_ChangedText_NullScore_RendersUnknownNotZero()
    {
        var (_, messages) = MapOne(AgreeWithTexts("New wording", "Old wording", score: null));

        var text = messages.Should().ContainSingle().Subject.Text;
        text.Should().Contain("unknown");
        text.Should().NotContain("0.0000");
    }

    [Fact]
    public void Agree_ChangedText_StillWritesAllFiveArms()
    {
        var c = Cur(expl: [Expl(null, 11)]);
        var p = Prev(providedBy: "OU1", expl: [Expl("e1", 101)]);

        var identical = MapOne(Agree(c, p), AllArmsWriting());
        var drifted = MapOne(
            new CrossFormatMatch<RlqV02Question, RlqV01Question>(
                c with { OriginalText = "New wording", QuestionText = "New wording" },
                CrossYearOutcome.Agree,
                p with { OriginalText = "Old wording", QuestionText = "Old wording" },
                null, null, 0.72),
            AllArmsWriting());

        drifted.cells.Should().BeEquivalentTo(identical.cells);
        drifted.cells.Select(e => e.Column).Should().BeEquivalentTo(new[] { "G", "H", "I", "N", "T" });
        drifted.messages.Should().ContainSingle().Which.Severity.Should().Be(MessageSeverity.Warning);
    }

    [Fact]
    public void Agree_ChangedText_MultiRowQuestion_StillEmitsExactlyOneWarning()
    {
        var c = Cur(expl: [Expl(null, 10), Expl(null, 11), Expl(null, 12)])
            with { OriginalText = "New wording", QuestionText = "New wording" };
        var p = Prev(expl: [Expl("a", 100), Expl("b", 101), Expl("c", 102)])
            with { OriginalText = "Old wording", QuestionText = "Old wording" };

        var (_, messages) = MapOne(
            new CrossFormatMatch<RlqV02Question, RlqV01Question>(c, CrossYearOutcome.Agree, p, p, p, 0.72),
            AllArmsWriting());

        messages.Where(m => m.Text.Contains("question text changed"))
            .Should().HaveCount(1, "the Warning is per question, not per row or per arm");
    }

    // ── M16. ambiguous → one Warning, no cells in any column; Neither → nothing ─

    [Fact]
    public void Ambiguous_EmitsOneWarningNoCells()
    {
        var c = Cur(row: 42, xref: "RLQ-3.1");

        foreach (var outcome in new[]
                 {
                     CrossYearOutcome.XrefIdConflict,
                     CrossYearOutcome.NewXrefIdWithLookalike,
                     CrossYearOutcome.SameXrefIdTextDiverged,
                 })
        {
            var (cells, messages) = MapOne(Ambiguous(c, outcome), AllArmsWriting(targetRow: 42));

            cells.Should().BeEmpty();
            messages.Should().ContainSingle();
            messages[0].Severity.Should().Be(MessageSeverity.Warning);
            messages[0].Text.Should().Be(
                $"Row 42 (xref RLQ-3.1): ambiguous previous match ({outcome}) — left untouched.");
        }
    }

    [Fact]
    public void Neither_EmitsNothing()
    {
        foreach (var outcome in new[]
                 {
                     CrossYearOutcome.Neither,
                     CrossYearOutcome.NotEvaluatedMalformedKey,
                 })
        {
            var (cells, messages) = MapOne(Ambiguous(Cur(), outcome), AllArmsWriting());
            cells.Should().BeEmpty();
            messages.Should().BeEmpty();
        }
    }

    // ── M17. never-written: v02 current columns J, K, L, O, P, Q ──────────────

    [Fact]
    public void FullScenario_NeverWritesV02CurrentColumns()
    {
        var c = Cur(row: 10, expl: [Expl(null, 11), Expl(null, 12)]);
        var p = Prev(row: 100, providedBy: "Bob", expl: [Expl("e1", 101), Expl("e2", 102)]) with
        {
            MaterialChange = "Yes",
        };

        var (cells, messages) = MapOne(Agree(c, p), AllArmsWriting());

        cells.Select(e => e.Column).Distinct().Should().BeEquivalentTo(new[] { "G", "H", "I", "N", "T" });
        var neverWritten = new[] { "J", "K", "L", "O", "P", "Q" };
        cells.Should().NotContain(e => neverWritten.Contains(e.Column));
        messages.Should().BeEmpty();
    }

    // ── M18. Holding / EBOe coordinates come from the ED config ───────────────

    [Fact]
    public void HoldingAndEBOeColumns_ComeFromEdConfig()
    {
        var (cells, messages) = MapOne(Agree(Cur(), Prev()), AllArmsWriting(), EdConfig(holding: "X", eboe: "Y"));

        Cell(cells, 10, "X")!.TypedValue.Should().Be(1.0);
        Cell(cells, 10, "Y")!.TypedValue.Should().Be(2.0);
        Cell(cells, 10, "G")!.TypedValue.Should().Be(3.0);
        cells.Should().NotContain(e => e.Column == "H" || e.Column == "I");
        messages.Should().BeEmpty();
    }

    // ── M19. the Answer arm's own messages carry the "[answer] " prefix ───────

    [Fact]
    public void AnswerArm_Error_CarriesAnswerPrefix()
    {
        var src = Src("List", "Yes", "Yes");
        var tgt = Tgt(TgtDv("WholeNumber"));

        var (_, messages) = MapOne(Agree(Cur(), Prev(answer: "Yes")), OnlyArm(RlqEDAnswerArm.Answer, src, tgt));

        messages.Should().ContainSingle();
        messages[0].Severity.Should().Be(MessageSeverity.Error);
        messages[0].Text.Should().StartWith("[answer] Row 10 (xref RLQ-1.1): ");
        messages[0].Text.Should().Be(
            "[answer] " + Standard(Agree(Cur(), Prev(answer: "Yes")), src, tgt).messages.Single().Text);
    }

    // ── F1 (review N1). per-question message order: drift → answer → Holding → EBOe → explanation ──

    [Fact]
    public void MessageOrder_DriftThenAnswerHoldingEBOeThenExplanations()
    {
        // Every arm skips (fraction into WholeNumber → Error); explanation counts differ (2 vs 1).
        var arms = new ArmMaps(
            new()
            {
                [RlqEDAnswerArm.Answer] = Src("Decimal", 3.5, "3.5"),
                [RlqEDAnswerArm.Holding] = Src("Decimal", 3.5, "3.5"),
                [RlqEDAnswerArm.EBOe] = Src("Decimal", 3.5, "3.5"),
            },
            new()
            {
                [RlqEDAnswerArm.Answer] = Tgt(TgtDv("WholeNumber")),
                [RlqEDAnswerArm.Holding] = Tgt(TgtDv("WholeNumber")),
                [RlqEDAnswerArm.EBOe] = Tgt(TgtDv("WholeNumber")),
            });

        var c = Cur(row: 10, expl: [Expl(null, 11)])
            with { OriginalText = "New wording", QuestionText = "New wording" };
        var p = Prev(row: 100, expl: [Expl("e1", 101), Expl("e2", 102)])
            with { OriginalText = "Old wording", QuestionText = "Old wording" };

        var (_, messages) = MapOne(
            new CrossFormatMatch<RlqV02Question, RlqV01Question>(c, CrossYearOutcome.Agree, p, p, p, 0.72),
            arms);

        messages.Should().HaveCount(5);

        messages[0].Severity.Should().Be(MessageSeverity.Warning);
        messages[0].Text.Should().StartWith("Row 10 (xref RLQ-1.1): question text changed");

        messages[1].Severity.Should().Be(MessageSeverity.Error);
        messages[1].Text.Should().StartWith("[answer] Row 10 (xref RLQ-1.1): ");

        messages[2].Severity.Should().Be(MessageSeverity.Error);
        messages[2].Text.Should().StartWith("[answer (Holding)] Row 10 (xref RLQ-1.1): ");

        messages[3].Severity.Should().Be(MessageSeverity.Error);
        messages[3].Text.Should().StartWith("[answer (EBOe)] Row 10 (xref RLQ-1.1): ");

        messages[4].Severity.Should().Be(MessageSeverity.Warning);
        messages[4].Text.Should().StartWith("Row 10 (xref RLQ-1.1): explanation row count mismatch (source 2, target 1)");
    }
}
