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
        var validationChanges = new List<ValidationChange>();
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
            if (score < 1.0)
                changed.Add(new ChangedQuestion(oldQ, newQ, score));

            bool dvDiffers = oldQ.DvType != newQ.DvType;
            bool cfDiffers = oldQ.CfOperator != newQ.CfOperator;

            if (dvDiffers || cfDiffers)
                validationChanges.Add(new ValidationChange(
                    oldQ, newQ,
                    oldQ.DvType, newQ.DvType,
                    oldQ.CfOperator, newQ.CfOperator));
        }

        return new DiffResult(added, removed, changed, validationChanges);
    }
}
