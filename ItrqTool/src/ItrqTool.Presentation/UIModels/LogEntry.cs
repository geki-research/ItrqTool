namespace ItrqTool.Presentation.UIModels;

using Microsoft.Extensions.Logging;

public record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string ShortCategory,
    string Message);
