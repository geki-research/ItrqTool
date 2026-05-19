namespace ItrqTool.Tasks.TemplateDiff;

public static class QuestionDiffEngine
{
    public static DiffResult Diff(
        IReadOnlyList<AuditQuestion> oldQuestions,
        IReadOnlyList<AuditQuestion> newQuestions)
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

        // Build similarity matrix: sim[i,j] = similarity of newQ[i] to oldQ[j]
        var sim = new double[m, n];
        for (int i = 0; i < m; i++)
            for (int j = 0; j < n; j++)
                sim[i, j] = TextSimilarity.Score(newQuestions[i].QuestionText,
                                                  oldQuestions[j].QuestionText);

        // Optimal assignment via Hungarian algorithm
        int[] assignment = HungarianAlgorithm.SolveMaximumAssignment(sim);

        // Track which old questions were matched above the threshold
        var matchedOldIndices = new HashSet<int>();

        for (int i = 0; i < m; i++)
        {
            int j     = assignment[i];
            double score = j >= 0 ? sim[i, j] : 0.0;
            double? secondBest = ComputeSecondBest(sim, i, j);

            if (j >= 0 && score >= 0.5)
            {
                matchedOldIndices.Add(j);
                var oldQ = oldQuestions[j];
                var newQ = newQuestions[i];

                bool textChanged   = score < 1.0;
                bool numberChanged = !string.Equals(oldQ.QuestionNumber, newQ.QuestionNumber,
                                                    StringComparison.Ordinal);
                bool dvChanged     = IsDvChanged(oldQ, newQ);
                bool cfChanged     = !IsDvList(oldQ.DvType)
                                     && !string.Equals(oldQ.CfOperator, newQ.CfOperator,
                                                       StringComparison.Ordinal);

                if (textChanged || numberChanged || dvChanged || cfChanged)
                    changed.Add(new ChangedQuestion(oldQ, newQ, score, secondBest,
                                                    textChanged, numberChanged, dvChanged, cfChanged));
                else
                    unchanged.Add(new UnchangedQuestion(newQ, secondBest));
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

    // Returns the next-highest similarity in row i excluding the assigned column j.
    // Returns null when fewer than 2 old questions exist (no second candidate).
    private static double? ComputeSecondBest(double[,] sim, int rowI, int assignedColJ)
    {
        int cols = sim.GetLength(1);
        if (cols < 2) return null;

        double best = double.MinValue;
        bool found  = false;

        for (int j = 0; j < cols; j++)
        {
            if (j == assignedColJ) continue;
            if (!found || sim[rowI, j] > best) { best = sim[rowI, j]; found = true; }
        }

        return found ? best : null;
    }

    private static bool IsDvList(string? dvType)
        => string.Equals(dvType, "List", StringComparison.OrdinalIgnoreCase);

    private static bool IsDvChanged(AuditQuestion old, AuditQuestion @new)
    {
        if (!string.Equals(old.DvType, @new.DvType, StringComparison.Ordinal))
            return true;

        if (old.DvType is null) return false;

        if (string.Equals(old.DvType, "List", StringComparison.OrdinalIgnoreCase))
            return !ListValuesEqual(old.DvFormula, @new.DvFormula);

        return false;
    }

    private static bool ListValuesEqual(string? oldFormula, string? newFormula)
    {
        if (oldFormula is null && newFormula is null) return true;
        if (oldFormula is null || newFormula is null) return false;

        bool oldInline = IsInlineList(oldFormula);
        bool newInline = IsInlineList(newFormula);

        if (oldInline && newInline)
        {
            var oldItems = ParseInlineList(oldFormula);
            var newItems = ParseInlineList(newFormula);
            return oldItems.SequenceEqual(newItems);
        }

        return string.Equals(oldFormula, newFormula, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInlineList(string formula)
        => !formula.StartsWith("=") && !formula.Contains('$');

    private static IReadOnlyList<string> ParseInlineList(string formula)
    {
        var s = formula.Trim().Trim('"');
        return s.Split(',')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
    }
}
