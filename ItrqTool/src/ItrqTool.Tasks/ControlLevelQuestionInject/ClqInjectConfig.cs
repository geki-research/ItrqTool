namespace ItrqTool.Tasks.ControlLevelQuestionInject;

/// <summary>
/// Configuration for the CLQ inject task. Carries inject-specific knobs only;
/// column letters are read from the referenced validation configs at run time.
/// </summary>
public sealed class ClqInjectConfig
{
    public string CurrentConfigFilename { get; init; } = "";
    public string PreviousConfigFilename { get; init; } = "";
    public bool CarryForwardEnabled { get; init; }
    public string StabilityTriggerToken { get; init; } = "";
    public string ExplanationMergeSeparator { get; init; } = "";
    public string ExplanationStrengthsPrefix { get; init; } = "";
    public string ExplanationWeaknessesPrefix { get; init; } = "";

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(CurrentConfigFilename))
            errors.Add("CurrentConfigFilename must not be empty.");

        if (string.IsNullOrWhiteSpace(PreviousConfigFilename))
            errors.Add("PreviousConfigFilename must not be empty.");

        if (CarryForwardEnabled && string.IsNullOrWhiteSpace(StabilityTriggerToken))
            errors.Add("StabilityTriggerToken must not be empty when CarryForwardEnabled is true.");

        if (ExplanationMergeSeparator is null)
            errors.Add("ExplanationMergeSeparator must not be null.");

        if (ExplanationStrengthsPrefix is null)
            errors.Add("ExplanationStrengthsPrefix must not be null.");

        if (ExplanationWeaknessesPrefix is null)
            errors.Add("ExplanationWeaknessesPrefix must not be null.");

        return errors;
    }
}
