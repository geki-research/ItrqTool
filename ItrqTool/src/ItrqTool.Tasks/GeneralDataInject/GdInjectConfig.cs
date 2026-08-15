namespace ItrqTool.Tasks.GeneralDataInject;

/// <summary>
/// Configuration for the GD inject task (v01 → v02 reference injection). Carries the two
/// validation-config filenames plus the qid-join similarity threshold; column letters are read
/// from the referenced validation configs at run time. GD inject is reference-only — it has none
/// of the CLQ inject's carry-forward / stability / explanation-merge knobs. Mirrors
/// <c>RlqInjectConfig</c>, plus <see cref="QidJoinSimilarityThreshold"/>.
/// </summary>
public sealed class GdInjectConfig
{
    /// <summary>Default for <see cref="QidJoinSimilarityThreshold"/> (BLG-0080 / DEC-1).</summary>
    public const double DefaultQidJoinSimilarityThreshold = 0.50;

    public string CurrentConfigFilename { get; init; } = "";
    public string PreviousConfigFilename { get; init; } = "";

    /// <summary>
    /// Similarity threshold for the GD inject qid-join: how alike two same-XrefId question texts
    /// must be, across years, to be treated as the SAME question. Range 0.0–1.0 inclusive
    /// (the closed range <c>TextSimilarity.Score</c> produces); out of range is a config error.
    /// Omit the JSON key to accept the <see cref="DefaultQidJoinSimilarityThreshold"/> of 0.50.
    /// <para>
    /// SCOPE (BLG-0080 chunk B1): this is the configuration seam ONLY. Nothing reads it yet —
    /// <c>GdInjectAligner</c> still decides text-sameness by <c>StringComparison.Ordinal</c>
    /// equality, and a later chunk switches that comparison over. It is deliberately named for
    /// the GD inject qid-join and NOT as a generic "match threshold": the four other threshold
    /// sites (the two <c>QuestionDiffEngine</c> copies, <c>GeneralDataDiffEngine</c>,
    /// <c>CrossFormatAligner</c>/<c>AlignmentEngine</c>) keep their own values and must not bind
    /// to this one without a separate decision.
    /// </para>
    /// </summary>
    public double QidJoinSimilarityThreshold { get; init; } = DefaultQidJoinSimilarityThreshold;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(CurrentConfigFilename))
            errors.Add("CurrentConfigFilename must not be empty.");

        if (string.IsNullOrWhiteSpace(PreviousConfigFilename))
            errors.Add("PreviousConfigFilename must not be empty.");

        // NaN fails both comparisons below, so it is rejected by the first branch.
        if (!(QidJoinSimilarityThreshold >= 0.0) || QidJoinSimilarityThreshold > 1.0)
            errors.Add("QidJoinSimilarityThreshold must be between 0.0 and 1.0 inclusive.");

        return errors;
    }
}
