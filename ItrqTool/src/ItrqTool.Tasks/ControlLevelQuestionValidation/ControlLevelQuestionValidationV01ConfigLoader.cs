using System.Text.Json;
using System.Text.Json.Serialization;

namespace ItrqTool.Tasks.ControlLevelQuestionValidation;

/// <summary>
/// Thrown when a CLQ_v01 configuration is invalid. The message names every problem
/// found; the task surfaces it as an Error with Succeeded:false (fail-loud — §4).
/// </summary>
public sealed class ClqConfigException : Exception
{
    public ClqConfigException(string message) : base(message) { }
}

/// <summary>
/// Strict, fail-loud loader for <see cref="ControlLevelQuestionValidationV01Config"/>.
/// Everything configured must be valid, otherwise we throw — no silent default, no
/// coercion. An unknown / typo'd JSON property, a bad enum value, a blank column
/// letter, an empty allowed-answer set, a non-positive deviation threshold, a
/// malformed section entry, an unknown severity-override key, or a literal-null
/// document all fail.
/// </summary>
public static class ControlLevelQuestionValidationV01ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Parses and fully validates a config document. Throws <see cref="ClqConfigException"/> on any problem.</summary>
    public static ControlLevelQuestionValidationV01Config Load(string json)
    {
        ControlLevelQuestionValidationV01Config? config;
        try
        {
            config = JsonSerializer.Deserialize<ControlLevelQuestionValidationV01Config>(json, Options);
        }
        catch (JsonException ex)
        {
            // Covers unknown properties (UnmappedMemberHandling.Disallow), bad enum values
            // for severityOverrides, and structurally malformed JSON.
            throw new ClqConfigException($"Configuration could not be parsed: {ex.Message}");
        }

        if (config is null)
            throw new ClqConfigException(
                "Configuration is null (file is empty or contains literal 'null').");

        var errors = new List<string>(config.Validate());

        // Section entries — ParsedSections throws FormatException on a bad entry.
        try { _ = config.ParsedSections; }
        catch (FormatException ex) { errors.Add(ex.Message); }

        // Severity-override keys must be known finding-ids.
        foreach (var key in config.SeverityOverrides.Keys)
        {
            if (!ClqFindings.IsValidId(key))
                errors.Add(
                    $"Unknown severityOverrides key '{key}'. Valid finding-ids are: " +
                    string.Join(", ", ClqFindings.AllIds()) + ".");
        }

        if (errors.Count > 0)
            throw new ClqConfigException(
                "Configuration is invalid: " + string.Join(" ", errors));

        return config;
    }
}
