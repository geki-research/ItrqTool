using ItrqTool.Tasks.Shared;

namespace ItrqTool.Tasks.GeneralDataDiff;

public static class GeneralDataDiffEngine
{
    private const double MatchThreshold = 0.5;
    private const double SectionBonus   = 0.10;
    private const double NumberBonus    = 0.10;

    public static DiffResult Diff(
        IReadOnlyList<GeneralDataQuestion> oldQuestions,
        IReadOnlyList<GeneralDataQuestion> newQuestions)
    {
        var added     = new List<AddedQuestion>();
        var removed   = new List<RemovedQuestion>();
        var changed   = new List<ChangedQuestion>();
        var unchanged = new List<UnchangedQuestion>();

        int m = newQuestions.Count;
        int n = oldQuestions.Count;

        if (m == 0 && n == 0)
            return new DiffResult(added, removed, changed, unchanged);

        if (m == 0)
        {
            foreach (var q in oldQuestions) removed.Add(new RemovedQuestion(q));
            return new DiffResult(added, removed, changed, unchanged);
        }

        if (n == 0)
        {
            foreach (var q in newQuestions) added.Add(new AddedQuestion(q));
            return new DiffResult(added, removed, changed, unchanged);
        }

        // Base text similarity matrix — Levenshtein-based on QuestionText.
        var baseSim = new double[m, n];
        for (int i = 0; i < m; i++)
            for (int j = 0; j < n; j++)
                baseSim[i, j] = TextSimilarity.Score(newQuestions[i].QuestionText,
                                                      oldQuestions[j].QuestionText);

        // Adjusted similarity matrix — base + section/number bonuses; matching only.
        var adjSim = new double[m, n];
        for (int i = 0; i < m; i++)
        for (int j = 0; j < n; j++)
        {
            double bonus = 0.0;
            var nq = newQuestions[i];
            var oq = oldQuestions[j];

            if (!string.IsNullOrEmpty(nq.SectionName) &&
                string.Equals(nq.SectionName, oq.SectionName, StringComparison.Ordinal))
                bonus += SectionBonus;

            if (!string.IsNullOrEmpty(nq.QuestionNumber) &&
                string.Equals(nq.QuestionNumber, oq.QuestionNumber, StringComparison.Ordinal))
                bonus += NumberBonus;

            adjSim[i, j] = bonus > 0.0
                ? Math.Min(1.0, baseSim[i, j] + bonus)
                : baseSim[i, j];
        }

        int[] assignment = HungarianAlgorithm.SolveMaximumAssignment(adjSim);

        var matchedOldIndices = new HashSet<int>();

        for (int i = 0; i < m; i++)
        {
            int j            = assignment[i];
            double adjScore  = j >= 0 ? adjSim[i, j]  : 0.0;
            double baseScore = j >= 0 ? baseSim[i, j] : 0.0;
            double? secondBest = ComputeSecondBest(baseSim, i, j);

            if (j >= 0 && adjScore >= MatchThreshold)
            {
                matchedOldIndices.Add(j);
                var oldQ = oldQuestions[j];
                var newQ = newQuestions[i];

                bool textChanged   = baseScore < 1.0;
                bool numberChanged = !string.Equals(oldQ.QuestionNumber, newQ.QuestionNumber,
                                                    StringComparison.Ordinal);

                var (answerChanges, answerCellsChanged) =
                    DiffAnswerCells(oldQ.AnswerCells, newQ.AnswerCells);
                var (explanationChanges, explanationCellsChanged) =
                    DiffExplanationCells(oldQ.ExplanationCells, newQ.ExplanationCells);

                if (textChanged || numberChanged || answerCellsChanged || explanationCellsChanged)
                    changed.Add(new ChangedQuestion(
                        oldQ, newQ, baseScore, secondBest,
                        textChanged, numberChanged,
                        answerCellsChanged, explanationCellsChanged,
                        answerChanges, explanationChanges));
                else
                    unchanged.Add(new UnchangedQuestion(newQ, secondBest, oldQ.RowNumber));
            }
            else
            {
                added.Add(new AddedQuestion(newQuestions[i]));
            }
        }

        for (int j = 0; j < n; j++)
        {
            if (!matchedOldIndices.Contains(j))
                removed.Add(new RemovedQuestion(oldQuestions[j]));
        }

        return new DiffResult(added, removed, changed, unchanged);
    }

    // Returns the next-highest BASE similarity in row i excluding the assigned column.
    private static double? ComputeSecondBest(double[,] baseSim, int rowI, int assignedColJ)
    {
        int cols = baseSim.GetLength(1);
        if (cols < 2) return null;

        double best = double.MinValue;
        bool found  = false;

        for (int j = 0; j < cols; j++)
        {
            if (j == assignedColJ) continue;
            if (!found || baseSim[rowI, j] > best) { best = baseSim[rowI, j]; found = true; }
        }

        return found ? best : null;
    }

    // ── Per-cell diff ─────────────────────────────────────────────────────────────

