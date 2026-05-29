using FluentAssertions;
using Xunit;
using ItrqTool.Tasks.GeneralDataDiff;

namespace ItrqTool.Tasks.Tests;

public sealed class GeneralDataDiffEngineTests
{
    // ── Question factory helpers ────────────────────────────────────────────────

    private static GeneralDataQuestion Q(
        string text,
        string? qn = null,
        int row = 1,
        IReadOnlyList<GeneralDataAnswerCell>?      answerCells = null,
        IReadOnlyList<GeneralDataExplanationCell>? explanationCells = null)
        => new("Section", text, qn, row,
               new[] { qn ?? "" },
               answerCells ?? [],
               explanationCells ?? []);

    private static GeneralDataQuestion Qs(string text, string section, string? qn = null,
        int row = 1)
        => new(section, text, qn, row, new[] { qn ?? "" }, [], []);

    private static GeneralDataAnswerCell AC(
        int rowOffset, string col, string text,
        string? dvType = null, string? dvFormula = null, string? cfOperator = null,
        string? dvOperator = null, string? dvFormula2 = null,
        string? cfType = null, string? cfValue = null, string? cfValue2 = null)
        => new(rowOffset, col, text, dvType, dvFormula, cfOperator,
               dvOperator, dvFormula2, cfType, cfValue, cfValue2);

    private static GeneralDataExplanationCell EC(
        int rowOffset, string text,
        string? dvType = null, string? dvFormula = null, string? cfOperator = null)
        => new(rowOffset, text, dvType, dvFormula, cfOperator);

    // ── Empty / trivial ────────────────────────────────────────────────────────

