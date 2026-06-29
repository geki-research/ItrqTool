namespace ItrqTool.Tasks.GeneralDataInject;

/// <summary>
/// Configuration for the GD inject task (v01 → v02 reference injection). Carries the two
/// validation-config filenames only; column letters are read from the referenced validation
/// configs at run time. GD inject is reference-only — it has none of the CLQ inject's
/// carry-forward / stability / explanation-merge knobs. Mirrors <c>RlqInjectConfig</c>.
/// </summary>
public sealed class GdInjectConfig
{
    public string CurrentConfigFilename { get; init; } = "";
    public string PreviousConfigFilename { get; init; } = "";

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(CurrentConfigFilename))
            errors.Add("CurrentConfigFilename must not be empty.");

        if (string.IsNullOrWhiteSpace(PreviousConfigFilename))
            errors.Add("PreviousConfigFilename must not be empty.");

        return errors;
    }
}