    private static (IReadOnlyList<AnswerCellChange> Changes, bool Changed)
        DiffAnswerCells(
            IReadOnlyList<GeneralDataAnswerCell> oldCells,
            IReadOnlyList<GeneralDataAnswerCell> newCells)
    {
        var oldByKey = oldCells.ToDictionary(c => (c.RowOffset, c.Column));
        var newByKey = newCells.ToDictionary(c => (c.RowOffset, c.Column));

        var allKeys = oldByKey.Keys
            .Union(newByKey.Keys)
            .OrderBy(k => k.RowOffset)
            .ThenBy(k => k.Column, StringComparer.Ordinal)
            .ToList();

        var changes = new List<AnswerCellChange>();

        foreach (var key in allKeys)
        {
            oldByKey.TryGetValue(key, out var oldCell);
            newByKey.TryGetValue(key, out var newCell);

            string? oldText = oldCell?.Text;
            string? newText = newCell?.Text;

            bool textChanged = !string.Equals(oldText ?? "", newText ?? "", StringComparison.Ordinal);
            bool dvChanged   = DvComparer.IsDvChangedFull(
                                   oldCell?.DvType, oldCell?.DvOperator, oldCell?.DvFormula, oldCell?.DvFormula2,
                                   newCell?.DvType, newCell?.DvOperator, newCell?.DvFormula, newCell?.DvFormula2);
            bool cfChanged   = CfComparer.IsCfChanged(
                                   oldCell?.CfType, oldCell?.CfOperator, oldCell?.CfValue, oldCell?.CfValue2,
                                   newCell?.CfType, newCell?.CfOperator, newCell?.CfValue, newCell?.CfValue2);

            bool addedOrRemoved = oldCell is null || newCell is null;

            if (addedOrRemoved || textChanged || dvChanged || cfChanged)
            {
                changes.Add(new AnswerCellChange(
                    key.RowOffset, key.Column,
                    oldText, newText,
                    oldCell?.DvType, oldCell?.DvFormula, oldCell?.CfOperator,
                    newCell?.DvType, newCell?.DvFormula, newCell?.CfOperator,
                    textChanged, dvChanged, cfChanged,
                    OldDvOperator: oldCell?.DvOperator, OldDvFormula2: oldCell?.DvFormula2,
                    OldCfType: oldCell?.CfType, OldCfValue: oldCell?.CfValue, OldCfValue2: oldCell?.CfValue2,
                    NewDvOperator: newCell?.DvOperator, NewDvFormula2: newCell?.DvFormula2,
                    NewCfType: newCell?.CfType, NewCfValue: newCell?.CfValue, NewCfValue2: newCell?.CfValue2));
            }
        }

        return (changes, changes.Count > 0);
    }

    private static (IReadOnlyList<ExplanationCellChange> Changes, bool Changed)
        DiffExplanationCells(
            IReadOnlyList<GeneralDataExplanationCell> oldCells,
            IReadOnlyList<GeneralDataExplanationCell> newCells)
    {
        var oldByKey = oldCells.ToDictionary(c => c.RowOffset);
        var newByKey = newCells.ToDictionary(c => c.RowOffset);

        var allKeys = oldByKey.Keys.Union(newByKey.Keys).OrderBy(k => k).ToList();

        var changes = new List<ExplanationCellChange>();

        foreach (var key in allKeys)
        {
            oldByKey.TryGetValue(key, out var oldCell);
            newByKey.TryGetValue(key, out var newCell);

            string? oldText = oldCell?.Text;
            string? newText = newCell?.Text;

            bool textChanged = !string.Equals(oldText ?? "", newText ?? "", StringComparison.Ordinal);
            bool dvChanged   = DvComparer.IsDvChangedFull(
                                   oldCell?.DvType, oldCell?.DvOperator, oldCell?.DvFormula, oldCell?.DvFormula2,
                                   newCell?.DvType, newCell?.DvOperator, newCell?.DvFormula, newCell?.DvFormula2);
            bool cfChanged   = CfComparer.IsCfChanged(
                                   oldCell?.CfType, oldCell?.CfOperator, oldCell?.CfValue, oldCell?.CfValue2,
                                   newCell?.CfType, newCell?.CfOperator, newCell?.CfValue, newCell?.CfValue2);

            bool addedOrRemoved = oldCell is null || newCell is null;

            if (addedOrRemoved || textChanged || dvChanged || cfChanged)
            {
                changes.Add(new ExplanationCellChange(
                    key, oldText, newText,
                    oldCell?.DvType, oldCell?.DvFormula, oldCell?.CfOperator,
                    newCell?.DvType, newCell?.DvFormula, newCell?.CfOperator,
                    textChanged, dvChanged, cfChanged,
                    OldDvOperator: oldCell?.DvOperator, OldDvFormula2: oldCell?.DvFormula2,
                    OldCfType: oldCell?.CfType, OldCfValue: oldCell?.CfValue, OldCfValue2: oldCell?.CfValue2,
                    NewDvOperator: newCell?.DvOperator, NewDvFormula2: newCell?.DvFormula2,
                    NewCfType: newCell?.CfType, NewCfValue: newCell?.CfValue, NewCfValue2: newCell?.CfValue2));
            }
        }

        return (changes, changes.Count > 0);
    }
}
