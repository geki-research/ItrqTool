using Microsoft.Extensions.Logging;

namespace ItrqTool.Domain;

public record TaskExecutionContext(
    string TaskId,
    IReadOnlyDictionary<string, string> InputPaths,
    IReadOnlyDictionary<string, string> OutputPaths,
    ILogger Logger,
    string WorkingDirectory
);