    [Fact]
    public void Diff_BothEmpty_AllBucketsEmpty()
    {
        var result = GeneralDataDiffEngine.Diff([], []);
        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
        result.Changed.Should().BeEmpty();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_OnlyOldQuestions_AllRemoved()
    {
        var result = GeneralDataDiffEngine.Diff([Q("What is risk?"), Q("Describe controls.")], []);
        result.Removed.Should().HaveCount(2);
        result.Added.Should().BeEmpty();
        result.Changed.Should().BeEmpty();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_OnlyNewQuestions_AllAdded()
    {
        var result = GeneralDataDiffEngine.Diff([], [Q("What is risk?"), Q("Describe controls.")]);
        result.Added.Should().HaveCount(2);
        result.Removed.Should().BeEmpty();
        result.Changed.Should().BeEmpty();
        result.Unchanged.Should().BeEmpty();
    }

    // ── Basic added / removed / changed / unchanged ────────────────────────────

    [Fact]
    public void Diff_AllIdentical_NoCells_AllUnchanged()
    {
        var questions = new[] { Q("What is risk?"), Q("Describe controls.") };
        var result = GeneralDataDiffEngine.Diff(questions, questions);

        result.Unchanged.Should().HaveCount(2);
        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_OneAdded_AppearsInAdded()
    {
        var old = new[] { Q("What is risk?") };
        var newQ = new[] { Q("What is risk?"), Q("New question.") };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Added.Should().HaveCount(1);
        result.Added[0].Question.QuestionText.Should().Be("New question.");
        result.Removed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_OneRemoved_AppearsInRemoved()
    {
        var old = new[] { Q("What is risk?"), Q("Old question.") };
        var newQ = new[] { Q("What is risk?") };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Removed.Should().HaveCount(1);
        result.Removed[0].Question.QuestionText.Should().Be("Old question.");
        result.Added.Should().BeEmpty();
    }

    [Fact]
    public void Diff_QuestionRewordedSignificantly_OldRemovedNewAdded()
    {
        var old = new[] { Q("aaa bbb ccc") };
        var newQ = new[] { Q("zzz yyy xxx") };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Removed.Should().HaveCount(1);
        result.Added.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_QuestionRewordedSlightly_AppearsInChanged()
    {
        var old = new[] { Q("What is the risk level for this control?") };
        var newQ = new[] { Q("What is the risk level for this process?") };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].TextChanged.Should().BeTrue();
        result.Changed[0].SimilarityScore.Should().BeGreaterThanOrEqualTo(0.5).And.BeLessThan(1.0);
        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
    }

    // ── Number changes ─────────────────────────────────────────────────────────

    [Fact]
    public void Diff_IdenticalTextDifferentNumbers_AppearsInChangedNotUnchanged()
    {
        var old = new[] { Q("What is risk?", qn: "1.1") };
        var newQ = new[] { Q("What is risk?", qn: "1.2") };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].OldQuestion.QuestionNumber.Should().Be("1.1");
        result.Changed[0].NewQuestion.QuestionNumber.Should().Be("1.2");
        result.Changed[0].NumberChanged.Should().BeTrue();
        result.Changed[0].TextChanged.Should().BeFalse();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_IdenticalTextSameNumberNoChanges_IsUnchanged()
    {
        var old = new[] { Q("What is risk?", qn: "1.1") };
        var newQ = new[] { Q("What is risk?", qn: "1.1") };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Unchanged.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
    }

    // ── Unchanged.PreviousRowNumber ────────────────────────────────────────────

    [Fact]
    public void Diff_UnchangedQuestion_PreviousRowNumberFromOldQuestion()
    {
        var oldQ = Q("What is risk?", qn: "1.1", row: 42);
        var newQ = Q("What is risk?", qn: "1.1", row: 55);

        var result = GeneralDataDiffEngine.Diff([oldQ], [newQ]);

        result.Unchanged.Should().HaveCount(1);
        result.Unchanged[0].PreviousRowNumber.Should().Be(42);
        result.Unchanged[0].Question.RowNumber.Should().Be(55);
    }

    // ── Per-cell answer changes ────────────────────────────────────────────────

    [Fact]
    public void Diff_IdenticalAnswerCells_NoCellChanges_IsUnchanged()
    {
        var cells = new[] { AC(0, "D", "Template label") };
        var old = new[] { Q("What is risk?", answerCells: cells) };
        var newQ = new[] { Q("What is risk?", answerCells: cells) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Unchanged.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_AnswerCellTextChanged_ReportedInAnswerCellChanges()
    {
        var oldCells = new[] { AC(0, "D", "Old label") };
        var newCells = new[] { AC(0, "D", "New label") };

        var old = new[] { Q("What is risk?", answerCells: oldCells) };
        var newQ = new[] { Q("What is risk?", answerCells: newCells) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        var changedQ = result.Changed[0];
        changedQ.AnswerCellsChanged.Should().BeTrue();
        changedQ.TextChanged.Should().BeFalse(); // question text is identical
        changedQ.AnswerCellChanges.Should().HaveCount(1);
        changedQ.AnswerCellChanges[0].TextChanged.Should().BeTrue();
        changedQ.AnswerCellChanges[0].OldText.Should().Be("Old label");
        changedQ.AnswerCellChanges[0].NewText.Should().Be("New label");
    }

    [Fact]
    public void Diff_AnswerCellAddedOnNewSide_ReportedAsAddedCell()
    {
        var old = new[] { Q("What is risk?", answerCells: []) };
        var newQ = new[] { Q("What is risk?", answerCells: [AC(0, "D", "New cell")]) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        var changedQ = result.Changed[0];
        changedQ.AnswerCellsChanged.Should().BeTrue();
        changedQ.AnswerCellChanges.Should().HaveCount(1);
        changedQ.AnswerCellChanges[0].OldText.Should().BeNull();
        changedQ.AnswerCellChanges[0].NewText.Should().Be("New cell");
    }

    [Fact]
    public void Diff_AnswerCellRemovedFromOldSide_ReportedAsRemovedCell()
    {
        var old = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Old cell")]) };
        var newQ = new[] { Q("What is risk?", answerCells: []) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        var changedQ = result.Changed[0];
        changedQ.AnswerCellsChanged.Should().BeTrue();
        changedQ.AnswerCellChanges.Should().HaveCount(1);
        changedQ.AnswerCellChanges[0].OldText.Should().Be("Old cell");
        changedQ.AnswerCellChanges[0].NewText.Should().BeNull();
    }

    [Fact]
    public void Diff_AnswerCellDvTypeChanged_DvChangedTrue()
    {
        var old = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "List")]) };
        var newQ = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "WholeNumber")]) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        var changedQ = result.Changed[0];
        changedQ.AnswerCellsChanged.Should().BeTrue();
        changedQ.AnswerCellChanges[0].DvChanged.Should().BeTrue();
        changedQ.AnswerCellChanges[0].TextChanged.Should().BeFalse();
    }

    [Fact]
    public void Diff_AnswerCellListDvSameItems_IsUnchanged()
    {
        var old = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "List", dvFormula: "\"Yes,No\"")]) };
        var newQ = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "List", dvFormula: "\"Yes,No\"")]) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Unchanged.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_AnswerCellListDvSameItemsDifferentOrder_IsUnchanged()
    {
        var old = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "List", dvFormula: "\"Yes,No\"")]) };
        var newQ = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "List", dvFormula: "\"No,Yes\"")]) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Unchanged.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_AnswerCellListDvDifferentItems_DvChangedTrue()
    {
        var old = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "List", dvFormula: "\"Yes,No\"")]) };
        var newQ = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "List", dvFormula: "\"Yes,No,N/A\"")]) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].AnswerCellChanges[0].DvChanged.Should().BeTrue();
        result.Changed[0].AnswerCellChanges[0].CfChanged.Should().BeFalse();
    }

    [Fact]
    public void Diff_AnswerCellNonListDv_CfChanged_CfChangedTrue()
    {
        var old = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "WholeNumber", cfOperator: "Equal")]) };
        var newQ = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "WholeNumber", cfOperator: "NotEqual")]) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].AnswerCellChanges[0].CfChanged.Should().BeTrue();
        result.Changed[0].AnswerCellChanges[0].DvChanged.Should().BeFalse();
    }

    [Fact]
    public void Diff_AnswerCellListDv_CfDiffers_CfChangedTrue()
    {
        // Detect-everything: the former List-CF mute is gone. A CF operator change on a
        // List/dropdown answer cell is now surfaced like any other CF change.
        var old = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "List", dvFormula: "\"Yes,No\"", cfOperator: "Equal")]) };
        var newQ = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "List", dvFormula: "\"Yes,No\"", cfOperator: "NotEqual")]) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].AnswerCellChanges[0].CfChanged.Should().BeTrue();
        result.Changed[0].AnswerCellChanges[0].DvChanged.Should().BeFalse();
        result.Unchanged.Should().BeEmpty();
    }

    // ── Detect-everything: full DV/CF detection (B.2b) ────────────────────────

    [Fact]
    public void Diff_AnswerCellDvOperatorOnlyChanged_DvChangedTrue()
    {
        var old  = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "Decimal", dvFormula: "0", dvOperator: "GreaterThan")]) };
        var newQ = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "Decimal", dvFormula: "0", dvOperator: "LessThan")]) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].AnswerCellChanges[0].DvChanged.Should().BeTrue();
        result.Changed[0].AnswerCellChanges[0].CfChanged.Should().BeFalse();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_AnswerCellDvSecondValueOnlyChanged_DvChangedTrue()
    {
        var old  = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "WholeNumber", dvFormula: "0", dvOperator: "Between", dvFormula2: "100")]) };
        var newQ = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "WholeNumber", dvFormula: "0", dvOperator: "Between", dvFormula2: "200")]) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].AnswerCellChanges[0].DvChanged.Should().BeTrue();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_AnswerCellCfTypeOnlyChanged_CfChangedTrue()
    {
        var old  = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "WholeNumber", cfType: "CellIs", cfOperator: "GreaterThan", cfValue: "5")]) };
        var newQ = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "WholeNumber", cfType: "Expression", cfOperator: "GreaterThan", cfValue: "5")]) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].AnswerCellChanges[0].CfChanged.Should().BeTrue();
        result.Changed[0].AnswerCellChanges[0].DvChanged.Should().BeFalse();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_AnswerCellCfValueOnlyChanged_CfChangedTrue()
    {
        var old  = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "WholeNumber", cfType: "CellIs", cfOperator: "GreaterThan", cfValue: "5")]) };
        var newQ = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "WholeNumber", cfType: "CellIs", cfOperator: "GreaterThan", cfValue: "10")]) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].AnswerCellChanges[0].CfChanged.Should().BeTrue();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_AnswerCellCfValue2OnlyChanged_CfChangedTrue()
    {
        var old  = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "WholeNumber", cfType: "CellIs", cfOperator: "Between", cfValue: "1", cfValue2: "10")]) };
        var newQ = new[] { Q("What is risk?", answerCells: [AC(0, "D", "Label", dvType: "WholeNumber", cfType: "CellIs", cfOperator: "Between", cfValue: "1", cfValue2: "20")]) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].AnswerCellChanges[0].CfChanged.Should().BeTrue();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Diff_MultipleAnswerColumnsChanged_AllChangesReported()
    {
        var oldCells = new[]
        {
            AC(0, "D", "Label D"),
            AC(0, "E", "Label E"),
            AC(0, "F", "Label F")
        };
        var newCells = new[]
        {
            AC(0, "D", "New Label D"),
            AC(0, "E", "Label E"),        // unchanged
            AC(0, "F", "New Label F")
        };

        var old = new[] { Q("What is risk?", answerCells: oldCells) };
        var newQ = new[] { Q("What is risk?", answerCells: newCells) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        var changedQ = result.Changed[0];
        changedQ.AnswerCellsChanged.Should().BeTrue();
        // Only D and F changed
        changedQ.AnswerCellChanges.Should().HaveCount(2);
        changedQ.AnswerCellChanges.Should().Contain(c => c.Column == "D" && c.TextChanged);
        changedQ.AnswerCellChanges.Should().Contain(c => c.Column == "F" && c.TextChanged);
    }

    [Fact]
    public void Diff_MultiRowQuestion_AnswerCellKeyedByRowOffsetAndColumn()
    {
        // Row 0 unchanged, row 1 changed
        var oldCells = new[]
        {
            AC(0, "D", "Row0 Label"),
            AC(1, "D", "Row1 Old Label")
        };
        var newCells = new[]
        {
            AC(0, "D", "Row0 Label"),
            AC(1, "D", "Row1 New Label")
        };

        var old = new[] { Q("What is risk?", answerCells: oldCells) };
        var newQ = new[] { Q("What is risk?", answerCells: newCells) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        var changedQ = result.Changed[0];
        changedQ.AnswerCellChanges.Should().HaveCount(1,
            because: "only the row-offset-1 cell changed");
        changedQ.AnswerCellChanges[0].RowOffset.Should().Be(1);
        changedQ.AnswerCellChanges[0].TextChanged.Should().BeTrue();
    }

    // ── Per-cell explanation changes ───────────────────────────────────────────

    [Fact]
    public void Diff_IdenticalExplanationCells_IsUnchanged()
    {
        var cells = new[] { EC(0, "Please explain.") };
        var old = new[] { Q("What is risk?", explanationCells: cells) };
        var newQ = new[] { Q("What is risk?", explanationCells: cells) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Unchanged.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_ExplanationCellTextChanged_ReportedInExplanationCellChanges()
    {
        var oldCells = new[] { EC(0, "Old prompt.") };
        var newCells = new[] { EC(0, "New prompt text.") };

        var old = new[] { Q("What is risk?", explanationCells: oldCells) };
        var newQ = new[] { Q("What is risk?", explanationCells: newCells) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        var changedQ = result.Changed[0];
        changedQ.ExplanationCellsChanged.Should().BeTrue();
        changedQ.TextChanged.Should().BeFalse();
        changedQ.ExplanationCellChanges.Should().HaveCount(1);
        changedQ.ExplanationCellChanges[0].TextChanged.Should().BeTrue();
        changedQ.ExplanationCellChanges[0].OldText.Should().Be("Old prompt.");
        changedQ.ExplanationCellChanges[0].NewText.Should().Be("New prompt text.");
    }

    [Fact]
    public void Diff_ExplanationCellAddedOnNewSide_ReportedAsAddedCell()
    {
        var old = new[] { Q("What is risk?", explanationCells: []) };
        var newQ = new[] { Q("What is risk?", explanationCells: [EC(0, "New prompt.")]) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        var changedQ = result.Changed[0];
        changedQ.ExplanationCellsChanged.Should().BeTrue();
        changedQ.ExplanationCellChanges[0].OldText.Should().BeNull();
        changedQ.ExplanationCellChanges[0].NewText.Should().Be("New prompt.");
    }

    // ── AnswerCellsChanged / ExplanationCellsChanged flags ────────────────────

    [Fact]
    public void Diff_OnlyAnswerCellsChanged_AnswerCellsChangedTrue_ExplanationCellsChangedFalse()
    {
        var oldCells = new[] { AC(0, "D", "Old") };
        var newCells = new[] { AC(0, "D", "New") };
        var expCells = new[] { EC(0, "Same prompt.") };

        var old = new[] { Q("What is risk?", answerCells: oldCells, explanationCells: expCells) };
        var newQ = new[] { Q("What is risk?", answerCells: newCells, explanationCells: expCells) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].AnswerCellsChanged.Should().BeTrue();
        result.Changed[0].ExplanationCellsChanged.Should().BeFalse();
    }

    [Fact]
    public void Diff_OnlyExplanationCellsChanged_ExplanationCellsChangedTrue_AnswerCellsChangedFalse()
    {
        var ansCells = new[] { AC(0, "D", "Same label") };
        var oldExp = new[] { EC(0, "Old prompt.") };
        var newExp = new[] { EC(0, "New prompt.") };

        var old = new[] { Q("What is risk?", answerCells: ansCells, explanationCells: oldExp) };
        var newQ = new[] { Q("What is risk?", answerCells: ansCells, explanationCells: newExp) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].ExplanationCellsChanged.Should().BeTrue();
        result.Changed[0].AnswerCellsChanged.Should().BeFalse();
    }

    // ── SecondBestSimilarity ───────────────────────────────────────────────────

    [Fact]
    public void Diff_OneOldQuestion_SecondBestSimilarityIsNull()
    {
        var old  = new[] { Q("What is risk?") };
        var newQ = new[] { Q("What is risk?") };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Unchanged.Should().HaveCount(1);
        result.Unchanged[0].SecondBestSimilarity.Should().BeNull();
    }

    [Fact]
    public void Diff_TwoOldQuestions_SecondBestSimilarityIsPopulated()
    {
        var oldClose = Q("What is risk?");
        var oldFar   = Q("zyxwv utsrq mnopq");

        var newQ = new[] { Q("What is risk?") };

        var result = GeneralDataDiffEngine.Diff([oldClose, oldFar], newQ);

        result.Unchanged.Should().HaveCount(1);
        result.Unchanged[0].SecondBestSimilarity.Should().NotBeNull();
        result.Unchanged[0].SecondBestSimilarity.Should().BeLessThan(1.0);
    }

    // ── Contextual match bonuses ───────────────────────────────────────────────

    [Fact]
    public void Diff_SectionBonus_ResolvesTieInFavourOfSameSection()
    {
        var oldQ0 = Qs("alpha beta gamma", "SectionX");
        var oldQ1 = Qs("alpha beta gamma", "SectionY");
        var newQ0 = Qs("alpha beta delta", "SectionX");
        var newQ1 = Qs("alpha beta delta", "SectionY");

        var result = GeneralDataDiffEngine.Diff([oldQ0, oldQ1], [newQ0, newQ1]);

        result.Changed.Should().HaveCount(2);
        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
        result.Changed.Should().Contain(c =>
            c.NewQuestion.SectionName == "SectionX" && c.OldQuestion.SectionName == "SectionX");
        result.Changed.Should().Contain(c =>
            c.NewQuestion.SectionName == "SectionY" && c.OldQuestion.SectionName == "SectionY");
    }

    [Fact]
    public void Diff_BothBonuses_SimilarityScoreIsBaseNotAdjusted()
    {
        var old  = new[] { Qs("What is the risk level for this control?", "SectionA", "1.1") };
        var newQ = new[] { Qs("What is the risk level for this process?",  "SectionA", "1.1") };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].TextChanged.Should().BeTrue();
        result.Changed[0].SimilarityScore.Should().BeApproximately(0.825, 0.005,
            because: "SimilarityScore is the base text similarity, not the bonus-adjusted score");
    }

    [Fact]
    public void Diff_BonusPushesSubThresholdPairAboveThreshold_MatchedWithBaseScore()
    {
        var old  = new[] { Qs("abcdefghij", "SectionA", "1.1") };
        var newQ = new[] { Qs("123456ghij", "SectionA", "1.1") };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1,
            because: "adjusted score 0.60 ≥ 0.50 so the pair is matched despite base < threshold");
        result.Changed[0].TextChanged.Should().BeTrue();
        result.Changed[0].SimilarityScore.Should().BeApproximately(0.40, 0.005,
            because: "SimilarityScore is the base score, not the bonus-adjusted score");
        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
    }

    [Fact]
    public void Diff_NoBonusSubThresholdPair_NotMatched_AddedAndRemoved()
    {
        var old  = new[] { Qs("abcdefghij", "SectionX") };
        var newQ = new[] { Qs("123456ghij", "SectionY") };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Removed.Should().HaveCount(1);
        result.Added.Should().HaveCount(1);
        result.Changed.Should().BeEmpty();
        result.Unchanged.Should().BeEmpty();
    }

    // ── Optimal vs greedy assignment ───────────────────────────────────────────

    [Fact]
    public void Diff_OptimalAssignmentDiffersFromGreedy_CorrectPairingProduced()
    {
        var oldQ0 = Q("abcde fghij");
        var oldQ1 = Q("zyxwv utsrq");

        var newQ0 = Q("abcde fghij klmno");
        var newQ1 = Q("abcde fghij p");

        var result = GeneralDataDiffEngine.Diff([oldQ0, oldQ1], [newQ0, newQ1]);

        result.Changed.Should().HaveCount(1);
        result.Changed[0].NewQuestion.QuestionText.Should().Be("abcde fghij p",
            because: "optimal assigns the closer new question (newQ1) to oldQ0");
        result.Changed[0].OldQuestion.QuestionText.Should().Be("abcde fghij");

        result.Added.Should().HaveCount(1);
        result.Added[0].Question.QuestionText.Should().Be("abcde fghij klmno");

        result.Removed.Should().HaveCount(1);
        result.Removed[0].Question.QuestionText.Should().Be("zyxwv utsrq");
    }

    // ── AnswerCellChanges order ────────────────────────────────────────────────

    [Fact]
    public void Diff_AnswerCellChanges_OrderedByRowOffsetThenColumn()
    {
        // Old has cells at (0,"D"), (1,"D"), (1,"E"), (1,"F")
        // New renames all of them
        var oldCells = new[]
        {
            AC(1, "F", "Old 1F"),
            AC(1, "E", "Old 1E"),
            AC(0, "D", "Old 0D"),
            AC(1, "D", "Old 1D"),
        };
        var newCells = new[]
        {
            AC(1, "D", "New 1D"),
            AC(0, "D", "New 0D"),
            AC(1, "E", "New 1E"),
            AC(1, "F", "New 1F"),
        };

        var old = new[] { Q("What is risk?", answerCells: oldCells) };
        var newQ = new[] { Q("What is risk?", answerCells: newCells) };

        var result = GeneralDataDiffEngine.Diff(old, newQ);

        result.Changed.Should().HaveCount(1);
        var changes = result.Changed[0].AnswerCellChanges;
        changes.Should().HaveCount(4);
        changes[0].RowOffset.Should().Be(0); changes[0].Column.Should().Be("D");
        changes[1].RowOffset.Should().Be(1); changes[1].Column.Should().Be("D");
        changes[2].RowOffset.Should().Be(1); changes[2].Column.Should().Be("E");
        changes[3].RowOffset.Should().Be(1); changes[3].Column.Should().Be("F");
    }
}
