using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.QuestionnaireValidation.Findings;

namespace ItrqTool.Tasks.QuestionnaireValidation.Config;

/// <summary>
/// Shared helper for SeverityOverrides key validation. A version's validate delegate
/// calls this instead of re-implementing the fail-loud unknown-key check.
/// </summary>
public static class CatalogueValidation
{
    /// <summary>
    /// Returns each key in <paramref name="severityOverrides"/> that is not a valid
    /// finding-id in <paramref name="catalogue"/>. An empty result means all keys are known.
    /// </summary>
    public static IReadOnlyList<string> UnknownOverrideKeys(
        IReadOnlyDictionary<string, FindingEvaluation> severityOverrides,
        FindingCatalogue catalogue)
    {
        var unknown = new List<string>();
        foreach (var key in severityOverrides.Keys)
        {
            if (!catalogue.IsValidId(key))
                unknown.Add(key);
        }
        return unknown;
    }
}
