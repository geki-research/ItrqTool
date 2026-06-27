using System.Text.Json;
using System.Text.Json.Serialization;

namespace ItrqTool.Tasks.WorksheetStructure;

/// <summary>
/// Thrown when a shipped structure schema cannot be loaded (file missing, unreadable, malformed JSON,
/// or an unknown property). The mediator converts this into the AssetError channel — a deployment
/// defect, NOT a data finding.
/// </summary>
public sealed class WorksheetSchemaLoadException : Exception
{
    public WorksheetSchemaLoadException(string message) : base(message) { }
    public WorksheetSchemaLoadException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Loads a <see cref="WorksheetStructureSchema"/> by convention from
/// <c>&lt;schemasBaseDir&gt;/schemas/&lt;questionnaire&gt;-&lt;version&gt;.structure.json</c>.
/// <para>
/// Strictness mirrors the validation <c>ConfigLoader</c>: case-insensitive property names, unknown
/// properties rejected (<see cref="JsonUnmappedMemberHandling.Disallow"/>), string enums. Any failure
/// throws <see cref="WorksheetSchemaLoadException"/> — fail-loud, because a broken shipped asset is a
/// deployment defect.
/// </para>
/// <para>
/// <c>schemasBaseDir</c> is injected (NOT baked to <c>AppContext.BaseDirectory</c>) so tests can point
/// at a temp directory without depending on copy-to-output; production wiring passes
/// <c>AppContext.BaseDirectory</c>.
/// </para>
/// </summary>
public sealed class WorksheetStructureSchemaLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public WorksheetStructureSchema Load(WorksheetSchemaRef reference, string schemasBaseDir)
    {
        var fileName = $"{reference.Questionnaire}-{reference.Version}.structure.json";
        var path = Path.Combine(schemasBaseDir, "schemas", fileName);

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new WorksheetSchemaLoadException(
                $"Structure schema could not be read: {path} ({ex.Message})", ex);
        }

        WorksheetStructureSchema? schema;
        try
        {
            schema = JsonSerializer.Deserialize<WorksheetStructureSchema>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new WorksheetSchemaLoadException(
                $"Structure schema could not be parsed: {path} ({ex.Message})", ex);
        }

        if (schema is null)
            throw new WorksheetSchemaLoadException(
                $"Structure schema is null (file empty or literal 'null'): {path}");

        return schema;
    }
}
