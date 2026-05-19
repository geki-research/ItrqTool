namespace ItrqTool.Tasks.TemplateDiff;

public static class QuestionDiffEngine
{
    public static DiffResult Diff(
        IReadOnlyList<AuditQuestion> oldQuestions,
        IReadOnlyList<AuditQuestion> newQuestions)
    {
        var remainingOld = oldQuestions.ToList();
        var added = new List<AddedQuestion>();
        var removed = new List<RemovedQuestion>();
        var changed = new List<ChangedQuestion>();
        var unchanged = new List<UnchangedQuestion>();
        var matched = new List<(AuditQuestion Old, AuditQuestion New, double Score)>();

        foreach (var newQ in newQuestions)
        {
            if (remainingOld.Count == 0)
            {
                added.Add(new AddedQuestion(newQ));
                continue;
            }

            double bestScore = -1.0;
            AuditQuestion? bestOld = null;

            foreach (var oldQ in remainingOld)
            {
                double score = TextSimilarity.Score(oldQ.QuestionText, newQ.QuestionText);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestOld = oldQ;
                }
            }

            if (bestScore >= 0.5 && bestOld is not null)
            {
                remainingOld.Remove(bestOld);
                matched.Add((bestOld, newQ, bestScore));
            }
            else
            {
                added.Add(new AddedQuestion(newQ));
            }
        }

        foreach (var oldQ in remainingOld)
            removed.Add(new RemovedQuestion(oldQ));

        foreach (var (oldQ, newQ, score) in matched)
        {
            bool textChanged   = score < 1.0;
            bool numberChanged = !string.Equals(oldQ.QuestionNumber, newQ.QuestionNumber,
                                                StringComparison.Ordinal);
            bool dvChanged     = IsDvChanged(oldQ, newQ);
            bool cfChanged     = !IsDvList(oldQ.DvType)
                                 && !string.Equals(oldQ.CfOperator, newQ.CfOperator,
                                                   StringComparison.Ordinal);

            if (textChanged || numberChanged || dvChanged || cfChanged)
                changed.Add(new ChangedQuestion(oldQ, newQ, score,
                                                textChanged, numberChanged, dvChanged, cfChanged));
            else
                unchanged.Add(new UnchangedQuestion(newQ));
        }

        return new DiffResult(added, removed, changed, unchanged);
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
