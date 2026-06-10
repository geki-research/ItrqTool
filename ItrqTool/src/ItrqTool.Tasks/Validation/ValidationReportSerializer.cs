using System.Text.Json;
using System.Text.Json.Serialization;
using ItrqTool.Domain.Validation;

namespace ItrqTool.Tasks.Validation;

public static class ValidationReportSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        WriteIndented               = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        Converters                  = { new JsonStringEnumConverter() }
    };

    public static string Serialize(ValidationReport report) =>
        JsonSerializer.Serialize(report, Options);

    public static ValidationReport Deserialize(string json) =>
        JsonSerializer.Deserialize<ValidationReport>(json, Options)
        ?? throw new JsonException("Deserialized ValidationReport was null.");
}
