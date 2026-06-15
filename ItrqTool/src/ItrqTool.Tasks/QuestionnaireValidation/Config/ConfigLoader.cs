using System.Text.Json;
using System.Text.Json.Serialization;

namespace ItrqTool.Tasks.QuestionnaireValidation.Config;

/// <summary>
/// Thrown when a config document is invalid. The message names every problem found;
/// the task surfaces it as an Error with Succeeded:false (fail-loud — §4).
/// </summary>
public sealed class ConfigException : Exception
{
    public ConfigException(string message) : base(message) { }
}

/// <summary>
/// Strict, fail-loud generic loader for any config shape. Unknown JSON properties,
/// bad enum values, and structurally malformed JSON are rejected at deserialize time.
/// Semantic errors are collected by the caller-supplied <paramref name="validate"/> delegate
/// and reported as a single aggregated <see cref="ConfigException"/>.
/// </summary>
public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Parses and fully validates a config document. Throws <see cref="ConfigException"/> on any problem.
    /// </summary>
    public static TConfig Load<TConfig>(string json, Func<TConfig, IReadOnlyList<string>> validate)
    {
        TConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<TConfig>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"Configuration could not be parsed: {ex.Message}");
        }

        if (config is null)
            throw new ConfigException(
                "Configuration is null (file is empty or contains literal 'null').");

        var errors = new List<string>(validate(config));

        if (errors.Count > 0)
            throw new ConfigException(
                "Configuration is invalid: " + string.Join(" ", errors));

        return config;
    }
}
